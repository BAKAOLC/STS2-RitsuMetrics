// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class OverlayStartupGateTests
    {
        [Fact]
        public void GameReadyAloneDoesNotAllowUiCreation()
        {
            var gate = new OverlayStartupGate();

            Assert.False(gate.CanAttach(true));
            gate.MarkMainMenuReady();
            Assert.True(gate.CanAttach(true));
        }

        [Fact]
        public void MainMenuReadyWaitsForAnAvailableGameRoot()
        {
            var gate = new OverlayStartupGate();

            gate.MarkMainMenuReady();

            Assert.False(gate.CanAttach(false));
            Assert.True(gate.CanAttach(true));
        }
    }
}
