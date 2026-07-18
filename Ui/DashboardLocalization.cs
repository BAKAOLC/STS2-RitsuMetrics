// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    public static class DashboardLocalization
    {
        public static string TimelineKind(CombatTimelineKind kind)
        {
            return ModLocalization.Get($"timeline.kind.{kind}", kind.ToString());
        }

        public static string TimelinePhase(TimelineEventPhase phase)
        {
            return ModLocalization.Get($"timeline.phase.{phase}", phase.ToString());
        }

        public static string TurnSide(TimelineTurnSide side)
        {
            return ModLocalization.Get($"timeline.side.{side}", side.ToString());
        }

        public static string ContributionStage(DamageContributionStage stage)
        {
            return ModLocalization.Get($"damage.stage.{stage}", stage.ToString());
        }

        public static string AttributionConfidence(AttributionConfidence confidence)
        {
            return ModLocalization.Get($"attribution.confidence.{confidence}", confidence.ToString());
        }

        public static string SourceKind(AnalyticsSourceKind kind)
        {
            return ModLocalization.Get($"overview.sourceKind.{kind.ToString().ToLowerInvariant()}", kind.ToString());
        }

        public static string TimelineDescription(CombatTimelineEvent timelineEvent)
        {
            var actor = timelineEvent.Actor?.DisplayName ??
                        ModLocalization.Get("timeline.entity.unknown", "Unknown actor");
            var target = timelineEvent.Target?.DisplayName ??
                         ModLocalization.Get("timeline.entity.noTarget", "no target");
            var source = timelineEvent.Source?.DisplayName ?? timelineEvent.DisplayText;
            var value = Format(timelineEvent.Value);
            return timelineEvent.ActionId switch
            {
                "combat.start" => Text("timeline.action.combatStart", "Combat started: {0}",
                    timelineEvent.DisplayText),
                "combat.resume" => Text("timeline.action.combatResume", "Combat resumed: {0}",
                    timelineEvent.DisplayText),
                "combat.end" => Text("timeline.action.combatEnd", "Combat ended: {0}", timelineEvent.DisplayText),
                "turn.start" => Text("timeline.action.turnStart", "{0} started",
                    TurnSide(timelineEvent.Side)),
                "turn.ready" => Text("timeline.action.turnReady", "{0} is ready",
                    TurnSide(timelineEvent.Side)),
                "turn.ending" => Text("timeline.action.turnEnding", "{0} is ending",
                    TurnSide(timelineEvent.Side)),
                "turn.end" => Text("timeline.action.turnEnd", "{0} ended", TurnSide(timelineEvent.Side)),
                "turn.extra_granted" => Text("timeline.action.extraTurn", "{0} gained an extra turn", actor),
                "hand.draw" => Text("timeline.action.handDraw", "{0} started drawing a hand", actor),
                "card.draw" => Text("timeline.action.cardDraw", "{0} drew {1} (draw pile → hand)", actor,
                    timelineEvent.DisplayText),
                "card.move" => Text("timeline.action.cardMove", "{0} moved {1}: {2} → {3}", actor,
                    timelineEvent.DisplayText, Pile(Detail(timelineEvent, "previous_pile")),
                    Pile(Detail(timelineEvent, "current_pile"))),
                "card.discard" => Text("timeline.action.cardDiscard", "{0} discarded {1} (hand → discard)", actor,
                    timelineEvent.DisplayText),
                "card.exhaust" => Text("timeline.action.cardExhaust", "{0} exhausted {1}{2}", actor,
                    timelineEvent.DisplayText, TrueDetail(timelineEvent, "ethereal")
                        ? ModLocalization.Get("timeline.action.etherealSuffix", " (ethereal)")
                        : string.Empty),
                "card.play" => Text(timelineEvent.Phase == TimelineEventPhase.Started
                        ? "timeline.action.cardPlayStart"
                        : "timeline.action.cardPlayEnd",
                    timelineEvent.Phase == TimelineEventPhase.Started
                        ? "{0} started playing {1} → {2}"
                        : "{0} finished playing {1} → {2}", actor, timelineEvent.DisplayText, target),
                "attack.start" => Text("timeline.action.attackStart", "{0} started an attack with {1}", actor,
                    source),
                "attack.end" => Text("timeline.action.attackEnd", "{0}'s {1} finished: {2} total damage", actor,
                    source, value),
                "damage" => DamageDescription(timelineEvent, source, target),
                "damage.modifier" => Text("timeline.action.damageModifier",
                    "{0} modified damage: {1} → {2} ({3}, {4})", source,
                    Detail(timelineEvent, "input"), Detail(timelineEvent, "output"),
                    ContributionStageName(Detail(timelineEvent, "stage")), Signed(timelineEvent.Value)),
                "block.gain" => Text("timeline.action.blockGain", "{0} gained {1} Block from {2}", target,
                    value, source),
                "power.change" => Text("timeline.action.powerChange", "{0} changed {1}'s {2} by {3} (now {4})",
                    actor, target, source, Signed(timelineEvent.Value), Detail(timelineEvent, "result_amount")),
                "healing" => Text("timeline.action.healing", "{0} healed {1} for {2}", source, target, value),
                "hp.loss" => Text("timeline.action.hpLoss", "{0} lost {1} HP from {2}", target, value, source),
                "execution" => Text("timeline.action.execution", "{0} executed {1}, removing {2} HP", source,
                    target, value),
                "hp.removed_on_death" => Text("timeline.action.deathHpRemoval",
                    "Removed {0}'s remaining HP during death cleanup", target),
                "energy.gain" => Text("timeline.action.energyGain", "{0} gained {1} Energy", actor, value),
                "energy.reset" => Text("timeline.action.energyReset", "{0}'s Energy was reset", actor),
                "energy.spend" => Text("timeline.action.energySpend", "{0} spent {1} Energy on {2}", actor, value,
                    timelineEvent.DisplayText),
                "potion.use" => Text(timelineEvent.Phase == TimelineEventPhase.Started
                        ? "timeline.action.potionStart"
                        : "timeline.action.potionEnd",
                    timelineEvent.Phase == TimelineEventPhase.Started
                        ? "{0} started using {1} → {2}"
                        : "{0} finished using {1} → {2}", actor, source, target),
                "deck.shuffle" => Text("timeline.action.shuffle", "{0} shuffled the discard pile into the draw pile",
                    actor),
                "death.start" => Text("timeline.action.deathStart", "{0} started dying", target),
                "death.end" => Text("timeline.action.deathEnd", "{0} died", target),
                "orb.channel" => Text("timeline.action.orb", "{0} channeled {1}", actor,
                    timelineEvent.DisplayText),
                "summon" => Text("timeline.action.summon", "{0} summoned a creature", actor),
                _ when timelineEvent.Kind == CombatTimelineKind.Effect => Text("timeline.action.effect",
                    "{0} triggered {1} ({2})", actor, source, timelineEvent.ActionId),
                _ => Text("timeline.action.default", "{0}: {1}{2}", actor,
                    string.IsNullOrWhiteSpace(timelineEvent.DisplayText)
                        ? TimelineKind(timelineEvent.Kind)
                        : timelineEvent.DisplayText,
                    timelineEvent.Target == null ? string.Empty : $" → {target}"),
            };
        }

        public static IReadOnlyList<string> TimelineTooltip(CombatTimelineEvent timelineEvent)
        {
            var lines = new List<string>
            {
                TimelineDescription(timelineEvent),
                Text("timeline.tooltip.position", "Round {0} · Turn {1} · {2}", timelineEvent.Round,
                    timelineEvent.TurnIndex, TurnSide(timelineEvent.Side)),
                Text("timeline.tooltip.action", "Action: {0} · {1}", timelineEvent.ActionId,
                    TimelinePhase(timelineEvent.Phase)),
            };
            if (timelineEvent.IsExtraTurn)
                lines.Add(ModLocalization.Get("analysis.extraTurn", "Extra turn"));
            if (timelineEvent.ParentEventId != null)
                lines.Add(Text("timeline.tooltip.parent", "Caused by: {0}", timelineEvent.ParentEventId));
            if (timelineEvent.Damage is { } damage)
                lines.Add(Text("timeline.tooltip.damage",
                    "Requested {0} · modified {1} · HP {2} · Block {3} · overkill {4}",
                    Format(damage.RequestedAmount), Format(damage.ModifiedAmount), Format(damage.HpLost),
                    Format(damage.BlockedAmount), Format(damage.OverkillAmount)));
            foreach (var (key, detail) in timelineEvent.Details.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
                lines.Add($"{key}: {detail}");
            return lines;
        }

        private static string DamageDescription(CombatTimelineEvent timelineEvent, string source, string target)
        {
            var damage = timelineEvent.Damage;
            return damage == null
                ? Text("timeline.action.damageSimple", "{0} dealt {1} damage to {2}", source,
                    Format(timelineEvent.Value), target)
                : Text("timeline.action.damage", "{0} dealt {1} damage to {2} (HP {3}, Block {4})", source,
                    Format(damage.HpLost + damage.BlockedAmount), target, Format(damage.HpLost),
                    Format(damage.BlockedAmount));
        }

        private static string ContributionStageName(string value)
        {
            return Enum.TryParse<DamageContributionStage>(value, out var stage)
                ? ContributionStage(stage)
                : value;
        }

        private static string Pile(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ModLocalization.Get("timeline.pile.Unknown", "unknown pile")
                : ModLocalization.Get($"timeline.pile.{value}", value);
        }

        private static string Detail(CombatTimelineEvent timelineEvent, string key)
        {
            return timelineEvent.Details.GetValueOrDefault(key) ?? string.Empty;
        }

        private static bool TrueDetail(CombatTimelineEvent timelineEvent, string key)
        {
            return bool.TryParse(Detail(timelineEvent, key), out var value) && value;
        }

        private static string Signed(decimal? value)
        {
            return value is null ? "—" : value >= 0m ? $"+{Format(value)}" : Format(value);
        }

        private static string Format(decimal? value)
        {
            return value?.ToString("0.##", CultureInfo.CurrentCulture) ?? "—";
        }

        private static string Text(string key, string fallback, params object[] args)
        {
            return ModLocalization.Format(key, fallback, args);
        }
    }
}
