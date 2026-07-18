// SPDX-License-Identifier: MPL-2.0

using MegaCrit.Sts2.Core.Logging;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed class CollectorRegistry(Logger logger)
    {
        private readonly Dictionary<string, IMetricCollector> _collectors = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private readonly Dictionary<string, MetricDefinition> _metrics = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedCallbackFailures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ITimelineCollector> _timelineCollectors = new(StringComparer.Ordinal);

        internal IReadOnlyCollection<MetricDefinition> Definitions
        {
            get
            {
                lock (_gate)
                {
                    return _metrics.Values.OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
                }
            }
        }

        internal event Action<MetricObservation>? ObservationPublished;
        internal event Action<CombatTimelineEvent>? TimelineEventPublished;
        internal event Action? SnapshotChanged;

        internal void RegisterBuiltIns()
        {
            Register(new(MetricIds.DamageDealt, "metric.damageDealt", "Damage dealt",
                MetricValueKind.Amount,
                "damage"));
            Register(new(MetricIds.DamageContribution, "metric.damageContribution", "Damage contribution",
                MetricValueKind.Amount, "contribution"));
            Register(new(MetricIds.EffectiveHpDamageDealt, "metric.effectiveHpDamageDealt",
                "Effective HP reduction (AD)", MetricValueKind.Amount, "damage"));
            Register(new(MetricIds.EffectiveHpDamageContribution, "metric.effectiveHpDamageContribution",
                "Effective HP reduction (RD)", MetricValueKind.Amount, "contribution"));
            Register(new(MetricIds.DamageTaken, "metric.damageTaken", "Damage taken",
                MetricValueKind.Amount, "damage",
                false));
            Register(new(MetricIds.Deaths, "metric.deaths", "Deaths", MetricValueKind.Count,
                "survival", false));
            Register(new(MetricIds.SummonDamageTaken, "metric.summonDamageTaken", "Summon HP lost",
                MetricValueKind.Amount, "survival", false));
            Register(new(MetricIds.SummonDeaths, "metric.summonDeaths", "Summon deaths",
                MetricValueKind.Count, "survival", false));
            Register(new(MetricIds.DamageBlocked, "metric.damageBlocked", "Damage blocked",
                MetricValueKind.Amount,
                "damage"));
            Register(new(MetricIds.DamagePrevented, "metric.damagePrevented", "Damage prevented",
                MetricValueKind.Amount, "contribution"));
            Register(new(MetricIds.DefenseContribution, "metric.defenseContribution",
                "Defense contribution",
                MetricValueKind.Amount, "contribution"));
            Register(new(MetricIds.Overkill, "metric.overkill", "Overkill", MetricValueKind.Amount,
                "damage"));
            Register(new(MetricIds.DamageAmplified, "metric.damageAmplified", "Damage amplified",
                MetricValueKind.Amount, "damage"));
            Register(new(MetricIds.DamageMitigated, "metric.damageMitigated", "Damage mitigated",
                MetricValueKind.Amount, "damage"));
            Register(
                new(MetricIds.BlockGained, "metric.blockGained", "Block gained", MetricValueKind.Amount,
                    "defense"));
            Register(new(MetricIds.HealingReceived, "metric.healingReceived", "Healing received",
                MetricValueKind.Amount, "defense"));
            Register(new(MetricIds.HealingContribution, "metric.healingContribution",
                "Healing contribution",
                MetricValueKind.Amount, "contribution"));
            Register(new(MetricIds.CardsPlayed, "metric.cardsPlayed", "Cards played",
                MetricValueKind.Count, "cards"));
            Register(new(MetricIds.CardsDrawn, "metric.cardsDrawn", "Cards drawn", MetricValueKind.Count,
                "cards"));
            Register(new(MetricIds.CardsDiscarded, "metric.cardsDiscarded", "Cards discarded",
                MetricValueKind.Count,
                "cards"));
            Register(new(MetricIds.CardsExhausted, "metric.cardsExhausted", "Cards exhausted",
                MetricValueKind.Count,
                "cards"));
            Register(new(MetricIds.EnergySpent, "metric.energySpent", "Energy spent",
                MetricValueKind.Amount,
                "resources"));
            Register(new(MetricIds.PotionsUsed, "metric.potionsUsed", "Potions used",
                MetricValueKind.Count,
                "resources"));
            Register(new(MetricIds.PowersApplied, "metric.powersApplied", "Powers applied",
                MetricValueKind.Amount,
                "effects"));
            Register(new(MetricIds.DebuffsApplied, "metric.debuffsApplied", "Debuffs applied",
                MetricValueKind.Amount,
                "effects"));
            Register(new(MetricIds.OrbsChanneled, "metric.orbsChanneled", "Orbs channeled",
                MetricValueKind.Count,
                "resources"));
            Register(new(MetricIds.StarsGained, "metric.starsGained", "Stars gained",
                MetricValueKind.Amount,
                "resources"));
            Register(new(MetricIds.StarsSpent, "metric.starsSpent", "Stars spent", MetricValueKind.Amount,
                "resources"));
            Register(new(MetricIds.SummonsCreated, "metric.summonsCreated", "Summons created",
                MetricValueKind.Amount,
                "resources"));
        }

        internal bool Register(MetricDefinition definition, bool replace = false)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!IsValidId(definition.Id) || string.IsNullOrWhiteSpace(definition.FallbackName))
                return false;

            lock (_gate)
            {
                if (!replace && _metrics.ContainsKey(definition.Id))
                    return false;
                _metrics[definition.Id] = definition;
            }

            NotifyChanged();
            return true;
        }

        internal IDisposable AddCollector(IMetricCollector collector)
        {
            ArgumentNullException.ThrowIfNull(collector);
            if (!IsValidId(collector.Id))
                throw new ArgumentException("Collector Id must be a stable dotted identifier.", nameof(collector));

            lock (_gate)
            {
                if (!_collectors.TryAdd(collector.Id, collector))
                    throw new InvalidOperationException($"A collector with id '{collector.Id}' is already registered.");
            }

            return new Subscription(() =>
            {
                lock (_gate)
                {
                    _collectors.Remove(collector.Id);
                }
            });
        }

        internal IDisposable AddTimelineCollector(ITimelineCollector collector)
        {
            ArgumentNullException.ThrowIfNull(collector);
            if (!IsValidId(collector.Id))
                throw new ArgumentException("Collector Id must be a stable dotted identifier.", nameof(collector));

            lock (_gate)
            {
                if (!_timelineCollectors.TryAdd(collector.Id, collector))
                    throw new InvalidOperationException(
                        $"A timeline collector with id '{collector.Id}' is already registered.");
            }

            return new Subscription(() =>
            {
                lock (_gate)
                {
                    _timelineCollectors.Remove(collector.Id);
                }
            });
        }

        internal bool IsRegisteredMetric(string metricId)
        {
            lock (_gate)
            {
                return _metrics.ContainsKey(metricId);
            }
        }

        internal void Publish(MetricObservation observation)
        {
            IMetricCollector[] collectors;
            lock (_gate)
            {
                collectors = _collectors.Values.ToArray();
            }

            foreach (var collector in collectors)
                try
                {
                    collector.OnObservation(observation);
                }
                catch (Exception exception)
                {
                    LogCallbackFailure($"observation:{collector.Id}",
                        $"Collector '{collector.Id}' failed while processing '{observation.MetricId}'", exception);
                }

            InvokeEach(ObservationPublished, handler => handler(observation), "ObservationPublished");

            NotifyChanged();
        }

        internal void CompleteCombat(CombatSnapshot combat)
        {
            IMetricCollector[] collectors;
            ITimelineCollector[] timelineCollectors;
            lock (_gate)
            {
                collectors = _collectors.Values.ToArray();
                timelineCollectors = _timelineCollectors.Values.ToArray();
            }

            foreach (var collector in collectors)
                try
                {
                    collector.OnCombatCompleted(combat);
                }
                catch (Exception exception)
                {
                    LogCallbackFailure($"combat:{collector.Id}",
                        $"Collector '{collector.Id}' failed while completing combat '{combat.CombatId}'", exception);
                }

            foreach (var collector in timelineCollectors)
                try
                {
                    collector.OnCombatCompleted(combat);
                }
                catch (Exception exception)
                {
                    LogCallbackFailure($"timeline-combat:{collector.Id}",
                        $"Timeline collector '{collector.Id}' failed while completing combat '{combat.CombatId}'",
                        exception);
                }

            NotifyChanged();
        }

        internal void PublishTimeline(CombatTimelineEvent timelineEvent)
        {
            ITimelineCollector[] timelineCollectors;
            lock (_gate)
            {
                timelineCollectors = _timelineCollectors.Values.ToArray();
            }

            foreach (var collector in timelineCollectors)
                try
                {
                    collector.OnTimelineEvent(timelineEvent);
                }
                catch (Exception exception)
                {
                    LogCallbackFailure($"timeline:{collector.Id}",
                        $"Timeline collector '{collector.Id}' failed while processing '{timelineEvent.ActionId}'",
                        exception);
                }

            InvokeEach(TimelineEventPublished, handler => handler(timelineEvent), "TimelineEventPublished");
            NotifyChanged();
        }

        internal void NotifyChanged()
        {
            InvokeEach(SnapshotChanged, handler => handler(), "SnapshotChanged");
        }

        private void InvokeEach<TDelegate>(TDelegate? handlers, Action<TDelegate> invoke, string eventName)
            where TDelegate : Delegate
        {
            if (handlers == null)
                return;
            foreach (var handler in handlers.GetInvocationList().Cast<TDelegate>())
                try
                {
                    invoke(handler);
                }
                catch (Exception exception)
                {
                    var method = handler.Method;
                    LogCallbackFailure($"event:{eventName}:{method.DeclaringType?.FullName}:{method.Name}",
                        $"{eventName} subscriber '{method.DeclaringType?.FullName}.{method.Name}' failed", exception);
                }
        }

        private void LogCallbackFailure(string key, string message, Exception exception)
        {
            lock (_gate)
            {
                if (!_reportedCallbackFailures.Add(key))
                    return;
            }

            logger.Error($"{message}; repeated failures from this callback are suppressed: {exception}");
        }

        private static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 160 || !id.Contains('.'))
                return false;
            return id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose()
            {
                Interlocked.Exchange(ref _dispose, null)?.Invoke();
            }
        }
    }
}
