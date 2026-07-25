// SPDX-License-Identifier: MPL-2.0

using System.Collections;

namespace STS2RitsuMetrics.Domain
{
    internal sealed class AppendOnlySnapshotBuffer<T>
    {
        private const int ChunkSize = 128;
        private readonly List<T[]> _sealedChunks = [];
        private T[] _activeChunk = new T[ChunkSize];
        private int _activeCount;
        private Snapshot? _snapshot;

        internal int Count { get; private set; }

        internal long Revision { get; private set; }

        internal void Add(T item)
        {
            if (_activeCount == ChunkSize)
            {
                _sealedChunks.Add(_activeChunk);
                _activeChunk = new T[ChunkSize];
                _activeCount = 0;
            }

            _activeChunk[_activeCount++] = item;
            Count++;
            Revision++;
            _snapshot = null;
        }

        internal void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
                Add(item);
        }

        internal IReadOnlyList<T> GetSnapshot()
        {
            if (_snapshot != null)
                return _snapshot;

            var chunks = new T[_sealedChunks.Count + (_activeCount == 0 ? 0 : 1)][];
            for (var index = 0; index < _sealedChunks.Count; index++)
                chunks[index] = _sealedChunks[index];
            if (_activeCount == 0)
                return _snapshot = new(chunks, Count);

            var active = new T[_activeCount];
            Array.Copy(_activeChunk, active, _activeCount);
            chunks[^1] = active;
            return _snapshot = new(chunks, Count);
        }

        private sealed class Snapshot(T[][] chunks, int count) : IReadOnlyList<T>
        {
            public int Count { get; } = count;

            public T this[int index]
            {
                get
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(index);
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                    return chunks[index / ChunkSize][index % ChunkSize];
                }
            }

            public IEnumerator<T> GetEnumerator()
            {
                var remaining = Count;
                foreach (var chunk in chunks)
                {
                    var length = Math.Min(chunk.Length, remaining);
                    for (var index = 0; index < length; index++)
                        yield return chunk[index];
                    remaining -= length;
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
