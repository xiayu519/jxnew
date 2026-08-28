using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jxqy.Editor.Animation.Conversion;
using Jxqy.Editor.Scanning;
using UnityEditor;
using UnityEngine;

namespace Jxqy.Editor.Image
{
    public static class JxqyStaticImageConversionJob
    {
        [MenuItem("TEngine/Jxqy/Convert Static Images")]
        public static void ConvertAll()
        {
            ConvertAll(new JxqySourceSettings());
        }

        public static JxqyStaticImageConversionReport ConvertAll(
            JxqySourceSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            string reportAssetPath =
                $"{settings.ReportDirectory}/image-conversion-report.json";
            var report = new JxqyStaticImageConversionReport
            {
                ConverterVersion = JxqyStaticImageConverter.ConverterVersion,
                GeneratedUtc = DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture)
            };
            try
            {
                settings.Validate();
                JxqySourceManifest manifest =
                    JsonUtility.FromJson<JxqySourceManifest>(
                        File.ReadAllText(
                            GetAbsoluteAssetPath(
                                settings.SourceManifestPath)));
                var sources = manifest.Files
                    .Where(file => file.Kind == JxqyFileKind.Image)
                    .OrderBy(
                        file => file.RelativePath,
                        StringComparer.Ordinal)
                    .ToArray();
                report.InputFileCount = sources.Length;
                var converter = new JxqyStaticImageConverter();
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
                        report.Add(new JxqyStaticImageFileReport
                        {
                            RelativePath = source.RelativePath,
                            StableId = source.StableId,
                            Status = JxqyStaticImageConverter.FailedStatus,
                            Error =
                                $"{exception.GetType().Name}: {exception.Message}"
                        });
                    }
                }
                JxqyAnimationConverter.WriteJsonAsset(
                    reportAssetPath,
                    report,
                    true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            string summary =
                $"Jxqy static image conversion complete. Inputs={report.InputFileCount}, " +
                $"Converted={report.ConvertedFileCount}, Reused={report.ReusedFileCount}, " +
                $"Failed={report.FailedFileCount}, Bytes={report.TotalBytes}.";
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
