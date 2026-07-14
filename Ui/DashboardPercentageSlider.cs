// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;

namespace STS2RitsuMetrics.Ui
{
    public sealed partial class DashboardPercentageSlider : HBoxContainer
    {
        private static ImageTexture? _grabber;
        private static ImageTexture? _grabberHighlight;
        private readonly HSlider _slider;
        private readonly Label _valueLabel;
        private bool _suppressChanges;

        public DashboardPercentageSlider()
        {
            CustomMinimumSize = new(260f, 40f);
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
            AddThemeConstantOverride("separation", 9);
            _slider = new()
            {
                MinValue = 0d,
                MaxValue = 100d,
                Step = 5d,
                Value = 100d,
                CustomMinimumSize = new(170f, 40f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                FocusMode = FocusModeEnum.All,
            };
            ApplySliderStyle(_slider);
            _slider.ValueChanged += OnValueChanged;
            _slider.DragEnded += _ => _slider.ReleaseFocus();
            AddChild(_slider);

            var valuePanel = new PanelContainer { CustomMinimumSize = new(62f, 40f) };
            valuePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new("101A27F5"),
                BorderColor = new("38516CE8"),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
            });
            _valueLabel = new()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _valueLabel.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SecondaryFontSize);
            _valueLabel.AddThemeColorOverride("font_color", new("DCE8F5FF"));
            valuePanel.AddChild(_valueLabel);
            AddChild(valuePanel);
            RefreshValueLabel();
        }

        public int Value => (int)Math.Round(_slider.Value, MidpointRounding.AwayFromZero);

        public event Action<int>? ValueChanged;

        public void Configure(int minimum, int maximum, int step)
        {
            _slider.MinValue = minimum;
            _slider.MaxValue = maximum;
            _slider.Step = step;
            SetValue(Value);
        }

        public void SetValue(int value)
        {
            _suppressChanges = true;
            _slider.Value = Math.Clamp(value, (int)_slider.MinValue, (int)_slider.MaxValue);
            _suppressChanges = false;
            RefreshValueLabel();
        }

        private void OnValueChanged(double _)
        {
            RefreshValueLabel();
            if (!_suppressChanges)
                ValueChanged?.Invoke(Value);
        }

        private void RefreshValueLabel()
        {
            _valueLabel.Text = Value.ToString(CultureInfo.CurrentCulture) + "%";
        }

        private static void ApplySliderStyle(HSlider slider)
        {
            slider.AddThemeStyleboxOverride("slider", Track("142131F2", "334B65E8"));
            slider.AddThemeStyleboxOverride("grabber_area", Track("245A7DF5", "4B9BCCF2"));
            slider.AddThemeStyleboxOverride("grabber_area_highlight", Track("2E7099FA", "74C8F2FF"));
            slider.AddThemeStyleboxOverride("focus", Track("152A3DF2", "72BCE8FF", 2));
            slider.AddThemeIconOverride("grabber", _grabber ??= MakeGrabber("75BDE7FF", "D8F1FFFF"));
            slider.AddThemeIconOverride("grabber_highlight",
                _grabberHighlight ??= MakeGrabber("8AD4F4FF", "FFFFFFFF"));
            slider.AddThemeIconOverride("grabber_disabled", _grabber);
        }

        private static StyleBoxFlat Track(string background, string border, int borderWidth = 1)
        {
            return new()
            {
                BgColor = new(background),
                BorderColor = new(border),
                BorderWidthLeft = borderWidth,
                BorderWidthTop = borderWidth,
                BorderWidthRight = borderWidth,
                BorderWidthBottom = borderWidth,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 8f,
                ContentMarginTop = 8f,
                ContentMarginRight = 8f,
                ContentMarginBottom = 8f,
            };
        }

        private static ImageTexture MakeGrabber(string fillValue, string borderValue)
        {
            const int size = 24;
            var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var fill = new Color(fillValue);
            var border = new Color(borderValue);
            const float center = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = new Vector2(x - center, y - center).Length();
                var color = distance <= 8.5f ? fill : distance <= 10.5f ? border : Colors.Transparent;
                if (distance is > 10.5f and < 11.5f)
                    color = border with { A = border.A * (11.5f - distance) };
                image.SetPixel(x, y, color);
            }

            return ImageTexture.CreateFromImage(image);
        }
    }
}
