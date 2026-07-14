// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Data.Models
{
    [JsonConverter(typeof(HistoryArchiveJsonConverter))]
    public sealed class HistoryArchive
    {
        public const int CurrentDataVersion = 1;
        internal const string CurrentStorageFormat = "combat-segments-v1";

        public int DataVersion { get; set; } = CurrentDataVersion;

        // ReSharper disable once CollectionNeverUpdated.Global
        public List<RunSnapshot> Runs { get; set; } = [];

        [JsonIgnore] internal bool RequiresStorageRewrite { get; set; }
    }
}
