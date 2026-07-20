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
            var dataDirectory = HistoryArchiveJsonConverter.GetDataDirectory(profileId);
            lock (Gate)
            {
                PendingWrites[path] = new(path, dataDirectory, archive, operation);
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
                    var archive = pending.Archive.CreatePersistenceSnapshot();
                    lock (Gate)
                    {
                        if (PendingWrites.ContainsKey(pending.Path))
                            continue;
                    }

                    HistoryArchiveJsonConverter.PrepareForWrite(
                        archive,
                        JsonOptions,
                        pending.DataDirectory);
                    lock (Gate)
                    {
                        if (PendingWrites.ContainsKey(pending.Path))
                            continue;
                    }

                    // PersistentDataEntry deep-clones constructor data through JSON. Initialize it empty so a
                    // history write does not deserialize every prepared combat before serializing it again.
                    var entry = new PersistentDataEntry<HistoryArchive>(
                        ModConstants.ModId,
                        ModConstants.HistoryFileName,
                        SaveScope.Profile,
                        new(),
                        JsonOptions,
                        new());
                    entry.Modify(data =>
                    {
                        data.DataVersion = archive.DataVersion;
                        data.Runs = archive.Runs;
                    });
                    var previousStorage = HistoryArchiveJsonConverter.GetLastWriteMetrics();
                    var saved = entry.SaveTo(pending.Path);
                    if (!saved)
                    {
                        Main.Logger.Error(
                            $"Asynchronous history write failed for operation '{pending.Operation}'.");
                        continue;
                    }

                    HistoryArchiveJsonConverter.CompleteWrite(archive, pending.DataDirectory);
                    var storage = HistoryArchiveJsonConverter.GetLastWriteMetrics();
                    LogCompletedWrite(pending, archive, previousStorage, storage);
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
            HistoryArchive archive,
            HistoryStorageWriteMetrics previousStorage,
            HistoryStorageWriteMetrics storage)
        {
            if (storage.Sequence == previousStorage.Sequence)
            {
                Main.Logger.Warn(
                    $"History serialization produced no completed payload for operation '{pending.Operation}'.");
                return;
            }

            var combats = archive.Runs.Sum(run => run.Combats.Count);
            var observations = archive.Runs.Sum(run => run.Combats.Sum(combat => combat.Events.Count));
            var timelineEvents = archive.Runs.Sum(run =>
                run.Combats.Sum(combat => combat.Timeline?.Count ?? 0));
            var reduction = storage.UncompressedBytes == 0
                ? 0d
                : 1d - (double)storage.StoredBytes / storage.UncompressedBytes;
            Main.Logger.Debug(
                $"History persisted asynchronously ({pending.Operation}): runs={archive.Runs.Count}, " +
                $"combats={combats}, observations={observations}, timeline={timelineEvents}, " +
                $"combat_json_bytes={storage.UncompressedBytes}, combat_file_bytes={storage.StoredBytes}, " +
                $"reduction={reduction.ToString("P1", CultureInfo.InvariantCulture)}.");
        }

        private sealed record PendingWrite(
            string Path,
            string DataDirectory,
            HistoryArchive Archive,
            string Operation);
    }
}
