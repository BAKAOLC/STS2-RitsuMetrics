// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Data.Models
{
    public sealed class ModSettings
    {
        public const int CurrentDataVersion = 4;
        public const float MinimumScale = 0.65f;
        public const float MaximumScale = 1.75f;
        public const float MinimumOpacity = 0.35f;
        public const float MinimumWidth = 360f;
        public const float MaximumWidth = 1100f;
        public const float MinimumHeight = 240f;
        public const float MaximumHeight = 900f;

        public int DataVersion { get; set; } = CurrentDataVersion;
        public bool OverlayEnabled { get; set; } = true;
        public bool StartCollapsed { get; set; }
        public bool LockWindow { get; set; }
        public bool HideOutsideCombat { get; set; }
        public bool ShowPercentages { get; set; } = true;
        public bool ShowGameOverOverviewButton { get; set; } = true;
        public string DefaultDashboardLayout { get; set; } = DashboardParameterValues.SingleLine;
        public int ScalePercent { get; set; } = 100;
        public int WindowOpacityPercent { get; set; } = 100;
        public int OpacityPercent { get; set; } = 92;
        public float PositionX { get; set; } = 26f;
        public float PositionY { get; set; } = 150f;
        public int Width { get; set; } = 470;
        public int Height { get; set; } = 560;
        public string ToggleKey { get; set; } = nameof(Key.F10);
        public int HistoryCombatLimit { get; set; } = 200;
        public int EventLimitPerCombat { get; set; } = 5000;
        public int TimelineLimitPerCombat { get; set; } = 20000;
        public string DefaultMetricId { get; set; } = MetricIds.DamageContribution;

        public List<DashboardWindowSettings> DashboardWindows { get; set; } =
        [
            new()
            {
                DashboardId = BuiltInDashboardIds.DamageContribution,
                PositionY = 92f,
                Width = 400f,
                Height = 360f,
                Parameters = new(StringComparer.Ordinal)
                {
                    [DashboardParameterIds.Layout] = DashboardParameterValues.SingleLine,
                },
            },
        ];
    }

    public sealed class DashboardWindowSettings
    {
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
        public string DashboardId { get; set; } = BuiltInDashboardIds.Meter;
        public DashboardDataScope Scope { get; set; } = DashboardDataScope.CurrentCombat;
        public string StyleId { get; set; } = "ritsumetrics.compact";
        public float PositionX { get; set; }
        public float PositionY { get; set; } = 92f;
        public float Width { get; set; } = 400f;
        public float Height { get; set; } = 360f;
        public bool HasCustomPosition { get; set; }
        public bool IsCollapsed { get; set; }
        public bool IsLocked { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
    }
}
