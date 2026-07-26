// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Ui
{
    internal sealed class VariableRowHeightCache
    {
        private const int MaximumSamples = 3;
        private const float StableTolerance = 0.5f;
        private const float WidthBucketSize = 8f;
        private readonly Dictionary<CacheKey, Measurement> _measurements = [];

        internal static int WidthBucket(float width)
        {
            return Math.Max(1, (int)MathF.Round(Math.Max(1f, width) / WidthBucketSize));
        }

        internal float Resolve(string key, string fingerprint, int widthBucket, float estimatedHeight)
        {
            return _measurements.TryGetValue(new(key, fingerprint, widthBucket), out var measurement)
                ? measurement.Height
                : estimatedHeight;
        }

        internal bool NeedsMeasurement(string key, string fingerprint, int widthBucket)
        {
            return !_measurements.TryGetValue(new(key, fingerprint, widthBucket), out var measurement) ||
                   !measurement.IsFinal;
        }

        internal bool Record(string key, string fingerprint, int widthBucket, float height)
        {
            var cacheKey = new CacheKey(key, fingerprint, widthBucket);
            if (!_measurements.TryGetValue(cacheKey, out var previous))
            {
                _measurements.Add(cacheKey, new(height, 1, false));
                return true;
            }

            if (previous.IsFinal)
                return false;

            var stable = MathF.Abs(previous.Height - height) <= StableTolerance;
            var sampleCount = previous.SampleCount + 1;
            var isFinal = stable || sampleCount >= MaximumSamples;
            _measurements[cacheKey] = new(height, sampleCount, isFinal);
            return MathF.Abs(previous.Height - height) > StableTolerance;
        }

        internal void Retain(IReadOnlySet<(string Key, string Fingerprint)> rows)
        {
            foreach (var stale in _measurements.Keys
                         .Where(key => !rows.Contains((key.Key, key.Fingerprint))).ToArray())
                _measurements.Remove(stale);
        }

        private readonly record struct CacheKey(string Key, string Fingerprint, int WidthGroup);

        private readonly record struct Measurement(float Height, int SampleCount, bool IsFinal);
    }
}
