// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Data;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class OverviewRenderer
    {
        private CanvasLayer? _metricDialogLayer;
        private DashboardRenderContext? _lastOverviewContext;
        private OverviewMetricDialog? _metricDialog;

        internal OverviewRenderer()
        {
            Toolbar.Hide();
            ModData.SettingsChanged += RefreshOverviewSettings;
            View.VisibilityChanged += () =>
            {
                if (!View.IsVisibleInTree() && GodotObject.IsInstanceValid(_metricDialog))
                    _metricDialog!.QueueFree();
            };
        }

        private void RefreshOverviewSettings()
        {
            if (_lastOverviewContext != null && View.IsInsideTree())
                Refresh(_lastOverviewContext);
        }

        private static MetricDefinition[] MetricOptions()
        {
            return Main.Api.MetricDefinitions.Concat(OverviewMetricSelection.Derived).ToArray();
        }

        private static MetricDefinition[] SelectedMetrics()
        {
            var options = MetricOptions().ToDictionary(item => item.Id, StringComparer.Ordinal);
            return OverviewMetricSelection.Normalize(ModData.Settings.OverviewMetrics, options.Keys)
                .Select(id => options[id]).ToArray();
        }

        private void OpenMetricConfiguration()
        {
            if (GodotObject.IsInstanceValid(_metricDialog))
                return;
            var options = MetricOptions();
            _metricDialog = new(options,
                OverviewMetricSelection.Normalize(ModData.Settings.OverviewMetrics, options.Select(item => item.Id)),
                ModData.Settings.OverviewShowCharts, ModData.Settings.OverviewShowAnalysis,
                (metrics, charts, analysis) =>
                {
                    ModData.ModifySettings(settings =>
                    {
                        settings.OverviewMetrics = metrics.ToList();
                        settings.OverviewShowCharts = charts;
                        settings.OverviewShowAnalysis = analysis;
                    });
                });
            _metricDialog.Theme = DashboardControlTheme.CreateTypographyTheme();
            var layer = 182;
            for (var current = View.GetParent(); current != null; current = current.GetParent())
                if (current is CanvasLayer canvas)
                {
                    layer = Math.Max(layer, canvas.Layer + 1);
                    break;
                }
            _metricDialogLayer = new CanvasLayer { Layer = layer };
            View.GetTree().Root.AddChild(_metricDialogLayer);
            var dialogLayer = _metricDialogLayer;
            _metricDialog.TreeExited += () => dialogLayer.QueueFree();
            dialogLayer.AddChild(_metricDialog);
        }

        protected override void Dispose(bool disposing)
        {
            ModData.SettingsChanged -= RefreshOverviewSettings;
            if (GodotObject.IsInstanceValid(_metricDialog))
                _metricDialog!.QueueFree();
            base.Dispose(disposing);
        }
    }
}
