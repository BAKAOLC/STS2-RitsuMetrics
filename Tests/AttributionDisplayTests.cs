// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Tests
{
    public sealed class AttributionDisplayTests
    {
        private static readonly EntityDescriptor Player = new("p", AnalyticsEntityKind.Player, 1, "p", "Player");
        private static readonly SourceDescriptor Card = new("card:shatter", AnalyticsSourceKind.Card, "shatter", "Shatter");
        private static readonly SourceDescriptor Dark = new("orb:dark", AnalyticsSourceKind.Orb, "dark", "Dark");
        private static readonly SourceDescriptor Lightning = new("orb:lightning", AnalyticsSourceKind.Orb, "lightning", "Lightning");
        private static readonly SourceDescriptor Strength = new("power:strength", AnalyticsSourceKind.Power, "strength", "Strength");
        private static readonly SourceDescriptor Relic = new("relic:vajra", AnalyticsSourceKind.Relic, "vajra", "Vajra");

        [Fact]
        public void OneObservationStreamProducesDirectLinkedAndChildrenWithoutChangingTotal()
        {
            var metrics = Captured();
            var stored = metrics.Snapshot();
            var direct = AttributionDisplay.Project(stored, false);
            var linked = AttributionDisplay.Project(stored, true);
            Assert.Equal(30m, direct.Sources[MetricIds.DamageContribution].Single(source => source.SourceKey == Dark.Key).Value);
            Assert.Equal(45m, linked.Sources[MetricIds.DamageContribution].Single(source => source.SourceKey == Card.Key).Value);
            Assert.Equal(2m, linked.Sources[MetricIds.DamageContribution].Single(source => source.SourceKey == Relic.Key).Value);
            var children = AttributionDisplay.Children(linked, MetricIds.DamageContribution, Card.Key);
            Assert.Equal(3, children.Count);
            Assert.Equal(30m, children.Single(child => child.DisplayName == "Dark").Value);
            Assert.Equal(8m, children.Single(child => child.DisplayName == "Lightning").Value);
            Assert.Equal(7m, children.Single(child => child.DisplayName == "Shatter").Value);
            Assert.Equal(47m, direct.Sources[MetricIds.DamageContribution].Sum(source => source.Value));
            Assert.Equal(47m, linked.Sources[MetricIds.DamageContribution].Sum(source => source.Value));
            Assert.Equal(47m, stored.Totals[MetricIds.DamageContribution]);
        }

        [Fact]
        public void AmplifierChildNamesItsEffectAndBenefitingCard()
        {
            var child = Assert.Single(AttributionDisplay.Children(Captured().Snapshot(), MetricIds.DamageContribution, Relic.Key));
            Assert.Equal("Strength → Shatter", child.DisplayName);
            Assert.Equal(2m, child.Value);
        }

        [Fact]
        public void MetricFilteredSnapshotKeepsBothViewsAndChildren()
        {
            var stored = Captured().Snapshot(new HashSet<string> { MetricIds.DamageContribution });
            Assert.Equal(4, AttributionDisplay.Project(stored, false).Sources[MetricIds.DamageContribution].Count);
            Assert.Equal(3, AttributionDisplay.Children(stored, MetricIds.DamageContribution, Card.Key).Count);
        }

        [Fact]
        public void JsonAndRestoreKeepViewSwitchingWithoutEventHistory()
        {
            var stored = Captured().Snapshot();
            var restored = MutablePlayerMetrics.Restore(JsonSerializer.Deserialize<PlayerMetricSnapshot>(
                JsonSerializer.Serialize(stored))!).Snapshot();
            Assert.Equal(30m, AttributionDisplay.Project(restored, false).Sources[MetricIds.DamageContribution]
                .Single(source => source.SourceKey == Dark.Key).Value);
            Assert.Equal(3, AttributionDisplay.Children(restored, MetricIds.DamageContribution, Card.Key).Count);
        }

        [Fact]
        public void RepeatedDisplaySwitchesDoNotMutateCapturedSnapshot()
        {
            var stored = Captured().Snapshot();
            var before = JsonSerializer.Serialize(stored);
            for (var i = 0; i < 10; i++) AttributionDisplay.Project(stored, i % 2 == 0);
            Assert.Equal(before, JsonSerializer.Serialize(stored));
        }

        [Fact]
        public void RunAggregationAndMetricProjectionPreserveEffectBreakdown()
        {
            var player = Captured().Snapshot();
            var combat = new CombatSnapshot("run", "combat", 1, 1, "encounter", "Encounter",
                DateTimeOffset.UtcNow, null, true, 1, [player], []);
            var run = new RunSnapshot("run", DateTimeOffset.UtcNow, null, false, false, null, null,
                [combat, combat with { CombatId = "second" }]);
            var aggregate = SnapshotAggregator.Combine(run, false, false)!;
            var projected = DashboardSnapshotProjector.Project(aggregate,
                new HashSet<string> { MetricIds.DamageContribution })!;
            var result = Assert.Single(projected.Players);
            Assert.Equal(94m, result.Totals[MetricIds.DamageContribution]);
            Assert.Equal(60m, AttributionDisplay.Children(result, MetricIds.DamageContribution, Card.Key)
                .Single(child => child.DisplayName == "Dark").Value);
            Assert.Equal(60m, AttributionDisplay.Project(result, false).Sources[MetricIds.DamageContribution]
                .Single(source => source.SourceKey == Dark.Key).Value);
        }

        private static MutablePlayerMetrics Captured()
        {
            var metrics = new MutablePlayerMetrics(Player);
            Add(Card, Card, 7);
            Add(Card, Dark, 30);
            Add(Card, Lightning, 8);
            Add(Relic, Strength, 2, "Shatter");
            return metrics;

            void Add(SourceDescriptor owner, SourceDescriptor direct, decimal value, string? detail = null)
            {
                metrics.Add(new(1, "run", "combat", 1, 1, 1, DateTimeOffset.UtcNow,
                    MetricIds.DamageContribution, value, Player, null, owner,
                    AttributionDisplay.Tags(MetricObservation.EmptyTags, direct, direct, detail)));
            }
        }
    }
}
