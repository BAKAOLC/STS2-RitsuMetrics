// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Builders;
using STS2RitsuLib.Patching.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal static class GameOverAnalyticsButtonPatch
    {
        private const string ButtonName = "RitsuMetricsRunOverviewButton";
        private static bool _initialized;
        private static ModPatcher? _patcher;
        private static WeakReference<Button>? _button;

        internal static void Initialize()
        {
            if (_initialized)
                return;
            var ready = typeof(NGameOverScreen).GetMethod(nameof(NGameOverScreen._Ready),
                            BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new MissingMethodException(typeof(NGameOverScreen).FullName,
                            nameof(NGameOverScreen._Ready));
            var builder = new DynamicPatchBuilder("game_over_analytics");
            builder.Add(ready,
                postfix: DynamicPatchBuilder.FromMethod(typeof(GameOverAnalyticsButtonPatch), nameof(Postfix)),
                isCritical: false,
                description: "Add a shortcut from the game-over screen to the completed run overview");
            _patcher = RitsuLibFramework.CreatePatcher(ModConstants.ModId, "game-over-analytics",
                "game-over analytics shortcut");
            if (!_patcher.ApplyDynamic(builder))
                Main.Logger.Warn("Could not install the optional game-over analytics shortcut patch.");
            _initialized = true;
        }

        internal static void RefreshVisibility()
        {
            if (_button?.TryGetTarget(out var button) != true || !GodotObject.IsInstanceValid(button))
                return;
            button.Visible = ModData.Settings.ShowGameOverOverviewButton;
        }

        private static void Postfix(NGameOverScreen __instance)
        {
            Callable.From(() => Inject(__instance)).CallDeferred();
        }

        private static void Inject(NGameOverScreen screen)
        {
            try
            {
                if (!GodotObject.IsInstanceValid(screen) || screen.FindChild(ButtonName, true, false) != null)
                    return;
                var mainMenuButton = screen.GetNodeOrNull<Control>("%MainMenuButton");
                if (mainMenuButton?.GetParent() is not Control parent)
                    return;

                var button = new Button
                {
                    Name = ButtonName,
                    Text = ModLocalization.Get("analysis.openCurrentRunOverview", "Open this run overview"),
                    TooltipText = ModLocalization.Get("analysis.openCurrentRunOverview.hint",
                        "Open RitsuMetrics with the complete run selected"),
                    CustomMinimumSize = new(320f, 44f),
                    FocusMode = Control.FocusModeEnum.None,
                    Theme = DashboardControlTheme.CreateTypographyTheme(),
                    ZIndex = 10,
                    Visible = ModData.Settings.ShowGameOverOverviewButton,
                };
                DashboardControlTheme.ApplyButton(button, DashboardButtonKind.Primary);
                button.Pressed += () => Main.DashboardHost?.OpenCurrentRunOverview();
                parent.AddChild(button);
                button.AnchorLeft = 0.5f;
                button.AnchorRight = 0.5f;
                button.AnchorTop = mainMenuButton.AnchorTop;
                button.AnchorBottom = mainMenuButton.AnchorBottom;
                button.OffsetLeft = -160f;
                button.OffsetRight = 160f;
                button.OffsetTop = mainMenuButton.OffsetBottom + 10f;
                button.OffsetBottom = mainMenuButton.OffsetBottom + 54f;
                _button = new(button);
                Main.Logger.Debug("Injected the game-over run overview shortcut.");
            }
            catch (Exception exception)
            {
                Main.Logger.Warn($"Could not add the game-over analytics shortcut: {exception.Message}");
            }
        }
    }
}
