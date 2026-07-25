// SPDX-License-Identifier: MPL-2.0

using System.Collections;

namespace STS2RitsuMetrics.Core
{
    internal sealed class CompositeReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly int[] _ends;
        private readonly IReadOnlyList<T>[] _segments;

        private CompositeReadOnlyList(IReadOnlyList<T>[] segments)
        {
            _segments = segments;
            _ends = new int[segments.Length];
            var count = 0;
            for (var index = 0; index < segments.Length; index++)
            {
                count += segments[index].Count;
                _ends[index] = count;
            }

            Count = count;
        }

        public int Count { get; }

        public T this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                if (index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                var segmentIndex = Array.BinarySearch(_ends, index + 1);
                if (segmentIndex < 0)
                    segmentIndex = ~segmentIndex;
                var segmentStart = segmentIndex == 0 ? 0 : _ends[segmentIndex - 1];
                return _segments[segmentIndex][index - segmentStart];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var segment in _segments)
            foreach (var item in segment)
                yield return item;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal static IReadOnlyList<T> Create(IEnumerable<IReadOnlyList<T>?> segments)
        {
            var materialized = segments.Where(segment => segment is { Count: > 0 }).Cast<IReadOnlyList<T>>()
                .ToArray();
            return materialized.Length switch
            {
                0 => Array.Empty<T>(),
                1 => materialized[0],
                _ => new CompositeReadOnlyList<T>(materialized),
            };
        }
    }
}
