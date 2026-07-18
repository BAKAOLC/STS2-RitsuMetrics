// SPDX-License-Identifier: MPL-2.0

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Data.Models
{
    internal sealed class HistoryArchiveJsonConverter : JsonConverter<HistoryArchive>
    {
        private const string BrotliEncoding = "brotli";
        private const string JsonEncoding = "json";
        private const int CompressionThresholdBytes = 64 * 1024;
        private const int MaxCompressedBytes = 64 * 1024 * 1024;
        private const int MaxUncompressedBytes = 512 * 1024 * 1024;

        private static readonly Dictionary<CombatStorageKey, StoredCombat> CompletedCombatCache = [];
        private static readonly Lock CacheGate = new();
        private static readonly ConditionalWeakTable<CombatSnapshot, StoredCombat> CombatReferenceCache = new();

        private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> CompactOptions =
            new();

        private static readonly Lock MetricsGate = new();
        private static HistoryStorageWriteMetrics _lastWriteMetrics;
        private static long _writeSequence;

        public override HistoryArchive Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("History archive root must be a JSON object.");

            if (!TryGetProperty(root, nameof(SegmentedArchive.StorageFormat), out var formatElement))
                return ReadLegacy(root, options);

            return formatElement.GetString() switch
            {
                HistoryArchive.CurrentStorageFormat => ReadSegmented(root, options),
                var format => throw new JsonException($"Unsupported history storage format '{format ?? "<null>"}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, HistoryArchive value, JsonSerializerOptions options)
        {
            var writeSequence = Interlocked.Increment(ref _writeSequence);
            var storedRuns = new List<StoredRun>(value.Runs.Count);
            var uncompressedBytes = 0;
            var compressedBytes = 0;
            var encodedPayloadBytes = 0;

            foreach (var run in value.Runs)
            {
                var storedCombats = new List<StoredCombat>(run.Combats.Count);
                foreach (var combat in run.Combats)
                {
                    if (!TryGetPreparedCombat(run.RunId, combat, out var storedCombat))
                        throw new JsonException(
                            $"Combat '{combat.CombatId}' was not prepared by the asynchronous history writer.");
                    storedCombats.Add(storedCombat);
                    uncompressedBytes = checked(uncompressedBytes + storedCombat.UncompressedLength);
                    compressedBytes = checked(compressedBytes + storedCombat.Payload.Length);
                    encodedPayloadBytes = checked(encodedPayloadBytes + (storedCombat.Payload.Length + 2) / 3 * 4);
                }

                storedRuns.Add(new()
                {
                    RunId = run.RunId,
                    StartedAtUtc = run.StartedAtUtc,
                    EndedAtUtc = run.EndedAtUtc,
                    IsMultiplayer = run.IsMultiplayer,
                    IsDaily = run.IsDaily,
                    IsVictory = run.IsVictory,
                    IsAbandoned = run.IsAbandoned,
                    Combats = storedCombats,
                });
            }

            var envelope = new SegmentedArchive
            {
                DataVersion = value.DataVersion,
                StorageFormat = HistoryArchive.CurrentStorageFormat,
                Runs = storedRuns,
            };
            JsonSerializer.Serialize(writer, envelope, GetCompactOptions(options));
            lock (MetricsGate)
            {
                _lastWriteMetrics = new(writeSequence, uncompressedBytes, compressedBytes,
                    encodedPayloadBytes);
            }
        }

        internal static void PrepareForWrite(HistoryArchive archive, JsonSerializerOptions options)
        {
            var compactOptions = GetCompactOptions(options);
            long totalUncompressed = 0;
            long totalStored = 0;
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var run in archive.Runs)
            foreach (var combat in run.Combats)
            {
                var stored = GetOrCreateStoredCombat(run.RunId, combat, compactOptions);
                totalUncompressed += stored.UncompressedLength;
                totalStored += stored.Payload.Length;
                if (totalUncompressed > MaxUncompressedBytes || totalStored > MaxCompressedBytes)
                    throw new JsonException("History archive exceeds the supported storage size limit.");
            }
        }

        internal static HistoryStorageWriteMetrics GetLastWriteMetrics()
        {
            lock (MetricsGate)
            {
                return _lastWriteMetrics;
            }
        }

        private static HistoryArchive ReadSegmented(JsonElement root, JsonSerializerOptions options)
        {
            var stored = root.Deserialize<SegmentedArchive>(GetCompactOptions(options))
                         ?? throw new JsonException("History archive is empty.");
            var runs = new List<RunSnapshot>(stored.Runs.Count);
            long totalUncompressed = 0;
            long totalStored = 0;
            foreach (var storedRun in stored.Runs)
            {
                var combats = new List<CombatSnapshot>(storedRun.Combats.Count);
                foreach (var storedCombat in storedRun.Combats)
                {
                    ValidateStoredCombat(storedCombat);
                    totalUncompressed += storedCombat.UncompressedLength;
                    totalStored += storedCombat.Payload.Length;
                    if (totalUncompressed > MaxUncompressedBytes || totalStored > MaxCompressedBytes)
                        throw new JsonException("History archive exceeds the supported storage size limit.");

                    var payload = Decode(storedCombat);
                    var combat = JsonSerializer.Deserialize<CombatSnapshot>(payload, GetCompactOptions(options))
                                 ?? throw new JsonException("History combat payload is empty.");
                    if (!string.Equals(combat.CombatId, storedCombat.CombatId, StringComparison.Ordinal))
                        throw new JsonException("History combat payload identity does not match its envelope.");
                    CachePreparedCombat(storedRun.RunId, combat, storedCombat);
                    combats.Add(combat);
                }

                runs.Add(new(
                    storedRun.RunId,
                    storedRun.StartedAtUtc,
                    storedRun.EndedAtUtc,
                    storedRun.IsMultiplayer,
                    storedRun.IsDaily,
                    storedRun.IsVictory,
                    storedRun.IsAbandoned,
                    combats));
            }

            return new() { DataVersion = stored.DataVersion, Runs = runs };
        }

        private static HistoryArchive ReadLegacy(JsonElement root, JsonSerializerOptions options)
        {
            var payload = root.Deserialize<LegacyHistoryArchive>(options)
                          ?? throw new JsonException("History archive is empty.");
            return new()
            {
                DataVersion = payload.DataVersion,
                Runs = payload.Runs ?? [],
                RequiresStorageRewrite = true,
            };
        }

        private static StoredCombat GetOrCreateStoredCombat(string runId, CombatSnapshot combat,
            JsonSerializerOptions options)
        {
            if (TryGetPreparedCombat(runId, combat, out var cached))
                return cached;

            var uncompressed = JsonSerializer.SerializeToUtf8Bytes(combat, options);
            if (uncompressed.Length > MaxUncompressedBytes)
                throw new JsonException($"Combat '{combat.CombatId}' exceeds the supported size limit.");

            var stored = CreateStoredCombat(combat.CombatId, uncompressed);
            CachePreparedCombat(runId, combat, stored);
            return stored;
        }

        private static StoredCombat CreateStoredCombat(string combatId, byte[] uncompressed)
        {
            if (uncompressed.Length < CompressionThresholdBytes)
                return new(combatId, JsonEncoding, uncompressed.Length, uncompressed);

            var compressed = Compress(uncompressed);
            return compressed.Length <= uncompressed.Length * 4 / 5
                ? new(combatId, BrotliEncoding, uncompressed.Length, compressed)
                : new StoredCombat(combatId, JsonEncoding, uncompressed.Length, uncompressed);
        }

        private static void CachePreparedCombat(string runId, CombatSnapshot combat, StoredCombat stored)
        {
            CombatReferenceCache.Remove(combat);
            CombatReferenceCache.Add(combat, stored);
            if (!combat.Completed)
                return;
            lock (CacheGate)
            {
                CompletedCombatCache[new(runId, combat.CombatId)] = stored;
            }
        }

        private static bool TryGetPreparedCombat(string runId, CombatSnapshot combat, out StoredCombat stored)
        {
            if (CombatReferenceCache.TryGetValue(combat, out stored!))
                return true;
            if (!combat.Completed)
                return false;
            lock (CacheGate)
            {
                return CompletedCombatCache.TryGetValue(new(runId, combat.CombatId), out stored!);
            }
        }

        private static void ValidateStoredCombat(StoredCombat stored)
        {
            if (string.IsNullOrWhiteSpace(stored.CombatId)
                || stored.UncompressedLength < 0
                || stored.UncompressedLength > MaxUncompressedBytes
                || stored.Payload.Length > MaxCompressedBytes
                || stored.Encoding is not (BrotliEncoding or JsonEncoding))
                throw new JsonException("History archive contains an invalid combat payload.");
            if (stored.Encoding == JsonEncoding && stored.Payload.Length != stored.UncompressedLength)
                throw new JsonException("Uncompressed history combat length does not match its metadata.");
        }

        private static byte[] Decode(StoredCombat stored)
        {
            return stored.Encoding == BrotliEncoding
                ? Decompress(stored.Payload, stored.UncompressedLength)
                : stored.Payload;
        }

        private static byte[] Compress(ReadOnlySpan<byte> input)
        {
            using var output = new MemoryStream();
            using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
            {
                brotli.Write(input);
            }

            return output.ToArray();
        }

        private static byte[] Decompress(byte[] input, int expectedLength)
        {
            try
            {
                using var source = new MemoryStream(input, false);
                using var brotli = new BrotliStream(source, CompressionMode.Decompress);
                using var output = new MemoryStream(Math.Min(expectedLength, 1024 * 1024));
                Span<byte> buffer = stackalloc byte[16 * 1024];
                var total = 0;
                while (true)
                {
                    var read = brotli.Read(buffer);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > expectedLength || total > MaxUncompressedBytes)
                        throw new JsonException("History archive expands beyond its declared size.");
                    output.Write(buffer[..read]);
                }

                return total == expectedLength
                    ? output.ToArray()
                    : throw new JsonException("History archive uncompressed length does not match its metadata.");
            }
            catch (InvalidDataException exception)
            {
                throw new JsonException("History archive Brotli payload is corrupt.", exception);
            }
        }

        private static JsonSerializerOptions GetCompactOptions(JsonSerializerOptions options)
        {
            return CompactOptions.GetValue(options,
                static source => new(source) { WriteIndented = false });
        }

        private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
        {
            if (root.TryGetProperty(propertyName, out value))
                return true;
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var property in root.EnumerateObject())
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

            value = default;
            return false;
        }

        private readonly record struct CombatStorageKey(string RunId, string CombatId);

        private sealed class LegacyHistoryArchive
        {
            public int DataVersion { get; init; } = HistoryArchive.CurrentDataVersion;
            public List<RunSnapshot>? Runs { get; init; }
        }

        private sealed class SegmentedArchive
        {
            public int DataVersion { get; init; }
            public string StorageFormat { get; init; } = string.Empty;
            public List<StoredRun> Runs { get; init; } = [];
        }

        private sealed class StoredRun
        {
            public string RunId { get; init; } = string.Empty;
            public DateTimeOffset StartedAtUtc { get; init; }
            public DateTimeOffset? EndedAtUtc { get; init; }
            public bool IsMultiplayer { get; init; }
            public bool IsDaily { get; init; }
            public bool? IsVictory { get; init; }
            public bool? IsAbandoned { get; init; }
            public List<StoredCombat> Combats { get; init; } = [];
        }

        private sealed record StoredCombat(
            string CombatId,
            string Encoding,
            int UncompressedLength,
            byte[] Payload);
    }

    internal readonly record struct HistoryStorageWriteMetrics(
        long Sequence,
        int UncompressedBytes,
        int CompressedBytes,
        int EncodedPayloadBytes);
}
