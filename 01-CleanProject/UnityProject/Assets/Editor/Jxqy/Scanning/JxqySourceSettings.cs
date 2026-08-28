using System;
using System.IO;

namespace Jxqy.Editor.Scanning
{
    [Serializable]
    public sealed class JxqySourceSettings
    {
        public const string SourceRootEnvironmentVariable =
            "JX_MOD_IMPORT_SOURCE_ROOT";
        public const string ReferenceRootEnvironmentVariable =
            "JX_MOD_IMPORT_REFERENCE_ROOT";
        public const string OutputRootEnvironmentVariable =
            "JX_MOD_IMPORT_OUTPUT_ROOT";
        public const string DefaultGameSourceRoot = @"D:\Games\Sword";
        public const string DefaultReferenceSourceRoot = @"D:\gitframework\JxqyHD";
        public const string DefaultOutputRoot =
            "Assets/Mods/XinJianXia/Content";

        public string GameSourceRoot = DefaultGameSourceRoot;
        public string ReferenceSourceRoot = DefaultReferenceSourceRoot;
        public string OutputRoot = DefaultOutputRoot;
        public bool IncludeHashes = true;

        public JxqySourceSettings()
        {
            GameSourceRoot = EnvironmentValueOrDefault(
                SourceRootEnvironmentVariable,
                DefaultGameSourceRoot);
            ReferenceSourceRoot = EnvironmentValueOrDefault(
                ReferenceRootEnvironmentVariable,
                DefaultReferenceSourceRoot);
            OutputRoot = EnvironmentValueOrDefault(
                    OutputRootEnvironmentVariable,
                    DefaultOutputRoot)
                .Replace('\\', '/')
                .TrimEnd('/');
        }

        public string ManifestDirectory => $"{OutputRoot}/Manifests";
        public string ReportDirectory => $"{OutputRoot}/Reports";
        public string SourceManifestPath =>
            $"{ManifestDirectory}/source-manifest.json";
        public string DependencyGraphPath =>
            $"{ManifestDirectory}/dependency-graph.json";
        public string CommandReportPath =>
            $"{ManifestDirectory}/script-command-report.json";
        public string ScriptCatalogPath =>
            $"{ManifestDirectory}/script-catalog.json";
        public string PreloadManifestPath =>
            $"{ManifestDirectory}/preload-manifest.json";

        public void Validate()
        {
            ValidateReadOnlyDirectory(GameSourceRoot, nameof(GameSourceRoot));
            ValidateReadOnlyDirectory(ReferenceSourceRoot, nameof(ReferenceSourceRoot));

            string normalizedOutput = OutputRoot?.Replace('\\', '/').TrimEnd('/');
            bool isModContentRoot = normalizedOutput?.StartsWith(
                "Assets/Mods/",
                StringComparison.Ordinal) == true;
            bool isSharedContentRoot = normalizedOutput?.StartsWith(
                "Assets/Shared/",
                StringComparison.Ordinal) == true;
            if (string.IsNullOrWhiteSpace(normalizedOutput) ||
                (!isModContentRoot && !isSharedContentRoot) ||
                !normalizedOutput.EndsWith("/Content", StringComparison.Ordinal) ||
                normalizedOutput.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{nameof(OutputRoot)} must match " +
                    "Assets/Mods/<OfficialMod>/Content or " +
                    "Assets/Shared/<Package>/Content.");
            }
        }

        private static string EnvironmentValueOrDefault(
            string variable,
            string fallback)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }

        private static void ValidateReadOnlyDirectory(string path, string settingName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"{settingName} is empty.");

            if (!Path.IsPathRooted(path))
                throw new InvalidOperationException($"{settingName} must be an absolute path: {path}");

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"{settingName} does not exist: {path}");
        }
    }
}
