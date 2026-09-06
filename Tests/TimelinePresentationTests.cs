// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Data.Models;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class TimelinePresentationTests
    {
        [Fact]
        public void RefreshAndPreferenceChangesPreserveManualExpansion()
        {
            HashSet<string> initialized = [];
            HashSet<string> collapsed = [];
            TimelinePresentation.ApplyInitialExpansion("card", CombatTimelineKind.CardPlay, true,
                initialized, collapsed);
            Assert.Contains("card", collapsed);
            collapsed.Remove("card");
            TimelinePresentation.ApplyInitialExpansion("card", CombatTimelineKind.CardPlay, true,
                initialized, collapsed);
            Assert.DoesNotContain("card", collapsed);
            collapsed.Add("card");
            TimelinePresentation.ApplyInitialExpansion("card", CombatTimelineKind.CardPlay, false,
                initialized, collapsed);
            Assert.Contains("card", collapsed);
            TimelinePresentation.ApplyInitialExpansion("next", CombatTimelineKind.CardPlay, false,
                initialized, collapsed);
            TimelinePresentation.ApplyInitialExpansion("next", CombatTimelineKind.CardPlay, true,
                initialized, collapsed);
            Assert.DoesNotContain("next", collapsed);
            TimelinePresentation.ApplyInitialExpansion("new", CombatTimelineKind.CardPlay, true,
                initialized, collapsed);
            Assert.Contains("new", collapsed);
        }

        [Theory]
        [InlineData(CombatTimelineKind.Combat, false)]
        [InlineData(CombatTimelineKind.Turn, false)]
        [InlineData(CombatTimelineKind.Phase, false)]
        [InlineData(CombatTimelineKind.CardPlay, true)]
        [InlineData(CombatTimelineKind.Damage, true)]
        [InlineData(CombatTimelineKind.HandDraw, true)]
        [InlineData(CombatTimelineKind.Effect, true)]
        public void CompactViewKeepsNavigationOpenAndActionDetailsChronological(
            CombatTimelineKind kind, bool action)
        {
            Assert.Equal(action, TimelinePresentation.IsAction(kind));
        }

        [Fact]
        public void ExistingSettingsReceiveDefaultsAndExplicitPreferencesRoundTrip()
        {
            var settings = JsonSerializer.Deserialize<ModSettings>("{}")!;
            Assert.True(settings.TimelineCollapseDetails);
            settings.TimelineCollapseDetails = false;
            var restored = JsonSerializer.Deserialize<ModSettings>(JsonSerializer.Serialize(settings))!;
            Assert.False(restored.TimelineCollapseDetails);
        }

        [Fact]
        public void RefreshPreservesFollowingUntilUserScrollsAway()
        {
            var state = new ScrollFollowState();
            state.Configure(true, true);
            for (var pass = 0; pass < 10; pass++)
            {
                state.Configure(true, false);
                Assert.True(state.Following);
            }
            state.UserScrolled(false);
            state.Configure(true, false);
            Assert.False(state.Following);
            state.UserScrolled(true);
            Assert.True(state.Following);
        }

        [Fact]
        public void DisabledFollowingCannotBeResumedByScrollingOrJumping()
        {
            var state = new ScrollFollowState();
            state.Configure(false, true);
            state.Resume();
            state.UserScrolled(true);
            Assert.False(state.Following);
            state.Configure(true, false);
            Assert.True(state.Following);
            state.UserScrolled(false);
            state.Resume();
            Assert.True(state.Following);
            state.Configure(false, false);
            Assert.False(state.Following);
        }
    }
}
