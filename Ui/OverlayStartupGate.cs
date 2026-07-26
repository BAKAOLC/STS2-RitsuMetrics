// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Ui
{
    internal sealed class OverlayStartupGate
    {
        private bool _mainMenuReady;

        internal void MarkMainMenuReady()
        {
            _mainMenuReady = true;
        }

        internal bool CanAttach(bool gameAvailable)
        {
            return _mainMenuReady && gameAvailable;
        }
    }
}
