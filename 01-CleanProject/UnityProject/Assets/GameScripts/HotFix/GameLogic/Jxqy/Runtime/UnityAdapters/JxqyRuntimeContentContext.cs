using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jxqy.UnityAdapters
{
    /// <summary>
    /// Immutable content coordinates supplied by the selected Mod adapter.
    /// Jxqy runtime code consumes this value without depending on the Mod host.
    /// </summary>
    public sealed class JxqyRuntimeContentContext
    {
        public JxqyRuntimeContentContext(
            string packageName,
            string preloadManifestAddress,
            string scriptCatalogAddress,
            string playerProfileAddress,
            string entryScriptAddress,
            string initialMapStableId,
            string saveNamespace,
            JxqyRuntimeContentProfile contentProfile,
            IJxqyLegacyMediaAddressResolver mediaAddressResolver = null,
            IEnumerable<JxqyLegacyAnimationAlias> animationAliases = null,
            bool? combinedCharacterSheet = null,
            IEnumerable<JxqyRuntimeResourcePackage> fallbackPackages = null,
            string scriptDialectId = null,
            string snapshotTemplateRelativeDirectory = null)
        {
            PackageName = RequirePackageName(packageName);
            PreloadManifestAddress = RequireAddress(
                preloadManifestAddress,
                nameof(preloadManifestAddress));
            ScriptCatalogAddress = RequireAddress(
                scriptCatalogAddress,
                nameof(scriptCatalogAddress));
            PlayerProfileAddress = RequirePlayerProfileAddress(
                playerProfileAddress);
            EntryScriptAddress = RequireAddress(
                entryScriptAddress,
                nameof(entryScriptAddress));
            InitialMapStableId = RequireText(
                initialMapStableId,
                nameof(initialMapStableId));
            SaveNamespace = RequireSaveNamespace(saveNamespace);
            ContentProfile = contentProfile ??
                             throw new ArgumentNullException(
                                 nameof(contentProfile));
            ContentRootAddress = GetContentRootAddress(
                PreloadManifestAddress);
            TextRootAddress = ContentRootAddress + "/text";
            SaveTemplateRelativeDirectory =
                GetSaveTemplateRelativeDirectory(
                    PlayerProfileAddress,
                    TextRootAddress);
            SnapshotTemplateRelativeDirectory =
                string.IsNullOrWhiteSpace(
                    snapshotTemplateRelativeDirectory)
                    ? SaveTemplateRelativeDirectory
                    : RequireRelativeAddress(
                        snapshotTemplateRelativeDirectory,
                        nameof(snapshotTemplateRelativeDirectory));
            MediaAddressResolver = mediaAddressResolver ??
                                   JxqyLegacyMediaAddressResolver
                                       .ForContentRoot(ContentRootAddress);
            AnimationAliases = (animationAliases ??
                                Enumerable.Empty<JxqyLegacyAnimationAlias>())
                .ToArray();
            CombinedCharacterSheet = combinedCharacterSheet ??
                                     ContentProfile.CombinedCharacterSheet;
            ResourcePackages = new JxqyResourcePackageChain(
                PackageName,
                fallbackPackages);
            ScriptDialectId = string.IsNullOrWhiteSpace(scriptDialectId)
                ? "jxqy-original"
                : RequireText(scriptDialectId, nameof(scriptDialectId));
        }

        public string PackageName { get; }
        public string PreloadManifestAddress { get; }
        public string ScriptCatalogAddress { get; }
        public string PlayerProfileAddress { get; }
        public string EntryScriptAddress { get; }
        public string InitialMapStableId { get; }
        public string SaveNamespace { get; }
        public string ContentRootAddress { get; }
        public string TextRootAddress { get; }
        public string SaveTemplateRelativeDirectory { get; }
        public string SnapshotTemplateRelativeDirectory { get; }
        public JxqyRuntimeContentProfile ContentProfile { get; }
        public IJxqyLegacyMediaAddressResolver MediaAddressResolver { get; }
        public IReadOnlyList<JxqyLegacyAnimationAlias> AnimationAliases { get; }
        public bool CombinedCharacterSheet { get; }
        public JxqyResourcePackageChain ResourcePackages { get; }
        public string ScriptDialectId { get; }

        public string EntryScriptFileName => ParentDirectoryName(
            EntryScriptAddress,
            "content.txt");

        public string NewGameStateAddress =>
            SaveTemplateAddress("game.ini");

        public string NewGameGoodsAddress =>
            SaveTemplateAddress("goods0.ini");

        public string NewGameMagicAddress =>
            SaveTemplateAddress("magic0.ini");

        public string TalkIndexAddress =>
            TextAddress(ContentProfile.TalkIndex);

        public string NewGameTrapsAddress =>
            SaveTemplateAddress("traps.ini");

        public string NpcLevelsAddress =>
            TextAddress(ContentProfile.NpcLevels);

        public string MapNamesAddress =>
            TextAddress(ContentProfile.MapNames);

        public string PortraitsAddress =>
            TextAddress(ContentProfile.Portraits);

        public string SceneCatalogAddress =>
            ContentAddress(ContentProfile.SceneCatalogRelativeAddress);

        public string CombatFloatTextPrefabAddress =>
            ContentAddress(
                ContentProfile.CombatFloatTextPrefabRelativeAddress);

        public string WaterDisplacementTextureAddress =>
            ContentAddress(
                ContentProfile.WaterDisplacementRelativeAddress);

        public string LittleMapTextureAddress(string baseName) =>
            ContentAddress(
                ContentProfile.LittleMapRootRelativeAddress + "/" +
                RequirePathSegment(baseName, nameof(baseName)) +
                ContentProfile.LittleMapFileExtension);

        public bool TryResolveLittleMapTextureAddress(
            string baseName,
            out string address)
        {
            string exactAddress = LittleMapTextureAddress(baseName);
            if (!JxqyResourceAddressCatalog.IsConfigured ||
                JxqyResourceAddressCatalog.Contains(exactAddress))
            {
                address = exactAddress;
                return true;
            }

            string rootAddress = ContentAddress(
                ContentProfile.LittleMapRootRelativeAddress);
            string mapAssetStem = GetLegacyMapAssetStem(baseName);
            return JxqyResourceAddressCatalog.TryFindUniqueSharedFileAddress(
                rootAddress,
                mapAssetStem,
                ContentProfile.LittleMapFileExtension,
                out address);
        }

        public string MaterialAddress(string materialKey) =>
            ContentAddress(
                ContentProfile.MaterialRootRelativeAddress + "/" +
                RequirePathSegment(materialKey, nameof(materialKey)) +
                ".mat");

        public string TextAddress(JxqyLegacyTextSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            return TextAddress(source.RelativeDirectory, source.FileName);
        }

        public string TextAddress(
            string relativeDirectory,
            string fileName)
        {
            string directory = RequireRelativeAddress(
                relativeDirectory,
                nameof(relativeDirectory));
            string safeFileName = RequirePathSegment(
                fileName,
                nameof(fileName));
            return $"{TextRootAddress}/{directory}/" +
                   $"{safeFileName}/content.txt";
        }

        public string ContentAddress(string relativeAddress) =>
            ContentRootAddress + "/" +
            RequireRelativeAddress(relativeAddress, nameof(relativeAddress));

        public string PlayerAddress(int index) =>
            SaveTemplateAddress($"player{Math.Max(0, index)}.ini");

        public string MagicAddress(int index) =>
            SaveTemplateAddress($"magic{Math.Max(0, index)}.ini");

        public static JxqyRuntimeContentContext XinJianXiaDefault { get; } =
            new(
                JxqyResourceLocations.PackageName,
                "jxqy/manifests/preload-manifest.json",
                "jxqy/manifests/script-catalog.json",
                "jxqy/text/ini/save/player0.ini/content.txt",
                "jxqy/text/script/common/newgame.txt/content.txt",
                "map:map/map001_衡山.map",
                "jxnewmod.xinjianxia.v1",
                JxqyRuntimeContentProfile.XinJianXia,
                JxqyLegacyMediaAddressResolver.XinJianXia,
                new[]
                {
                    new JxqyLegacyAnimationAlias(
                        "bottom",
                        "window.asf",
                        "jxqy/animations/asf/ui/bottom/window-bottom.asf/animation.json"),
                    new JxqyLegacyAnimationAlias(
                        "column",
                        "panel9.asf",
                        "jxqy/animations/asf/ui/column/window-column.asf/animation.json"),
                    new JxqyLegacyAnimationAlias(
                        "dialog",
                        "panel.asf",
                        "jxqy/animations/asf/ui/dialog/window-dialog.asf/animation.json"),
                    new JxqyLegacyAnimationAlias(
                        "littlemap",
                        "panel.asf",
                        "jxqy/animations/asf/ui/littlemap/window-littlemap.asf/animation.json"),
                    new JxqyLegacyAnimationAlias(
                        "saveload",
                        "panel.asf",
                        "jxqy/animations/asf/ui/saveload/window-saveload.asf/animation.json"),
                    new JxqyLegacyAnimationAlias(
                        "timer",
                        "window.asf",
                        "jxqy/animations/asf/ui/timer/window-timer.asf/animation.json"),
                    new JxqyLegacyAnimationAlias(
                        "top",
                        "window.asf",
                        "jxqy/animations/asf/ui/top/window-top.asf/animation.json"),
                },
                scriptDialectId: "xin-jian-xia-original");

        public static string GetContentRootAddress(
            string preloadManifestAddress)
        {
            string address = RequireAddress(
                preloadManifestAddress,
                nameof(preloadManifestAddress));
            const string marker = "/manifests/";
            int markerIndex = address.IndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);
            if (markerIndex <= 0)
            {
                throw new ArgumentException(
                    "Preload manifest must be below a Mod manifests directory.",
                    nameof(preloadManifestAddress));
            }
            return address.Substring(0, markerIndex);
        }

        private string SaveTemplateAddress(string fileName)
        {
            int playerDirectoryLength =
                "player0.ini/content.txt".Length;
            string root = PlayerProfileAddress.Substring(
                0,
                PlayerProfileAddress.Length - playerDirectoryLength);
            return root + fileName.ToLowerInvariant() + "/content.txt";
        }

        private static string GetSaveTemplateRelativeDirectory(
            string playerProfileAddress,
            string textRootAddress)
        {
            string prefix = textRootAddress + "/";
            const string suffix = "/player0.ini/content.txt";
            if (!playerProfileAddress.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !playerProfileAddress.EndsWith(
                    suffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Player profile address must be below the selected " +
                    "Mod text root.",
                    nameof(playerProfileAddress));
            }

            int length = playerProfileAddress.Length -
                         prefix.Length - suffix.Length;
            if (length <= 0)
            {
                throw new ArgumentException(
                    "Player profile address must include a save template " +
                    "directory.",
                    nameof(playerProfileAddress));
            }
            return RequireRelativeAddress(
                playerProfileAddress.Substring(prefix.Length, length),
                nameof(playerProfileAddress));
        }

        private static string RequirePathSegment(
            string value,
            string parameterName)
        {
            string result = RequireText(value, parameterName)
                .Replace('\\', '/');
            if (result.IndexOf('/') >= 0 ||
                result == "." ||
                result == "..")
            {
                throw new ArgumentException(
                    "Value must be one content path segment.",
                    parameterName);
            }
            return result.ToLowerInvariant();
        }

        private static string GetLegacyMapAssetStem(string baseName)
        {
            string value = RequirePathSegment(baseName, nameof(baseName));
            int firstDigit = 0;
            while (firstDigit < value.Length &&
                   !char.IsDigit(value[firstDigit]))
            {
                firstDigit++;
            }
            if (firstDigit >= value.Length)
                return value;

            int end = firstDigit;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;
            if (end < value.Length && value[end] == '_')
            {
                int variantEnd = end + 1;
                while (variantEnd < value.Length &&
                       char.IsDigit(value[variantEnd]))
                {
                    variantEnd++;
                }
                if (variantEnd > end + 1)
                    end = variantEnd;
            }
            return value.Substring(0, end);
        }

        private static string RequireRelativeAddress(
            string value,
            string parameterName)
        {
            string result = RequireText(value, parameterName)
                .Replace('\\', '/')
                .Trim('/');
            if (result.Length == 0 ||
                result.Split('/').Any(
                    segment => segment.Length == 0 ||
                               segment == "." ||
                               segment == ".."))
            {
                throw new ArgumentException(
                    "Relative content address contains an invalid segment.",
                    parameterName);
            }
            return result.ToLowerInvariant();
        }

        private static string ParentDirectoryName(
            string address,
            string expectedFileName)
        {
            string normalized = address.Replace('\\', '/');
            string suffix = "/" + expectedFileName;
            if (!normalized.EndsWith(
                    suffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Address must end with '{suffix}'.",
                    nameof(address));
            }
            string parent = normalized.Substring(
                0,
                normalized.Length - suffix.Length);
            return Path.GetFileName(parent);
        }

        private static string RequirePlayerProfileAddress(string value)
        {
            string address = RequireAddress(
                value,
                nameof(value));
            if (!address.EndsWith(
                    "/player0.ini/content.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Player profile address must end with " +
                    "'/player0.ini/content.txt'.",
                    nameof(value));
            }
            return address;
        }

        private static string RequirePackageName(string value)
        {
            string result = RequireText(value, nameof(value));
            if (result.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException(
                    "Package name cannot contain path separators.",
                    nameof(value));
            return result;
        }

        private static string RequireAddress(
            string value,
            string parameterName)
        {
            string result = RequireText(value, parameterName)
                .Replace('\\', '/')
                .TrimStart('/');
            if (result.Contains("../") ||
                result.Contains("/./") ||
                result.EndsWith("/..", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Content address contains path traversal.",
                    parameterName);
            }
            return result.ToLowerInvariant();
        }

        private static string RequireSaveNamespace(string value)
        {
            string result = RequireText(value, nameof(value));
            foreach (char character in result)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '.' &&
                    character != '-' &&
                    character != '_')
                {
                    throw new ArgumentException(
                        "Save namespace contains an invalid character.",
                        nameof(value));
                }
            }
            return result;
        }

        private static string RequireText(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Value is required.",
                    parameterName);
            return value.Trim();
        }
    }
}
