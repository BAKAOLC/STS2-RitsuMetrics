// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuLib.Ui.Shell.Theme;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Ui
{
    public enum DashboardButtonKind
    {
        Standard,
        Primary,
        Subtle,
        Danger,
    }

    public enum DashboardControlDensity
    {
        Comfortable,
        Compact,
    }

    public static class DashboardControlTheme
    {
        private const string StyledMeta = "ritsumetrics_control_styled";
        private static ImageTexture? _selectorArrow;
        public static int CaptionFontSize => 15;
        public static int SecondaryFontSize => 16;
        public static int BodyFontSize => 17;
        public static int SelectionItemFontSize => BodyFontSize;
        public static int WindowTitleFontSize => 19;
        public static int SectionTitleFontSize => 21;
        public static int DialogTitleFontSize => 23;
        public static float CompactControlHeight => 40f;
        public static float StandardControlHeight => 46f;
        public static float TouchControlHeight => 44f;
        public static float ScrollBarThickness => 9f;
        public static Font BodyFont => RitsuShellTheme.Current.Font.Body;
        public static Font EmphasisFont => RitsuShellTheme.Current.Font.BodyBold;
        public static Font ButtonFont => RitsuShellTheme.Current.Font.Button;

        public static bool IsStyled(Control control)
        {
            return control.HasMeta(StyledMeta);
        }

        public static Theme CreateTypographyTheme()
        {
            var theme = new Theme
            {
                DefaultFont = BodyFont,
                DefaultFontSize = BodyFontSize,
            };
            theme.SetFont("font", "Label", BodyFont);
            theme.SetFont("font", "LineEdit", BodyFont);
            theme.SetFont("font", "TextEdit", BodyFont);
            theme.SetFont("font", "TooltipLabel", BodyFont);
            theme.SetFont("normal_font", "RichTextLabel", BodyFont);
            theme.SetFont("italics_font", "RichTextLabel", BodyFont);
            theme.SetFont("mono_font", "RichTextLabel", BodyFont);
            theme.SetFont("bold_font", "RichTextLabel", EmphasisFont);
            theme.SetFont("bold_italics_font", "RichTextLabel", EmphasisFont);
            theme.SetFont("font", "Button", ButtonFont);
            theme.SetFont("font", "CheckButton", ButtonFont);
            theme.SetFont("font", "CheckBox", ButtonFont);
            theme.SetFont("font", "MenuButton", ButtonFont);
            theme.SetFontSize("font_size", "Button", BodyFontSize + 1);
            theme.SetFontSize("font_size", "CheckButton", BodyFontSize + 1);
            theme.SetFontSize("font_size", "CheckBox", BodyFontSize + 1);
            theme.SetFontSize("font_size", "MenuButton", BodyFontSize + 1);
            theme.SetFontSize("font_size", "TooltipLabel", BodyFontSize);
            theme.SetFontSize("normal_font_size", "RichTextLabel", BodyFontSize);
            theme.SetFontSize("bold_font_size", "RichTextLabel", BodyFontSize);
            theme.SetColor("font_color", "TooltipLabel", new("E7EFF9FF"));
            theme.SetStylebox("panel", "TooltipPanel", Box("0A111BFC", "587594F4", 1));
            return theme;
        }

        public static void ApplyTypography(Control root, Theme theme)
        {
            root.Theme = theme;
        }

        public static void ApplySelector(Button button, DashboardStyleDefinition? dashboardStyle = null,
            DashboardControlDensity density = DashboardControlDensity.Comfortable)
        {
            button.SetMeta(StyledMeta, true);
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            var compact = density != DashboardControlDensity.Comfortable;
            var height = compact ? CompactControlHeight : StandardControlHeight;
            button.CustomMinimumSize = new(button.CustomMinimumSize.X,
                Math.Max(height, button.CustomMinimumSize.Y));
            button.Alignment = HorizontalAlignment.Left;
            button.IconAlignment = HorizontalAlignment.Right;
            button.Icon = _selectorArrow ??= MakeArrow();
            button.ExpandIcon = false;
            button.AddThemeFontSizeOverride("font_size", compact ? SecondaryFontSize : BodyFontSize + 1);
            button.AddThemeColorOverride("font_color", Color(dashboardStyle?.TextColor, "DCE6F4FF"));
            button.AddThemeColorOverride("font_hover_color", new("F4F8FDFF"));
            button.AddThemeColorOverride("font_pressed_color", new("FFFFFFFF"));
            button.AddThemeColorOverride("font_focus_color", new("FFFFFFFF"));
            button.AddThemeColorOverride("font_disabled_color", new("718096FF"));
            button.AddThemeStyleboxOverride("normal", Box(dashboardStyle?.TrackColor ?? "121C2AF4",
                dashboardStyle?.BorderColor ?? "344A64E8", 1));
            button.AddThemeStyleboxOverride("hover", Box("19283AF8", "5A82ACEF", 1));
            button.AddThemeStyleboxOverride("pressed", Box("20334AFB", "75A9D8FF", 1));
            button.AddThemeStyleboxOverride("focus", Box("17283CF0", "5FB2EEFF", 2));
            button.AddThemeStyleboxOverride("disabled", Box("0C121BDC", "283647B8", 1));
        }

        public static void ApplySelectionPopup(PopupPanel popup, DashboardStyleDefinition? dashboardStyle = null)
        {
            popup.TransparentBg = true;
            var panel = Box(dashboardStyle?.BackgroundColor ?? "0B111BFC",
                dashboardStyle?.BorderColor ?? "4B6686F4", 1, 7);
            panel.ContentMarginLeft = 0f;
            panel.ContentMarginTop = 0f;
            panel.ContentMarginRight = 0f;
            panel.ContentMarginBottom = 0f;
            popup.AddThemeStyleboxOverride("panel", panel);
        }

        public static void ApplySelectionItem(Button button, bool selected,
            DashboardStyleDefinition? dashboardStyle = null)
        {
            ApplyButton(button, selected ? DashboardButtonKind.Primary : DashboardButtonKind.Subtle, true,
                dashboardStyle);
            button.AddThemeFontOverride("font", ButtonFont);
            button.AddThemeFontSizeOverride("font_size", SelectionItemFontSize);
            button.Alignment = HorizontalAlignment.Left;
            button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.ClipText = true;
        }

        public static void ApplySearch(LineEdit search, DashboardStyleDefinition? dashboardStyle = null,
            DashboardControlDensity density = DashboardControlDensity.Comfortable)
        {
            search.SetMeta(StyledMeta, true);
            var compact = density != DashboardControlDensity.Comfortable;
            var height = compact ? CompactControlHeight : TouchControlHeight;
            search.CustomMinimumSize = new(search.CustomMinimumSize.X,
                Math.Max(height, search.CustomMinimumSize.Y));
            search.AddThemeFontSizeOverride("font_size", compact ? SecondaryFontSize : BodyFontSize);
            search.AddThemeColorOverride("font_color", Color(dashboardStyle?.TextColor, "DCE6F4FF"));
            search.AddThemeColorOverride("font_placeholder_color", new("71849CFF"));
            search.AddThemeColorOverride("caret_color", new("6BC0F1FF"));
            search.AddThemeColorOverride("selection_color", new("315F82D9"));
            search.AddThemeStyleboxOverride("normal", Box(dashboardStyle?.TrackColor ?? "0D1622F4",
                dashboardStyle?.BorderColor ?? "2D4056E8", 1));
            search.AddThemeStyleboxOverride("focus", Box("101D2CF8", "5FADE5FF", 2));
        }

        public static void ApplyButton(Button button, DashboardButtonKind kind = DashboardButtonKind.Standard,
            bool compact = false, DashboardStyleDefinition? dashboardStyle = null)
        {
            ApplyButton(button, kind,
                compact ? DashboardControlDensity.Compact : DashboardControlDensity.Comfortable, dashboardStyle);
        }

        public static void ApplyButton(Button button, DashboardButtonKind kind, DashboardControlDensity density,
            DashboardStyleDefinition? dashboardStyle = null)
        {
            button.SetMeta(StyledMeta, true);
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            var compact = density != DashboardControlDensity.Comfortable;
            var minimumHeight = compact ? CompactControlHeight : StandardControlHeight;
            button.CustomMinimumSize = new(button.CustomMinimumSize.X,
                Math.Max(minimumHeight, button.CustomMinimumSize.Y));
            button.AddThemeFontSizeOverride("font_size", compact ? BodyFontSize : BodyFontSize + 1);
            var colors = ColorsFor(kind, dashboardStyle);
            button.AddThemeColorOverride("font_color", new(colors.Text));
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeColorOverride("font_pressed_color", Colors.White);
            button.AddThemeColorOverride("font_focus_color", Colors.White);
            button.AddThemeColorOverride("font_disabled_color", new("718096FF"));
            button.AddThemeStyleboxOverride("normal", Box(colors.Normal, colors.Border, 1));
            button.AddThemeStyleboxOverride("hover", Box(colors.Hover, colors.HoverBorder, 1));
            button.AddThemeStyleboxOverride("pressed", Box(colors.Pressed, colors.HoverBorder, 1));
            button.AddThemeStyleboxOverride("focus", Box(colors.Hover, "76BDEBFF", 2));
            button.AddThemeStyleboxOverride("disabled", Box("0B1119C8", "283545A0", 1));
        }

        public static void ApplyIconButton(Button button, DashboardButtonKind kind = DashboardButtonKind.Subtle,
            DashboardStyleDefinition? dashboardStyle = null, bool compact = false)
        {
            var size = compact ? CompactControlHeight : StandardControlHeight;
            button.CustomMinimumSize = new(Math.Max(size, button.CustomMinimumSize.X),
                Math.Max(size, button.CustomMinimumSize.Y));
            ApplyButton(button, kind, compact, dashboardStyle);
            button.AddThemeFontSizeOverride("font_size", compact ? WindowTitleFontSize : SectionTitleFontSize);
        }

        public static void ApplyScrollContainer(ScrollContainer scroll)
        {
            scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            var vertical = scroll.GetVScrollBar();
            vertical.CustomMinimumSize = new(ScrollBarThickness, 0f);
            ApplyScrollBar(vertical);
            var horizontal = scroll.GetHScrollBar();
            horizontal.CustomMinimumSize = new(0f, ScrollBarThickness);
            ApplyScrollBar(horizontal);
        }

        public static HSeparator Separator()
        {
            var separator = new HSeparator { CustomMinimumSize = new(0f, 9f) };
            separator.AddThemeStyleboxOverride("separator", new StyleBoxLine
            {
                Color = new("344A62C8"),
                Thickness = 1,
            });
            return separator;
        }

        public static StyleBoxFlat DialogStyle(float padding = 20f)
        {
            var style = Box("0D1622FE", "6686A8FF", 1, 8);
            style.ContentMarginLeft = padding;
            style.ContentMarginTop = padding - 2f;
            style.ContentMarginRight = padding;
            style.ContentMarginBottom = padding - 2f;
            style.ShadowColor = new(0f, 0f, 0f, 0.8f);
            style.ShadowSize = 16;
            return style;
        }

        public static void ApplyDialogTitle(Label label)
        {
            label.AddThemeFontSizeOverride("font_size", DialogTitleFontSize);
            label.AddThemeColorOverride("font_color", new("F3F7FCFF"));
        }

        public static void ApplyFieldLabel(Label label)
        {
            label.AddThemeFontSizeOverride("font_size", CaptionFontSize);
            label.AddThemeColorOverride("font_color", new("BCCADDFF"));
        }

        public static void ApplySecondaryText(Label label)
        {
            label.AddThemeFontSizeOverride("font_size", SecondaryFontSize);
            label.AddThemeColorOverride("font_color", new("AEBED2FF"));
        }

        private static void ApplyScrollBar(ScrollBar scrollBar)
        {
            scrollBar.AddThemeStyleboxOverride("scroll", ScrollBox("101925D9", "27394EE0"));
            scrollBar.AddThemeStyleboxOverride("scroll_focus", ScrollBox("132033E8", "426181EF"));
            scrollBar.AddThemeStyleboxOverride("grabber", ScrollBox("49647FD9", "6E8EACEF"));
            scrollBar.AddThemeStyleboxOverride("grabber_highlight", ScrollBox("6689A8F2", "8CB6D7FF"));
            scrollBar.AddThemeStyleboxOverride("grabber_pressed", ScrollBox("78A5C8FF", "A8D4F3FF"));
        }

        private static ButtonColors ColorsFor(DashboardButtonKind kind, DashboardStyleDefinition? style)
        {
            return kind switch
            {
                DashboardButtonKind.Primary => new("173A54FA", "327DB0FF", "215273FF", "193E5BFF",
                    "68BCEFFF", "F4FAFFFF"),
                DashboardButtonKind.Danger => new("351923F4", "7E394AFF", "512331FF", "2D151DF8",
                    "D95D75FF", "FFDCE4FF"),
                DashboardButtonKind.Subtle => new(style?.HeaderColor ?? "101824E8",
                    style?.BorderColor ?? "30445BE0", "20334AF4", style?.TrackColor ?? "0B111AF2",
                    "5C86AEF2", style?.SecondaryTextColor ?? "B8C7D9FF"),
                _ => new(style?.SurfaceColor ?? "152131F4", style?.BorderColor ?? "3C526CE8", "21354CF8",
                    style?.TrackColor ?? "101A27F8", "6092BDF5", style?.TextColor ?? "DCE6F4FF"),
            };
        }

        private static Color Color(string? value, string fallback)
        {
            return new(value ?? fallback);
        }

        private static StyleBoxFlat Box(string background, string border, int borderWidth, int radius = 5)
        {
            return new()
            {
                BgColor = new(background),
                BorderColor = new(border),
                BorderWidthLeft = borderWidth,
                BorderWidthTop = borderWidth,
                BorderWidthRight = borderWidth,
                BorderWidthBottom = borderWidth,
                CornerRadiusTopLeft = radius,
                CornerRadiusTopRight = radius,
                CornerRadiusBottomLeft = radius,
                CornerRadiusBottomRight = radius,
                ContentMarginLeft = 9f,
                ContentMarginTop = 5f,
                ContentMarginRight = 9f,
                ContentMarginBottom = 5f,
            };
        }

        private static StyleBoxFlat ScrollBox(string background, string border)
        {
            return new()
            {
                BgColor = new(background),
                BorderColor = new(border),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
            };
        }

        private static ImageTexture MakeArrow()
        {
            var image = Image.CreateEmpty(10, 6, false, Image.Format.Rgba8);
            var color = new Color("8FC8EDFF");
            for (var y = 0; y < 5; y++)
            for (var x = y; x < 10 - y; x++)
                image.SetPixel(x, y, color);
            return ImageTexture.CreateFromImage(image);
        }

        private sealed record ButtonColors(
            string Normal,
            string Border,
            string Hover,
            string Pressed,
            string HoverBorder,
            string Text);
    }
}
