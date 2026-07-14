// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Api
{
    public sealed class RitsuMetricsApi : IRitsuMetricsApi
    {
        private readonly CollectorRegistry _collectors;
        private readonly Func<string, MetricObservation, bool> _customPublisher;
        private readonly DashboardRegistry _dashboards;
        private readonly ExportService _exports;
        private readonly QueryService _queries;
        private readonly MetricsRepository _repository;

        internal RitsuMetricsApi(
            CollectorRegistry collectors,
            MetricsRepository repository,
            QueryService queries,
            ExportService exports,
            DashboardRegistry dashboards,
            Func<string, MetricObservation, bool> customPublisher)
        {
            _collectors = collectors;
            _repository = repository;
            _queries = queries;
            _exports = exports;
            _dashboards = dashboards;
            _customPublisher = customPublisher;
        }

        public static IRitsuMetricsApi? Instance { get; internal set; }

        public int Version => ModConstants.ApiVersion;

        public IReadOnlyCollection<MetricDefinition> MetricDefinitions => _collectors.Definitions;
        public IReadOnlyCollection<DashboardDefinition> DashboardDefinitions => _dashboards.Definitions;
        public IReadOnlyCollection<DashboardStyleDefinition> DashboardStyles => _dashboards.Styles;

        public IReadOnlyCollection<DashboardWindowInfo> DashboardWindows =>
            Main.DashboardHost?.WindowInfos ?? [];

        public event Action<MetricObservation>? ObservationPublished
        {
            add => _collectors.ObservationPublished += value;
            remove => _collectors.ObservationPublished -= value;
        }

        public event Action<CombatTimelineEvent>? TimelineEventPublished
        {
            add => _collectors.TimelineEventPublished += value;
            remove => _collectors.TimelineEventPublished -= value;
        }

        public event Action? SnapshotChanged
        {
            add => _collectors.SnapshotChanged += value;
            remove => _collectors.SnapshotChanged -= value;
        }

        public bool RegisterMetric(MetricDefinition definition, bool replaceExisting = false)
        {
            return _collectors.Register(definition, replaceExisting);
        }

        public IDisposable RegisterCollector(IMetricCollector collector)
        {
            return _collectors.AddCollector(collector);
        }

        public IDisposable RegisterTimelineCollector(ITimelineCollector collector)
        {
            return _collectors.AddTimelineCollector(collector);
        }

        public IDisposable RegisterDashboard(IDashboardProvider provider, bool replaceExisting = false)
        {
            return _dashboards.RegisterDashboard(provider, replaceExisting);
        }

        public IDisposable RegisterDashboardStyle(DashboardStyleDefinition style, bool replaceExisting = false)
        {
            return _dashboards.RegisterStyle(style, replaceExisting);
        }

        public string? OpenDashboard(string dashboardId, DashboardWindowOptions? options = null)
        {
            return _dashboards.RequestOpen(dashboardId, options);
        }

        public bool CloseDashboard(string instanceId)
        {
            return Main.DashboardHost?.ContainsWindow(instanceId) == true &&
                   _dashboards.RequestClose(instanceId);
        }

        public bool PublishCustomObservation(string ownerModId, MetricObservation observation)
        {
            return _customPublisher(ownerModId, observation);
        }

        public CombatSnapshot? GetLiveCombat()
        {
            return _repository.GetLiveCombat(true);
        }

        public RunSnapshot? GetLiveRun()
        {
            return _repository.GetLiveRun(true);
        }

        public MetricsQueryResult Query(MetricsQuery query)
        {
            return _queries.Query(query);
        }

        public TimelineQueryResult QueryTimeline(TimelineQuery query)
        {
            return _queries.QueryTimeline(query);
        }

        public MetricsExportResult Export(MetricsExportRequest request)
        {
            return _exports.Export(request);
        }

        public string ResolveSourceDisplayName(AnalyticsSourceKind kind, string modelId, string fallback = "")
        {
            return LocalizedModelNameResolver.Resolve(kind, modelId, fallback);
        }
    }
}
