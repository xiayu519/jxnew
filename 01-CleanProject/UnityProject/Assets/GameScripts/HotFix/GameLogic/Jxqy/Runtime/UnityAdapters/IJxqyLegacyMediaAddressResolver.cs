using System;
using System.IO;

namespace Jxqy.UnityAdapters
{
    public interface IJxqyLegacyMediaAddressResolver
    {
        string ResolveMusic(string legacyPath);
        string ResolveSound(string legacyPath);
        string ResolveVideo(string legacyPath);
    }

    /// <summary>
    /// Converts original script filenames into package-local YooAsset
    /// addresses using the New Swordsman Love content layout.
    /// </summary>
    public sealed class JxqyLegacyMediaAddressResolver :
        IJxqyLegacyMediaAddressResolver
    {
        public static IJxqyLegacyMediaAddressResolver XinJianXia { get; } =
            ForContentRoot("jxqy");

        public static IJxqyLegacyMediaAddressResolver ForContentRoot(
            string contentRootAddress)
        {
            if (string.IsNullOrWhiteSpace(contentRootAddress))
                throw new ArgumentException(
                    "Content root address is empty.",
                    nameof(contentRootAddress));
            string root = contentRootAddress
                .Trim()
                .Replace('\\', '/')
                .Trim('/');
            if (root.Length == 0 || root.Contains("../"))
                throw new ArgumentException(
                    "Content root address is invalid.",
                    nameof(contentRootAddress));
            return new JxqyLegacyMediaAddressResolver(
                root + "/",
                root + "/media/music/music",
                string.Empty,
                ".mp3",
                root + "/audio/sound/sound",
                string.Empty,
                ".wav",
                root + "/media/video/video",
                string.Empty,
                ".mp4");
        }

        private readonly MediaRule _music;
        private readonly MediaRule _sound;
        private readonly MediaRule _video;

        private JxqyLegacyMediaAddressResolver(
            string addressPrefix,
            string musicRoot,
            string musicContainerExtension,
            string musicOutput,
            string soundRoot,
            string soundContainerExtension,
            string soundOutput,
            string videoRoot,
            string videoContainerExtension,
            string videoOutput)
        {
            string prefix = addressPrefix.ToLowerInvariant();
            _music = new MediaRule(
                prefix,
                musicRoot,
                musicContainerExtension,
                musicOutput);
            _sound = new MediaRule(
                prefix,
                soundRoot,
                soundContainerExtension,
                soundOutput);
            _video = new MediaRule(
                prefix,
                videoRoot,
                videoContainerExtension,
                videoOutput);
        }

        public string ResolveMusic(string legacyPath) =>
            Resolve(legacyPath, _music);

        public string ResolveSound(string legacyPath) =>
            Resolve(legacyPath, _sound);

        public string ResolveVideo(string legacyPath) =>
            Resolve(legacyPath, _video);

        private static string Resolve(string legacyPath, MediaRule rule)
        {
            if (string.IsNullOrWhiteSpace(legacyPath))
                throw new ArgumentException(
                    "Legacy media path is empty.",
                    nameof(legacyPath));
            string normalized = legacyPath.Trim().Replace('\\', '/');
            if (normalized.StartsWith(
                    rule.AddressPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return normalized.ToLowerInvariant();
            }

            string stem = Path.GetFileNameWithoutExtension(
                Path.GetFileName(normalized));
            string address = string.IsNullOrEmpty(rule.ContainerExtension)
                ? $"{rule.Root}/{stem}{rule.OutputName}"
                : $"{rule.Root}/{stem}{rule.ContainerExtension}/" +
                  rule.OutputName;
            return address.ToLowerInvariant();
        }

        private readonly struct MediaRule
        {
            public MediaRule(
                string addressPrefix,
                string root,
                string containerExtension,
                string outputName)
            {
                AddressPrefix = addressPrefix;
                Root = root;
                ContainerExtension = containerExtension;
                OutputName = outputName;
            }

            public string AddressPrefix { get; }
            public string Root { get; }
            public string ContainerExtension { get; }
            public string OutputName { get; }
        }
    }
}
