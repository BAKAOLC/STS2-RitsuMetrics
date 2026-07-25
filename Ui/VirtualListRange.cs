// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Ui
{
    internal readonly record struct VirtualListRange(int Start, int End)
    {
        internal int Count => End - Start;

        internal static VirtualListRange Calculate(
            int itemCount,
            double scrollOffset,
            double viewportHeight,
            double itemExtent,
            int overscan = 6)
        {
            if (itemCount <= 0 || itemExtent <= 0d)
                return new(0, 0);

            var effectiveViewport = viewportHeight > 0d ? viewportHeight : itemExtent * 12d;
            var firstVisible = Math.Clamp((int)Math.Floor(Math.Max(0d, scrollOffset) / itemExtent),
                0, itemCount - 1);
            var visibleCount = Math.Max(1, (int)Math.Ceiling(effectiveViewport / itemExtent) + 1);
            var start = Math.Max(0, firstVisible - Math.Max(0, overscan));
            var end = Math.Min(itemCount, firstVisible + visibleCount + Math.Max(0, overscan));
            return new(start, end);
        }
    }
}
