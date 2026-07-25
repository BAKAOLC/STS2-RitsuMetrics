// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class VirtualListRangeTests
    {
        [Fact]
        public void LargeListMaterializesOnlyViewportAndOverscan()
        {
            var range = VirtualListRange.Calculate(100_000, 50_000d, 480d, 32d);

            Assert.InRange(range.Count, 1, 32);
            Assert.True(range.Start > 0);
            Assert.True(range.End < 100_000);
            Assert.InRange(50_000d / 32d, range.Start, range.End);
        }

        [Fact]
        public void RangeClampsAtBothEnds()
        {
            Assert.Equal(new(0, 10), VirtualListRange.Calculate(10, -100d, 200d, 30d));

            var end = VirtualListRange.Calculate(10, 10_000d, 200d, 30d);
            Assert.Equal(10, end.End);
            Assert.True(end.Start < end.End);
        }

        [Fact]
        public void EmptyListProducesEmptyRange()
        {
            Assert.Equal(new(0, 0), VirtualListRange.Calculate(0, 0d, 500d, 30d));
        }
    }
}
