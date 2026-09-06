// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Ui
{
    internal sealed class ScrollFollowState
    {
        internal bool Enabled { get; private set; }
        internal bool Following { get; private set; }

        internal void Configure(bool enabled, bool reset)
        {
            if (reset || enabled != Enabled)
                Following = enabled;
            Enabled = enabled;
        }

        internal void UserScrolled(bool atEnd)
        {
            Following = Enabled && atEnd;
        }

        internal void Resume()
        {
            Following = Enabled;
        }
    }
}
