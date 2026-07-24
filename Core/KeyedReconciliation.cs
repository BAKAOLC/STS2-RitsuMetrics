// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Core
{
    internal readonly record struct ReconciliationItem(string Key, string Fingerprint);

    internal readonly record struct ReconciliationDecision(
        string Key,
        string Fingerprint,
        int Index,
        bool Reuse);

    internal sealed record ReconciliationPlan(
        IReadOnlyList<ReconciliationDecision> Decisions,
        IReadOnlyList<string> RemovedKeys);

    internal static class KeyedReconciliation
    {
        internal static ReconciliationPlan Plan(
            IReadOnlyDictionary<string, string> previous,
            IReadOnlyList<ReconciliationItem> next)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var decisions = new ReconciliationDecision[next.Count];
            for (var index = 0; index < next.Count; index++)
            {
                var item = next[index];
                if (!seen.Add(item.Key))
                    throw new InvalidOperationException($"Duplicate dashboard row key '{item.Key}'.");
                var reuse = previous.TryGetValue(item.Key, out var fingerprint) &&
                            string.Equals(fingerprint, item.Fingerprint, StringComparison.Ordinal);
                decisions[index] = new(item.Key, item.Fingerprint, index, reuse);
            }

            var removed = previous.Keys.Where(key => !seen.Contains(key)).ToArray();
            return new(decisions, removed);
        }
    }
}
