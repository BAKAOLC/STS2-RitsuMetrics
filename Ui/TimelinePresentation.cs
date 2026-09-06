// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Ui
{
    internal static class TimelinePresentation
    {
        internal static void ApplyInitialExpansion(string eventId, CombatTimelineKind kind, bool collapseDetails,
            HashSet<string> initialized, HashSet<string> collapsed)
        {
            if (initialized.Add(eventId) && collapseDetails && IsAction(kind))
                collapsed.Add(eventId);
        }

        internal static bool IsAction(CombatTimelineKind kind)
        {
            return kind is not (CombatTimelineKind.Combat or CombatTimelineKind.Turn or CombatTimelineKind.Phase);
        }

    }
}
