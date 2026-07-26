// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class VariableRowHeightCacheTests
    {
        [Fact]
        public void StableMeasurementStopsFurtherLayoutSampling()
        {
            var cache = new VariableRowHeightCache();
            var width = VariableRowHeightCache.WidthBucket(400f);

            Assert.True(cache.Record("row", "value", width, 80f));
            Assert.True(cache.NeedsMeasurement("row", "value", width));
            Assert.False(cache.Record("row", "value", width, 80.25f));
            Assert.False(cache.NeedsMeasurement("row", "value", width));
            Assert.Equal(80.25f, cache.Resolve("row", "value", width, 120f));
        }

        [Fact]
        public void UnstableMeasurementIsBounded()
        {
            var cache = new VariableRowHeightCache();
            var width = VariableRowHeightCache.WidthBucket(400f);

            Assert.True(cache.Record("row", "value", width, 80f));
            Assert.True(cache.Record("row", "value", width, 120f));
            Assert.True(cache.NeedsMeasurement("row", "value", width));
            Assert.True(cache.Record("row", "value", width, 90f));
            Assert.False(cache.NeedsMeasurement("row", "value", width));
            Assert.False(cache.Record("row", "value", width, 140f));
            Assert.Equal(90f, cache.Resolve("row", "value", width, 120f));
        }

        [Fact]
        public void WidthAndContentHaveIndependentMeasurements()
        {
            var cache = new VariableRowHeightCache();
            var narrow = VariableRowHeightCache.WidthBucket(320f);
            var wide = VariableRowHeightCache.WidthBucket(480f);

            cache.Record("row", "first", narrow, 120f);

            Assert.Equal(120f, cache.Resolve("row", "first", narrow, 80f));
            Assert.Equal(80f, cache.Resolve("row", "first", wide, 80f));
            Assert.Equal(80f, cache.Resolve("row", "second", narrow, 80f));
        }

        [Fact]
        public void RetainRemovesRowsNoLongerInTheDataset()
        {
            var cache = new VariableRowHeightCache();
            var width = VariableRowHeightCache.WidthBucket(400f);
            cache.Record("removed", "old", width, 90f);
            cache.Retain(new HashSet<(string Key, string Fingerprint)> { ("current", "new") });

            Assert.Equal(70f, cache.Resolve("removed", "old", width, 70f));
        }
    }
}
