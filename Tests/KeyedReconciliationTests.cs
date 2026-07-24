// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class KeyedReconciliationTests
    {
        [Fact]
        public void AppendingToLongTimelineReusesEveryExistingRow()
        {
            var previous = Enumerable.Range(0, 1500)
                .ToDictionary(index => $"event:{index}", index => $"v:{index}", StringComparer.Ordinal);
            var next = Enumerable.Range(0, 1501)
                .Select(index => new ReconciliationItem($"event:{index}", $"v:{index}"))
                .ToArray();

            var plan = KeyedReconciliation.Plan(previous, next);

            Assert.Equal(1500, plan.Decisions.Count(item => item.Reuse));
            Assert.Single(plan.Decisions, item => !item.Reuse);
            Assert.Empty(plan.RemovedKeys);
        }

        [Fact]
        public void ChangingOneTimelineEventReplacesOnlyThatRow()
        {
            var previous = Enumerable.Range(0, 1500)
                .ToDictionary(index => $"event:{index}", index => $"v:{index}", StringComparer.Ordinal);
            var next = Enumerable.Range(0, 1500)
                .Select(index => new ReconciliationItem($"event:{index}", index == 700 ? "changed" : $"v:{index}"))
                .ToArray();

            var plan = KeyedReconciliation.Plan(previous, next);

            var replaced = Assert.Single(plan.Decisions, item => !item.Reuse);
            Assert.Equal("event:700", replaced.Key);
            Assert.Empty(plan.RemovedKeys);
        }

        [Fact]
        public void DuplicateKeysFailFast()
        {
            var next = new[]
            {
                new ReconciliationItem("same", "one"),
                new ReconciliationItem("same", "two"),
            };

            Assert.Throws<InvalidOperationException>(() =>
                KeyedReconciliation.Plan(new Dictionary<string, string>(), next));
        }
    }
}
