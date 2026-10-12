// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Ui
{
    internal static class DashboardVisibility
    {
        internal static bool ShouldShowFloatingDashboards(
            bool overlayEnabled,
            bool runInProgress,
            bool automaticVisibility,
            bool isCombatRoom,
            bool hasCompletedCombat)
        {
            return overlayEnabled && runInProgress &&
                   (!automaticVisibility || isCombatRoom || hasCompletedCombat);
        }
    }
}
