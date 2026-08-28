using System;

namespace JxNewMod.Domain
{
    public sealed class ModContentAddresses
    {
        public ModContentAddresses(
            string preloadManifestAddress,
            string scriptCatalogAddress,
            string uiCatalogAddress,
            string playerProfileAddress,
            string entryScriptAddress,
            string initialMapAddress,
            string snapshotTemplateRelativeDirectory = null)
        {
            PreloadManifestAddress = NormalizeRequired(
                preloadManifestAddress,
                nameof(preloadManifestAddress));
            ScriptCatalogAddress = NormalizeRequired(
                scriptCatalogAddress,
                nameof(scriptCatalogAddress));
            UiCatalogAddress = NormalizeRequired(
                uiCatalogAddress,
                nameof(uiCatalogAddress));
            PlayerProfileAddress = NormalizeRequired(
                playerProfileAddress,
                nameof(playerProfileAddress));
            EntryScriptAddress = NormalizeRequired(
                entryScriptAddress,
                nameof(entryScriptAddress));
            InitialMapAddress = NormalizeRequired(
                initialMapAddress,
                nameof(initialMapAddress));
            SnapshotTemplateRelativeDirectory =
                string.IsNullOrWhiteSpace(snapshotTemplateRelativeDirectory)
                    ? null
                    : NormalizeRequired(
                        snapshotTemplateRelativeDirectory,
                        nameof(snapshotTemplateRelativeDirectory));
        }

        public string PreloadManifestAddress { get; }
        public string ScriptCatalogAddress { get; }
        public string UiCatalogAddress { get; }
        public string PlayerProfileAddress { get; }
        public string EntryScriptAddress { get; }
        public string InitialMapAddress { get; }
        public string SnapshotTemplateRelativeDirectory { get; }

        private static string NormalizeRequired(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Content address is required.",
                    parameterName);

            string normalized = value.Trim().Replace('\\', '/').TrimStart('/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
                    throw new ArgumentException(
                        $"Content address '{value}' contains an invalid path segment.",
                        parameterName);
            }

            return normalized;
        }
    }
}
