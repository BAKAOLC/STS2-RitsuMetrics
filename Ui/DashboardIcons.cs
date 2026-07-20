// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Ui
{
    internal enum DashboardIcon
    {
        ManageDashboards,
        Minimize,
        Close,
        Settings,
        Focus,
        Configure,
        ResetGeometry,
        Lock,
        Unlock,
        Duplicate,
        Refresh,
        Delete,
        Expand,
        Collapse,
        Back,
        DragHandle,
        NoData,
    }

    internal static class DashboardIcons
    {
        private static readonly Dictionary<(DashboardIcon Icon, int PixelSize), ImageTexture> Textures = [];

        internal static void Apply(Button button, DashboardIcon icon, int maximumWidth = 18)
        {
            button.Icon = Texture(icon, maximumWidth);
            button.ExpandIcon = false;
        }

        internal static void ApplyIconOnly(Button button, DashboardIcon icon, int maximumWidth = 18)
        {
            button.Text = string.Empty;
            button.Icon = null;
            button.ExpandIcon = false;
            var iconLayer = new DashboardButtonIcon(Texture(icon, maximumWidth * 2), maximumWidth,
                button.Disabled ? new("718096FF") : Colors.White);
            button.AddChild(iconLayer);
            iconLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }

        internal static TextureRect View(DashboardIcon icon, float size = 18f, Color? color = null)
        {
            return new()
            {
                Texture = Texture(icon, Math.Max(24, (int)MathF.Ceiling(size * 2f))),
                CustomMinimumSize = new(size, size),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = color ?? Colors.White,
            };
        }

        internal static Texture2D Texture(DashboardIcon icon, int pixelSize = 24)
        {
            pixelSize = Math.Clamp(pixelSize, 8, 96);
            var key = (icon, pixelSize);
            if (Textures.TryGetValue(key, out var texture))
                return texture;

            var image = new Image();
            var error = image.LoadSvgFromString(Svg(Markup(icon)), pixelSize / 24f);
            if (error != Error.Ok)
                throw new InvalidOperationException($"Could not load the embedded '{icon}' SVG icon: {error}.");
            if (image.GetWidth() != pixelSize || image.GetHeight() != pixelSize)
                image.Resize(pixelSize, pixelSize, Image.Interpolation.Lanczos);

            texture = ImageTexture.CreateFromImage(image);
            Textures.Add(key, texture);
            return texture;
        }

        private static string Svg(string markup)
        {
            return $"""
                    <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"
                         fill="none" stroke="#f2f6fb" stroke-width="2" stroke-linecap="round"
                         stroke-linejoin="round">{markup}</svg>
                    """;
        }

        private static string Markup(DashboardIcon icon)
        {
            return icon switch
            {
                DashboardIcon.ManageDashboards => """
                                                  <rect width="7" height="9" x="3" y="3" rx="1"/>
                                                  <rect width="7" height="5" x="14" y="3" rx="1"/>
                                                  <rect width="7" height="9" x="14" y="12" rx="1"/>
                                                  <rect width="7" height="5" x="3" y="16" rx="1"/>
                                                  """,
                DashboardIcon.Minimize => """<path d="M5 12h14"/>""",
                DashboardIcon.Close => """<path d="M18 6 6 18"/><path d="m6 6 12 12"/>""",
                DashboardIcon.Settings => """
                                          <path d="M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915"/>
                                          <circle cx="12" cy="12" r="3"/>
                                          """,
                DashboardIcon.Focus => """
                                       <path d="M3 7V5a2 2 0 0 1 2-2h2"/><path d="M17 3h2a2 2 0 0 1 2 2v2"/>
                                       <path d="M21 17v2a2 2 0 0 1-2 2h-2"/><path d="M7 21H5a2 2 0 0 1-2-2v-2"/>
                                       """,
                DashboardIcon.Configure => """
                                           <path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/>
                                           <path d="m15 5 4 4"/>
                                           """,
                DashboardIcon.ResetGeometry => """
                                               <line x1="22" x2="2" y1="6" y2="6"/><line x1="22" x2="2" y1="18" y2="18"/>
                                               <line x1="6" x2="6" y1="2" y2="22"/><line x1="18" x2="18" y1="2" y2="22"/>
                                               """,
                DashboardIcon.Lock => """
                                      <circle cx="12" cy="16" r="1"/><rect x="3" y="10" width="18" height="12" rx="2"/>
                                      <path d="M7 10V7a5 5 0 0 1 10 0v3"/>
                                      """,
                DashboardIcon.Unlock => """
                                        <circle cx="12" cy="16" r="1"/><rect width="18" height="12" x="3" y="10" rx="2"/>
                                        <path d="M7 10V7a5 5 0 0 1 9.33-2.5"/>
                                        """,
                DashboardIcon.Duplicate => """
                                           <rect width="14" height="14" x="8" y="8" rx="2" ry="2"/>
                                           <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>
                                           """,
                DashboardIcon.Refresh => """
                                         <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/>
                                         <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>
                                         """,
                DashboardIcon.Delete => """
                                        <path d="M10 11v6"/><path d="M14 11v6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/>
                                        <path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                                        """,
                DashboardIcon.Expand => """<path d="m9 18 6-6-6-6"/>""",
                DashboardIcon.Collapse => """<path d="m6 9 6 6 6-6"/>""",
                DashboardIcon.Back => """<path d="m12 19-7-7 7-7"/><path d="M19 12H5"/>""",
                DashboardIcon.DragHandle => """
                                            <circle cx="9" cy="12" r="1"/><circle cx="9" cy="5" r="1"/><circle cx="9" cy="19" r="1"/>
                                            <circle cx="15" cy="12" r="1"/><circle cx="15" cy="5" r="1"/><circle cx="15" cy="19" r="1"/>
                                            """,
                DashboardIcon.NoData => """
                                        <path d="m17 17 5 5"/><path d="M19.323 13.744A9 3 0 0 0 21 12"/><path d="M21 13.127V5"/>
                                        <path d="m22 17-5 5"/><path d="M3 12A9 3 0 0 0 13.563 14.954"/>
                                        <path d="M3 5V19A9 3 0 0 0 13 21.981"/><ellipse cx="12" cy="5" rx="9" ry="3"/>
                                        """,
                _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
            };
        }
    }

    internal sealed partial class DashboardButtonIcon : Control
    {
        private const float MinimumInset = 8f;
        private readonly float _maximumSize;
        private readonly Color _modulate;
        private readonly Texture2D _texture;

        internal DashboardButtonIcon(Texture2D texture, float maximumSize, Color modulate)
        {
            _texture = texture;
            _maximumSize = Math.Max(1f, maximumSize);
            _modulate = modulate;
            MouseFilter = MouseFilterEnum.Ignore;
            Resized += QueueRedraw;
        }

        public override void _Draw()
        {
            var availableSize = Math.Max(0f, Math.Min(Size.X, Size.Y) - MinimumInset);
            var iconSize = Math.Min(_maximumSize, availableSize);
            if (iconSize <= 0f)
                return;

            var topLeft = (Size - new Vector2(iconSize, iconSize)) / 2f;
            DrawTextureRect(_texture, new(topLeft, new(iconSize, iconSize)), false, _modulate);
        }
    }
}
