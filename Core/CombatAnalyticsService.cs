// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Capture;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Core
{
    internal sealed class CombatAnalyticsService(
        MetricsRepository repository,
        CollectorRegistry collectors) : IDisposable
    {
        private static readonly Func<RunManager, long> RunStartTimeGetter = CompileRunStartTimeGetter();

        private readonly Dictionary<AttackCommand, ExplicitScope> _attackScopes =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<Creature, BlockAttribution> _blockCredits =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<CardPlay, ExplicitScope> _cardScopes =
            new(ReferenceEqualityComparer.Instance);

        private readonly HashSet<ulong> _pendingExtraTurnPlayers = [];

        private readonly Dictionary<PotionModel, Stack<ExplicitScope>> _potionScopes =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<PowerModel, PowerAttribution> _powerCredits =
            new(ReferenceEqualityComparer.Instance);

        private readonly List<IDisposable> _subscriptions = [];
        private CardModel? _activeCard;
        private MutableCombatSession? _activeCombat;
        private string? _activeTurnEventId;
        private volatile bool _captureActive;
        private TimelineTurnSide _currentSide;
        private ICombatState? _currentState;
        private bool _disposed;
        private CombatHistory? _history;
        private int _historyProcessingFailures;
        private bool _isExtraTurn;
        private MutableRunSession? _liveRun;
        private long _metricSequence;
        private MutableCombatSession? _pendingRestoredCombat;
        private int _processedHistoryEntries;
        private long _timelineSequence;
        private int _turnIndex;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _captureActive = false;
            DetachHistory();
            RestoreScopes();
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            CaptureBridge.IsCombatActive = null;
            CaptureBridge.FallbackParentResolver = null;
            CaptureBridge.EffectMaterialized = null;
            CaptureBridge.DamageRequestCompleted = null;
        }

        internal void Initialize()
        {
            MetricsRepository.ReconcileLegacyMultiplayerRuns();
            CaptureBridge.IsCombatActive = () => _captureActive;
            CaptureBridge.FallbackParentResolver = () => _activeTurnEventId;
            CaptureBridge.EffectMaterialized = OnEffectMaterialized;
            CaptureBridge.DamageRequestCompleted = OnDamageRequestCompleted;

            Subscribe<RunStartedEvent>(evt =>
                StartNewRun(evt.RunState, evt.IsMultiplayer, evt.IsDaily, evt.OccurredAtUtc));
            Subscribe<GameReadyEvent>(_ => TimelineCapturePatches.RefreshModelPatches());
            Subscribe<MainMenuReadyEvent>(_ => OnMainMenuReady());
            Subscribe<RunLoadedEvent>(evt =>
                ResumeRun(evt.RunState, evt.IsMultiplayer, evt.IsDaily, evt.OccurredAtUtc));
            Subscribe<RunSavedEvent>(OnRunSaved, false);
            Subscribe<RunEndedEvent>(EndRun);
            Subscribe<CombatStartingEvent>(StartCombat);
            Subscribe<CombatEndedEvent>(EndCombat);
            Subscribe<RoomExitedEvent>(OnRoomExited, false);
            Subscribe<SideTurnStartingEvent>(OnSideTurnStarting);
            Subscribe<SideTurnStartedEvent>(OnSideTurnStarted);
            Subscribe<SideTurnEndingEvent>(OnSideTurnEnding);
            Subscribe<SideTurnEndedEvent>(OnSideTurnEnded);
            Subscribe<ExtraTurnTakenEvent>(OnExtraTurnTaken);
            Subscribe<HandDrawingEvent>(OnHandDrawing, false);
            Subscribe<CardDrawnEvent>(OnCardDrawn, false);
            Subscribe<CardPlayingEvent>(OnCardPlaying, false);
            Subscribe<CardPlayedEvent>(OnCardPlayed, false);
            Subscribe<CardMovedBetweenPilesEvent>(OnCardMoved, false);
            Subscribe<CardDiscardedEvent>(OnCardDiscarded, false);
            Subscribe<CardExhaustedEvent>(OnCardExhausted, false);
            Subscribe<AttackStartingEvent>(OnAttackStarting, false);
            Subscribe<AttackEndedEvent>(OnAttackEnded, false);
            Subscribe<CurrentHpChangedEvent>(OnCurrentHpChanged, false);
            Subscribe<EnergyGainedEvent>(OnEnergyGained, false);
            Subscribe<EnergyResetEvent>(OnEnergyReset, false);
            Subscribe<EnergySpentEvent>(OnEnergySpent, false);
            Subscribe<PotionUsingEvent>(OnPotionUsing, false);
            Subscribe<PotionUsedEvent>(OnPotionUsed, false);
            Subscribe<ShuffledEvent>(OnShuffled, false);
            Subscribe<CreatureDyingEvent>(OnCreatureDying, false);
            Subscribe<CreatureDiedEvent>(OnCreatureDied, false);

            TimelineCapturePatches.Initialize();
        }

        private void OnMainMenuReady()
        {
            var hadSession = _liveRun != null || _activeCombat != null || _pendingRestoredCombat != null;
            _captureActive = false;
            DetachHistory();
            RestoreScopes();
            _liveRun = null;
            _activeCombat = null;
            _pendingRestoredCombat = null;
            repository.SetLiveRun(null);
            var clearedRetainedCombat = repository.ClearRetainedCombat();
            ResetCombatState();
            _currentState = null;
            collectors.NotifyChanged();
            if (hadSession || clearedRetainedCombat)
                Main.Logger.Info("Analytics session cleared after returning to the main menu.");
        }

        internal bool PublishCustom(string ownerModId, MetricObservation observation)
        {
            if (string.IsNullOrWhiteSpace(ownerModId) || !collectors.IsRegisteredMetric(observation.MetricId))
                return false;
            var run = _liveRun;
            var combat = _activeCombat;
            if (run == null || combat == null || observation.Subject.Kind != AnalyticsEntityKind.Player)
                return false;

            var tags = new Dictionary<string, string>(observation.Tags, StringComparer.Ordinal)
            {
                ["owner_mod_id"] = ownerModId,
            };
            Record(observation with
            {
                Sequence = Interlocked.Increment(ref _metricSequence),
                RunId = run.RunId,
                CombatId = combat.CombatId,
                ActIndex = combat.ActIndex,
                Floor = combat.Floor,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Tags = ReadOnly(tags),
            });
            return true;
        }

        private void Subscribe<TEvent>(Action<TEvent> handler, bool replayLatest = true)
            where TEvent : struct, IFrameworkLifecycleEvent
        {
            _subscriptions.Add(RitsuLibFramework.SubscribeLifecycle(handler, replayLatest));
        }

        private void StartNewRun(IRunState runState, bool isMultiplayer, bool isDaily, DateTimeOffset occurredAtUtc)
        {
            FinalizeActiveCombat(occurredAtUtc);
            var previous = repository.GetLiveRun(true);
            if (previous is { Combats.Count: > 0 })
                MetricsRepository.SaveRunSnapshot(previous);
            var identity = ResolveRunIdentity(runState, isMultiplayer, isDaily, occurredAtUtc);
            if (isMultiplayer)
                identity = AllocateNewMultiplayerIdentity(identity, occurredAtUtc);
            var session = CreateRunSession(identity, isMultiplayer, isDaily);
            _liveRun = session;
            _activeCombat = null;
            _pendingRestoredCombat = null;
            repository.ClearRetainedCombat();
            repository.SetLiveRun(session);
            collectors.NotifyChanged();
            Main.Logger.Info(
                $"Started analytics run '{LogId(session.RunId)}' (multiplayer={isMultiplayer}, daily={isDaily}).");
        }

        private void ResumeRun(RunState runState, bool isMultiplayer, bool isDaily, DateTimeOffset occurredAtUtc)
        {
            DiscardActiveCombat();
            var identity = ResolveRunIdentity(runState, isMultiplayer, isDaily, occurredAtUtc);
            var previous = _liveRun;
            if (previous?.RunId != identity.RunId &&
                previous?.Snapshot(true) is { Combats.Count: > 0 } previousSnapshot)
                MetricsRepository.SaveRunSnapshot(previousSnapshot);
            var saved = FindSavedRunForResume(identity, runState, isMultiplayer, isDaily);
            if (saved == null && isMultiplayer)
                identity = AllocateNewMultiplayerIdentity(identity, occurredAtUtc);
            var session = saved == null
                ? CreateRunSession(identity, isMultiplayer, isDaily)
                : MutableRunSession.Restore(saved);
            if (saved != null)
                RestoreRunSequenceCounters(saved);
            session.Resume();
            _pendingRestoredCombat = session.GetActiveCombat();
            session.DiscardActiveCombat();

            _liveRun = session;
            _activeCombat = null;
            repository.ClearRetainedCombat();
            repository.SetLiveRun(session);
            collectors.NotifyChanged();
            Main.Logger.Info(
                $"Resumed analytics run '{LogId(session.RunId)}' from " +
                $"{(saved == null ? "live game state" : $"saved history with {saved.Combats.Count} combat(s)")}" +
                $"{(saved != null && saved.RunId != identity.RunId ? " via compatible multiplayer identity" : string.Empty)}.");
        }

        private void OnRunSaved(RunSavedEvent evt)
        {
            ProcessHistoryChanges();
            var run = _liveRun;
            if (run == null)
                return;
            _activeCombat?.UpdateRoundCount(_currentState?.RoundNumber ?? 0);
            var snapshot = run.Snapshot(true);
            if (_pendingRestoredCombat != null &&
                snapshot.Combats.All(combat => combat.CombatId != _pendingRestoredCombat.CombatId))
                snapshot = snapshot with
                {
                    Combats = snapshot.Combats.Append(_pendingRestoredCombat.Snapshot(true)).ToArray(),
                };
            MetricsRepository.SaveRunSnapshot(snapshot);
            Main.Logger.Debug(
                $"Captured run checkpoint '{LogId(snapshot.RunId)}' with {snapshot.Combats.Count} combat(s).");
        }

        private void EndRun(RunEndedEvent evt)
        {
            FinalizeActiveCombat(evt.OccurredAtUtc);
            var run = _liveRun;
            if (run == null)
                return;
            run.CompleteRun(evt.OccurredAtUtc, evt.IsVictory, evt.IsAbandoned);
            _pendingRestoredCombat = null;
            var snapshot = run.Snapshot(true);
            MetricsRepository.SaveRunSnapshot(snapshot);
            collectors.NotifyChanged();
            Main.Logger.Info(
                $"Completed analytics run '{LogId(snapshot.RunId)}' with {snapshot.Combats.Count} combat(s) " +
                $"(victory={snapshot.IsVictory?.ToString() ?? "unknown"}, " +
                $"abandoned={snapshot.IsAbandoned?.ToString() ?? "unknown"}).");
        }

        private void StartCombat(CombatStartingEvent evt)
        {
            var run = _liveRun ?? repository.LiveRun;
            if (run == null)
            {
                StartNewRun(evt.RunState, evt.RunState.Players.Count > 1, false, evt.OccurredAtUtc);
                run = _liveRun;
            }

            if (run == null)
                return;

            var room = evt.RunState.CurrentRoom as CombatRoom;
            var encounter = room?.Encounter;
            var restored = _pendingRestoredCombat;
            var resumeRestored = restored != null && MatchesCombat(restored, evt.RunState, encounter);
            MutableCombatSession combat;
            if (resumeRestored)
            {
                combat = restored!;
                Main.Logger.Info($"Restoring analytics combat checkpoint '{LogId(combat.CombatId)}'.");
            }
            else
            {
                if (_pendingRestoredCombat != null)
                {
                    Main.Logger.Info(
                        $"Discarding analytics checkpoint '{LogId(_pendingRestoredCombat.CombatId)}' because the " +
                        "loaded combat room does not match it.");
                    _pendingRestoredCombat = null;
                }
                else
                {
                    FinalizeActiveCombat(evt.OccurredAtUtc);
                }

                combat = new()
                {
                    RunId = run.RunId,
                    CombatId = Guid.NewGuid().ToString("N"),
                    ActIndex = evt.RunState.CurrentActIndex,
                    Floor = evt.RunState.TotalFloor,
                    EncounterId = encounter == null ? string.Empty : Safe(() => encounter.Id.Entry, string.Empty),
                    EncounterName = encounter == null
                        ? string.Empty
                        : Safe(() => encounter.Title.GetFormattedText(), string.Empty),
                    StartedAtUtc = evt.OccurredAtUtc,
                };
            }

            _pendingRestoredCombat = null;
            TimelineCapturePatches.ConsumeSkippedOriginalDiagnostics();
            combat.UpdateRoundCount(evt.CombatState?.RoundNumber ?? 0);
            repository.ClearRetainedCombat();
            run.SetActiveCombat(combat);
            _liveRun = run;
            _activeCombat = combat;
            _captureActive = true;
            _currentState = evt.CombatState ?? room?.CombatState;
            ResetCombatState();
            if (resumeRestored)
                RestoreCombatState(combat);
            AttachHistory(CombatManager.Instance.History);
            ProcessHistoryChanges();
            AddTimeline(CombatTimelineKind.Combat,
                resumeRestored ? TimelineEventPhase.Instant : TimelineEventPhase.Started,
                resumeRestored ? "combat.resume" : "combat.start",
                combat.EncounterName, occurredAtUtc: evt.OccurredAtUtc, parentEventId: null, bypassScope: true);
            Main.Logger.Info(
                $"{(resumeRestored ? "Resumed" : "Started")} analytics combat '{LogId(combat.CombatId)}' " +
                $"for run '{LogId(run.RunId)}' at act {combat.ActIndex}, floor {combat.Floor}, " +
                $"encounter='{EncounterLogName(combat)}'.");
        }

        private static bool MatchesCombat(
            MutableCombatSession combat,
            IRunState runState,
            EncounterModel? encounter)
        {
            var encounterId = encounter == null ? string.Empty : Safe(() => encounter.Id.Entry, string.Empty);
            var encounterMatches = !string.IsNullOrEmpty(combat.EncounterId) && !string.IsNullOrEmpty(encounterId)
                ? combat.EncounterId == encounterId
                : encounter != null && !string.IsNullOrEmpty(combat.EncounterName) &&
                  combat.EncounterName == Safe(() => encounter.Title.GetFormattedText(), string.Empty);
            return combat.ActIndex == runState.CurrentActIndex &&
                   combat.Floor == runState.TotalFloor &&
                   encounterMatches;
        }

        private void RestoreRunSequenceCounters(RunSnapshot run)
        {
            _metricSequence = Math.Max(_metricSequence, run.Combats
                .SelectMany(combat => combat.Events)
                .Select(observation => observation.Sequence)
                .DefaultIfEmpty()
                .Max());
            _timelineSequence = Math.Max(_timelineSequence, run.Combats
                .SelectMany(combat => combat.Timeline ?? [])
                .Select(timelineEvent => timelineEvent.Sequence)
                .DefaultIfEmpty()
                .Max());
        }

        private void RestoreCombatState(MutableCombatSession combat)
        {
            var snapshot = combat.Snapshot(true);
            _metricSequence = Math.Max(_metricSequence,
                snapshot.Events.Select(observation => observation.Sequence).DefaultIfEmpty().Max());
            var timeline = snapshot.Timeline ?? [];
            _timelineSequence = Math.Max(_timelineSequence,
                timeline.Select(timelineEvent => timelineEvent.Sequence).DefaultIfEmpty().Max());
            _turnIndex = timeline.Select(timelineEvent => timelineEvent.TurnIndex).DefaultIfEmpty().Max();
            _currentSide = timeline.Count == 0 ? TimelineTurnSide.None : timeline[^1].Side;
        }

        private void EndCombat(CombatEndedEvent evt)
        {
            ProcessHistoryChanges();
            AddTimeline(CombatTimelineKind.Combat, TimelineEventPhase.Completed, "combat.end",
                _activeCombat?.EncounterName ?? string.Empty,
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: null, bypassScope: true);
            FinalizeActiveCombat(evt.OccurredAtUtc);
        }

        private void FinalizeActiveCombat(DateTimeOffset endedAtUtc)
        {
            _captureActive = false;
            ProcessHistoryChanges();
            var processedHistoryEntries = _processedHistoryEntries;
            DetachHistory();
            RestoreScopes();
            var run = _liveRun;
            if (run == null)
                return;
            var captureBuffers = _activeCombat?.GetCaptureBufferDiagnostics() ?? default;
            var snapshot = run.CompleteActiveCombat(endedAtUtc);
            _activeCombat = null;
            if (snapshot == null)
                return;
            repository.RetainCompletedCombat(snapshot);
            MetricsRepository.SaveRunSnapshot(run.Snapshot(true));
            collectors.CompleteCombat(snapshot);
            LogCompletedCombat(snapshot, captureBuffers, processedHistoryEntries, _historyProcessingFailures,
                TimelineCapturePatches.ConsumeSkippedOriginalDiagnostics());
            ResetCombatState();
            _currentState = null;
        }

        private void DiscardActiveCombat()
        {
            var discardedCombatId = _activeCombat?.CombatId;
            _captureActive = false;
            DetachHistory();
            RestoreScopes();
            _liveRun?.DiscardActiveCombat();
            _activeCombat = null;
            _pendingRestoredCombat = null;
            repository.ClearRetainedCombat();
            ResetCombatState();
            _currentState = null;
            TimelineCapturePatches.ConsumeSkippedOriginalDiagnostics();
            if (discardedCombatId != null)
                Main.Logger.Info($"Discarded incomplete analytics combat '{LogId(discardedCombatId)}'.");
        }

        private void OnRoomExited(RoomExitedEvent evt)
        {
            if (evt.Room is not CombatRoom || _activeCombat != null || !repository.ClearRetainedCombat())
                return;
            collectors.NotifyChanged();
            Main.Logger.Debug("Cleared the retained combat snapshot after leaving its room.");
        }

        private void ResetCombatState()
        {
            _powerCredits.Clear();
            _blockCredits.Clear();
            _pendingExtraTurnPlayers.Clear();
            _activeCard = null;
            _activeTurnEventId = null;
            _turnIndex = 0;
            _currentSide = TimelineTurnSide.None;
            _isExtraTurn = false;
            _historyProcessingFailures = 0;
        }

        private void OnSideTurnStarting(SideTurnStartingEvent evt)
        {
            _currentState = evt.CombatState;
            _currentSide = Side(evt.Side);
            _isExtraTurn = _currentSide == TimelineTurnSide.Player && _pendingExtraTurnPlayers.Count > 0;
            _turnIndex++;
            Main.Logger.Debug(
                $"Turn {_turnIndex} starting: side={_currentSide}, round={evt.CombatState.RoundNumber}, " +
                $"extra={_isExtraTurn}.");
            var started = AddTimeline(CombatTimelineKind.Turn, TimelineEventPhase.Started, "turn.start",
                _currentSide.ToString(), occurredAtUtc: evt.OccurredAtUtc, parentEventId: null, bypassScope: true,
                details: Details(("extra_turn", _isExtraTurn.ToString()),
                    ("extra_player_net_ids", string.Join(';', _pendingExtraTurnPlayers.Order()))));
            _activeTurnEventId = started?.EventId;
        }

        private void OnSideTurnStarted(SideTurnStartedEvent evt)
        {
            AddTimeline(CombatTimelineKind.Phase, TimelineEventPhase.Completed, "turn.ready", Side(evt.Side).ToString(),
                occurredAtUtc: evt.OccurredAtUtc);
            if (_isExtraTurn)
                _pendingExtraTurnPlayers.Clear();
        }

        private void OnSideTurnEnding(SideTurnEndingEvent evt)
        {
            AddTimeline(CombatTimelineKind.Phase, TimelineEventPhase.Started, "turn.ending", Side(evt.Side).ToString(),
                occurredAtUtc: evt.OccurredAtUtc,
                details: Details(("participants",
                    (evt.Participants?.Count ?? 0).ToString(CultureInfo.InvariantCulture))));
        }

        private void OnSideTurnEnded(SideTurnEndedEvent evt)
        {
            AddTimeline(CombatTimelineKind.Turn, TimelineEventPhase.Completed, "turn.end", Side(evt.Side).ToString(),
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: _activeTurnEventId, bypassScope: true,
                details: Details(("extra_turn", _isExtraTurn.ToString())));
            Main.Logger.Debug(
                $"Turn {_turnIndex} ended: side={Side(evt.Side)}, round={_currentState?.RoundNumber ?? 0}, " +
                $"extra={_isExtraTurn}.");
            RestoreScopes();
            _activeTurnEventId = null;
            _currentSide = TimelineTurnSide.None;
            _isExtraTurn = false;
        }

        private void OnExtraTurnTaken(ExtraTurnTakenEvent evt)
        {
            _pendingExtraTurnPlayers.Add(evt.Player.NetId);
            Main.Logger.Info(
                $"Extra player turn granted in combat '{LogId(_activeCombat?.CombatId ?? "unknown")}' " +
                $"for player net id {evt.Player.NetId}.");
            AddTimeline(CombatTimelineKind.Phase, TimelineEventPhase.Instant, "turn.extra_granted",
                GameDescriptorFactory.Player(evt.Player).DisplayName,
                GameDescriptorFactory.Player(evt.Player), occurredAtUtc: evt.OccurredAtUtc,
                details: Details(("player_net_id", evt.Player.NetId.ToString(CultureInfo.InvariantCulture))));
        }

        private void OnHandDrawing(HandDrawingEvent evt)
        {
            AddTimeline(CombatTimelineKind.HandDraw, TimelineEventPhase.Started, "hand.draw",
                GameDescriptorFactory.Player(evt.Player).DisplayName, GameDescriptorFactory.Player(evt.Player),
                occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnCardDrawn(CardDrawnEvent evt)
        {
            var card = GameDescriptorFactory.Card(evt.Card);
            AddTimeline(CombatTimelineKind.CardDraw, TimelineEventPhase.Instant, "card.draw", evt.Card.Title,
                GameDescriptorFactory.Player(evt.Card.Owner), source: card, occurredAtUtc: evt.OccurredAtUtc,
                details: Details(("from_hand_draw", evt.FromHandDraw.ToString())));
        }

        private void OnCardPlaying(CardPlayingEvent evt)
        {
            var play = evt.CardPlay;
            var card = GameDescriptorFactory.Card(play.Card);
            var parent = CausalScopeRuntime.ResolveParentEventId() ?? _activeTurnEventId;
            var started = AddTimeline(CombatTimelineKind.CardPlay, TimelineEventPhase.Started, "card.play",
                play.Card.Title,
                GameDescriptorFactory.Player(play.Card.Owner), GameDescriptorFactory.CreatureOrNull(play.Target),
                card, occurredAtUtc: evt.OccurredAtUtc, parentEventId: parent, bypassScope: true,
                details: Details(
                    ("auto_play", play.IsAutoPlay.ToString()),
                    ("play_index", play.PlayIndex.ToString(CultureInfo.InvariantCulture)),
                    ("play_count", play.PlayCount.ToString(CultureInfo.InvariantCulture)),
                    ("result_pile", play.ResultPile.ToString())));
            if (started == null)
                return;
            if (_cardScopes.Remove(play, out var staleScope))
                CausalScopeRuntime.Restore(staleScope.State);
            var state = CausalScopeRuntime.EnterExplicit(started.EventId, play.Card, card, "card.play");
            _cardScopes[play] = new(started.EventId, state);
        }

        private void OnCardPlayed(CardPlayedEvent evt)
        {
            if (!_cardScopes.Remove(evt.CardPlay, out var scope))
                return;
            AddTimeline(CombatTimelineKind.CardPlay, TimelineEventPhase.Completed, "card.play", evt.CardPlay.Card.Title,
                GameDescriptorFactory.Player(evt.CardPlay.Card.Owner),
                GameDescriptorFactory.CreatureOrNull(evt.CardPlay.Target),
                GameDescriptorFactory.Card(evt.CardPlay.Card),
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: scope.EventId, bypassScope: true);
            CausalScopeRuntime.Restore(scope.State);
        }

        private void OnCardMoved(CardMovedBetweenPilesEvent evt)
        {
            var source = evt.Source == null
                ? GameDescriptorFactory.Card(evt.Card)
                : GameDescriptorFactory.Model(evt.Source);
            AddTimeline(CombatTimelineKind.CardMove, TimelineEventPhase.Instant, "card.move", evt.Card.Title,
                GameDescriptorFactory.Player(evt.Card.Owner), source: source, occurredAtUtc: evt.OccurredAtUtc,
                details: Details(("previous_pile", evt.PreviousPile.ToString()),
                    ("current_pile", Safe(() => evt.Card.Pile?.Type.ToString() ?? string.Empty, string.Empty))));
        }

        private void OnCardDiscarded(CardDiscardedEvent evt)
        {
            AddTimeline(CombatTimelineKind.CardMove, TimelineEventPhase.Instant, "card.discard", evt.Card.Title,
                GameDescriptorFactory.Player(evt.Card.Owner), source: GameDescriptorFactory.Card(evt.Card),
                occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnCardExhausted(CardExhaustedEvent evt)
        {
            AddTimeline(CombatTimelineKind.CardMove, TimelineEventPhase.Instant, "card.exhaust", evt.Card.Title,
                GameDescriptorFactory.Player(evt.Card.Owner), source: GameDescriptorFactory.Card(evt.Card),
                occurredAtUtc: evt.OccurredAtUtc,
                details: Details(("ethereal", evt.CausedByEthereal.ToString())));
        }

        private void OnAttackStarting(AttackStartingEvent evt)
        {
            var source = evt.Attack.ModelSource == null
                ? GameDescriptorFactory.CreatureSource(evt.Attack.Attacker)
                : GameDescriptorFactory.Model(evt.Attack.ModelSource);
            var parent = CausalScopeRuntime.ResolveParentEventId() ?? _activeTurnEventId;
            var started = AddTimeline(CombatTimelineKind.Attack, TimelineEventPhase.Started, "attack.start",
                source.DisplayName,
                GameDescriptorFactory.CreatureOrNull(evt.Attack.Attacker), source: source,
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: parent, bypassScope: true,
                details: Details(("value_props", evt.Attack.DamageProps.ToString()),
                    ("single_target", evt.Attack.IsSingleTargeted.ToString()),
                    ("multi_target", evt.Attack.IsMultiTargeted.ToString()),
                    ("random_target", evt.Attack.IsRandomlyTargeted.ToString())));
            if (started == null)
                return;
            if (_attackScopes.Remove(evt.Attack, out var staleScope))
                CausalScopeRuntime.Restore(staleScope.State);
            var state = CausalScopeRuntime.EnterExplicit(started.EventId, evt.Attack.ModelSource, source, "attack");
            _attackScopes[evt.Attack] = new(started.EventId, state);
        }

        private void OnAttackEnded(AttackEndedEvent evt)
        {
            if (!_attackScopes.Remove(evt.Attack, out var scope))
                return;
            var source = evt.Attack.ModelSource == null
                ? GameDescriptorFactory.CreatureSource(evt.Attack.Attacker)
                : GameDescriptorFactory.Model(evt.Attack.ModelSource);
            var results = evt.Attack.Results.SelectMany(result => result).ToArray();
            AddTimeline(CombatTimelineKind.Attack, TimelineEventPhase.Completed, "attack.end", source.DisplayName,
                GameDescriptorFactory.CreatureOrNull(evt.Attack.Attacker), source: source,
                value: results.Sum(result => result.UnblockedDamage + result.BlockedDamage),
                occurredAtUtc: evt.OccurredAtUtc,
                parentEventId: scope.EventId, bypassScope: true,
                details: Details(("hits", results.Length.ToString(CultureInfo.InvariantCulture))));
            CausalScopeRuntime.Restore(scope.State);
        }

        private void OnEffectMaterialized(EffectScopeCapture effect)
        {
            AddTimeline(CombatTimelineKind.Effect, TimelineEventPhase.Instant, effect.HookName,
                effect.Source.DisplayName, GameDescriptorFactory.ModelOwner(effect.Model), source: effect.Source,
                occurredAtUtc: effect.OccurredAtUtc, eventId: effect.EventId, parentEventId: effect.ParentEventId,
                bypassScope: true, details: Details(("hook", effect.HookName),
                    ("model_type", effect.Model.GetType().FullName ?? effect.Model.GetType().Name)));
        }

        private void AttachHistory(CombatHistory history)
        {
            DetachHistory();
            _history = history;
            _processedHistoryEntries = 0;
            _history.Changed += ProcessHistoryChanges;
        }

        private void DetachHistory()
        {
            if (_history != null)
                _history.Changed -= ProcessHistoryChanges;
            _history = null;
            _processedHistoryEntries = 0;
        }

        private void ProcessHistoryChanges()
        {
            var history = _history;
            var combat = _activeCombat;
            if (history == null || combat == null)
                return;
            try
            {
                var entries = history.Entries as IReadOnlyList<CombatHistoryEntry> ?? history.Entries.ToArray();
                if (_processedHistoryEntries > entries.Count)
                    _processedHistoryEntries = 0;
                while (_processedHistoryEntries < entries.Count)
                    ProcessEntry(entries[_processedHistoryEntries++], combat);
            }
            catch (Exception exception)
            {
                _historyProcessingFailures++;
                if (_historyProcessingFailures == 1)
                    Main.Logger.Error(
                        $"Failed to process a combat history entry; further failures in this combat are suppressed: {exception}");
            }
        }

        private void ProcessEntry(CombatHistoryEntry entry, MutableCombatSession combat)
        {
            combat.UpdateRoundCount(_currentState?.RoundNumber ?? 0);
            switch (entry)
            {
                case CardPlayStartedEntry started:
                    _activeCard = started.CardPlay.Card;
                    break;
                case CardPlayFinishedEntry finished:
                    RecordCardAction(finished.CardPlay.Card, MetricIds.CardsPlayed);
                    if (ReferenceEquals(_activeCard, finished.CardPlay.Card))
                        _activeCard = null;
                    break;
                case DamageReceivedEntry damage:
                    RecordDamage(damage);
                    break;
                case BlockGainedEntry block:
                    RecordBlock(block);
                    break;
                case CardDrawnEntry drawn:
                    RecordCardAction(drawn.Card, MetricIds.CardsDrawn);
                    break;
                case CardDiscardedEntry discarded:
                    RecordCardAction(discarded.Card, MetricIds.CardsDiscarded);
                    break;
                case CardExhaustedEntry exhausted:
                    RecordCardAction(exhausted.Card, MetricIds.CardsExhausted);
                    break;
                case PotionUsedEntry potion:
                    RecordForCreature(potion.Actor, MetricIds.PotionsUsed, 1m,
                        GameDescriptorFactory.Potion(potion.Potion),
                        GameDescriptorFactory.CreatureOrNull(potion.Target));
                    break;
                case PowerReceivedEntry power:
                    RecordPower(power);
                    break;
                case OrbChanneledEntry orb:
                    RecordForCreature(orb.Actor, MetricIds.OrbsChanneled, 1m, GameDescriptorFactory.Orb(orb.Orb));
                    AddTimeline(CombatTimelineKind.Orb, TimelineEventPhase.Instant, "orb.channel", orb.Orb.Id.Entry,
                        GameDescriptorFactory.Creature(orb.Actor), source: GameDescriptorFactory.Orb(orb.Orb),
                        value: 1m);
                    break;
                case StarsModifiedEntry { Amount: > 0 } stars:
                    RecordForCreature(stars.Actor, MetricIds.StarsGained, stars.Amount,
                        GameDescriptorFactory.Environment());
                    break;
                case StarsModifiedEntry { Amount: < 0 } stars:
                    RecordForCreature(stars.Actor, MetricIds.StarsSpent, -stars.Amount,
                        GameDescriptorFactory.Environment());
                    break;
                case SummonedEntry summoned:
                    RecordForCreature(summoned.Actor, MetricIds.SummonsCreated, summoned.Amount,
                        GameDescriptorFactory.Environment());
                    AddTimeline(CombatTimelineKind.Summon, TimelineEventPhase.Instant, "summon", string.Empty,
                        GameDescriptorFactory.Creature(summoned.Actor), value: summoned.Amount);
                    break;
            }
        }

        private void RecordDamage(DamageReceivedEntry damage)
        {
            DamageCaptureHub.TryConsume(damage.Result, damage.Receiver, damage.Dealer, damage.CardSource,
                damage.Result.Props, out _, out var calculation);
            RecordDamage(damage.Result, damage.Receiver, damage.Dealer, damage.CardSource, calculation, null);
        }

        private void OnDamageRequestCompleted(
            DamageRequestCapture request,
            IReadOnlyList<DamageResult> unrecordedResults)
        {
            if (!_captureActive || _activeCombat == null)
                return;
            Main.Logger.Debug($"Recovered {unrecordedResults.Count} damage result(s) omitted from combat history.");
            foreach (var result in unrecordedResults)
            {
                DamageCaptureHub.TryConsume(request, result.Receiver, request.Dealer, request.CardSource,
                    result.Props, out var calculation);
                RecordDamage(result, result.Receiver, request.Dealer, request.CardSource, calculation, request.Cause);
            }
        }

        private void RecordDamage(
            DamageResult result,
            Creature damageReceiver,
            Creature? damageDealer,
            CardModel? damageCardSource,
            DamageCalculationCapture? calculation,
            CausalScopeSnapshot? requestCause)
        {
            var cause = calculation?.Cause ?? requestCause ?? CausalScopeRuntime.Snapshot();
            var source = cause?.Source ?? (damageCardSource != null
                ? GameDescriptorFactory.Card(damageCardSource)
                : GameDescriptorFactory.CreatureSource(damageDealer));
            var directDealer = GameDescriptorFactory.Player(damageDealer);
            var dealerEntity = GameDescriptorFactory.CreatureOrNull(damageDealer);

            var receiver = GameDescriptorFactory.Creature(damageReceiver);
            var hpLost = (decimal)result.UnblockedDamage;
            var blocked = (decimal)result.BlockedDamage;
            var overkill = (decimal)result.OverkillDamage;
            var damageDealt = hpLost + blocked;
            var modified = calculation?.ModifiedAmount ?? hpLost + blocked + overkill;
            var requested = calculation?.RequestedAmount ?? modified;
            var contributions = BuildContributions(calculation, source, requested, modified, blocked, damageDealt,
                overkill);
            var attributionShares = ResolveAttributionShares(damageReceiver, cause?.Model, result.Props,
                damageDealt, directDealer, source);
            var effectiveHpShares = ScaleShares(attributionShares, hpLost);
            if (directDealer == null && attributionShares.Length > 0)
                source = attributionShares[0].Source;
            var breakdown = new DamageBreakdown(requested, modified, blocked, hpLost, overkill, damageDealt,
                result.Props.ToString(), contributions, attributionShares);
            var tags = WithActorTags(Tags(
                ("value_props", result.Props.ToString()),
                ("was_kill", result.WasTargetKilled.ToString()),
                ("fully_blocked", result.WasFullyBlocked.ToString()),
                ("attribution",
                    calculation != null
                        ? nameof(AttributionConfidence.Exact)
                        : nameof(AttributionConfidence.Heuristic))), damageDealer);

            var receivingPlayer = GameDescriptorFactory.Player(damageReceiver);
            var receivingPlayerBody = GameDescriptorFactory.PlayerBody(damageReceiver);
            if (receivingPlayer == null)
            {
                foreach (var share in AggregateShares(attributionShares)
                             .Where(share => share.EffectiveContribution > 0m))
                    Record(share.Contributor, MetricIds.DamageDealt, share.EffectiveContribution, share.Source,
                        receiver,
                        tags);
                foreach (var share in AggregateShares(effectiveHpShares)
                             .Where(share => share.EffectiveContribution > 0m))
                    Record(share.Contributor, MetricIds.EffectiveHpDamageDealt, share.EffectiveContribution,
                        share.Source,
                        receiver, tags);
                RecordOffensiveContributions(damageReceiver, calculation, contributions, attributionShares, damageDealt,
                    receiver, tags, MetricIds.DamageContribution);
                RecordOffensiveContributions(damageReceiver, calculation, contributions, effectiveHpShares, hpLost,
                    receiver, tags, MetricIds.EffectiveHpDamageContribution);
            }

            if (receivingPlayerBody != null && hpLost > 0)
                Record(receivingPlayerBody, MetricIds.DamageTaken, hpLost, source,
                    GameDescriptorFactory.CreatureOrNull(damageDealer), tags);
            else if (receivingPlayer != null && receiver.Kind == AnalyticsEntityKind.Summon && hpLost > 0)
                Record(receivingPlayer, MetricIds.SummonDamageTaken, hpLost, source,
                    GameDescriptorFactory.CreatureOrNull(damageDealer), WithActorTags(tags, damageReceiver));
            if (receivingPlayer != null && blocked > 0)
            {
                if (receivingPlayerBody != null)
                    Record(receivingPlayerBody, MetricIds.DamageBlocked, blocked, source,
                        GameDescriptorFactory.CreatureOrNull(damageDealer), tags);
                RecordBlockedContribution(damageReceiver, receivingPlayer, blocked,
                    GameDescriptorFactory.CreatureOrNull(damageDealer), tags);
            }

            if (receivingPlayer == null && overkill > 0m && attributionShares.Length > 0)
                foreach (var share in AggregateShares(ScaleShares(attributionShares, overkill)))
                    Record(share.Contributor, MetricIds.Overkill, share.EffectiveContribution, share.Source, receiver,
                        tags);

            var damageEvent = AddTimeline(CombatTimelineKind.Damage, TimelineEventPhase.Instant, "damage",
                source.DisplayName,
                dealerEntity ?? (attributionShares.Length > 0 ? attributionShares[0].Contributor : null),
                receiver, source,
                damageDealt, damage: breakdown, parentEventId: cause?.EventId,
                details: Details(("value_props", result.Props.ToString()),
                    ("killed", result.WasTargetKilled.ToString()),
                    ("fully_blocked", result.WasFullyBlocked.ToString()),
                    ("attribution", calculation != null ? "exact" : "heuristic"),
                    ("contributors", attributionShares.Length.ToString(CultureInfo.InvariantCulture)),
                    ("origin_event_ids", string.Join(';', attributionShares.Select(share => share.OriginEventId)
                        .Where(origin => !string.IsNullOrEmpty(origin)).Distinct(StringComparer.Ordinal)))));
            if (damageEvent == null)
                return;

            foreach (var contribution in contributions.Where(item => item.Stage != DamageContributionStage.Base))
            {
                var role = DamageContributionSemantics.GetRole(contribution);
                var settlementKind = DamageContributionSemantics.GetSettlementKind(contribution);
                AddTimeline(role == DamageContributionRole.Settlement
                        ? CombatTimelineKind.DamageSettlement
                        : CombatTimelineKind.DamageModifier,
                    TimelineEventPhase.Instant,
                    role == DamageContributionRole.Settlement ? "damage.settlement" : "damage.modifier",
                    contribution.Source.DisplayName, target: receiver, source: contribution.Source,
                    value: contribution.EffectiveContribution, parentEventId: damageEvent.EventId, bypassScope: true,
                    details: Details(("stage", contribution.Stage.ToString()),
                        ("role", role.ToString()),
                        ("settlement_kind", settlementKind.ToString()),
                        ("input", contribution.InputValue.ToString(CultureInfo.InvariantCulture)),
                        ("output", contribution.OutputValue.ToString(CultureInfo.InvariantCulture)),
                        ("raw_contribution", contribution.RawContribution.ToString(CultureInfo.InvariantCulture)),
                        ("effective_contribution",
                            contribution.EffectiveContribution.ToString(CultureInfo.InvariantCulture)),
                        ("factor", contribution.Factor?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                        ("confidence", contribution.Confidence.ToString()),
                        ("note", contribution.Note)));
                if (calculation != null && role == DamageContributionRole.Modifier)
                    RecordModifierMetric(damageReceiver, calculation, contribution, receiver, tags);
            }
        }

        private void RecordOffensiveContributions(
            Creature receiver,
            DamageCalculationCapture? calculation,
            IReadOnlyList<DamageContribution> contributions,
            IReadOnlyList<DamageAttributionShare> directShares,
            decimal realizedDamage,
            EntityDescriptor target,
            IReadOnlyDictionary<string, string> tags,
            string metricId)
        {
            if (realizedDamage <= 0m || GameDescriptorFactory.Player(receiver) != null)
                return;
            var positive = contributions.Where(contribution => contribution.EffectiveContribution > 0m &&
                                                               DamageContributionSemantics.GetRole(contribution) !=
                                                               DamageContributionRole.Settlement)
                .ToArray();
            var grossContribution = positive.Sum(contribution => contribution.EffectiveContribution);
            if (grossContribution <= 0m)
                return;

            foreach (var contribution in positive)
            {
                var realized = realizedDamage * contribution.EffectiveContribution / grossContribution;
                var shares = contribution.Stage is DamageContributionStage.Base or DamageContributionStage.Execution
                    ? ScaleShares(directShares, realized)
                    : ResolveModifierShares(receiver, calculation, contribution, realized);
                if (shares.Length == 0)
                    shares = ScaleShares(directShares, realized);
                var component = contribution.Stage switch
                {
                    DamageContributionStage.Execution => ContributionComponentIds.Execution,
                    DamageContributionStage.Base => ContributionComponentIds.BaseDamage,
                    _ => ContributionComponentIds.DamageAmplification,
                };
                foreach (var share in AggregateShares(shares))
                    Record(share.Contributor, metricId, share.EffectiveContribution,
                        share.Source, target,
                        ContributionTags(tags, component, share.Confidence));
            }
        }

        private static ReadOnlyCollection<DamageContribution> BuildContributions(
            DamageCalculationCapture? calculation,
            SourceDescriptor source,
            decimal requested,
            decimal modified,
            decimal blocked,
            decimal effectiveDamage,
            decimal overkill)
        {
            var hpLost = effectiveDamage - blocked;
            var appliedHpDamage = hpLost + overkill;
            var confidence = calculation != null ? AttributionConfidence.Exact : AttributionConfidence.Heuristic;
            var contributions = new List<DamageContribution>
            {
                new(source, DamageContributionStage.Base, 0m, requested, requested, requested, null,
                    confidence),
            };
            if (calculation == null)
            {
                if (blocked > 0m)
                    contributions.Add(new(GameDescriptorFactory.BlockResolution(),
                        DamageContributionStage.Block,
                        modified, modified - blocked, -blocked, -blocked, null, AttributionConfidence.Exact));
                if (overkill > 0m)
                    contributions.Add(new(GameDescriptorFactory.OverkillResolution(),
                        DamageContributionStage.Overkill,
                        appliedHpDamage, hpLost, -overkill, -overkill, null, AttributionConfidence.Exact));
                return FinalizeContributions(contributions, effectiveDamage, hpLost);
            }

            var capturedOutput = requested;
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var modifier in calculation.Modifiers)
            {
                if (modifier.Stage == DamageContributionStage.HpLoss)
                    continue;
                contributions.Add(new(modifier.Source, modifier.Stage, modifier.InputValue,
                    modifier.OutputValue,
                    modifier.RawContribution, modifier.RawContribution, modifier.Factor,
                    AttributionConfidence.Exact, modifier.Context));
                capturedOutput = modifier.OutputValue;
            }

            var residual = modified - capturedOutput;
            if (residual != 0m)
            {
                var isLowerBound = capturedOutput < 0m && modified == 0m;
                contributions.Add(new(isLowerBound
                        ? GameDescriptorFactory.DamageFloor()
                        : GameDescriptorFactory.Unknown(),
                    isLowerBound ? DamageContributionStage.Clamp : DamageContributionStage.Additive,
                    capturedOutput, modified, residual, residual, null, AttributionConfidence.Derived,
                    isLowerBound ? "Host damage lower bound" : "Unexposed host damage modifier"));
            }

            if (blocked > 0m)
                contributions.Add(new(GameDescriptorFactory.BlockResolution(), DamageContributionStage.Block,
                    modified, modified - blocked, -blocked, -blocked, null, AttributionConfidence.Exact));
            if (calculation.HpLossPasses.Count == 0)
                foreach (var modifier in calculation.Modifiers.Where(item =>
                             item.Stage == DamageContributionStage.HpLoss))
                    AddHpLossModifier(modifier);
            else
                foreach (var hpLossPass in calculation.HpLossPasses)
                {
                    var passModifiers = calculation.Modifiers
                        .Skip(hpLossPass.ModifierStartIndex)
                        .Take(hpLossPass.ModifierEndIndex - hpLossPass.ModifierStartIndex)
                        .Where(item => item.Stage == DamageContributionStage.HpLoss)
                        .ToArray();
                    foreach (var modifier in passModifiers)
                        AddHpLossModifier(modifier);
                    var capturedHpLossOutput = passModifiers.LastOrDefault()?.OutputValue ?? hpLossPass.InputValue;
                    var hpLossResidual = hpLossPass.OutputValue - capturedHpLossOutput;
                    if (hpLossResidual != 0m)
                        contributions.Add(new(GameDescriptorFactory.Unknown(), DamageContributionStage.HpLoss,
                            capturedHpLossOutput, hpLossPass.OutputValue, hpLossResidual, hpLossResidual, null,
                            AttributionConfidence.Derived, "Unexposed host HP-loss modifier"));
                }

            var finalHpLossPass = calculation.HpLossPasses.LastOrDefault();
            var hpLossOutput = finalHpLossPass?.OutputValue ?? calculation.FinalHpLossAmount ??
                Math.Max(modified - blocked, 0m);
            if (hpLossOutput != appliedHpDamage)
                contributions.Add(new(GameDescriptorFactory.DamageQuantization(),
                    DamageContributionStage.Quantization, hpLossOutput, appliedHpDamage,
                    appliedHpDamage - hpLossOutput, appliedHpDamage - hpLossOutput, null,
                    AttributionConfidence.Exact, "Host integer HP settlement"));
            if (overkill > 0m)
                contributions.Add(new(GameDescriptorFactory.OverkillResolution(),
                    DamageContributionStage.Overkill,
                    appliedHpDamage, hpLost, -overkill, -overkill, null, AttributionConfidence.Exact));
            return FinalizeContributions(contributions, effectiveDamage, hpLost);

            void AddHpLossModifier(ModifierCapture modifier)
            {
                contributions.Add(new(modifier.Source, modifier.Stage, modifier.InputValue,
                    modifier.OutputValue, modifier.RawContribution, modifier.RawContribution, modifier.Factor,
                    AttributionConfidence.Exact, modifier.Context));
            }
        }

        private static ReadOnlyCollection<DamageContribution> FinalizeContributions(
            List<DamageContribution> contributions,
            decimal effectiveDamage,
            decimal hpLost)
        {
            var result = AllocateEffectiveContributions(contributions, effectiveDamage);
            var settledHp = result.Sum(contribution => contribution.RawContribution);
            if (Math.Abs(settledHp - hpLost) > 0.0001m)
                Main.Logger.Warn(
                    $"Damage settlement invariant mismatch: waterfall={settledHp.ToString(CultureInfo.InvariantCulture)}, " +
                    $"hp_lost={hpLost.ToString(CultureInfo.InvariantCulture)}, " +
                    $"effective={effectiveDamage.ToString(CultureInfo.InvariantCulture)}.");
            return result;
        }

        private static ReadOnlyCollection<DamageContribution> AllocateEffectiveContributions(
            List<DamageContribution> contributions,
            decimal effectiveDamage)
        {
            var positiveTotal = contributions
                .Where(contribution => contribution.RawContribution > 0m &&
                                       DamageContributionSemantics.GetRole(contribution) !=
                                       DamageContributionRole.Settlement)
                .Sum(contribution => contribution.RawContribution);
            var scale = positiveTotal == 0m ? 0m : effectiveDamage / positiveTotal;
            for (var index = 0; index < contributions.Count; index++)
            {
                var contribution = contributions[index];
                if (contribution.RawContribution <= 0m ||
                    DamageContributionSemantics.GetRole(contribution) == DamageContributionRole.Settlement)
                    continue;
                contributions[index] = contribution with
                {
                    EffectiveContribution = contribution.RawContribution * scale,
                };
            }

            return contributions.AsReadOnly();
        }

        private void RecordModifierMetric(
            Creature receiver,
            DamageCalculationCapture calculation,
            DamageContribution contribution,
            EntityDescriptor target,
            IReadOnlyDictionary<string, string> tags)
        {
            if (contribution.RawContribution == 0m ||
                DamageContributionSemantics.GetRole(contribution) == DamageContributionRole.Settlement)
                return;
            var model = calculation.Modifiers.FirstOrDefault(candidate =>
                candidate.Source.Key == contribution.Source.Key && candidate.Stage == contribution.Stage)?.Model;
            var isAmplification = contribution.RawContribution > 0m;
            var metricId = isAmplification
                ? MetricIds.DamageAmplified
                : MetricIds.DamageMitigated;
            var owner = GameDescriptorFactory.ModelOwner(model);
            var attributedAmount = isAmplification
                ? Math.Max(contribution.EffectiveContribution, 0m)
                : Math.Abs(contribution.RawContribution);
            if (attributedAmount <= 0m)
                return;
            var shares = ResolveAttributionShares(receiver, model, calculation.Props, attributedAmount,
                model is PowerModel ? null : owner?.Kind == AnalyticsEntityKind.Player ? owner : null,
                contribution.Source);
            foreach (var share in AggregateShares(shares))
                Record(share.Contributor, metricId, share.EffectiveContribution, contribution.Source, target, tags);
            if (isAmplification)
                return;
            var receivingPlayer = GameDescriptorFactory.Player(receiver);
            if (receivingPlayer == null)
                return;
            if (shares.Length == 0)
                shares =
                [
                    new(receivingPlayer, contribution.Source, 1m, attributedAmount,
                        AttributionConfidence.Heuristic),
                ];
            foreach (var share in AggregateShares(shares))
            {
                var contributionTags = ContributionTags(tags, ContributionComponentIds.DamageMitigation,
                    share.Confidence);
                Record(share.Contributor, MetricIds.DamagePrevented, share.EffectiveContribution,
                    contribution.Source, target, contributionTags);
                Record(share.Contributor, MetricIds.DefenseContribution, share.EffectiveContribution,
                    contribution.Source, target, contributionTags);
            }
        }

        private void RecordBlock(BlockGainedEntry block)
        {
            var cause = CausalScopeRuntime.Snapshot();
            var source = block.CardPlay == null
                ? cause?.Source ?? GameDescriptorFactory.Unknown()
                : GameDescriptorFactory.Card(block.CardPlay.Card);
            RecordForCreature(block.Receiver, MetricIds.BlockGained, block.Amount, source, null,
                Tags(("value_props", block.Props.ToString())));
            var receiver = GameDescriptorFactory.Creature(block.Receiver);
            var cardProvider = block.CardPlay is { } cardPlay
                ? GameDescriptorFactory.Player(cardPlay.Card.Owner)
                : null;
            var shares = ResolveAttributionShares(block.Receiver, cause?.Model, ValueProp.Unpowered, block.Amount,
                cardProvider, source);
            var receivingPlayer = GameDescriptorFactory.Player(block.Receiver);
            if (shares.Length == 0 && receivingPlayer != null)
                shares =
                [
                    new(receivingPlayer, source, 1m, block.Amount, AttributionConfidence.Heuristic),
                ];
            if (shares.Length > 0)
            {
                if (!_blockCredits.TryGetValue(block.Receiver, out var credits))
                {
                    credits = new();
                    _blockCredits.Add(block.Receiver, credits);
                }

                credits.Synchronize(Math.Max(0m, block.Receiver.Block - block.Amount),
                    GameDescriptorFactory.Player(block.Receiver), GameDescriptorFactory.Unknown());
                foreach (var share in shares)
                    credits.Add(share.Contributor, share.Source, share.EffectiveContribution, share.Confidence,
                        share.OriginEventId ?? cause?.EventId);
            }

            AddTimeline(CombatTimelineKind.Block, TimelineEventPhase.Instant, "block.gain", source.DisplayName,
                shares.FirstOrDefault()?.Contributor ?? receiver, receiver,
                source, block.Amount,
                Details(("value_props", block.Props.ToString())));
        }

        private void RecordBlockedContribution(
            Creature receiver,
            EntityDescriptor receivingPlayer,
            decimal blocked,
            EntityDescriptor? attacker,
            IReadOnlyDictionary<string, string> tags)
        {
            if (!_blockCredits.TryGetValue(receiver, out var credits))
            {
                credits = new();
                _blockCredits.Add(receiver, credits);
            }

            credits.Synchronize(receiver.Block + blocked, receivingPlayer, GameDescriptorFactory.Unknown());
            foreach (var share in credits.Consume(blocked))
            {
                var contributionTags = ContributionTags(tags, ContributionComponentIds.Block, share.Confidence);
                Record(share.Contributor, MetricIds.DamagePrevented, share.EffectiveContribution, share.Source,
                    attacker, contributionTags);
                Record(share.Contributor, MetricIds.DefenseContribution, share.EffectiveContribution, share.Source,
                    attacker, contributionTags);
            }
        }

        private void RecordPower(PowerReceivedEntry entry)
        {
            var source = GameDescriptorFactory.Power(entry.Power);
            if (!_powerCredits.TryGetValue(entry.Power, out var attribution))
            {
                attribution = new(source);
                _powerCredits.Add(entry.Power, attribution);
            }

            var player = GameDescriptorFactory.Player(entry.Applier);
            var cause = CausalScopeRuntime.Snapshot();
            var causalActor = GameDescriptorFactory.ModelOwner(cause?.OriginModel) ??
                              GameDescriptorFactory.ModelOwner(cause?.Model);
            var causalSource = cause?.OriginSource ?? cause?.Source;
            switch (entry.Amount)
            {
                case > 0m when player != null:
                    attribution.Add(player, entry.Amount, cause?.EventId);
                    break;
                case < 0m:
                    attribution.Remove(-entry.Amount);
                    break;
            }

            AddTimeline(CombatTimelineKind.Power, TimelineEventPhase.Instant, "power.change", source.DisplayName,
                player ?? GameDescriptorFactory.CreatureOrNull(entry.Applier) ?? causalActor,
                GameDescriptorFactory.Creature(entry.Actor),
                source, entry.Amount,
                Details(("power_type", entry.Power.GetTypeForAmount(entry.Amount).ToString()),
                    ("result_amount", (entry.Power.Amount + entry.Amount).ToString(CultureInfo.InvariantCulture)),
                    ("origin_event_id", cause?.OriginEventId ?? string.Empty),
                    ("cause_source_key", causalSource?.Key ?? string.Empty),
                    ("cause_source_name", causalSource?.DisplayName ?? string.Empty)));
            if (player == null || entry.Amount <= 0m)
                return;
            var tags = WithActorTags(null, entry.Applier);
            Record(player, MetricIds.PowersApplied, entry.Amount, source, GameDescriptorFactory.Creature(entry.Actor),
                tags);
            if (entry.Power.GetTypeForAmount(entry.Amount) == PowerType.Debuff)
                Record(player, MetricIds.DebuffsApplied, entry.Amount, source,
                    GameDescriptorFactory.Creature(entry.Actor), tags);
        }

        private DamageAttributionShare[] ResolveAttributionShares(
            Creature receiver,
            AbstractModel? model,
            ValueProp props,
            decimal effectiveAmount,
            EntityDescriptor? directPlayer,
            SourceDescriptor source)
        {
            if (effectiveAmount <= 0m)
                return [];
            if (model is PowerModel power && _powerCredits.TryGetValue(power, out var exact))
                return exact.Shares(effectiveAmount, AttributionConfidence.Exact);
            if (directPlayer?.Kind == AnalyticsEntityKind.Player)
                return [new(directPlayer, source, 1m, effectiveAmount, AttributionConfidence.Exact)];
            if (!props.HasFlag(ValueProp.Unpowered))
                return [];
            var candidates = receiver.Powers
                .Where(powerModel => _powerCredits.ContainsKey(powerModel))
                .ToArray();
            var poison = candidates.FirstOrDefault(powerModel =>
                powerModel.Id.Entry.Contains("POISON", StringComparison.OrdinalIgnoreCase));
            if (poison != null && props.HasFlag(ValueProp.Unblockable))
                return _powerCredits[poison].Shares(effectiveAmount, AttributionConfidence.Derived);
            return candidates.Length == 1
                ? _powerCredits[candidates[0]].Shares(effectiveAmount, AttributionConfidence.Heuristic)
                : [];
        }

        private DamageAttributionShare[] ResolveModifierShares(
            Creature receiver,
            DamageCalculationCapture? calculation,
            DamageContribution contribution,
            decimal effectiveAmount)
        {
            if (calculation == null)
                return [];
            var model = calculation.Modifiers.FirstOrDefault(candidate =>
                candidate.Source.Key == contribution.Source.Key && candidate.Stage == contribution.Stage)?.Model;
            var owner = GameDescriptorFactory.ModelOwner(model);
            return ResolveAttributionShares(receiver, model, calculation.Props, effectiveAmount,
                model is PowerModel ? null : owner?.Kind == AnalyticsEntityKind.Player ? owner : null,
                contribution.Source);
        }

        private static DamageAttributionShare[] ScaleShares(
            IReadOnlyList<DamageAttributionShare> shares,
            decimal effectiveAmount)
        {
            var total = shares.Sum(share => share.EffectiveContribution);
            if (total <= 0m)
                return [];
            return shares.Select(share => share with
            {
                EffectiveContribution = effectiveAmount * share.EffectiveContribution / total,
            }).ToArray();
        }

        private static DamageAttributionShare[] AggregateShares(
            IReadOnlyList<DamageAttributionShare> shares)
        {
            return shares
                .GroupBy(share => (share.Contributor.Key, share.Source.Key))
                .Select(group => group.First() with
                {
                    Weight = group.Sum(share => share.Weight),
                    EffectiveContribution = group.Sum(share => share.EffectiveContribution),
                    OriginEventId = null,
                })
                .ToArray();
        }

        private void OnCurrentHpChanged(CurrentHpChangedEvent evt)
        {
            if (evt.CombatState == null || evt.Delta == 0m)
                return;
            if (evt.Delta > 0m)
            {
                var cause = CausalScopeRuntime.Snapshot();
                var source = cause?.Source ?? (_activeCard == null
                    ? GameDescriptorFactory.Unknown()
                    : GameDescriptorFactory.Card(_activeCard));
                RecordForCreature(evt.Creature, MetricIds.HealingReceived, evt.Delta, source);
                var receiver = GameDescriptorFactory.Player(evt.Creature);
                var healingOwner = GameDescriptorFactory.ModelOwner(cause?.Model);
                var shares = receiver == null
                    ? []
                    : ResolveAttributionShares(evt.Creature, cause?.Model, ValueProp.Unpowered, evt.Delta,
                        cause?.Model is PowerModel ? null : healingOwner, source);
                if (receiver != null && shares.Length == 0)
                    shares = [new(receiver, source, 1m, evt.Delta, AttributionConfidence.Heuristic)];
                foreach (var share in AggregateShares(shares))
                {
                    var contributionTags = ContributionTags(null, ContributionComponentIds.Healing,
                        share.Confidence);
                    var healingTarget = GameDescriptorFactory.Creature(evt.Creature);
                    Record(share.Contributor, MetricIds.HealingContribution, share.EffectiveContribution,
                        share.Source, healingTarget, contributionTags);
                    Record(share.Contributor, MetricIds.DefenseContribution, share.EffectiveContribution,
                        share.Source, healingTarget, contributionTags);
                }

                AddTimeline(CombatTimelineKind.Healing, TimelineEventPhase.Instant, "healing", source.DisplayName,
                    shares.FirstOrDefault()?.Contributor ?? healingOwner,
                    GameDescriptorFactory.Creature(evt.Creature),
                    source, evt.Delta, occurredAtUtc: evt.OccurredAtUtc, parentEventId: cause?.EventId);
                return;
            }

            if (DamageCaptureHub.HasActiveRequest)
                return;

            var amount = -evt.Delta;
            var causeSnapshot = CausalScopeRuntime.Snapshot();
            var doomSource = ExecutionCaptureHub.DoomSource(evt.Creature);
            var attributionModel = doomSource ?? causeSnapshot?.Model;
            var sourceDescriptor = doomSource == null
                ? causeSnapshot?.Source ?? GameDescriptorFactory.Unknown()
                : GameDescriptorFactory.Power(doomSource);
            var owner = GameDescriptorFactory.ModelOwner(attributionModel);
            var attributionShares = ResolveAttributionShares(evt.Creature, attributionModel, ValueProp.Unpowered,
                amount, owner?.Kind == AnalyticsEntityKind.Player ? owner : null, sourceDescriptor);
            var isExecution = evt.Creature.CurrentHp <= 0m;
            if (isExecution && ExecutionCaptureHub.IsExplicitKill(evt.Creature) && doomSource == null &&
                !attributionShares.Any(IsReliableExecutionAttribution))
            {
                RecordAdministrativeDeathHpRemoval(evt, amount, causeSnapshot);
                return;
            }

            var actor = attributionShares.Length > 0 ? attributionShares[0].Contributor : owner;
            if (attributionShares.Length > 0)
                sourceDescriptor = attributionShares[0].Source;
            var target = GameDescriptorFactory.Creature(evt.Creature);
            var contribution = new DamageContribution(sourceDescriptor,
                isExecution ? DamageContributionStage.Execution : DamageContributionStage.Base,
                amount, 0m, amount, amount, null,
                doomSource != null || causeSnapshot != null
                    ? AttributionConfidence.Exact
                    : AttributionConfidence.Heuristic,
                doomSource != null
                    ? "Doom effective contribution equals HP removed at execution"
                    : isExecution
                        ? "Effective contribution equals HP removed by direct execution"
                        : "Direct HP loss");
            var breakdown = new DamageBreakdown(amount, amount, 0m, amount, 0m, amount,
                nameof(ValueProp.Unpowered), [contribution], attributionShares);
            AddTimeline(isExecution ? CombatTimelineKind.Execution : CombatTimelineKind.HpLoss,
                TimelineEventPhase.Instant, isExecution ? "execution" : "hp.loss", sourceDescriptor.DisplayName,
                actor, target, sourceDescriptor, amount, damage: breakdown,
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: causeSnapshot?.EventId,
                details: Details(("effective_contribution", amount.ToString(CultureInfo.InvariantCulture)),
                    ("doom_execution", (doomSource != null).ToString()),
                    ("attribution", contribution.Confidence.ToString())));
            var receivingPlayer = GameDescriptorFactory.Player(evt.Creature);
            if (receivingPlayer == null)
                foreach (var share in AggregateShares(attributionShares))
                {
                    Record(share.Contributor, MetricIds.DamageDealt, share.EffectiveContribution, share.Source, target,
                        Tags(("execution", isExecution.ToString()), ("direct_hp_loss", "True")));
                    Record(share.Contributor, MetricIds.DamageContribution, share.EffectiveContribution, share.Source,
                        target, ContributionTags(null,
                            isExecution ? ContributionComponentIds.Execution : ContributionComponentIds.BaseDamage,
                            share.Confidence));
                    Record(share.Contributor, MetricIds.EffectiveHpDamageDealt, share.EffectiveContribution,
                        share.Source,
                        target, Tags(("execution", isExecution.ToString()), ("direct_hp_loss", "True")));
                    Record(share.Contributor, MetricIds.EffectiveHpDamageContribution, share.EffectiveContribution,
                        share.Source, target, ContributionTags(null,
                            isExecution ? ContributionComponentIds.Execution : ContributionComponentIds.BaseDamage,
                            share.Confidence));
                }

            if (GameDescriptorFactory.PlayerBody(evt.Creature) is { } receivingPlayerBody)
                Record(receivingPlayerBody, MetricIds.DamageTaken, amount, sourceDescriptor, actor,
                    Tags(("execution", isExecution.ToString()), ("direct_hp_loss", "True")));
            else if (receivingPlayer != null && target.Kind == AnalyticsEntityKind.Summon)
                Record(receivingPlayer, MetricIds.SummonDamageTaken, amount, sourceDescriptor, actor,
                    WithActorTags(Tags(("execution", isExecution.ToString()), ("direct_hp_loss", "True")),
                        evt.Creature));
        }

        private static bool IsReliableExecutionAttribution(DamageAttributionShare share)
        {
            return share.Contributor.Kind == AnalyticsEntityKind.Player &&
                   share.Confidence is AttributionConfidence.Exact or AttributionConfidence.Derived;
        }

        private void RecordAdministrativeDeathHpRemoval(CurrentHpChangedEvent evt, decimal amount,
            CausalScopeSnapshot? cause)
        {
            var target = GameDescriptorFactory.Creature(evt.Creature);
            var source = cause?.Source ?? GameDescriptorFactory.CreatureSource(evt.Creature);
            var actor = GameDescriptorFactory.ModelOwner(cause?.Model) ?? target;
            AddTimeline(CombatTimelineKind.HpLoss, TimelineEventPhase.Instant, "hp.removed_on_death",
                target.DisplayName, actor, target, source, details: Details(
                    ("excluded_from_damage", "True"),
                    ("reason", "explicit_kill"),
                    ("remaining_hp_removed", amount.ToString(CultureInfo.InvariantCulture))),
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: cause?.EventId);
            Main.Logger.Debug(
                $"Excluded {amount.ToString(CultureInfo.InvariantCulture)} remaining HP removed by explicit death " +
                $"from damage attribution for '{target.ModelId}'.");
        }

        private void OnEnergyGained(EnergyGainedEvent evt)
        {
            AddTimeline(CombatTimelineKind.Energy, TimelineEventPhase.Instant, "energy.gain", string.Empty,
                GameDescriptorFactory.Player(evt.Gainer), value: evt.Amount, occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnEnergyReset(EnergyResetEvent evt)
        {
            AddTimeline(CombatTimelineKind.Energy, TimelineEventPhase.Instant, "energy.reset", string.Empty,
                GameDescriptorFactory.Player(evt.Player), occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnEnergySpent(EnergySpentEvent evt)
        {
            RecordForCreature(evt.Card.Owner.Creature, MetricIds.EnergySpent, evt.Amount,
                GameDescriptorFactory.Card(evt.Card));
            AddTimeline(CombatTimelineKind.Energy, TimelineEventPhase.Instant, "energy.spend", evt.Card.Title,
                GameDescriptorFactory.Player(evt.Card.Owner), source: GameDescriptorFactory.Card(evt.Card),
                value: evt.Amount, occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnPotionUsing(PotionUsingEvent evt)
        {
            var source = GameDescriptorFactory.Potion(evt.Potion);
            var started = AddTimeline(CombatTimelineKind.Potion, TimelineEventPhase.Started, "potion.use",
                source.DisplayName, GameDescriptorFactory.Player(evt.Potion.Owner),
                GameDescriptorFactory.CreatureOrNull(evt.Target), GameDescriptorFactory.Potion(evt.Potion),
                occurredAtUtc: evt.OccurredAtUtc);
            if (started == null)
                return;
            var state = CausalScopeRuntime.EnterExplicit(started.EventId, evt.Potion, source, "potion.use");
            if (!_potionScopes.TryGetValue(evt.Potion, out var scopes))
            {
                scopes = [];
                _potionScopes.Add(evt.Potion, scopes);
            }

            scopes.Push(new(started.EventId, state));
        }

        private void OnPotionUsed(PotionUsedEvent evt)
        {
            var scope = _potionScopes.TryGetValue(evt.Potion, out var scopes) && scopes.Count > 0
                ? scopes.Pop()
                : null;
            AddTimeline(CombatTimelineKind.Potion, TimelineEventPhase.Completed, "potion.use",
                GameDescriptorFactory.Potion(evt.Potion).DisplayName, GameDescriptorFactory.Player(evt.Potion.Owner),
                GameDescriptorFactory.CreatureOrNull(evt.Target), GameDescriptorFactory.Potion(evt.Potion),
                occurredAtUtc: evt.OccurredAtUtc, parentEventId: scope?.EventId, bypassScope: scope != null);
            if (scope != null)
                CausalScopeRuntime.Restore(scope.State);
            if (scopes is { Count: 0 })
                _potionScopes.Remove(evt.Potion);
        }

        private void OnShuffled(ShuffledEvent evt)
        {
            AddTimeline(CombatTimelineKind.Shuffle, TimelineEventPhase.Instant, "deck.shuffle", string.Empty,
                GameDescriptorFactory.Player(evt.Shuffler), occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnCreatureDying(CreatureDyingEvent evt)
        {
            AddTimeline(CombatTimelineKind.Death, TimelineEventPhase.Started, "death.start",
                GameDescriptorFactory.Creature(evt.Creature).DisplayName,
                target: GameDescriptorFactory.Creature(evt.Creature),
                occurredAtUtc: evt.OccurredAtUtc);
        }

        private void OnCreatureDied(CreatureDiedEvent evt)
        {
            var target = GameDescriptorFactory.Creature(evt.Creature);
            AddTimeline(CombatTimelineKind.Death, TimelineEventPhase.Completed, "death.end",
                target.DisplayName,
                target: target,
                occurredAtUtc: evt.OccurredAtUtc,
                details: Details(("removal_prevented", evt.WasRemovalPrevented.ToString()),
                    ("animation_seconds", evt.DeathAnimationDurationSeconds.ToString(CultureInfo.InvariantCulture))));
            var receivingPlayer = GameDescriptorFactory.Player(evt.Creature);
            switch (target.Kind)
            {
                case AnalyticsEntityKind.Player when receivingPlayer != null:
                    Record(receivingPlayer, MetricIds.Deaths, 1m, GameDescriptorFactory.Environment());
                    break;
                case AnalyticsEntityKind.Summon when receivingPlayer != null:
                    Record(receivingPlayer, MetricIds.SummonDeaths, 1m,
                        GameDescriptorFactory.CreatureSource(evt.Creature), tags: WithActorTags(null, evt.Creature));
                    break;
            }
        }

        private void RecordCardAction(CardModel card, string metricId)
        {
            RecordForCreature(card.Owner.Creature, metricId, 1m, GameDescriptorFactory.Card(card));
        }

        private void RecordForCreature(
            Creature creature,
            string metricId,
            decimal value,
            SourceDescriptor source,
            EntityDescriptor? target = null,
            IReadOnlyDictionary<string, string>? tags = null)
        {
            var player = GameDescriptorFactory.Player(creature);
            if (player != null)
                Record(player, metricId, value, source, target, WithActorTags(tags, creature));
        }

        private void Record(
            EntityDescriptor player,
            string metricId,
            decimal value,
            SourceDescriptor source,
            EntityDescriptor? target = null,
            IReadOnlyDictionary<string, string>? tags = null)
        {
            var run = _liveRun;
            var combat = _activeCombat;
            if (run == null || combat == null || value <= 0m || !collectors.IsRegisteredMetric(metricId))
                return;
            Record(new(
                Interlocked.Increment(ref _metricSequence),
                run.RunId,
                combat.CombatId,
                combat.ActIndex,
                combat.Floor,
                _currentState?.RoundNumber ?? combat.RoundCount,
                DateTimeOffset.UtcNow,
                metricId,
                value,
                player,
                target,
                source,
                tags ?? MetricObservation.EmptyTags));
        }

        private void Record(MetricObservation observation)
        {
            var combat = _activeCombat;
            if (combat == null)
                return;
            combat.Add(observation, Math.Clamp(ModData.Settings.EventLimitPerCombat, 500, 25000));
            collectors.Publish(observation);
        }

        private CombatTimelineEvent? AddTimeline(
            CombatTimelineKind kind,
            TimelineEventPhase phase,
            string actionId,
            string displayText,
            EntityDescriptor? actor = null,
            EntityDescriptor? target = null,
            SourceDescriptor? source = null,
            decimal? value = null,
            IReadOnlyDictionary<string, string>? details = null,
            DamageBreakdown? damage = null,
            DateTimeOffset? occurredAtUtc = null,
            string? eventId = null,
            string? parentEventId = null,
            bool bypassScope = false)
        {
            var run = _liveRun;
            var combat = _activeCombat;
            if (run == null || combat == null)
                return null;
            var resolvedParentEventId = parentEventId ??
                                        (bypassScope
                                            ? null
                                            : CausalScopeRuntime.ResolveParentEventId() ?? _activeTurnEventId);
            var sequence = Interlocked.Increment(ref _timelineSequence);
            var timelineEvent = new CombatTimelineEvent(
                sequence,
                eventId ?? $"timeline:{sequence}",
                resolvedParentEventId,
                run.RunId,
                combat.CombatId,
                occurredAtUtc ?? DateTimeOffset.UtcNow,
                _currentState?.RoundNumber ?? combat.RoundCount,
                _turnIndex,
                _currentSide,
                _isExtraTurn,
                kind,
                phase,
                actionId,
                displayText,
                actor,
                target,
                source,
                value,
                details ?? MetricObservation.EmptyTags,
                damage);
            combat.AddTimeline(timelineEvent, Math.Clamp(ModData.Settings.TimelineLimitPerCombat, 1000, 100000));
            collectors.PublishTimeline(timelineEvent);
            return timelineEvent;
        }

        private static void LogCompletedCombat(
            CombatSnapshot snapshot,
            CaptureBufferDiagnostics captureBuffers,
            int processedHistoryEntries,
            int historyProcessingFailures,
            SkippedOriginalDiagnostics skippedOriginals)
        {
            var timeline = snapshot.Timeline ?? [];
            var duration = (snapshot.EndedAtUtc.GetValueOrDefault(snapshot.StartedAtUtc) - snapshot.StartedAtUtc)
                .TotalSeconds;
            Main.Logger.Info(
                $"Completed analytics combat '{LogId(snapshot.CombatId)}': rounds={snapshot.RoundCount}, " +
                $"players={snapshot.Players.Count}, observations={snapshot.Events.Count}, timeline={timeline.Count}, " +
                $"duration_seconds={duration.ToString("0.0", CultureInfo.InvariantCulture)}.");

            if (captureBuffers is { DroppedObservations: > 0 } or { DroppedTimelineEvents: > 0 })
                Main.Logger.Warn(
                    $"Combat '{LogId(snapshot.CombatId)}' reached capture limits; dropped " +
                    $"{captureBuffers.DroppedObservations} observation(s) and " +
                    $"{captureBuffers.DroppedTimelineEvents} timeline event(s) from detailed history. " +
                    "Aggregate totals remain available for dropped observations.");

            var unknownSources = snapshot.Events.Count(observation =>
                                     observation.Source.Kind == AnalyticsSourceKind.Unknown) +
                                 timeline.Count(timelineEvent =>
                                     timelineEvent.Source?.Kind == AnalyticsSourceKind.Unknown);
            var approximateContributions = timeline.Sum(timelineEvent =>
                timelineEvent.Damage?.Contributions.Count(contribution =>
                    contribution.Confidence is AttributionConfidence.Heuristic or AttributionConfidence.Unknown) ?? 0);
            Main.Logger.Debug(
                $"Combat diagnostics '{LogId(snapshot.CombatId)}': history_entries={processedHistoryEntries}, " +
                $"history_failures={historyProcessingFailures}, " +
                $"unknown_sources={unknownSources}, approximate_damage_contributions={approximateContributions}, " +
                $"prefix_false_total={skippedOriginals.Total} " +
                $"(requests={skippedOriginals.DamageRequests}, calculations={skippedOriginals.DamageCalculations}, " +
                $"hp_loss={skippedOriginals.HpLossCalculations}, doom={skippedOriginals.DoomExecutions}, " +
                $"explicit_kill={skippedOriginals.ExplicitKills}, " +
                $"model_hooks={skippedOriginals.ModelHooks}, modifiers={skippedOriginals.Modifiers}).");
        }

        private static string EncounterLogName(MutableCombatSession combat)
        {
            if (!string.IsNullOrWhiteSpace(combat.EncounterId))
                return combat.EncounterId;
            return string.IsNullOrWhiteSpace(combat.EncounterName) ? "unknown" : combat.EncounterName;
        }

        private static string LogId(string id)
        {
            return id.Length <= 20 ? id : $"{id[..10]}...{id[^7..]}";
        }

        private void RestoreScopes()
        {
            foreach (var scope in _potionScopes.Values.SelectMany(scopes => scopes).Reverse())
                CausalScopeRuntime.Restore(scope.State);
            foreach (var scope in _attackScopes.Values.Reverse())
                CausalScopeRuntime.Restore(scope.State);
            foreach (var scope in _cardScopes.Values.Reverse())
                CausalScopeRuntime.Restore(scope.State);
            _attackScopes.Clear();
            _cardScopes.Clear();
            _potionScopes.Clear();
        }

        private static TimelineTurnSide Side(CombatSide side)
        {
            return side switch
            {
                CombatSide.Player => TimelineTurnSide.Player,
                CombatSide.Enemy => TimelineTurnSide.Enemy,
                _ => TimelineTurnSide.None,
            };
        }

        private static ReadOnlyDictionary<string, string> Tags(params (string Key, string Value)[] values)
        {
            return ReadOnly(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        private static ReadOnlyDictionary<string, string> Details(params (string Key, string Value)[] values)
        {
            return Tags(values);
        }

        private static IReadOnlyDictionary<string, string> WithActorTags(
            IReadOnlyDictionary<string, string>? tags,
            Creature? actor)
        {
            if (actor == null)
                return tags ?? MetricObservation.EmptyTags;
            var entity = GameDescriptorFactory.Creature(actor);
            var owner = GameDescriptorFactory.Player(actor);
            var values = tags == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(tags, StringComparer.Ordinal);
            values[ObservationTagIds.ActorKey] = entity.Key;
            values[ObservationTagIds.ActorKind] = entity.Kind.ToString();
            values[ObservationTagIds.ActorModelId] = entity.ModelId;
            values[ObservationTagIds.ActorDisplayName] = entity.DisplayName;
            values[ObservationTagIds.ActorOwnerKey] = owner?.Key ?? string.Empty;
            return ReadOnly(values);
        }

        private static ReadOnlyDictionary<string, string> ContributionTags(
            IReadOnlyDictionary<string, string>? tags,
            string component,
            AttributionConfidence confidence)
        {
            var values = tags == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(tags, StringComparer.Ordinal);
            values[ObservationTagIds.ContributionComponent] = component;
            values[ObservationTagIds.AttributionConfidence] = confidence.ToString();
            return ReadOnly(values);
        }

        private static ReadOnlyDictionary<string, string> ReadOnly(Dictionary<string, string> values)
        {
            return new(values);
        }

        private static MutableRunSession CreateRunSession(
            RunIdentity identity,
            bool isMultiplayer,
            bool isDaily)
        {
            return new()
            {
                RunId = identity.RunId,
                StartedAtUtc = identity.StartedAtUtc,
                IsMultiplayer = isMultiplayer,
                IsDaily = isDaily,
            };
        }

        private static RunIdentity ResolveRunIdentity(
            IRunState runState,
            bool isMultiplayer,
            bool isDaily,
            DateTimeOffset occurredAtUtc)
        {
            var startTime = Safe(() => RunStartTimeGetter(RunManager.Instance), 0L);
            var startTimeSource = "run manager";
            if (startTime <= 0L)
            {
                startTime = Safe(() => RunManager.Instance.ToSave(null).StartTime, 0L);
                startTimeSource = "serialized run state";
            }

            if (startTime <= 0L)
            {
                startTimeSource = "lifecycle timestamp fallback";
                Main.Logger.Warn(
                    "Could not resolve the game's stable run start time; run identity is using the lifecycle timestamp fallback.");
            }

            var seed = Safe(() => runState.Rng.StringSeed, string.Empty);
            var canonical = new StringBuilder(256);
            AppendIdentityPart(canonical, isMultiplayer
                ? "ritsumetrics-multiplayer-run-v2"
                : "ritsumetrics-run-v1");
            if (!isMultiplayer)
                AppendIdentityPart(canonical, startTime.ToString(CultureInfo.InvariantCulture));
            AppendIdentityPart(canonical, seed);
            AppendIdentityPart(canonical, ((int)runState.GameMode).ToString(CultureInfo.InvariantCulture));
            AppendIdentityPart(canonical, runState.AscensionLevel.ToString(CultureInfo.InvariantCulture));
            AppendIdentityPart(canonical, isMultiplayer ? "1" : "0");
            AppendIdentityPart(canonical, isDaily ? "1" : "0");
            AppendIdentityPart(canonical, Safe(
                () => RunManager.Instance.DailyTime?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                null) ?? string.Empty);
            foreach (var player in runState.Players
                         .OrderBy(player => player.NetId)
                         .ThenBy(player => Safe(() => player.Character.Id.Entry, string.Empty), StringComparer.Ordinal))
            {
                AppendIdentityPart(canonical, player.NetId.ToString(CultureInfo.InvariantCulture));
                AppendIdentityPart(canonical, Safe(() => player.Character.Id.Entry, string.Empty));
            }

            foreach (var modifierId in runState.Modifiers
                         .Select(modifier => Safe(() => modifier.Id.Entry, string.Empty))
                         .OrderBy(id => id, StringComparer.Ordinal))
                AppendIdentityPart(canonical, modifierId);

            var bytes = Encoding.UTF8.GetBytes(canonical.ToString());
            var runId = (isMultiplayer ? "sts2-mp-v2-" : "sts2-v1-") +
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var startedAtUtc = startTime > 0L
                ? Safe(() => DateTimeOffset.FromUnixTimeSeconds(startTime), occurredAtUtc)
                : occurredAtUtc;
            Main.Logger.Debug(
                $"Resolved analytics run identity '{LogId(runId)}': identity_version=" +
                $"{(isMultiplayer ? "multiplayer-v2" : "v1")}, start_time_source={startTimeSource}, " +
                $"start_time_in_hash={!isMultiplayer}, " +
                $"players={runState.Players.Count}, modifiers={runState.Modifiers.Count}, " +
                $"seed_present={!string.IsNullOrEmpty(seed)}.");
            return new(runId, startedAtUtc);
        }

        private static RunIdentity AllocateNewMultiplayerIdentity(
            RunIdentity identity,
            DateTimeOffset occurredAtUtc)
        {
            var prefix = identity.RunId + '-';
            var collision = MetricsRepository.GetSavedRuns(false, false).Any(run =>
                run.RunId == identity.RunId || run.RunId.StartsWith(prefix, StringComparison.Ordinal));
            if (!collision)
                return identity;
            var discriminator = $"{identity.RunId}:{occurredAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(discriminator)))
                .ToLowerInvariant()[..16];
            return identity with { RunId = $"{identity.RunId}-{hash}" };
        }

        private static RunSnapshot? FindSavedRunForResume(
            RunIdentity identity,
            RunState runState,
            bool isMultiplayer,
            bool isDaily)
        {
            var runs = MetricsRepository.GetSavedRuns(false, false);
            if (!isMultiplayer)
                return runs.LastOrDefault(run => run.RunId == identity.RunId);

            var prefix = identity.RunId + '-';
            var stable = runs.Where(run => run.IsMultiplayer && run.IsDaily == isDaily &&
                                           run.EndedAtUtc == null &&
                                           (run.RunId == identity.RunId ||
                                            run.RunId.StartsWith(prefix, StringComparison.Ordinal)))
                .OrderByDescending(LastActivity)
                .FirstOrDefault();
            if (stable != null)
                return stable;

            var currentPlayers = runState.Players.ToDictionary(player => player.NetId,
                player => Safe(() => player.Character.Id.Entry, string.Empty));
            return runs.Where(run => run.RunId.StartsWith("sts2-v1-", StringComparison.Ordinal) &&
                                     run.IsMultiplayer && run.IsDaily == isDaily && run.EndedAtUtc == null &&
                                     HasCompatiblePlayers(run, currentPlayers))
                .OrderByDescending(run => LastRecordedFloor(run) <= runState.TotalFloor)
                .ThenByDescending(run => Math.Min(LastRecordedFloor(run), runState.TotalFloor))
                .ThenByDescending(LastActivity)
                .FirstOrDefault();
        }

        private static bool HasCompatiblePlayers(
            RunSnapshot run,
            Dictionary<ulong, string> currentPlayers)
        {
            var recordedPlayers = run.Combats.SelectMany(combat => combat.Players)
                .Where(player => player.PlayerNetId.HasValue)
                .GroupBy(player => player.PlayerNetId!.Value)
                .ToDictionary(group => group.Key,
                    group => group.Select(player => player.CharacterId)
                        .FirstOrDefault(characterId => !string.IsNullOrEmpty(characterId)) ?? string.Empty);
            return recordedPlayers.Count > 0 && recordedPlayers.All(recorded =>
                currentPlayers.TryGetValue(recorded.Key, out var characterId) &&
                (string.IsNullOrEmpty(recorded.Value) || string.Equals(recorded.Value, characterId,
                    StringComparison.Ordinal)));
        }

        private static DateTimeOffset LastActivity(RunSnapshot run)
        {
            return run.Combats.Select(combat => combat.EndedAtUtc ?? combat.StartedAtUtc)
                .DefaultIfEmpty(run.StartedAtUtc)
                .Max();
        }

        private static int LastRecordedFloor(RunSnapshot run)
        {
            return run.Combats.Select(combat => combat.Floor).DefaultIfEmpty(-1).Max();
        }

        private static void AppendIdentityPart(StringBuilder builder, string value)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }

        private static Func<RunManager, long> CompileRunStartTimeGetter()
        {
            var field = typeof(RunManager).GetField("_startTime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return static _ => 0L;
            try
            {
                var manager = Expression.Parameter(typeof(RunManager), "manager");
                var value = Expression.Field(manager, field);
                return Expression.Lambda<Func<RunManager, long>>(value, manager).Compile();
            }
            catch
            {
                return manager => field.GetValue(manager) is long value ? value : 0L;
            }
        }

        private static T Safe<T>(Func<T> read, T fallback)
        {
            try
            {
                return read() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private sealed record ExplicitScope(string EventId, CausalScopeRuntime.ScopeState State);

        private sealed record RunIdentity(string RunId, DateTimeOffset StartedAtUtc);

        private sealed record PowerCredit(
            EntityDescriptor Player,
            SourceDescriptor Source,
            decimal Weight,
            string? OriginEventId);

        private sealed record BlockCredit(
            EntityDescriptor Player,
            SourceDescriptor Source,
            decimal Amount,
            AttributionConfidence Confidence,
            string? OriginEventId);

        private sealed class BlockAttribution
        {
            private readonly Dictionary<string, BlockCredit> _credits = new(StringComparer.Ordinal);

            internal void Add(
                EntityDescriptor player,
                SourceDescriptor source,
                decimal amount,
                AttributionConfidence confidence,
                string? originEventId)
            {
                if (amount <= 0m)
                    return;
                var key = $"{player.Key}\u001f{source.Key}\u001f{originEventId ?? "unknown"}";
                var previous = _credits.GetValueOrDefault(key);
                _credits[key] = new(player, source, (previous?.Amount ?? 0m) + amount,
                    MoreApproximate(previous?.Confidence ?? confidence, confidence),
                    originEventId ?? previous?.OriginEventId);
            }

            internal void Synchronize(
                decimal actualBlock,
                EntityDescriptor? fallbackPlayer,
                SourceDescriptor fallbackSource)
            {
                actualBlock = Math.Max(0m, actualBlock);
                var tracked = _credits.Values.Sum(credit => credit.Amount);
                if (tracked > actualBlock && tracked > 0m)
                {
                    Scale(actualBlock / tracked);
                    return;
                }

                if (fallbackPlayer != null && actualBlock > tracked)
                    Add(fallbackPlayer, fallbackSource, actualBlock - tracked, AttributionConfidence.Heuristic, null);
            }

            internal DamageAttributionShare[] Consume(decimal amount)
            {
                var total = _credits.Values.Sum(credit => credit.Amount);
                var consumed = Math.Min(Math.Max(0m, amount), total);
                if (consumed <= 0m || total <= 0m)
                    return [];
                var result = _credits.Values.Select(credit => new DamageAttributionShare(
                    credit.Player,
                    credit.Source,
                    credit.Amount / total,
                    consumed * credit.Amount / total,
                    credit.Confidence,
                    credit.OriginEventId)).ToArray();
                Scale(Math.Max(0m, total - consumed) / total);
                return result;
            }

            private void Scale(decimal ratio)
            {
                foreach (var key in _credits.Keys.ToArray())
                {
                    var credit = _credits[key];
                    var amount = credit.Amount * ratio;
                    if (amount <= 0.0001m)
                        _credits.Remove(key);
                    else
                        _credits[key] = credit with { Amount = amount };
                }
            }

            private static AttributionConfidence MoreApproximate(
                AttributionConfidence first,
                AttributionConfidence second)
            {
                return first > second ? first : second;
            }
        }

        private sealed class PowerAttribution(SourceDescriptor source)
        {
            private readonly Dictionary<string, PowerCredit> _credits = new(StringComparer.Ordinal);

            internal void Add(EntityDescriptor player, decimal amount, string? originEventId)
            {
                if (amount <= 0m)
                    return;
                var key = $"{player.Key}\u001f{originEventId ?? "unknown"}";
                var previous = _credits.GetValueOrDefault(key);
                _credits[key] = new(player, source, (previous?.Weight ?? 0m) + amount,
                    originEventId ?? previous?.OriginEventId);
            }

            internal void Remove(decimal amount)
            {
                var total = _credits.Values.Sum(credit => credit.Weight);
                if (amount <= 0m || total <= 0m)
                    return;
                var remainingRatio = Math.Max(0m, total - amount) / total;
                foreach (var key in _credits.Keys.ToArray())
                {
                    var credit = _credits[key];
                    var weight = credit.Weight * remainingRatio;
                    if (weight <= 0.0001m)
                        _credits.Remove(key);
                    else
                        _credits[key] = credit with { Weight = weight };
                }
            }

            internal DamageAttributionShare[] Shares(
                decimal effectiveAmount,
                AttributionConfidence confidence)
            {
                var total = _credits.Values.Sum(credit => credit.Weight);
                if (total <= 0m || effectiveAmount <= 0m)
                    return [];
                return _credits.Values
                    .OrderByDescending(credit => credit.Weight)
                    .Select(credit => new DamageAttributionShare(
                        credit.Player,
                        credit.Source,
                        credit.Weight / total,
                        effectiveAmount * credit.Weight / total,
                        confidence,
                        credit.OriginEventId))
                    .ToArray();
            }
        }
    }
}
