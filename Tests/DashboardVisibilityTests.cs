// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DashboardVisibilityTests
    {
        [Fact]
        public void AutomaticVisibilityHidesInNonCombatRoom()
        {
            Assert.False(DashboardVisibility.ShouldShowFloatingDashboards(
                overlayEnabled: true,
                runInProgress: true,
                automaticVisibility: true,
                isCombatRoom: false,
                hasCompletedCombat: false));
        }

        [Fact]
        public void CombatRoomRemainsVisibleAfterCombatEnds()
        {
            Assert.True(DashboardVisibility.ShouldShowFloatingDashboards(
                overlayEnabled: true,
                runInProgress: true,
                automaticVisibility: true,
                isCombatRoom: true,
                hasCompletedCombat: false));
        }

        [Fact]
        public void CompletedCombatDashboardPreservesRunCompletionVisibility()
        {
            Assert.True(DashboardVisibility.ShouldShowFloatingDashboards(
                overlayEnabled: true,
                runInProgress: true,
                automaticVisibility: true,
                isCombatRoom: false,
                hasCompletedCombat: true));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ManualVisibilityShowsThroughoutRunButNeverOutsideRun(bool combatRoom)
        {
            Assert.True(DashboardVisibility.ShouldShowFloatingDashboards(true, true, false, combatRoom, false));
            Assert.False(DashboardVisibility.ShouldShowFloatingDashboards(true, false, false, combatRoom, true));
            Assert.False(DashboardVisibility.ShouldShowFloatingDashboards(true, false, true, combatRoom, true));
            Assert.False(DashboardVisibility.ShouldShowFloatingDashboards(false, true, false, combatRoom, true));
        }
    }
}
