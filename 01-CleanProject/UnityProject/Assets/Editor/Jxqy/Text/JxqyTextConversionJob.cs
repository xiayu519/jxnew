using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jxqy.Editor.Animation.Conversion;
using Jxqy.Editor.Scanning;
using UnityEditor;
using UnityEngine;

namespace Jxqy.Editor.Text
{
    public static class JxqyTextConversionJob
    {
        [MenuItem("TEngine/Jxqy/Convert All Text Resources")]
        public static void ConvertAll()
        {
            ConvertAll(new JxqySourceSettings());
        }

        public static JxqyTextConversionReport ConvertAll(
            JxqySourceSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            string reportAssetPath =
                $"{settings.ReportDirectory}/text-conversion-report.json";
            var report = new JxqyTextConversionReport
            {
                ConverterVersion = JxqyTextConverter.TextConverterVersion,
                GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            try
            {
                settings.Validate();
                JxqySourceManifest manifest = JsonUtility.FromJson<JxqySourceManifest>(
                    File.ReadAllText(GetAbsoluteAssetPath(
                        settings.SourceManifestPath)));
                var sources = manifest.Files
                    .Where(file => JxqyTextConverter.IsTextKind(file.Kind))
                    .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                    .ToArray();
                CleanupStaleGeneratedText(
                    settings.OutputRoot,
                    reportAssetPath,
                    sources);
                report.InputFileCount = sources.Length;
                var converter = new JxqyTextConverter();
                foreach (JxqySourceFileRecord source in sources)
                {
                    try
                    {
                        report.Add(converter.Convert(
                            source,
                            settings.GameSourceRoot,
                            settings.OutputRoot));
                    }
                    catch (Exception exception)
                    {
                        report.Add(new JxqyTextConversionFileReport
                        {
                            RelativePath = source.RelativePath,
                            StableId = source.StableId,
                            Kind = source.Kind.ToString(),
                            Status = JxqyTextConverter.FailedStatus,
                            Error = $"{exception.GetType().Name}: {exception.Message}"
                        });
                    }
                }
                JxqyAnimationConverter.WriteJsonAsset(
                    reportAssetPath,
                    report,
                    false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            string summary =
                $"Jxqy text conversion complete. Inputs={report.InputFileCount}, " +
                $"Converted={report.ConvertedFileCount}, Reused={report.ReusedFileCount}, " +
                $"Failed={report.FailedFileCount}, Lines={report.TotalLineCount}, " +
                $"Sections={report.TotalSectionCount}, Properties={report.TotalPropertyCount}.";
            if (report.FailedFileCount == 0)
                Debug.Log(summary);
            else
                Debug.LogError(summary);
            return report;
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void CleanupStaleGeneratedText(
            string outputRoot,
            string reportAssetPath,
            JxqySourceFileRecord[] currentSources)
        {
            string absoluteReport = GetAbsoluteAssetPath(reportAssetPath);
            if (!File.Exists(absoluteReport))
                return;

            JxqyTextConversionReport previous =
                JsonUtility.FromJson<JxqyTextConversionReport>(
                    File.ReadAllText(absoluteReport));
            if (previous?.Files == null)
                return;

            var currentIds = new System.Collections.Generic.HashSet<string>(
                currentSources.Select(source => source.StableId),
                StringComparer.Ordinal);
            string normalizedOutput = outputRoot.Replace('\\', '/').TrimEnd('/');
            foreach (JxqyTextConversionFileReport stale in previous.Files)
            {
                if (currentIds.Contains(stale.StableId))
                    continue;
                string generatedDirectory =
                    $"{normalizedOutput}/Text/{stale.RelativePath.Replace('\\', '/')}";
                AssetDatabase.DeleteAsset(generatedDirectory);
            }
        }
    }
}
