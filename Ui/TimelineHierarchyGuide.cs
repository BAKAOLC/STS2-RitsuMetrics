// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class TimelineHierarchyGuide : Control
    {
        private const float IndentWidth = 18f;
        private readonly Color[] _ancestorColors;
        private readonly int _visibleDepth;

        internal TimelineHierarchyGuide(Color[] ancestorColors, int maximumVisibleDepth)
        {
            _ancestorColors = ancestorColors;
            _visibleDepth = Math.Min(ancestorColors.Length, maximumVisibleDepth);
            CustomMinimumSize = new(_visibleDepth * IndentWidth, 0f);
            MouseFilter = MouseFilterEnum.Ignore;
            Resized += QueueRedraw;
        }

        public override void _Draw()
        {
            for (var index = 0; index < _visibleDepth; index++)
            {
                var sourceIndex = _ancestorColors.Length - _visibleDepth + index;
                var color = _ancestorColors[sourceIndex] with { A = index == _visibleDepth - 1 ? 0.72f : 0.26f };
                var x = index * IndentWidth + IndentWidth / 2f;
                DrawLine(new(x, 0f), new(x, Size.Y), color, index == _visibleDepth - 1 ? 2f : 1f, true);
                if (index == _visibleDepth - 1)
                    DrawLine(new(x, Size.Y / 2f), new(Size.X, Size.Y / 2f), color, 2f, true);
            }
        }
    }
}
