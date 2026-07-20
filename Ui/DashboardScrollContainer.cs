// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Ui
{
    public sealed partial class DashboardScrollContainer : ScrollContainer
    {
        private readonly MarginContainer _contentHost;
        private readonly VScrollBar _verticalScrollBar;

        private int _contentGutter = DefaultContentGutter;
        private bool _layoutRefreshPending;

        public DashboardScrollContainer()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            HorizontalScrollMode = ScrollMode.Disabled;
            _contentHost = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            _contentHost.AddThemeConstantOverride("margin_right", 0);
            AddChild(_contentHost);
            DashboardControlTheme.ApplyScrollContainer(this);
            _verticalScrollBar = GetVScrollBar();
            _verticalScrollBar.VisibilityChanged += UpdateContentGutter;
            Callable.From(UpdateContentGutter).CallDeferred();
        }

        public static int DefaultContentGutter => 13;

        public int ContentGutter
        {
            get => _contentGutter;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _contentGutter = value;
                UpdateContentGutter();
            }
        }

        public void SetContent(Control content)
        {
            ArgumentNullException.ThrowIfNull(content);
            _contentHost.AddChild(content);
            content.MinimumSizeChanged += InvalidateContentSize;
            InvalidateContentSize();
        }

        public void InvalidateContentSize()
        {
            _contentHost.CustomMinimumSize = Vector2.Zero;
            _contentHost.ResetSize();
            _contentHost.UpdateMinimumSize();
            if (_layoutRefreshPending)
                return;
            _layoutRefreshPending = true;
            Callable.From(RefreshContentLayout).CallDeferred();
        }

        private void UpdateContentGutter()
        {
            _contentHost.AddThemeConstantOverride("margin_right", _verticalScrollBar.Visible ? _contentGutter : 0);
        }

        private void RefreshContentLayout()
        {
            _layoutRefreshPending = false;
            _contentHost.ResetSize();
            _contentHost.UpdateMinimumSize();
            _contentHost.QueueSort();
            QueueSort();
            Callable.From(ClampScrollAfterLayout).CallDeferred();
        }

        private void ClampScrollAfterLayout()
        {
            var maximum = Math.Max(0d, _verticalScrollBar.MaxValue - _verticalScrollBar.Page);
            ScrollVertical = Math.Min(ScrollVertical, (int)Math.Ceiling(maximum));
            UpdateContentGutter();
        }
    }
}
