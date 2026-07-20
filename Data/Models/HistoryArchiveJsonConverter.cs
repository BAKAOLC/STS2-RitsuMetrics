// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using STS2RitsuLib.Utils.Persistence;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Data.Models
{
    internal sealed class HistoryArchiveJsonConverter : JsonConverter<HistoryArchive>
    {
        private const string SegmentedStorageFormat = "combat-segments-v1";
        private const string BrotliEncoding = "brotli";
        private const string JsonEncoding = "json";
        private const int CombatFileHeaderSize = 12;
        private const byte CombatFileVersion = 1;
        private const byte BrotliFileEncoding = 1;
        private const byte JsonFileEncoding = 0;
        private const int CompressionThresholdBytes = 64 * 1024;
        private const int MaxCombatStoredBytes = 64 * 1024 * 1024;
        private const int MaxCombatUncompressedBytes = 512 * 1024 * 1024;

        private static readonly Dictionary<CombatStorageKey, StoredCombat> CompletedCombatCache = [];
        private static readonly Lock CacheGate = new();
        private static readonly ConditionalWeakTable<CombatSnapshot, StoredCombat> CombatReferenceCache = new();

        private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> CompactOptions =
            new();

        private static readonly Lock MetricsGate = new();
        private static HistoryStorageWriteMetrics _lastWriteMetrics;
        private static long _writeSequence;

        private static ReadOnlySpan<byte> CombatFileMagic => "RTMX"u8;

        public override HistoryArchive Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("History archive root must be a JSON object.");

            if (!TryGetProperty(root, nameof(FileArchiveIndex.StorageFormat), out var formatElement))
                return ReadLegacyAsynchronously(root, options);

            return formatElement.GetString() switch
            {
                HistoryArchive.CurrentStorageFormat => ReadFileIndex(root, options),
                SegmentedStorageFormat => ReadSegmentedAsynchronously(root, options),
                var format => throw new JsonException($"Unsupported history storage format '{format ?? "<null>"}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, HistoryArchive value, JsonSerializerOptions options)
        {
            var writeSequence = Interlocked.Increment(ref _writeSequence);
            var storedRuns = new List<FileRun>(value.Runs.Count);
            long uncompressedBytes = 0;
            long storedBytes = 0;

            foreach (var run in value.Runs)
            {
                var storedCombats = new List<CombatFileReference>(run.Combats.Count);
                foreach (var combat in run.Combats)
                {
                    if (!TryGetPreparedCombat(run.RunId, combat, out var storedCombat))
                        throw new JsonException(
                            $"Combat '{combat.CombatId}' was not prepared by the asynchronous history writer.");

                    var fileName = GetCombatFileName(run.RunId, combat.CombatId);
                    storedCombats.Add(new(
                        combat.CombatId,
                        fileName,
                        storedCombat.Encoding,
                        storedCombat.UncompressedLength,
                        storedCombat.StoredLength,
                        combat.ActIndex,
                        combat.Floor,
                        combat.EncounterId,
                        combat.EncounterName,
                        combat.StartedAtUtc,
                        combat.EndedAtUtc,
                        combat.Completed,
                        combat.RoundCount));
                    uncompressedBytes = checked(uncompressedBytes + storedCombat.UncompressedLength);
                    storedBytes = checked(storedBytes + storedCombat.StoredLength);
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

            var index = new FileArchiveIndex
            {
                DataVersion = value.DataVersion,
                StorageFormat = HistoryArchive.CurrentStorageFormat,
                Runs = storedRuns,
            };
            JsonSerializer.Serialize(writer, index, GetCompactOptions(options));
            lock (MetricsGate)
            {
                _lastWriteMetrics = new(writeSequence, uncompressedBytes, storedBytes);
            }
        }

        internal static string GetDataDirectory(int profileId)
        {
            var markerPath = ProfileManager.GetFilePath(
                $"{ModConstants.HistoryDataDirectoryName}/.marker",
                SaveScope.Profile,
                profileId,
                ModConstants.ModId);
            var markerAbsolutePath = ProjectSettings.GlobalizePath(markerPath);
            return Path.GetDirectoryName(markerAbsolutePath)
                   ?? throw new InvalidOperationException("Could not resolve the analytics history data directory.");
        }

        internal static void PrepareForWrite(HistoryArchive archive, JsonSerializerOptions options,
            string dataDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
            var compactOptions = GetCompactOptions(options);
            foreach (var run in archive.Runs)
            foreach (var combat in run.Combats)
            {
                var stored = GetOrCreateStoredCombat(run.RunId, combat, compactOptions);
                var fileName = GetCombatFileName(run.RunId, combat.CombatId);
                var filePath = Path.Combine(dataDirectory, fileName);
                if (stored.PersistedFileName == fileName && File.Exists(filePath))
                    continue;

                WriteCombatFile(filePath, stored);
                stored = stored with { PersistedFileName = fileName };
                CachePreparedCombat(run.RunId, combat, stored);
            }
        }

        internal static void CompleteWrite(HistoryArchive archive, string dataDirectory)
        {
            if (!Directory.Exists(dataDirectory))
                return;

            var referencedFiles = archive.Runs
                .SelectMany(run => run.Combats.Select(combat => GetCombatFileName(run.RunId, combat.CombatId)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in Directory.EnumerateFiles(dataDirectory, "*.bin", SearchOption.TopDirectoryOnly))
                if (!referencedFiles.Contains(Path.GetFileName(filePath)))
                    File.Delete(filePath);

            foreach (var tempPath in Directory.EnumerateFiles(dataDirectory, "*.tmp", SearchOption.TopDirectoryOnly))
                File.Delete(tempPath);
        }

        internal static HistoryStorageWriteMetrics GetLastWriteMetrics()
        {
            lock (MetricsGate)
            {
                return _lastWriteMetrics;
            }
        }

        private static HistoryArchive ReadFileIndex(JsonElement root, JsonSerializerOptions options)
        {
            var index = root.Deserialize<FileArchiveIndex>(GetCompactOptions(options))
                        ?? throw new JsonException("History archive index is empty.");
            var archive = new HistoryArchive
            {
                DataVersion = index.DataVersion,
                Runs = CreateIndexStubs(index),
            };
            if (index.Runs.Sum(run => run.Combats.Count) > 0)
            {
                var profileId = ProfileManager.Instance.CurrentProfileId;
                var dataDirectory = GetDataDirectory(profileId < 0 ? 1 : profileId);
                archive.AttachPendingLoad(Task.Run(() => LoadCombatFiles(index, options, dataDirectory)));
            }
            return archive;
        }

        private static List<RunSnapshot> CreateIndexStubs(FileArchiveIndex index)
        {
            var runs = new List<RunSnapshot>(index.Runs.Count);
            foreach (var storedRun in index.Runs)
            {
                var combats = new List<CombatSnapshot>(storedRun.Combats.Count);
                foreach (var reference in storedRun.Combats)
                {
                    ValidateFileReference(storedRun.RunId, reference);
                    var combat = new CombatSnapshot(
                        storedRun.RunId,
                        reference.CombatId,
                        reference.ActIndex,
                        reference.Floor,
                        reference.EncounterId,
                        reference.EncounterName,
                        reference.StartedAtUtc,
                        reference.EndedAtUtc,
                        reference.Completed,
                        reference.RoundCount,
                        [],
                        [],
                        []);
                    CachePreparedCombat(storedRun.RunId, combat,
                        new(
                            reference.CombatId,
                            reference.Encoding,
                            reference.UncompressedLength,
                            null,
                            reference.StoredLength,
                            reference.FileName));
                    combats.Add(combat);
                }

                runs.Add(CreateRun(storedRun, combats));
            }

            return runs;
        }

        private static HistoryArchive LoadCombatFiles(FileArchiveIndex index, JsonSerializerOptions options,
            string dataDirectory)
        {
            var runs = new List<RunSnapshot>(index.Runs.Count);
            var requiresRewrite = false;
            foreach (var storedRun in index.Runs)
            {
                var combats = new List<CombatSnapshot>(storedRun.Combats.Count);
                foreach (var reference in storedRun.Combats)
                    try
                    {
                        ValidateFileReference(storedRun.RunId, reference);
                        var stored = ReadCombatFile(Path.Combine(dataDirectory, reference.FileName), reference);
                        var payload = Decode(stored);
                        var combat = JsonSerializer.Deserialize<CombatSnapshot>(payload, GetCompactOptions(options))
                                     ?? throw new JsonException("History combat payload is empty.");
                        if (!string.Equals(combat.CombatId, reference.CombatId, StringComparison.Ordinal)
                            || !string.Equals(combat.RunId, storedRun.RunId, StringComparison.Ordinal))
                            throw new JsonException("History combat payload identity does not match its index.");
                        CachePreparedCombat(storedRun.RunId, combat, stored);
                        combats.Add(combat);
                    }
                    catch (Exception exception)
                    {
                        requiresRewrite = true;
                        Main.Logger.Error(
                            $"Skipping unreadable analytics combat '{reference.CombatId}' from run " +
                            $"'{storedRun.RunId}': {exception.Message}");
                    }

                runs.Add(CreateRun(storedRun, combats));
            }

            return new()
            {
                DataVersion = index.DataVersion,
                Runs = runs,
                RequiresStorageRewrite = requiresRewrite,
            };
        }

        private static HistoryArchive ReadSegmentedAsynchronously(JsonElement root, JsonSerializerOptions options)
        {
            var archive = new HistoryArchive { RequiresStorageRewrite = true };
            var rawJson = root.GetRawText();
            archive.AttachPendingLoad(Task.Run(() => ReadSegmented(rawJson, options)));
            return archive;
        }

        private static HistoryArchive ReadSegmented(string rawJson, JsonSerializerOptions options)
        {
            var stored = JsonSerializer.Deserialize<SegmentedArchive>(rawJson, GetCompactOptions(options))
                         ?? throw new JsonException("History archive is empty.");
            var runs = new List<RunSnapshot>(stored.Runs.Count);
            foreach (var storedRun in stored.Runs)
            {
                var combats = new List<CombatSnapshot>(storedRun.Combats.Count);
                foreach (var oldStoredCombat in storedRun.Combats)
                {
                    ValidateSegmentedCombat(oldStoredCombat);
                    var storedCombat = new StoredCombat(
                        oldStoredCombat.CombatId,
                        oldStoredCombat.Encoding,
                        oldStoredCombat.UncompressedLength,
                        oldStoredCombat.Payload,
                        checked(oldStoredCombat.Payload.Length + CombatFileHeaderSize),
                        null);
                    var payload = Decode(storedCombat);
                    var combat = JsonSerializer.Deserialize<CombatSnapshot>(payload, GetCompactOptions(options))
                                 ?? throw new JsonException("History combat payload is empty.");
                    if (!string.Equals(combat.CombatId, storedCombat.CombatId, StringComparison.Ordinal))
                        throw new JsonException("History combat payload identity does not match its envelope.");
                    CachePreparedCombat(storedRun.RunId, combat, storedCombat);
                    combats.Add(combat);
                }

                runs.Add(CreateRun(storedRun, combats));
            }

            return new() { DataVersion = stored.DataVersion, Runs = runs };
        }

        private static HistoryArchive ReadLegacyAsynchronously(JsonElement root, JsonSerializerOptions options)
        {
            var archive = new HistoryArchive { RequiresStorageRewrite = true };
            var rawJson = root.GetRawText();
            archive.AttachPendingLoad(Task.Run(() =>
            {
                var payload = JsonSerializer.Deserialize<LegacyHistoryArchive>(rawJson, options)
                              ?? throw new JsonException("History archive is empty.");
                return new HistoryArchive
                {
                    DataVersion = payload.DataVersion,
                    Runs = payload.Runs ?? [],
                };
            }));
            return archive;
        }

        private static RunSnapshot CreateRun(StoredRunBase storedRun, IReadOnlyList<CombatSnapshot> combats)
        {
            return new(
                storedRun.RunId,
                storedRun.StartedAtUtc,
                storedRun.EndedAtUtc,
                storedRun.IsMultiplayer,
                storedRun.IsDaily,
                storedRun.IsVictory,
                storedRun.IsAbandoned,
                combats);
        }

        private static StoredCombat GetOrCreateStoredCombat(string runId, CombatSnapshot combat,
            JsonSerializerOptions options)
        {
            if (TryGetPreparedCombat(runId, combat, out var cached) && cached.Payload != null)
                return cached;

            var uncompressed = JsonSerializer.SerializeToUtf8Bytes(combat, options);
            if (uncompressed.Length > MaxCombatUncompressedBytes)
                throw new JsonException($"Combat '{combat.CombatId}' exceeds the supported size limit.");

            var stored = CreateStoredCombat(combat.CombatId, uncompressed);
            CachePreparedCombat(runId, combat, stored);
            return stored;
        }

        private static StoredCombat CreateStoredCombat(string combatId, byte[] uncompressed)
        {
            if (uncompressed.Length < CompressionThresholdBytes)
                return new(
                    combatId,
                    JsonEncoding,
                    uncompressed.Length,
                    uncompressed,
                    checked(uncompressed.Length + CombatFileHeaderSize),
                    null);

            var compressed = Compress(uncompressed);
            var payload = compressed.Length <= uncompressed.Length * 4 / 5 ? compressed : uncompressed;
            var encoding = ReferenceEquals(payload, compressed) ? BrotliEncoding : JsonEncoding;
            if (payload.Length > MaxCombatStoredBytes)
                throw new JsonException($"Combat '{combatId}' exceeds the supported compressed size limit.");
            return new(
                combatId,
                encoding,
                uncompressed.Length,
                payload,
                checked(payload.Length + CombatFileHeaderSize),
                null);
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

        private static void WriteCombatFile(string filePath, StoredCombat stored)
        {
            if (stored.Payload == null)
                throw new JsonException($"Combat '{stored.CombatId}' has no prepared payload.");

            var fileBytes = GC.AllocateUninitializedArray<byte>(stored.StoredLength);
            CombatFileMagic.CopyTo(fileBytes);
            fileBytes[4] = CombatFileVersion;
            fileBytes[5] = stored.Encoding == BrotliEncoding ? BrotliFileEncoding : JsonFileEncoding;
            fileBytes[6] = 0;
            fileBytes[7] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(fileBytes.AsSpan(8, 4), stored.UncompressedLength);
            stored.Payload.CopyTo(fileBytes, CombatFileHeaderSize);

            var tempPath = filePath + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, fileBytes);
                File.Move(tempPath, filePath, true);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        private static StoredCombat ReadCombatFile(string filePath, CombatFileReference reference)
        {
            var fileBytes = File.ReadAllBytes(filePath);
            if (fileBytes.Length != reference.StoredLength
                || fileBytes.Length < CombatFileHeaderSize
                || !fileBytes.AsSpan(0, 4).SequenceEqual(CombatFileMagic)
                || fileBytes[4] != CombatFileVersion
                || fileBytes[6] != 0
                || fileBytes[7] != 0)
                throw new JsonException("History combat file has an invalid header or length.");

            var encoding = fileBytes[5] switch
            {
                BrotliFileEncoding => BrotliEncoding,
                JsonFileEncoding => JsonEncoding,
                _ => throw new JsonException("History combat file uses an unsupported encoding."),
            };
            var uncompressedLength = BinaryPrimitives.ReadInt32LittleEndian(fileBytes.AsSpan(8, 4));
            if (encoding != reference.Encoding || uncompressedLength != reference.UncompressedLength)
                throw new JsonException("History combat file metadata does not match its index.");
            var payload = fileBytes.AsSpan(CombatFileHeaderSize).ToArray();
            var stored = new StoredCombat(
                reference.CombatId,
                encoding,
                uncompressedLength,
                payload,
                fileBytes.Length,
                reference.FileName);
            ValidateStoredCombat(stored);
            return stored;
        }

        private static void ValidateFileReference(string runId, CombatFileReference reference)
        {
            if (string.IsNullOrWhiteSpace(reference.CombatId)
                || reference.FileName != GetCombatFileName(runId, reference.CombatId)
                || reference.Encoding is not (BrotliEncoding or JsonEncoding)
                || reference.UncompressedLength < 0
                || reference.UncompressedLength > MaxCombatUncompressedBytes
                || reference.StoredLength < CombatFileHeaderSize
                || reference.StoredLength > MaxCombatStoredBytes + CombatFileHeaderSize)
                throw new JsonException("History archive contains an invalid combat file reference.");
        }

        private static void ValidateSegmentedCombat(SegmentedCombat stored)
        {
            if (string.IsNullOrWhiteSpace(stored.CombatId)
                || stored.UncompressedLength < 0
                || stored.UncompressedLength > MaxCombatUncompressedBytes
                || stored.Payload.Length > MaxCombatStoredBytes
                || stored.Encoding is not (BrotliEncoding or JsonEncoding))
                throw new JsonException("History archive contains an invalid combat payload.");
            if (stored.Encoding == JsonEncoding && stored.Payload.Length != stored.UncompressedLength)
                throw new JsonException("Uncompressed history combat length does not match its metadata.");
        }

        private static void ValidateStoredCombat(StoredCombat stored)
        {
            if (stored.Payload == null
                || stored.UncompressedLength < 0
                || stored.UncompressedLength > MaxCombatUncompressedBytes
                || stored.Payload.Length > MaxCombatStoredBytes
                || stored.Encoding is not (BrotliEncoding or JsonEncoding))
                throw new JsonException("History combat file contains an invalid payload.");
            if (stored.Encoding == JsonEncoding && stored.Payload.Length != stored.UncompressedLength)
                throw new JsonException("Uncompressed history combat length does not match its metadata.");
        }

        private static byte[] Decode(StoredCombat stored)
        {
            if (stored.Payload == null)
                throw new JsonException($"Combat '{stored.CombatId}' has no stored payload.");
            return stored.Encoding == BrotliEncoding
                ? Decompress(stored.Payload, stored.UncompressedLength)
                : stored.Payload;
        }

        private static string GetCombatFileName(string runId, string combatId)
        {
            var identity = Encoding.UTF8.GetBytes($"{runId}\0{combatId}");
            return $"{Convert.ToHexStringLower(SHA256.HashData(identity))}.bin";
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
                var output = GC.AllocateUninitializedArray<byte>(expectedLength);
                var total = 0;
                while (total < output.Length)
                {
                    var read = brotli.Read(output.AsSpan(total));
                    if (read == 0)
                        break;
                    total += read;
                }

                Span<byte> trailing = stackalloc byte[1];
                if (total != expectedLength || brotli.Read(trailing) != 0)
                    throw new JsonException("History archive uncompressed length does not match its metadata.");
                return output;
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

        private abstract class StoredRunBase
        {
            public string RunId { get; init; } = string.Empty;
            public DateTimeOffset StartedAtUtc { get; init; }
            public DateTimeOffset? EndedAtUtc { get; init; }
            public bool IsMultiplayer { get; init; }
            public bool IsDaily { get; init; }
            public bool? IsVictory { get; init; }
            public bool? IsAbandoned { get; init; }
        }

        private sealed class FileArchiveIndex
        {
            public int DataVersion { get; init; }
            public string StorageFormat { get; init; } = string.Empty;
            public List<FileRun> Runs { get; init; } = [];
        }

        private sealed class FileRun : StoredRunBase
        {
            public List<CombatFileReference> Combats { get; init; } = [];
        }

        private sealed record CombatFileReference(
            string CombatId,
            string FileName,
            string Encoding,
            int UncompressedLength,
            int StoredLength,
            int ActIndex,
            int Floor,
            string EncounterId,
            string EncounterName,
            DateTimeOffset StartedAtUtc,
            DateTimeOffset? EndedAtUtc,
            bool Completed,
            int RoundCount);

        private sealed class SegmentedArchive
        {
            public int DataVersion { get; init; }
            public string StorageFormat { get; init; } = string.Empty;
            public List<SegmentedRun> Runs { get; init; } = [];
        }

        private sealed class SegmentedRun : StoredRunBase
        {
            public List<SegmentedCombat> Combats { get; } = [];
        }

        private sealed record SegmentedCombat(
            string CombatId,
            string Encoding,
            int UncompressedLength,
            byte[] Payload);

        private sealed record StoredCombat(
            string CombatId,
            string Encoding,
            int UncompressedLength,
            byte[]? Payload,
            int StoredLength,
            string? PersistedFileName);
    }

    internal readonly record struct HistoryStorageWriteMetrics(
        long Sequence,
        long UncompressedBytes,
        long StoredBytes);
}
