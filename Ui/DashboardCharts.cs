// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;

namespace STS2RitsuMetrics.Ui
{
    public readonly record struct DashboardBarDatum(
        string Label,
        decimal Value,
        string Color,
        string Detail = "");

    public readonly record struct DashboardDonutDatum(
        string Label,
        decimal Value,
        string Color);

    public readonly record struct DashboardLineDatum(
        string Label,
        decimal Value);

    public sealed partial class DashboardBarChart : Control
    {
        private DashboardBarDatum[] _data = [];
        private int _fontSize = 14;

        public DashboardBarChart()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        public void SetData(IEnumerable<DashboardBarDatum> data, int fontSize)
        {
            _data = data.Where(item => item.Value >= 0m).ToArray();
            _fontSize = Math.Max(10, fontSize);
            var rowHeight = Math.Max(25, _fontSize + 11);
            CustomMinimumSize = new(240f, Math.Max(52f, _data.Length * rowHeight));
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_data.Length == 0 || Size.X < 120f)
                return;
            var font = DashboardControlTheme.BodyFont;
            var rowHeight = Math.Max(25f, _fontSize + 11f);
            var labelWidth = Math.Clamp(Size.X * 0.34f, 92f, 190f);
            var valueWidth = Math.Clamp(Size.X * 0.18f, 62f, 92f);
            var barX = labelWidth + 8f;
            var barWidth = Math.Max(36f, Size.X - barX - valueWidth - 8f);
            var maximum = Math.Max(1m, _data.Max(item => item.Value));
            var textColor = new Color("DCE6F4FF");
            var secondary = new Color("91A2B8FF");
            var track = new Color("111A27E8");
            for (var index = 0; index < _data.Length; index++)
            {
                var item = _data[index];
                var top = index * rowHeight;
                var baseline = top + (rowHeight + font.GetAscent(_fontSize) - font.GetDescent(_fontSize)) / 2f;
                DrawString(font, new(0f, baseline), Trim(font, item.Label, _fontSize, labelWidth - 4f),
                    HorizontalAlignment.Left, labelWidth - 4f, _fontSize, textColor);
                var barY = top + (rowHeight - 12f) / 2f;
                DrawRect(new(barX, barY, barWidth, 12f), track);
                var fillWidth = barWidth * (float)(item.Value / maximum);
                if (fillWidth > 0f)
                    DrawRect(new(barX, barY, Math.Max(2f, fillWidth), 12f),
                        ColorOf(item.Color) with { A = 0.88f });
                var detail = string.IsNullOrWhiteSpace(item.Detail) ? Format(item.Value) : item.Detail;
                DrawString(font, new(barX + barWidth + 7f, baseline), detail, HorizontalAlignment.Right,
                    valueWidth, _fontSize, secondary);
            }
        }

        private static string Trim(Font font, string text, int fontSize, float width)
        {
            if (font.GetStringSize(text, fontSize: fontSize).X <= width)
                return text;
            const string ellipsis = "…";
            var end = text.Length;
            while (end > 1 && font.GetStringSize(text[..end] + ellipsis, fontSize: fontSize).X > width)
                end--;
            return text[..end] + ellipsis;
        }

        private static string Format(decimal value)
        {
            return value == decimal.Truncate(value)
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N1", CultureInfo.CurrentCulture);
        }

