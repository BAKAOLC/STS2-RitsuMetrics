// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed class ExportService(QueryService queries)
    {
        private static readonly JsonSerializerOptions CompactJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        private static readonly JsonSerializerOptions IndentedJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        internal MetricsExportResult Export(MetricsExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            try
            {
                var query = request is { Format: MetricsExportFormat.Csv, Query.IncludeEvents: false }
                    ? WithEvents(request.Query)
                    : request.Query;
                var result = queries.Query(query);
                var path = ResolvePath(request);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                switch (request.Format)
                {
                    case MetricsExportFormat.Json:
                        var options = request.IndentedJson ? IndentedJsonOptions : CompactJsonOptions;
                        File.WriteAllText(path, JsonSerializer.Serialize(result, options), new UTF8Encoding(true));
                        break;
                    case MetricsExportFormat.Csv:
                        WriteCsv(path, result);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(request), request.Format,
                            "Unsupported analytics export format.");
                }

                var eventCount = result.Combats.Sum(combat =>
                    combat.Events.Count + (combat.Timeline?.Count ?? 0));
                Main.Logger.Debug(
                    $"Analytics export completed: format={request.Format}, combats={result.Combats.Count}, " +
                    $"events={eventCount}, path='{path}'.");
                return new(true, path, result.Combats.Count, eventCount);
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Analytics export failed: {exception}");
                return new(false, request.DestinationPath ?? string.Empty, 0, 0, exception.Message);
            }
        }

        private static MetricsQuery WithEvents(MetricsQuery query)
        {
            return new()
            {
                RunId = query.RunId,
                CombatId = query.CombatId,
                PlayerNetId = query.PlayerNetId,
                ActIndex = query.ActIndex,
                MinimumFloor = query.MinimumFloor,
                MaximumFloor = query.MaximumFloor,
                FromUtc = query.FromUtc,
                ToUtc = query.ToUtc,
                MetricIds = query.MetricIds,
                IncludeEvents = true,
                IncludeTimeline = true,
                Limit = query.Limit,
            };
        }

        private static string ResolvePath(MetricsExportRequest request)
        {
            var extension = request.Format == MetricsExportFormat.Json ? "json" : "csv";
            var requested = string.IsNullOrWhiteSpace(request.DestinationPath)
                ? $"user://RitsuMetrics/exports/ritsumetrics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{extension}"
                : request.DestinationPath;
            var path = requested.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
                ? ProjectSettings.GlobalizePath(requested)
                : requested;
            return Path.GetFullPath(path);
        }

        private static void WriteCsv(string path, MetricsQueryResult result)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            writer.WriteLine(
                "record_type,run_id,combat_id,act,floor,encounter,started_utc,ended_utc,sequence,occurred_utc,round,turn,side,extra_turn,kind,phase,action_id,event_id,parent_event_id,metric_id,value,actor_id,actor_name,character_id,target_id,target_name,source_kind,source_id,source_name,requested,modified,blocked,hp_lost,overkill,effective,details_json,contributions_json");
            foreach (var combat in result.Combats)
            {
                foreach (var observation in combat.Events)
                {
                    var fields = new[]
                    {
                        "metric",
                        combat.RunId,
                        combat.CombatId,
                        (combat.ActIndex + 1).ToString(CultureInfo.InvariantCulture),
                        combat.Floor.ToString(CultureInfo.InvariantCulture),
                        combat.EncounterName,
                        combat.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                        combat.EndedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                        observation.Sequence.ToString(CultureInfo.InvariantCulture),
                        observation.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                        observation.Round.ToString(CultureInfo.InvariantCulture),
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        observation.MetricId,
                        observation.Value.ToString(CultureInfo.InvariantCulture),
                        observation.Subject.PlayerNetId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        observation.Subject.DisplayName,
                        observation.Subject.CharacterId,
                        observation.Target?.Key ?? string.Empty,
                        observation.Target?.DisplayName ?? string.Empty,
                        observation.Source.Kind.ToString(),
                        observation.Source.ModelId,
                        observation.Source.DisplayName,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        JsonSerializer.Serialize(observation.Tags),
                        string.Empty,
                    };
                    writer.WriteLine(string.Join(',', fields.Select(EscapeCsv)));
                }

                // ReSharper disable once UseDeconstruction
                foreach (var timelineEvent in combat.Timeline ?? [])
                {
                    var damage = timelineEvent.Damage;
                    var fields = new[]
                    {
                        "timeline",
                        combat.RunId,
                        combat.CombatId,
                        (combat.ActIndex + 1).ToString(CultureInfo.InvariantCulture),
                        combat.Floor.ToString(CultureInfo.InvariantCulture),
                        combat.EncounterName,
                        combat.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                        combat.EndedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                        timelineEvent.Sequence.ToString(CultureInfo.InvariantCulture),
                        timelineEvent.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                        timelineEvent.Round.ToString(CultureInfo.InvariantCulture),
                        timelineEvent.TurnIndex.ToString(CultureInfo.InvariantCulture),
                        timelineEvent.Side.ToString(),
                        timelineEvent.IsExtraTurn.ToString(),
                        timelineEvent.Kind.ToString(),
                        timelineEvent.Phase.ToString(),
                        timelineEvent.ActionId,
                        timelineEvent.EventId,
                        timelineEvent.ParentEventId ?? string.Empty,
                        string.Empty,
                        timelineEvent.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        timelineEvent.Actor?.Key ?? string.Empty,
                        timelineEvent.Actor?.DisplayName ?? string.Empty,
                        timelineEvent.Actor?.CharacterId ?? string.Empty,
                        timelineEvent.Target?.Key ?? string.Empty,
                        timelineEvent.Target?.DisplayName ?? string.Empty,
                        timelineEvent.Source?.Kind.ToString() ?? string.Empty,
                        timelineEvent.Source?.ModelId ?? string.Empty,
                        timelineEvent.Source?.DisplayName ?? string.Empty,
                        damage?.RequestedAmount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        damage?.ModifiedAmount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        damage?.BlockedAmount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        damage?.HpLost.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        damage?.OverkillAmount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        damage?.EffectiveAmount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        JsonSerializer.Serialize(timelineEvent.Details),
                        damage == null
                            ? string.Empty
                            : JsonSerializer.Serialize(new
                            {
                                damage.Contributions,
                                damage.AttributionShares,
                            }),
                    };
                    writer.WriteLine(string.Join(',', fields.Select(EscapeCsv)));
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
