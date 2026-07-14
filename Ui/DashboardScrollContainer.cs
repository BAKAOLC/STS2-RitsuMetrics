// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Ui
{
    public sealed partial class DashboardScrollContainer : ScrollContainer
    {
        private readonly MarginContainer _contentHost;
        private readonly VScrollBar _verticalScrollBar;

        private int _contentGutter = DefaultContentGutter;

        public DashboardScrollContainer()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            HorizontalScrollMode = ScrollMode.Disabled;
            _contentHost = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
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
        }

        private void UpdateContentGutter()
        {
            _contentHost.AddThemeConstantOverride("margin_right", _verticalScrollBar.Visible ? _contentGutter : 0);
        }
    }
}