        private static Color ColorOf(string value)
        {
            try
            {
                return new(value);
            }
            catch
            {
                return new("56A8E8FF");
            }
        }
    }

    public sealed partial class DashboardDonutChart : Control
    {
        private DashboardDonutDatum[] _data = [];
        private int _fontSize = 14;

        public DashboardDonutChart()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new(240f, 190f);
        }

        public void SetData(IEnumerable<DashboardDonutDatum> data, int fontSize)
        {
            _data = data.Where(item => item.Value > 0m).Take(8).ToArray();
            _fontSize = Math.Max(10, fontSize);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_data.Length == 0 || Size.X < 160f || Size.Y < 100f)
                return;
            var font = DashboardControlTheme.BodyFont;
            var total = _data.Sum(item => item.Value);
            var chartWidth = Math.Min(Size.X * 0.48f, Size.Y);
            var radius = Math.Max(26f, Math.Min(chartWidth, Size.Y) * 0.32f);
            var center = new Vector2(chartWidth * 0.5f, Size.Y * 0.5f);
            var stroke = Math.Max(13f, radius * 0.34f);
            var angle = -MathF.PI / 2f;
            const float seamOverlap = 0.006f;
            foreach (var item in _data)
            {
                var sweep = MathF.Tau * (float)(item.Value / total);
                DrawArc(center, radius, angle - seamOverlap, angle + sweep + seamOverlap, 48,
                    ColorOf(item.Color), stroke, true);
                angle += sweep;
            }

            var totalText = Format(total);
            var totalWidth = font.GetStringSize(totalText, fontSize: _fontSize + 2).X;
            DrawString(font,
                new(center.X - totalWidth / 2f,
                    center.Y + (font.GetAscent(_fontSize + 2) - font.GetDescent(_fontSize + 2)) / 2f), totalText,
                HorizontalAlignment.Left, -1f, _fontSize + 2, new("F0F4FAFF"));

            var legendX = chartWidth + 5f;
            var legendWidth = Math.Max(50f, Size.X - legendX);
            var rowHeight = Math.Max(21f, _fontSize + 7f);
            var startY = Math.Max(0f, (Size.Y - _data.Length * rowHeight) / 2f);
            for (var index = 0; index < _data.Length; index++)
            {
                var item = _data[index];
                var y = startY + index * rowHeight;
                DrawCircle(new(legendX + 5f, y + rowHeight / 2f), 4f, ColorOf(item.Color));
                var label = $"{item.Label}  {item.Value / total:P0}";
                var baseline = y + (rowHeight + font.GetAscent(_fontSize) - font.GetDescent(_fontSize)) / 2f;
                DrawString(font, new(legendX + 15f, baseline), label, HorizontalAlignment.Left,
                    legendWidth - 15f, _fontSize, new("B7C4D5FF"));
            }
        }

        private static string Format(decimal value)
        {
            return value == decimal.Truncate(value)
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N1", CultureInfo.CurrentCulture);
        }

        private static Color ColorOf(string value)
        {
            try
            {
                return new(value);
            }
            catch
            {
                return new("56A8E8FF");
            }
        }
    }

    public sealed partial class DashboardLineChart : Control
    {
        private string _color = "56A8E8FF";
        private DashboardLineDatum[] _data = [];
        private int _fontSize = 13;

        public DashboardLineChart()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new(240f, 190f);
        }

        public void SetData(IEnumerable<DashboardLineDatum> data, string color, int fontSize)
        {
            _data = data.ToArray();
            _color = color;
            _fontSize = Math.Max(10, fontSize);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_data.Length == 0 || Size.X < 120f || Size.Y < 90f)
                return;
            var font = DashboardControlTheme.BodyFont;
            const float left = 42f;
            const float right = 10f;
            const float top = 10f;
            var bottom = _fontSize + 12f;
            var width = Math.Max(20f, Size.X - left - right);
            var height = Math.Max(20f, Size.Y - top - bottom);
            var maximum = Math.Max(1m, _data.Max(item => item.Value));
            var grid = new Color("52627D38");
            var secondary = new Color("8999AEFF");
            for (var step = 0; step <= 4; step++)
            {
                var y = top + height * step / 4f;
                DrawLine(new(left, y), new(left + width, y), grid, 1f);
                var value = maximum * (4 - step) / 4m;
                DrawString(font, new(0f, y + _fontSize * 0.35f), Short(value), HorizontalAlignment.Right,
                    left - 6f, Math.Max(9, _fontSize - 2), secondary);
            }

            var color = ColorOf(_color);
            var points = new Vector2[_data.Length];
            for (var index = 0; index < _data.Length; index++)
            {
                var x = _data.Length == 1 ? left + width / 2f : left + width * index / (_data.Length - 1f);
                var y = top + height - height * (float)(_data[index].Value / maximum);
                points[index] = new(x, y);
                if (index > 0)
                    DrawLine(points[index - 1], points[index], color with { A = 0.9f }, 2.5f, true);
                DrawCircle(points[index], 3.5f, color);
            }

            var stride = Math.Max(1, (int)Math.Ceiling(_data.Length / Math.Max(2f, width / 55f)));
            for (var index = 0; index < _data.Length; index++)
            {
                if (index % stride != 0 && index != _data.Length - 1)
                    continue;
                var x = points[index].X;
                DrawString(font, new(x - 22f, top + height + _fontSize + 5f), _data[index].Label,
                    HorizontalAlignment.Center, 44f, Math.Max(9, _fontSize - 1), secondary);
            }
        }

        private static string Short(decimal value)
        {
            var absolute = Math.Abs(value);
            return absolute >= 1_000_000m
                ? (value / 1_000_000m).ToString("0.#M", CultureInfo.CurrentCulture)
                : absolute >= 1_000m
                    ? (value / 1_000m).ToString("0.#K", CultureInfo.CurrentCulture)
                    : value.ToString("0.#", CultureInfo.CurrentCulture);
        }

        private static Color ColorOf(string value)
        {
            try
            {
                return new(value);
            }
            catch
            {
                return new("56A8E8FF");
            }
        }
    }
}
