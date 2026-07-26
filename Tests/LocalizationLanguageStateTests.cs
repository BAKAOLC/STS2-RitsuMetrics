// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Tests
{
    public sealed class LocalizationLanguageStateTests
    {
        [Fact]
        public void SwitchToDetectsLanguageThatChangedAfterInitialLoad()
        {
            var state = new LocalizationLanguageState();
            state.Record("zhs");

            Assert.True(state.SwitchTo("eng"));
            Assert.False(state.SwitchTo("eng"));
        }

        [Fact]
        public void SwitchToTreatsLanguageCodesAsCaseInsensitive()
        {
            var state = new LocalizationLanguageState();
            state.Record("ENG");

            Assert.False(state.SwitchTo("eng"));
        }
    }
}
