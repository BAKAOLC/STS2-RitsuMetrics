// SPDX-License-Identifier: MPL-2.0

using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Data.Models;
using STS2RitsuMetrics.Localization;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Settings
{
    internal static class SettingsBootstrap
    {
        private static bool _initialized;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            RitsuLibFramework.RegisterModSettings(ModConstants.ModId, page => page
                .WithModDisplayName(T("mod.name", "RitsuMetrics"))
                .WithTitle(T("settings.title", "RitsuMetrics"))
                .WithDescription(T("settings.description", "Configure the read-only analytics overlay."))
                .AddSection("display", section => section
                    .WithTitle(T("settings.display", "Display"))
                    .AddToggle("overlay_enabled", T("settings.overlayEnabled", "Show analytics overlay"),
                        Binding(settings => settings.OverlayEnabled,
                            (settings, value) => settings.OverlayEnabled = value),
                        T("settings.overlayEnabled.description", "The F10 hotkey can also show or hide the overlay."))
                    .AddToggle("show_percentages", T("settings.showPercentages", "Show percentages"),
                        Binding(settings => settings.ShowPercentages,
                            (settings, value) => settings.ShowPercentages = value))
                    .AddChoice("default_dashboard_layout",
                        T("settings.defaultDashboardLayout", "Default layout for new dashboards"),
                        Binding(settings => settings.DefaultDashboardLayout,
                            (settings, value) => settings.DefaultDashboardLayout =
                                DashboardPresentation.NormalizeLayout(value)),
                        [
                            new(DashboardParameterValues.Standard,
                                T("dashboard.layout.standard", "Standard")),
                            new(DashboardParameterValues.SingleLine,
                                T("dashboard.layout.singleLine", "Single line")),
                        ],
                        T("settings.defaultDashboardLayout.description",
                            "Used when creating a new floating dashboard; existing windows keep their own layout."))
                    .AddToggle("game_over_overview_button",
                        T("settings.gameOverOverviewButton", "Show run overview button after game over"),
                        Binding(settings => settings.ShowGameOverOverviewButton, (settings, value) =>
                        {
                            settings.ShowGameOverOverviewButton = value;
                            GameOverAnalyticsButtonPatch.RefreshVisibility();
                        }),
                        T("settings.gameOverOverviewButton.description",
                            "Shows a shortcut to this run's analytics on the game-over screen."))
                    .AddIntSlider("scale_percent", T("settings.scale", "UI scale"),
                        Binding(settings => settings.ScalePercent, (settings, value) => settings.ScalePercent = value),
                        65, 175, 5, value => $"{value}%")
                    .AddIntSlider("window_opacity_percent", T("settings.windowOpacity", "Window opacity"),
                        Binding(settings => settings.WindowOpacityPercent,
                            (settings, value) => settings.WindowOpacityPercent = value),
                        20, 100, 5, value => $"{value}%")
                    .AddIntSlider("opacity_percent", T("settings.opacity", "Default background opacity"),
                        Binding(settings => settings.OpacityPercent,
                            (settings, value) => settings.OpacityPercent = value),
                        0, 100, 5, value => $"{value}%")
                    .AddKeyBinding("toggle_key", T("settings.toggleKey", "Toggle hotkey"),
                        Binding(settings => settings.ToggleKey, (settings, value) => settings.ToggleKey = value),
                        allowModifierOnly: false)
                    .AddButton("manage_dashboards", T("settings.manageDashboards", "Manage floating dashboards"),
                        T("settings.manageDashboards.open", "Open"), () => Main.DashboardHost?.ToggleManager())
                    .AddButton("analysis_center", T("analysis.title", "Analytics center"),
                        T("settings.analysisCenter.open", "Open"), () => Main.DashboardHost?.ToggleAnalysisCenter())
                    .AddButton("reset_layout", T("settings.resetLayout", "Reset window layout"),
                        T("settings.resetLayout.confirm", "Reset"), ResetLayout))
                .AddSection("data", section => section
                    .WithTitle(T("settings.data", "Data and history"))
                    .AddIntSlider("history_limit", T("settings.historyLimit", "Stored combat limit"),
                        Binding(settings => settings.HistoryCombatLimit,
                            (settings, value) => settings.HistoryCombatLimit = value),
                        10, 500, 10)
                    .AddIntSlider("event_limit", T("settings.eventLimit", "Event limit per combat"),
                        Binding(settings => settings.EventLimitPerCombat,
                            (settings, value) => settings.EventLimitPerCombat = value),
                        500, 25000, 500)
                    .AddIntSlider("timeline_limit", T("settings.timelineLimit", "Timeline event limit per combat"),
                        Binding(settings => settings.TimelineLimitPerCombat,
                            (settings, value) => settings.TimelineLimitPerCombat = value),
                        1000, 100000, 1000)
                    .AddButton("export_json", T("settings.exportJson", "Export history as JSON"),
                        T("overlay.export", "Export"), () => Export(MetricsExportFormat.Json))
                    .AddButton("export_csv", T("settings.exportCsv", "Export history as CSV"),
                        T("overlay.export", "Export"), () => Export(MetricsExportFormat.Csv))
                    .AddButton("clear_history", T("settings.clearHistory", "Clear saved history"),
                        T("settings.clearHistory.confirm", "Clear"), ClearHistory, ModSettingsButtonTone.Danger)));

            _initialized = true;
        }

        private static ModSettingsValueBinding<ModSettings, T> Binding<T>(
            Func<ModSettings, T> read,
            Action<ModSettings, T> write)
        {
            return ModSettingsBindings.Global(ModConstants.ModId, ModConstants.SettingsKey, read, write);
        }

        private static ModSettingsText T(string key, string fallback)
        {
            return ModSettingsText.I18N(ModLocalization.Instance, key, fallback);
        }

        private static void ResetLayout()
        {
            ModData.ModifySettings(settings =>
            {
                settings.PositionX = 26f;
                settings.PositionY = 150f;
                settings.Width = 470;
                settings.Height = 560;
                settings.ScalePercent = 100;
            });
            Main.DashboardHost?.RestoreDefaultLayout();
        }

        private static void Export(MetricsExportFormat format)
        {
            var result = Main.Api.Export(new()
            {
                Format = format,
                Query = new() { IncludeEvents = true, Limit = 5000 },
            });
            if (result.Success)
                Main.Logger.Info($"Exported analytics history to {result.Path}");
            else
                Main.Logger.Error($"Analytics history export failed: {result.Error}");
        }

        private static void ClearHistory()
        {
            MetricsRepository.ClearHistory();
            Main.Collectors.NotifyChanged();
        }
    }
}
