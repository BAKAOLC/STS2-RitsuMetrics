// SPDX-License-Identifier: MPL-2.0

using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Settings;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics
{
    [ModInitializer(nameof(Initialize))]
    public static class Main
    {
        public static readonly Logger Logger = RitsuLibFramework.CreateLogger(ModConstants.ModId);

        internal static CollectorRegistry Collectors { get; private set; } = null!;
        internal static DashboardRegistry Dashboards { get; private set; } = null!;
        internal static MetricsRepository Repository { get; private set; } = null!;
        internal static RitsuMetricsApi Api { get; private set; } = null!;
        internal static DashboardHost? DashboardHost { get; set; }

        public static bool IsActive { get; private set; }

        public static void Initialize()
        {
            try
            {
                Logger.Info(
                    $"Initializing {ModConstants.ModId} {ModConstants.Version} (API v{ModConstants.ApiVersion})");
                ModData.Initialize();
                Collectors = new(Logger);
                Collectors.RegisterBuiltIns();
                Dashboards = new();
                BuiltinDashboardCatalog.Register(Dashboards);
                Repository = new();
                var queries = new QueryService(Repository);
                var exports = new ExportService(queries);
                var analytics = new CombatAnalyticsService(Repository, Collectors);
                Api = new(Collectors, Repository, queries, exports, Dashboards, analytics.PublishCustom);
                RitsuMetricsApi.Instance = Api;
                SettingsBootstrap.Initialize();
                OverlayBootstrap.Initialize();
                GameOverAnalyticsButtonPatch.Initialize();
                analytics.Initialize();
                IsActive = true;
                Logger.Info("RitsuMetrics initialized; all capture paths are observational and local-only.");
            }
            catch (Exception exception)
            {
                IsActive = false;
                Logger.Error($"RitsuMetrics initialization failed: {exception}");
            }
        }
    }
}
