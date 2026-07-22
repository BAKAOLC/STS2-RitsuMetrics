// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Data.Models;

namespace STS2RitsuMetrics.Data
{
    internal static class ModData
    {
        private static readonly ModDataStore Store = ModDataStore.For(ModConstants.ModId);
        private static readonly Lock LegacyMigrationGate = new();
        private static readonly HashSet<int> PendingLegacyCleanupProfiles = [];

        private static readonly JsonSerializerOptions HistoryJsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IncludeFields = false,
        };

        private static HistoryArchive? _publishedHistoryArchive;
        private static long _publishedHistoryLoadRevision;

        internal static ModSettings Settings => Store.Get<ModSettings>(ModConstants.SettingsKey);

        internal static long HistoryLoadRevision => History.LoadRevision;

        internal static bool IsHistoryReady => History.IsLoadReady;

        internal static HistoryArchive History
        {
            get
            {
                var history = Store.Get<HistoryArchive>(ModConstants.HistoryKey);
                if (!history.RequiresStorageRewrite)
                    return history;

                Main.Logger.Info(
                    $"Converting analytics history storage to {HistoryArchive.CurrentStorageFormat}.");
                history.RequiresStorageRewrite = false;
                QueueHistoryWrite(history, "legacy conversion");
                return history;
            }
        }

        internal static event Action? HistoryReady;

        internal static void Initialize()
        {
            using (RitsuLibFramework.BeginModDataRegistration(ModConstants.ModId))
            {
                Store.Register(
                    ModConstants.SettingsKey,
                    ModConstants.SettingsFileName,
                    SaveScope.Global,
                    () => new ModSettings(),
                    true);
                Store.Register(
                    ModConstants.HistoryKey,
                    ModConstants.HistoryFileName,
                    SaveScope.Profile,
                    false,
                    () => new HistoryArchive(),
                    true);
            }

            RitsuLibFramework.SubscribeLifecycle<ProfileDataReadyEvent>(OnProfileDataReady);
            MigrateSettings();
        }

        internal static void ModifySettings(Action<ModSettings> modifier, bool save = true)
        {
            Store.Modify(ModConstants.SettingsKey, modifier);
            if (save)
                Store.Save(ModConstants.SettingsKey);
        }

        internal static void ModifyHistory(Action<HistoryArchive> modifier, bool save = true,
            string operation = "update")
        {
            Store.Modify<HistoryArchive>(ModConstants.HistoryKey, archive => archive.ApplyMutation(modifier));
            if (!save)
                return;

            var history = Store.Get<HistoryArchive>(ModConstants.HistoryKey);
            history.RequiresStorageRewrite = false;
            QueueHistoryWrite(history, operation);
        }

        internal static void ClearHistory()
        {
            var history = History;
            Store.Modify<HistoryArchive>(ModConstants.HistoryKey, archive => archive.ClearForMutation());
            history.RequiresStorageRewrite = false;
            QueueHistoryWrite(history, "clear");
        }

        internal static void PumpHistoryLoad()
        {
            var history = History;
            var revision = history.LoadRevision;
            if (!history.IsLoadReady ||
                (ReferenceEquals(history, _publishedHistoryArchive) &&
                 revision == Interlocked.Read(ref _publishedHistoryLoadRevision)))
                return;
            _publishedHistoryArchive = history;
            Interlocked.Exchange(ref _publishedHistoryLoadRevision, revision);
            HistoryReady?.Invoke();
        }

