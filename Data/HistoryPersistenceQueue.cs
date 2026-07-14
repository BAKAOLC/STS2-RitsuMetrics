// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2RitsuLib.Utils.Persistence;
using STS2RitsuMetrics.Data.Models;

namespace STS2RitsuMetrics.Data
{
    internal static class HistoryPersistenceQueue
    {
        private static readonly Lock Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IncludeFields = false,
        };

        private static readonly Dictionary<string, PendingWrite> PendingWrites = new(StringComparer.Ordinal);
        private static bool _workerRunning;

        internal static void Enqueue(HistoryArchive archive, int profileId, string operation)
        {
            var path = ProfileManager.GetFilePath(
                ModConstants.HistoryFileName,
                SaveScope.Profile,
                profileId,
                ModConstants.ModId);
            var snapshot = new HistoryArchive
            {
                DataVersion = archive.DataVersion,
                Runs = [.. archive.Runs],
            };
            lock (Gate)
            {
                PendingWrites[path] = new(path, snapshot, operation);
                if (_workerRunning)
                    return;
                _workerRunning = true;
            }

            var worker = new Thread(ProcessWrites)
            {
                IsBackground = true,
                Name = "RitsuMetrics history writer",
            };
            try
            {
                worker.Priority = ThreadPriority.BelowNormal;
            }
            catch (Exception)
            {
                // Some mobile runtimes do not expose thread priorities.
            }

            worker.Start();
        }

        private static void ProcessWrites()
        {
            while (true)
            {
                PendingWrite pending;
                lock (Gate)
                {
                    if (PendingWrites.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }

                    var first = PendingWrites.First();
                    pending = first.Value;
                    PendingWrites.Remove(first.Key);
                }

                try
                {
                    HistoryArchiveJsonConverter.PrepareForWrite(pending.Archive, JsonOptions);
                    lock (Gate)
                    {
                        if (PendingWrites.ContainsKey(pending.Path))
                            continue;
                    }

                    var entry = new PersistentDataEntry<HistoryArchive>(
                        ModConstants.ModId,
                        ModConstants.HistoryFileName,
                        SaveScope.Profile,
                        pending.Archive,
                        JsonOptions,
                        new());
                    var previousStorage = HistoryArchiveJsonConverter.GetLastWriteMetrics();
                    var saved = entry.SaveTo(pending.Path);
                    if (!saved)
                    {
                        Main.Logger.Error(
                            $"Asynchronous history write failed for operation '{pending.Operation}'.");
                        continue;
                    }

                    var storage = HistoryArchiveJsonConverter.GetLastWriteMetrics();
                    LogCompletedWrite(pending, previousStorage, storage);
                }
                catch (Exception exception)
                {
                    Main.Logger.Error(
                        $"Asynchronous history persistence failed for operation '{pending.Operation}': {exception}");
                }
            }
        }

        private static void LogCompletedWrite(
            PendingWrite pending,
            HistoryStorageWriteMetrics previousStorage,
            HistoryStorageWriteMetrics storage)
        {
            if (storage.Sequence == previousStorage.Sequence)
            {
                Main.Logger.Warn(
                    $"History serialization produced no completed payload for operation '{pending.Operation}'.");
                return;
            }

            var combats = pending.Archive.Runs.Sum(run => run.Combats.Count);
            var observations = pending.Archive.Runs.Sum(run => run.Combats.Sum(combat => combat.Events.Count));
            var timelineEvents = pending.Archive.Runs.Sum(run =>
                run.Combats.Sum(combat => combat.Timeline?.Count ?? 0));
            var reduction = storage.UncompressedBytes == 0
                ? 0d
                : 1d - (double)storage.EncodedPayloadBytes / storage.UncompressedBytes;
            Main.Logger.Debug(
                $"History persisted asynchronously ({pending.Operation}): runs={pending.Archive.Runs.Count}, " +
                $"combats={combats}, observations={observations}, timeline={timelineEvents}, " +
                $"combat_json_bytes={storage.UncompressedBytes}, stored_payload_bytes={storage.CompressedBytes}, " +
                $"base64_bytes={storage.EncodedPayloadBytes}, " +
                $"reduction={reduction.ToString("P1", CultureInfo.InvariantCulture)}.");
        }

        private sealed record PendingWrite(string Path, HistoryArchive Archive, string Operation);
    }
}
