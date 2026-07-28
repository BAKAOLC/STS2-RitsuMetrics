// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Ui
{
    public sealed partial class DashboardScrollContainer : ScrollContainer
    {
        private readonly MarginContainer _contentHost;
        private readonly List<Control> _contents = [];
        private readonly VScrollBar _verticalScrollBar;
        private int _appliedContentGutter = -1;
        private int _contentGutter = DefaultContentGutter;

        private float _contentMinimumHeight = -1f;
        private bool _layoutRefreshPending;
        private int _layoutSettlePasses;
        private bool _visibleRangeNotificationPending;

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
            _verticalScrollBar.ValueChanged += _ => ScheduleVisibleRangeChanged();
            Resized += OnResized;
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

        internal event Action? VisibleRangeChanged;

        public void SetContent(Control content)
        {
            ArgumentNullException.ThrowIfNull(content);
            _contents.Add(content);
            _contentHost.AddChild(content);
            content.MinimumSizeChanged += InvalidateContentSize;
            InvalidateContentSize();
        }

        public void InvalidateContentSize()
        {
            _layoutSettlePasses = 2;
            if (_layoutRefreshPending)
                return;
            _layoutRefreshPending = true;
            Callable.From(RefreshContentLayout).CallDeferred();
        }

        private void UpdateContentGutter()
        {
            if (_appliedContentGutter == _contentGutter)
                return;
            _appliedContentGutter = _contentGutter;
            _contentHost.AddThemeConstantOverride("margin_right", _contentGutter);
            InvalidateContentSize();
        }

        private void RefreshContentLayout()
        {
            _layoutRefreshPending = false;
            ApplyMeasuredContentHeight();
            _contentHost.QueueSort();
            QueueSort();
            Callable.From(SettleContentLayout).CallDeferred();
        }

        private void SettleContentLayout()
        {
            if (!IsInsideTree())
                return;
            if (ApplyMeasuredContentHeight() && _layoutSettlePasses-- > 0)
            {
                _contentHost.QueueSort();
                QueueSort();
                Callable.From(SettleContentLayout).CallDeferred();
                return;
            }

            ClampScrollAfterLayout();
        }

        private bool ApplyMeasuredContentHeight()
        {
            var height = 0f;
            var measured = false;
            for (var index = _contents.Count - 1; index >= 0; index--)
            {
                var content = _contents[index];
                if (!IsInstanceValid(content))
                {
                    _contents.RemoveAt(index);
                    continue;
                }

                if (!content.IsInsideTree())
                    continue;
                height = Math.Max(height, content.GetCombinedMinimumSize().Y);
                measured = true;
            }

            if (!measured)
                return false;
            height = MathF.Ceiling(Math.Max(0f, height));
            if (Mathf.IsEqualApprox(_contentMinimumHeight, height))
                return false;
            _contentMinimumHeight = height;
            _contentHost.CustomMinimumSize = new(_contentHost.CustomMinimumSize.X, height);
            return true;
        }

        private void ClampScrollAfterLayout()
        {
            var maximum = Math.Max(0d, _verticalScrollBar.MaxValue - _verticalScrollBar.Page);
            ScrollVertical = Math.Min(ScrollVertical, (int)Math.Ceiling(maximum));
            UpdateContentGutter();
            ScheduleVisibleRangeChanged();
        }

        private void OnResized()
        {
            InvalidateContentSize();
            ScheduleVisibleRangeChanged();
        }

        private void ScheduleVisibleRangeChanged()
        {
            if (_visibleRangeNotificationPending || !IsInsideTree())
                return;
            _visibleRangeNotificationPending = true;
            Callable.From(() =>
            {
                _visibleRangeNotificationPending = false;
                if (IsInsideTree())
                    VisibleRangeChanged?.Invoke();
            }).CallDeferred();
        }
    }
}
