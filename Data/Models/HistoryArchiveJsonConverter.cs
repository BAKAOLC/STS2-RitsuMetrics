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
using STS2RitsuMetrics.Core;

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
            if (!value.IsLoadReady)
                throw new JsonException("Analytics history is still loading and cannot be serialized yet.");

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
                        combat.RoundCount,
                        AnalysisSnapshotSelector.SummarizeCombat(combat).Players.ToList()));
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
                    Identity = run.Identity,
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

        internal static void VerifyPersistedArchive(
            HistoryArchive expected,
            string indexPath,
            string dataDirectory,
            JsonSerializerOptions options)
        {
            var index = JsonSerializer.Deserialize<FileArchiveIndex>(
                            File.ReadAllText(indexPath),
                            GetCompactOptions(options))
                        ?? throw new JsonException("Persisted analytics history index is empty.");
            var expectedIds = expected.Runs
                .SelectMany(run => run.Combats.Select(combat => (run.RunId, combat.CombatId)))
                .ToHashSet();
            var actualIds = index.Runs
                .SelectMany(run => run.Combats.Select(combat => (run.RunId, combat.CombatId)))
                .ToHashSet();
            if (expected.Runs.Count != index.Runs.Count || !expectedIds.SetEquals(actualIds))
                throw new JsonException("Persisted analytics history index does not match the migrated history.");

            foreach (var run in index.Runs)
            foreach (var reference in run.Combats)
            {
                ValidateFileReference(run.RunId, reference);
                var stored = ReadCombatFile(Path.Combine(dataDirectory, reference.FileName), reference);
                var combat = JsonSerializer.Deserialize<CombatSnapshot>(Decode(stored), GetCompactOptions(options))
                             ?? throw new JsonException("Persisted analytics combat is empty.");
                if (combat.RunId != run.RunId || combat.CombatId != reference.CombatId)
                    throw new JsonException("Persisted analytics combat identity does not match its index.");
            }
        }

        internal static HistoryStorageWriteMetrics GetLastWriteMetrics()
        {
            lock (MetricsGate)
            {
                return _lastWriteMetrics;
            }
        }

        internal static HistoryArchive ReadFileArchive(
            string indexJson,
            JsonSerializerOptions options,
            string dataDirectory)
        {
            using var document = JsonDocument.Parse(indexJson);
            var index = document.RootElement.Deserialize<FileArchiveIndex>(GetCompactOptions(options))
                        ?? throw new JsonException("History archive index is empty.");
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (index.StorageFormat != HistoryArchive.CurrentStorageFormat)
                throw new JsonException($"Unsupported history storage format '{index.StorageFormat}'.");
            return CreateFileArchive(index, options, dataDirectory);
        }

        private static HistoryArchive ReadFileIndex(JsonElement root, JsonSerializerOptions options)
        {
            var index = root.Deserialize<FileArchiveIndex>(GetCompactOptions(options))
                        ?? throw new JsonException("History archive index is empty.");
            if (index.Runs.Count > 0 && index.Runs.All(run => run.Combats.Count == 0)
                                     && TryRecoverEmptyIndex(options, out var recovered))
                return recovered;

            if (index.Runs.Sum(run => run.Combats.Count) == 0)
                return CreateFileArchive(index, options, null);

            var profileId = ProfileManager.Instance.CurrentProfileId;
            var archive = CreateFileArchive(index, options, GetDataDirectory(profileId < 0 ? 1 : profileId));
            var combatCount = index.Runs.Sum(run => run.Combats.Count);
            var storedBytes = index.Runs.Sum(run => run.Combats.Sum(combat => (long)combat.StoredLength));
            var uncompressedBytes =
                index.Runs.Sum(run => run.Combats.Sum(combat => (long)combat.UncompressedLength));
            Main.Logger.Info(
                $"Analytics history index ready for on-demand loading: runs={index.Runs.Count}, " +
                $"combats={combatCount}, combat_file_bytes={storedBytes}, " +
                $"combat_json_bytes={uncompressedBytes}.");
            return archive;
        }

        private static HistoryArchive CreateFileArchive(
            FileArchiveIndex index,
            JsonSerializerOptions options,
            string? dataDirectory)
        {
            var loader = new FileCombatLoader(options);
            var archive = new HistoryArchive
            {
                DataVersion = index.DataVersion,
                Runs = CreateIndexStubs(index, loader),
            };
            if (index.Runs.Sum(run => run.Combats.Count) == 0)
                return archive;

            loader.SetDataDirectory(dataDirectory
                                    ?? throw new JsonException("Analytics history data directory is unavailable."));
            archive.AttachCombatLoader(loader.Load, loader.LoadSummary, loader.ReplaceStub);
            return archive;
        }

        private static bool TryRecoverEmptyIndex(JsonSerializerOptions options, out HistoryArchive recovered)
        {
            recovered = null!;
            try
            {
                var profileId = ProfileManager.Instance.CurrentProfileId;
                var historyPath = ProfileManager.GetFilePath(
                    ModConstants.HistoryFileName,
                    SaveScope.Profile,
                    profileId < 0 ? 1 : profileId,
                    ModConstants.ModId);
                var backupPath = ProjectSettings.GlobalizePath(historyPath) + ".backup";
                if (!File.Exists(backupPath))
                    return false;

                var backupJson = File.ReadAllText(backupPath);
                using var backupDocument = JsonDocument.Parse(backupJson);
                if (!ContainsCombatData(backupDocument.RootElement))
                    return false;

                recovered = JsonSerializer.Deserialize<HistoryArchive>(backupJson, options)
                            ?? throw new JsonException("Analytics history backup is empty.");
                recovered.RequiresStorageRewrite = true;
                Main.Logger.Warn(
                    "Recovered analytics history from backup after detecting the 0.1.13 empty-index regression.");
                return true;
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Failed to recover analytics history from backup: {exception}");
                recovered = null!;
                return false;
            }
        }

        private static bool ContainsCombatData(JsonElement root)
        {
            if (!TryGetProperty(root, nameof(FileArchiveIndex.Runs), out var runs)
                || runs.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var run in runs.EnumerateArray())
                if (TryGetProperty(run, nameof(FileRun.Combats), out var combats)
                    && combats.ValueKind == JsonValueKind.Array
                    && combats.GetArrayLength() > 0)
                    return true;
            return false;
        }

        private static List<RunSnapshot> CreateIndexStubs(FileArchiveIndex index, FileCombatLoader loader)
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
                        reference.Players ?? [],
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
                    loader.Add(storedRun.RunId, combat, reference);
                    combats.Add(combat);
                }

                runs.Add(CreateRun(storedRun, combats));
            }

            return runs;
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
                combats)
            {
                Identity = storedRun.Identity,
            };
        }

        private static StoredCombat GetOrCreateStoredCombat(string runId, CombatSnapshot combat,
            JsonSerializerOptions options)
        {
            if (CombatReferenceCache.TryGetValue(combat, out var referenced))
                return referenced;
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

        private abstract class StoredRunBase
        {
            public string RunId { get; init; } = string.Empty;
            public DateTimeOffset StartedAtUtc { get; init; }
            public DateTimeOffset? EndedAtUtc { get; init; }
            public bool IsMultiplayer { get; init; }
            public bool IsDaily { get; init; }
            public bool? IsVictory { get; init; }
            public bool? IsAbandoned { get; init; }
            public RunIdentitySnapshot? Identity { get; init; }
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
            int RoundCount,
            List<PlayerMetricSnapshot>? Players = null);

        private sealed class FileCombatLoader(JsonSerializerOptions options)
        {
            private readonly Dictionary<CombatStorageKey, IndexedCombat> _entries = [];
            private readonly Lock _failureGate = new();
            private readonly HashSet<CombatStorageKey> _failures = [];
            private readonly Lock _loadGate = new();
            private readonly JsonSerializerOptions _options = GetCompactOptions(options);
            private string? _dataDirectory;

            internal void Add(string runId, CombatSnapshot stub, CombatFileReference reference)
            {
                lock (_loadGate)
                {
                    _entries.Add(new(runId, reference.CombatId), new(stub, reference));
                }
            }

            internal CombatSnapshot Load(CombatSnapshot combat)
            {
                lock (_loadGate)
                {
                    return LoadCore(combat);
                }
            }

            internal CombatSnapshot LoadSummary(CombatSnapshot combat)
            {
                lock (_loadGate)
                {
                    return LoadSummaryCore(combat);
                }
            }

            internal void ReplaceStub(CombatSnapshot previous, CombatSnapshot replacement)
            {
                lock (_loadGate)
                {
                    var key = new CombatStorageKey(previous.RunId, previous.CombatId);
                    if (!_entries.TryGetValue(key, out var indexed) ||
                        !ReferenceEquals(indexed.Stub, previous))
                        return;

                    _entries[key] = indexed with { Stub = replacement };
                    if (CombatReferenceCache.TryGetValue(previous, out var stored))
                        CachePreparedCombat(previous.RunId, replacement, stored);
                }
            }

            private CombatSnapshot LoadCore(CombatSnapshot combat)
            {
                var key = new CombatStorageKey(combat.RunId, combat.CombatId);
                if (!_entries.TryGetValue(key, out var indexed) || !ReferenceEquals(indexed.Stub, combat))
                    return combat;

                try
                {
                    var dataDirectory = _dataDirectory
                                        ?? throw new InvalidOperationException(
                                            "Analytics history data directory is unavailable.");
                    var stored = ReadCombatFile(
                        Path.Combine(dataDirectory, indexed.Reference.FileName),
                        indexed.Reference);
                    var loaded = JsonSerializer.Deserialize<CombatSnapshot>(Decode(stored), _options)
                                 ?? throw new JsonException("History combat payload is empty.");
                    if (!string.Equals(loaded.CombatId, indexed.Reference.CombatId, StringComparison.Ordinal)
                        || !string.Equals(loaded.RunId, combat.RunId, StringComparison.Ordinal))
                        throw new JsonException("History combat payload identity does not match its index.");

                    CachePreparedCombat(combat.RunId, loaded, stored with { Payload = null });
                    return loaded;
                }
                catch (Exception exception)
                {
                    lock (_failureGate)
                    {
                        if (_failures.Add(key))
                            Main.Logger.Error(
                                $"Could not load analytics combat '{combat.CombatId}' from run " +
                                $"'{combat.RunId}': {exception}");
                    }

                    return combat;
                }
            }

            private CombatSnapshot LoadSummaryCore(CombatSnapshot combat)
            {
                var key = new CombatStorageKey(combat.RunId, combat.CombatId);
                if (!_entries.TryGetValue(key, out var indexed))
                    return AnalysisSnapshotSelector.SummarizeCombat(combat);

                try
                {
                    var dataDirectory = _dataDirectory
                                        ?? throw new InvalidOperationException(
                                            "Analytics history data directory is unavailable.");
                    var stored = ReadCombatFile(
                        Path.Combine(dataDirectory, indexed.Reference.FileName),
                        indexed.Reference);
                    var loaded = ReadSummary(Decode(stored));
                    if (!string.Equals(loaded.CombatId, indexed.Reference.CombatId, StringComparison.Ordinal)
                        || !string.Equals(loaded.RunId, combat.RunId, StringComparison.Ordinal))
                        throw new JsonException("History combat summary identity does not match its index.");

                    return combat with
                    {
                        Players = loaded.Players,
                        Events = [],
                        Timeline = [],
                    };
                }
                catch (Exception exception)
                {
                    lock (_failureGate)
                    {
                        if (_failures.Add(key))
                            Main.Logger.Error(
                                $"Could not load analytics combat summary '{combat.CombatId}' from run " +
                                $"'{combat.RunId}': {exception}");
                    }

                    return AnalysisSnapshotSelector.SummarizeCombat(combat);
                }
            }

            private static CombatSummaryData ReadSummary(byte[] payload)
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (!TryGetProperty(root, nameof(CombatSnapshot.RunId), out var runIdElement) ||
                    !TryGetProperty(root, nameof(CombatSnapshot.CombatId), out var combatIdElement) ||
                    !TryGetProperty(root, nameof(CombatSnapshot.Players), out var playersElement) ||
                    playersElement.ValueKind != JsonValueKind.Array)
                    throw new JsonException("History combat summary fields are missing.");

                var players = new List<PlayerMetricSnapshot>(playersElement.GetArrayLength());
                foreach (var player in playersElement.EnumerateArray())
                {
                    var playerKey = String(player, nameof(PlayerMetricSnapshot.PlayerKey));
                    var displayName = String(player, nameof(PlayerMetricSnapshot.DisplayName));
                    var characterId = String(player, nameof(PlayerMetricSnapshot.CharacterId));
                    var identityColor = String(player, nameof(PlayerMetricSnapshot.IdentityColor));
                    ulong? playerNetId = null;
                    if (TryGetProperty(player, nameof(PlayerMetricSnapshot.PlayerNetId), out var playerNetIdElement) &&
                        playerNetIdElement.ValueKind == JsonValueKind.Number &&
                        playerNetIdElement.TryGetUInt64(out var parsedPlayerNetId))
                        playerNetId = parsedPlayerNetId;

                    var damage = 0m;
                    if (TryGetProperty(player, nameof(PlayerMetricSnapshot.Totals), out var totals) &&
                        totals.ValueKind == JsonValueKind.Object &&
                        TryGetProperty(totals, MetricIds.DamageDealt, out var damageElement) &&
                        damageElement.ValueKind == JsonValueKind.Number)
                        damage = damageElement.GetDecimal();
                    IReadOnlyDictionary<string, decimal> summaryTotals = damage == 0m
                        ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                        : new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            [MetricIds.DamageDealt] = damage,
                        };
                    players.Add(new(
                        playerKey,
                        playerNetId,
                        displayName,
                        characterId,
                        summaryTotals,
                        new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal),
                        identityColor));
                }

                return new(
                    runIdElement.GetString() ?? string.Empty,
                    combatIdElement.GetString() ?? string.Empty,
                    players);

                static string String(JsonElement element, string propertyName)
                {
                    return TryGetProperty(element, propertyName, out var value) &&
                           value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : string.Empty;
                }
            }

            internal void SetDataDirectory(string dataDirectory)
            {
                lock (_loadGate)
                {
                    _dataDirectory = dataDirectory;
                }
            }

            private sealed record IndexedCombat(CombatSnapshot Stub, CombatFileReference Reference);

            private sealed record CombatSummaryData(
                string RunId,
                string CombatId,
                IReadOnlyList<PlayerMetricSnapshot> Players);
        }

        private sealed class SegmentedArchive
        {
            public int DataVersion { get; init; }
            public string StorageFormat { get; init; } = string.Empty;
            public List<SegmentedRun> Runs { get; init; } = [];
        }

        private sealed class SegmentedRun : StoredRunBase
        {
            // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
            // ReSharper disable once CollectionNeverUpdated.Local
            public List<SegmentedCombat> Combats { get; init; } = [];
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
