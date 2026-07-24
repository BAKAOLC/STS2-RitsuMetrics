// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class RunTransferService
    {
        private const int CurrentFormatVersion = 1;
        private const int MaxPayloadCharacters = 256 * 1024 * 1024;
        private const string RunPayloadType = "ritsumetrics-run";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            MaxDepth = 128,
        };

        internal static string Serialize(RunSnapshot run)
        {
            ArgumentNullException.ThrowIfNull(run);
            return JsonSerializer.Serialize(
                new RunTransferPackage(RunPayloadType, CurrentFormatVersion, ModConstants.Version, run),
                JsonOptions);
        }

        internal static bool TryDeserialize(string payload, out RunSnapshot? run, out string error)
        {
            run = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                error = "Clipboard is empty.";
                return false;
            }

            if (payload.Length > MaxPayloadCharacters)
            {
                error = "Clipboard data exceeds the supported size limit.";
                return false;
            }

            try
            {
                var package = JsonSerializer.Deserialize<RunTransferPackage>(payload, JsonOptions);
                if (package == null ||
                    !string.Equals(package.PayloadType, RunPayloadType, StringComparison.Ordinal) ||
                    package.FormatVersion != CurrentFormatVersion ||
                    package.Run == null)
                {
                    error = "Clipboard does not contain a supported RitsuMetrics run.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.Run.RunId))
                {
                    error = "Clipboard run has no identity.";
                    return false;
                }

                run = package.Run;
                return true;
            }
            catch (JsonException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private sealed record RunTransferPackage(
            string PayloadType,
            int FormatVersion,
            string ModVersion,
            RunSnapshot? Run);
    }
}
