// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DashboardPresentationTests
    {
        [Fact]
        public void FloatingWindowKeepsItsHistoryLimit()
        {
            Assert.Equal(200, DashboardPresentation.HistoryItemLimit(
                new Dictionary<string, string>(StringComparer.Ordinal),
                200));
        }

        [Fact]
        public void AnalysisCenterDoesNotTruncateRecordedHistory()
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DashboardPresentation.HostParameter] = DashboardPresentation.AnalysisCenterHost,
            };

            Assert.Equal(int.MaxValue, DashboardPresentation.HistoryItemLimit(parameters, 200));
        }
    }
}
