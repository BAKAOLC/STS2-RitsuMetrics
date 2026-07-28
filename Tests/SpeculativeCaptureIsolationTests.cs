// SPDX-License-Identifier: MPL-2.0

using STS2RitsuLib.Utils.Speculation;
using STS2RitsuMetrics.Capture;

namespace STS2RitsuMetrics.Tests
{
    public sealed class SpeculativeCaptureIsolationTests
    {
        [Fact]
        public void CaptureIsInactiveInsideSpeculativeExecution()
        {
            var previous = CaptureBridge.IsCombatActive;
            try
            {
                CaptureBridge.IsCombatActive = static () => true;
                Assert.True(CaptureBridge.Active);

                var session = new SpeculativeExecutionSession();
                using (session.Enter())
                {
                    Assert.False(CaptureBridge.Active);
                }

                Assert.True(CaptureBridge.Active);
            }
            finally
            {
                CaptureBridge.IsCombatActive = previous;
            }
        }

        [Fact]
        public async Task LifecycleDispatchIsSuppressedAcrossSpeculativeAsyncFlow()
        {
            var dispatchCount = 0;
            CaptureBridge.DispatchLifecycle(_ => dispatchCount++, 0);

            var session = new SpeculativeExecutionSession();
            using (session.Enter())
            {
                await Task.Yield();
                Assert.True(CaptureBridge.IsSpeculative);
                CaptureBridge.DispatchLifecycle(_ => dispatchCount++, 0);
            }

            CaptureBridge.DispatchLifecycle(_ => dispatchCount++, 0);
            Assert.Equal(2, dispatchCount);
        }
    }
}
