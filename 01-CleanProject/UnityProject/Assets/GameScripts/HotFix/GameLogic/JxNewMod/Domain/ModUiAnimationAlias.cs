using System;

namespace JxNewMod.Domain
{
    /// <summary>
    /// Maps a UI animation role used by the shared window logic to the
    /// official asset path supplied by one Mod package.
    /// </summary>
    public sealed class ModUiAnimationAlias
    {
        public ModUiAnimationAlias(
            string requestedRelativePath,
            string actualRelativePath)
        {
            RequestedRelativePath = Normalize(
                requestedRelativePath,
                nameof(requestedRelativePath));
            ActualRelativePath = Normalize(
                actualRelativePath,
                nameof(actualRelativePath));
        }

        public string RequestedRelativePath { get; }
        public string ActualRelativePath { get; }

        private static string Normalize(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("UI animation path is required.", parameterName);
            string normalized = value.Trim().Replace('\\', '/').TrimStart('/');
            if (!normalized.EndsWith(".asf", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("../", StringComparison.Ordinal) ||
                normalized.Contains("/./", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Invalid UI animation path: {value}",
                    parameterName);
            }
            return normalized.ToLowerInvariant();
        }
    }
}