        internal static void OnHistoryWriteCompleted(
            int profileId,
            HistoryArchive archive,
            string indexPath,
            string dataDirectory,
            JsonSerializerOptions options)
        {
            lock (LegacyMigrationGate)
            {
                if (!PendingLegacyCleanupProfiles.Contains(profileId))
                    return;
            }

            HistoryArchiveJsonConverter.VerifyPersistedArchive(archive, indexPath, dataDirectory, options);
            var legacyPath = GetLegacyHistoryPath(profileId);
            var directory = Path.GetDirectoryName(legacyPath)
                            ?? throw new InvalidOperationException("Could not resolve the legacy history directory.");
            var legacyFileName = Path.GetFileName(legacyPath);
            foreach (var path in Directory.EnumerateFiles(directory, $"{legacyFileName}*",
                         SearchOption.TopDirectoryOnly))
                File.Delete(path);

            lock (LegacyMigrationGate)
            {
                PendingLegacyCleanupProfiles.Remove(profileId);
            }

            Main.Logger.Info(
                $"Verified analytics history migration for profile {profileId} and removed legacy local files.");
            Callable.From(() => DeleteLegacyCloudHistory(profileId)).CallDeferred();
        }

        private static void QueueHistoryWrite(HistoryArchive history, string operation)
        {
            var profileId = ProfileManager.Instance.CurrentProfileId;
            HistoryPersistenceQueue.Enqueue(history, profileId < 0 ? 1 : profileId, operation);
        }

        private static void OnProfileDataReady(ProfileDataReadyEvent evt)
        {
            if (Store.HasExistingData(ModConstants.HistoryKey))
            {
                Callable.From(() => DeleteLegacyCloudHistory(evt.ProfileId)).CallDeferred();
                return;
            }

            var candidates = GetLegacyHistoryCandidates(evt.ProfileId);
            if (candidates.Length == 0)
                return;

            var history = Store.Get<HistoryArchive>(ModConstants.HistoryKey);
            history.AttachPendingLoad(Task.Run(() => LoadBestLegacyHistory(candidates)));
            history.RequiresStorageRewrite = true;
            lock (LegacyMigrationGate)
            {
                PendingLegacyCleanupProfiles.Add(evt.ProfileId);
            }

            Main.Logger.Info(
                $"Migrating analytics history to '{ModConstants.HistoryFileName}' for profile {evt.ProfileId}.");
        }

        private static HistoryArchive LoadBestLegacyHistory(IReadOnlyList<string> candidates)
        {
            foreach (var path in candidates)
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var runCount = CountRuns(document.RootElement);
                    var combatCount = CountCombats(document.RootElement);
                    if (runCount > 0 && combatCount == 0)
                        throw new JsonException(
                            "History contains run records but no combat records (0.1.13 empty-index regression).");

                    var loaded = JsonSerializer.Deserialize<HistoryArchive>(
                                     File.ReadAllText(path),
                                     HistoryJsonOptions)
                                 ?? throw new JsonException("Legacy analytics history is empty.");
                    var snapshot = loaded.CreatePersistenceSnapshot();
                    var loadedCombatCount = snapshot.Runs.Sum(run => run.Combats.Count);
                    if (loadedCombatCount != combatCount)
                        throw new JsonException(
                            $"Legacy analytics history combat count changed while loading: " +
                            $"indexed={combatCount}, loaded={loadedCombatCount}, source='{path}'.");
                    if (path.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
                        Main.Logger.Warn(
                            "The legacy analytics history main file was damaged; using its verified backup.");
                    return snapshot;
                }
                catch (Exception exception)
                {
                    Main.Logger.Warn($"Legacy analytics history candidate '{path}' failed validation: " +
                                     exception.Message);
                }

            throw new JsonException("Neither the legacy analytics history main file nor its backup is valid.");
        }

        private static int CountRuns(JsonElement root)
        {
            return TryGetProperty(root, "Runs", out var runs) && runs.ValueKind == JsonValueKind.Array
                ? runs.GetArrayLength()
                : 0;
        }

