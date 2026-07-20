// SPDX-License-Identifier: MPL-2.0

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

        internal static ModSettings Settings => Store.Get<ModSettings>(ModConstants.SettingsKey);

        internal static long HistoryLoadRevision => History.LoadRevision;

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

        private static void QueueHistoryWrite(HistoryArchive history, string operation)
        {
            var profileId = ProfileManager.Instance.CurrentProfileId;
            HistoryPersistenceQueue.Enqueue(history, profileId < 0 ? 1 : profileId, operation);
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
