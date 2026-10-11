// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class TooltipLayoutTests
    {
        [Theory]
        [InlineData(280f, 200f, 40f)]
        [InlineData(280f, 40f, 200f)]
        [InlineData(500f, 330f, 160f)]
        public void FittingTextGetsEnoughWidthInBothColumns(float available, float label, float value)
        {
            var result = DashboardTooltipCard.TooltipColumnWidths(available, label, value);
            Assert.True(result.Left >= label);
            Assert.True(result.Right >= value);
            Assert.Equal(available, result.Left + result.Right);
        }

        [Fact]
        public void LongValueReceivesMoreRoomWithoutExceedingCardWidth()
        {
            var result = DashboardTooltipCard.TooltipColumnWidths(280f, 100f, 400f);
            Assert.True(result.Right > result.Left);
            Assert.Equal(280f, result.Left + result.Right);
        }
    }
}
