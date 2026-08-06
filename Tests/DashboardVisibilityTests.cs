// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DashboardVisibilityTests
    {
        [Fact]
        public void LiveCombatDashboardIsHiddenWhenAnotherScreenIsActive()
        {
            Assert.False(DashboardVisibility.ShouldShowFloatingDashboards(
                overlayEnabled: true,
                runInProgress: true,
                hasLiveCombat: true,
                isCombatScreenActive: false,
                hasCompletedCombat: false));
        }

        [Fact]
        public void LiveCombatDashboardIsVisibleOnCombatScreen()
        {
            Assert.True(DashboardVisibility.ShouldShowFloatingDashboards(
                overlayEnabled: true,
                runInProgress: true,
                hasLiveCombat: true,
                isCombatScreenActive: true,
                hasCompletedCombat: false));
        }

        [Fact]
        public void CompletedCombatDashboardPreservesRunCompletionVisibility()
        {
            Assert.True(DashboardVisibility.ShouldShowFloatingDashboards(
                overlayEnabled: true,
                runInProgress: true,
                hasLiveCombat: false,
                isCombatScreenActive: false,
                hasCompletedCombat: true));
        }
    }
}
