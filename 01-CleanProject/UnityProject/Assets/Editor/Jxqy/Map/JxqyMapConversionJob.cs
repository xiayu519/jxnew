using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jxqy.Editor.Animation.Conversion;
using Jxqy.Editor.Scanning;
using UnityEditor;
using UnityEngine;

namespace Jxqy.Editor.Map
{
    public static class JxqyMapConversionJob
    {
        [MenuItem("TEngine/Jxqy/Convert All Maps")]
        public static void ConvertAll()
        {
            ConvertAll(new JxqySourceSettings());
        }

        public static JxqyMapConversionReport ConvertAll(
            JxqySourceSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            string reportAssetPath =
                $"{settings.ReportDirectory}/map-conversion-report.json";
            var report = new JxqyMapConversionReport
            {
                ConverterVersion = JxqyMapConverter.MapConverterVersion,
                GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            try
            {
                settings.Validate();
                JxqySourceManifest manifest = JsonUtility.FromJson<JxqySourceManifest>(
                    File.ReadAllText(GetAbsoluteAssetPath(
                        settings.SourceManifestPath)));
                var sources = manifest.Files
                    .Where(file => file.Kind == JxqyFileKind.Map)
                    .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                    .ToArray();
                report.InputFileCount = sources.Length;
                var converter = new JxqyMapConverter();
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
                        report.Add(new JxqyMapConversionFileReport
                        {
                            RelativePath = source.RelativePath,
                            StableId = source.StableId,
                            Status = JxqyMapConverter.FailedStatus,
                            Error = $"{exception.GetType().Name}: {exception.Message}"
                        });
                        Debug.LogError(
                            $"Jxqy MAP conversion failed: {source.RelativePath}\n{exception}");
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                JxqyAnimationConverter.WriteJsonAsset(
                    reportAssetPath,
                    report,
                    true);
            }

            string summary =
                $"Jxqy MAP conversion complete. Inputs={report.InputFileCount}, " +
                $"Converted={report.ConvertedFileCount}, Reused={report.ReusedFileCount}, " +
                $"Failed={report.FailedFileCount}, Tiles={report.TotalTileCount}.";
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
    }
}
