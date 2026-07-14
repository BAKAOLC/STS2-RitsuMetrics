// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed class DashboardRegistry
    {
        private readonly Dictionary<string, IDashboardProvider> _dashboards = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private readonly Queue<OpenRequest> _pendingOpenRequests = new();
        private readonly Dictionary<string, DashboardStyleDefinition> _styles = new(StringComparer.Ordinal);

        internal IReadOnlyCollection<DashboardDefinition> Definitions
        {
            get
            {
                lock (_gate)
                {
                    return _dashboards.Values.Select(provider => provider.Definition)
                        .OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
                }
            }
        }

        internal IReadOnlyCollection<DashboardStyleDefinition> Styles
        {
            get
            {
                lock (_gate)
                {
                    return _styles.Values.OrderBy(style => style.Id, StringComparer.Ordinal).ToArray();
                }
            }
        }

        internal event Action? Changed;
        internal event Action? OpenRequested;
        internal event Action<string>? CloseRequested;

        internal IDisposable RegisterDashboard(IDashboardProvider provider, bool replace)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ValidateId(provider.Definition.Id, nameof(provider));
            lock (_gate)
            {
                if (!replace && _dashboards.ContainsKey(provider.Definition.Id))
                    throw new InvalidOperationException($"Dashboard '{provider.Definition.Id}' is already registered.");
                _dashboards[provider.Definition.Id] = provider;
            }

            Changed?.Invoke();
            return new Registration(() => RemoveDashboard(provider));
        }

        internal IDisposable RegisterStyle(DashboardStyleDefinition style, bool replace)
        {
            ArgumentNullException.ThrowIfNull(style);
            ValidateId(style.Id, nameof(style));
            lock (_gate)
            {
                if (!replace && _styles.ContainsKey(style.Id))
                    throw new InvalidOperationException($"Dashboard style '{style.Id}' is already registered.");
                _styles[style.Id] = style;
            }

            Changed?.Invoke();
            return new Registration(() => RemoveStyle(style));
        }

        internal bool TryGetProvider(string id, out IDashboardProvider provider)
        {
            lock (_gate)
            {
                return _dashboards.TryGetValue(id, out provider!);
            }
        }

        internal DashboardStyleDefinition ResolveStyle(string? id, string? fallbackId = null)
        {
            lock (_gate)
            {
                if ((id != null && _styles.TryGetValue(id, out var style)) ||
                    (fallbackId != null && _styles.TryGetValue(fallbackId, out style)))
                    return style;
                return _styles.Values.FirstOrDefault() ?? throw new InvalidOperationException(
                    "At least one dashboard style must be registered.");
            }
        }

        internal string? RequestOpen(string dashboardId, DashboardWindowOptions? options)
        {
            string instanceId;
            lock (_gate)
            {
                if (!_dashboards.ContainsKey(dashboardId))
                    return null;
                instanceId = Guid.NewGuid().ToString("N");
                _pendingOpenRequests.Enqueue(new(instanceId, dashboardId, options ?? new()));
            }

            OpenRequested?.Invoke();
            return instanceId;
        }

        internal OpenRequest[] DrainOpenRequests()
        {
            lock (_gate)
            {
                var requests = _pendingOpenRequests.ToArray();
                _pendingOpenRequests.Clear();
                return requests;
            }
        }

        internal bool RequestClose(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false;
            CloseRequested?.Invoke(instanceId);
            return true;
        }

        private void RemoveDashboard(IDashboardProvider provider)
        {
            lock (_gate)
            {
                if (_dashboards.TryGetValue(provider.Definition.Id, out var current) &&
                    ReferenceEquals(current, provider))
                    _dashboards.Remove(provider.Definition.Id);
            }

            Changed?.Invoke();
        }

        private void RemoveStyle(DashboardStyleDefinition style)
        {
            lock (_gate)
            {
                if (_styles.TryGetValue(style.Id, out var current) && ReferenceEquals(current, style))
                    _styles.Remove(style.Id);
            }

            Changed?.Invoke();
        }

        private static void ValidateId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 160 || !id.Contains('.') ||
                !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
                throw new ArgumentException("Dashboard identifiers must be stable dotted identifiers.", parameterName);
        }

        internal sealed record OpenRequest(string InstanceId, string DashboardId, DashboardWindowOptions Options);

        private sealed class Registration(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose()
            {
                Interlocked.Exchange(ref _dispose, null)?.Invoke();
            }
        }
    }
}