        private static int CountCombats(JsonElement root)
        {
            if (!TryGetProperty(root, "Runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
                return 0;
            var count = 0;
            foreach (var run in runs.EnumerateArray())
                if (TryGetProperty(run, "Combats", out var combats) && combats.ValueKind == JsonValueKind.Array)
                    count = checked(count + combats.GetArrayLength());
            return count;
        }

        private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
        {
            if (root.TryGetProperty(name, out value))
                return true;
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var property in root.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

            value = default;
            return false;
        }

        private static string[] GetLegacyHistoryCandidates(int profileId)
        {
            var legacyPath = GetLegacyHistoryPath(profileId);
            return new[] { legacyPath, legacyPath + ".backup" }
                .Where(File.Exists)
                .ToArray();
        }

        private static string GetLegacyHistoryPath(int profileId)
        {
            var userPath = ProfileManager.GetFilePath(
                ModConstants.LegacyHistoryFileName,
                SaveScope.Profile,
                profileId,
                ModConstants.ModId);
            return ProjectSettings.GlobalizePath(userPath);
        }

        private static void DeleteLegacyCloudHistory(int profileId)
        {
            try
            {
                var remoteStorage = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("Steamworks.SteamRemoteStorage", false))
                    .FirstOrDefault(type => type != null);
                var getFileCount = remoteStorage?.GetMethod(
                    "GetFileCount",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                var getFileNameAndSize = remoteStorage?.GetMethod(
                    "GetFileNameAndSize",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(int), typeof(int).MakeByRefType()],
                    null);
                var fileDelete = remoteStorage?.GetMethod(
                    "FileDelete",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(string)],
                    null);
                if (getFileCount == null || getFileNameAndSize == null || fileDelete == null)
                {
                    Main.Logger.Warn(
                        "Steam Cloud is unavailable; legacy analytics cloud data was not present or removed.");
                    return;
                }

                var expectedSuffix =
                    $"mod_data/{ModConstants.ModId}/modded/profile{profileId}/{ModConstants.LegacyHistoryFileName}";
                var count = (int)getFileCount.Invoke(null, null)!;
                var deleted = 0;
                for (var index = 0; index < count; index++)
                {
                    object?[] arguments = [index, 0];
                    var path = getFileNameAndSize.Invoke(null, arguments) as string;
                    if (path == null || !path.Replace('\\', '/').EndsWith(expectedSuffix, StringComparison.Ordinal))
                        continue;
                    if (fileDelete.Invoke(null, [path]) is true)
                        deleted++;
                }

                Main.Logger.Info($"Removed {deleted} legacy analytics history file(s) from Steam Cloud.");
            }
            catch (Exception exception)
            {
                Main.Logger.Warn($"Could not remove legacy analytics history from Steam Cloud: {exception.Message}");
            }
        }

        private static void MigrateSettings()
        {
            if (Settings.DataVersion >= ModSettings.CurrentDataVersion)
                return;
            ModifySettings(settings =>
            {
                if (settings.DataVersion < 3)
                {
                    settings.DashboardWindows.Clear();
                    settings.DashboardWindows.Add(new()
                    {
                        DashboardId = BuiltInDashboardIds.Meter,
                        PositionY = 92f,
                        Width = 400f,
                        Height = 360f,
                        IsCollapsed = settings.StartCollapsed,
                        IsLocked = settings.LockWindow,
                        Parameters = new(StringComparer.Ordinal)
                        {
                            [DashboardParameterIds.MetricId] = settings.DefaultMetricId,
                        },
                    });
                }

                if (settings.DefaultMetricId == MetricIds.DamageDealt)
                    settings.DefaultMetricId = MetricIds.DamageContribution;
                foreach (var window in settings.DashboardWindows.Where(window =>
                             window.DashboardId == BuiltInDashboardIds.Meter &&
                             window.Parameters.GetValueOrDefault(DashboardParameterIds.MetricId) ==
                             MetricIds.DamageDealt))
                {
                    window.DashboardId = BuiltInDashboardIds.DamageContribution;
                    window.Parameters.Remove(DashboardParameterIds.MetricId);
                }

                settings.DataVersion = ModSettings.CurrentDataVersion;
            });
        }
    }
}
