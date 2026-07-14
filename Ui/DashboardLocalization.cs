// SPDX-License-Identifier: MPL-2.0

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
    }
}
