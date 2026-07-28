// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Data.Models;

namespace STS2RitsuMetrics.Tests
{
    public sealed class HistoryArchiveLazyLoadingTests
    {
        [Fact]
        public void FileArchiveKeepsIndexStubsAndLoadsOnlySelectedCombat()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"ritsumetrics-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var options = new JsonSerializerOptions();
                var combat = Combat("loaded", [Player()]);
                var payload = JsonSerializer.SerializeToUtf8Bytes(combat, options);
                var loadedFileName = FileName("run", combat.CombatId);
                WriteCombatFile(Path.Combine(directory, loadedFileName), payload);
                var notRequested = Combat("not-requested", [Player()]);
                var notRequestedPayload = JsonSerializer.SerializeToUtf8Bytes(notRequested, options);
                var notRequestedFileName = FileName("run", notRequested.CombatId);
                var notRequestedPath = Path.Combine(directory, notRequestedFileName);
                WriteCombatFile(notRequestedPath, notRequestedPayload);
                var notRequestedFile = File.ReadAllBytes(notRequestedPath);
                var indexJson = JsonSerializer.Serialize(new
                {
                    DataVersion = 1,
                    StorageFormat = HistoryArchive.CurrentStorageFormat,
                    Runs = new[]
                    {
                        new
                        {
                            RunId = "run",
                            StartedAtUtc = DateTimeOffset.UnixEpoch,
                            EndedAtUtc = (DateTimeOffset?)DateTimeOffset.UnixEpoch.AddMinutes(2),
                            IsMultiplayer = false,
                            IsDaily = false,
                            IsVictory = (bool?)true,
                            IsAbandoned = (bool?)false,
                            Identity = (RunIdentitySnapshot?)null,
                            Combats = new[]
                            {
                                Reference(combat, loadedFileName, payload.Length, true),
                                Reference(notRequested, notRequestedFileName, notRequestedPayload.Length),
                            },
                        },
                    },
                }, options);

                var archive = HistoryArchiveJsonConverter.ReadFileArchive(indexJson, options, directory);

                Assert.True(archive.IsLoadReady);
                Assert.Equal(2, archive.Runs.Single().Combats.Count);
                Assert.Single(archive.Runs.Single().Combats[0].Players);
                Assert.Empty(archive.Runs.Single().Combats[1].Players);

                var summaries = archive.MaterializeRunSummary("run", out var cacheChanged);

                Assert.True(cacheChanged);
                Assert.Equal(2, summaries!.Combats.Count);
                foreach (var summary in summaries.Combats)
                {
                    var player = Assert.Single(summary.Players);
                    Assert.Equal(42m, player.Totals[MetricIds.DamageDealt]);
                    Assert.Empty(summary.Events);
                    Assert.Empty(summary.Timeline!);
                }

                Assert.Single(archive.Runs.Single().Combats[1].Players);
                File.Delete(notRequestedPath);

                var cachedSummaries = archive.MaterializeRunSummary("run", out cacheChanged);

                Assert.False(cacheChanged);
                Assert.Equal(2, cachedSummaries!.Combats.Count);
                Assert.All(cachedSummaries.Combats, summary => Assert.Single(summary.Players));
                File.WriteAllBytes(notRequestedPath, notRequestedFile);

                var materialized = archive.MaterializeRun("run", true, true, "loaded");

                var selected = Assert.Single(materialized!.Combats);
                Assert.Equal("loaded", selected.CombatId);
                Assert.Single(selected.Players);
                Assert.Single(archive.Runs.Single().Combats[0].Players);
                Assert.Single(archive.Runs.Single().Combats[1].Players);

                var persistenceSnapshot = archive.CreatePersistenceSnapshot();
                HistoryArchiveJsonConverter.PrepareForWrite(persistenceSnapshot, options, directory);
                var persistedIndex = JsonSerializer.Serialize(persistenceSnapshot, options);
                var reopened = HistoryArchiveJsonConverter.ReadFileArchive(
                    persistedIndex,
                    options,
                    directory);

                Assert.Equal(notRequestedFile, File.ReadAllBytes(notRequestedPath));
                Assert.All(
                    reopened.Runs.Single().Combats,
                    summary => Assert.Single(summary.Players));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static CombatSnapshot Combat(string combatId, IReadOnlyList<PlayerMetricSnapshot> players)
        {
            return new(
                "run",
                combatId,
                0,
                1,
                string.Empty,
                "Encounter",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                true,
                1,
                players,
                [],
                []);
        }

        private static PlayerMetricSnapshot Player()
        {
            return new(
                "player",
                1,
                "Player",
                "CHARACTER.IRONCLAD",
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [MetricIds.DamageDealt] = 42m,
                },
                new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal));
        }

        private static object Reference(
            CombatSnapshot combat,
            string fileName,
            int payloadLength,
            bool includePlayers = false)
        {
            return new
            {
                combat.CombatId,
                FileName = fileName,
                Encoding = "json",
                UncompressedLength = payloadLength,
                StoredLength = payloadLength + 12,
                combat.ActIndex,
                combat.Floor,
                combat.EncounterId,
                combat.EncounterName,
                combat.StartedAtUtc,
                combat.EndedAtUtc,
                combat.Completed,
                combat.RoundCount,
                Players = includePlayers ? combat.Players : null,
            };
        }

        private static string FileName(string runId, string combatId)
        {
            var identity = Encoding.UTF8.GetBytes($"{runId}\0{combatId}");
            return $"{Convert.ToHexStringLower(SHA256.HashData(identity))}.bin";
        }

        private static void WriteCombatFile(string path, byte[] payload)
        {
            var file = new byte[payload.Length + 12];
            "RTMX"u8.CopyTo(file);
            file[4] = 1;
            file[5] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8, 4), payload.Length);
            payload.CopyTo(file, 12);
            File.WriteAllBytes(path, file);
        }
    }
}
