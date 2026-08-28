using System;
using System.Collections.Generic;
using System.IO;
using Jxqy.Domain.Content;
using UnityEngine;

namespace Jxqy.UnityAdapters
{
    public enum JxqyLegacyResourceKind
    {
        Animation,
        Music,
        Sound,
        Video,
        Text,
    }

    public readonly struct JxqyResolvedResourceLocation
    {
        public JxqyResolvedResourceLocation(
            string logicalKey,
            string address,
            string packageName)
        {
            LogicalKey = logicalKey ?? string.Empty;
            Address = address ?? string.Empty;
            PackageName = string.IsNullOrWhiteSpace(packageName)
                ? JxqyResourceAddressCatalog.ActivePackageName
                : packageName.Trim();
        }

        public string LogicalKey { get; }
        public string Address { get; }
        public string PackageName { get; }
    }

    /// <summary>
    /// Provides a case-insensitive view of the generated preload addresses.
    /// Legacy scripts may reference assets that were absent from the source
    /// installation; callers must consult this catalog before asking YooAsset
    /// to load a synthesized address.
    /// </summary>
    public static class JxqyResourceAddressCatalog
    {
        private static readonly HashSet<string> Addresses =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SharedAddresses =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>>
            AddressesByOwner =
                new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Overrides =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LegacyAliases =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<
            string,
            JxqyResolvedResourceLocation> LocationsByLogicalKey =
                new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<
            string,
            JxqyResolvedResourceLocation> LogicalOverrides =
                new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ReportedMissing =
            new(StringComparer.OrdinalIgnoreCase);
        private static string _activeOwnerStableId = string.Empty;
        private static string _activePackageName =
            JxqyResourceLocations.PackageName;
        private static JxqyYooAssetPackageResolver _packageResolver;
        private static bool _configured;

        public static bool IsConfigured => _configured;
        public static string ActiveOwnerStableId => _activeOwnerStableId;
        public static string ActivePackageName => _activePackageName;

        public static void Configure(
            JxqyPreloadManifest manifest,
            string activeOwnerStableId,
            string packageName = null,
            JxqyYooAssetPackageResolver packageResolver = null)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            Addresses.Clear();
            SharedAddresses.Clear();
            AddressesByOwner.Clear();
            Overrides.Clear();
            LegacyAliases.Clear();
            LocationsByLogicalKey.Clear();
            LogicalOverrides.Clear();
            ReportedMissing.Clear();
            _activePackageName = string.IsNullOrWhiteSpace(packageName)
                ? JxqyResourceLocations.PackageName
                : packageName.Trim();
            if (packageResolver != null)
                _packageResolver = packageResolver;
            foreach (JxqyPreloadGroup group in manifest.Groups)
            {
                if (group?.Resources == null)
                    continue;
                bool isShared = string.IsNullOrWhiteSpace(
                        group.ResourceNamespace)
                    ? !string.Equals(
                        group.Kind,
                        "Map",
                        StringComparison.OrdinalIgnoreCase)
                    : !string.Equals(
                        group.ResourceNamespace,
                        "scene",
                        StringComparison.OrdinalIgnoreCase);
                string owner = isShared
                    ? string.Empty
                    : ResolveOwner(group);
                if (!AddressesByOwner.TryGetValue(
                        owner,
                        out HashSet<string> ownerAddresses))
                {
                    ownerAddresses = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    AddressesByOwner.Add(owner, ownerAddresses);
                }
                foreach (JxqyPreloadResource resource in group.Resources)
                {
                    if (string.IsNullOrWhiteSpace(resource?.Address))
                        continue;
                    string address = Normalize(resource.Address);
                    Addresses.Add(address);
                    ownerAddresses.Add(address);
                    if (isShared)
                        SharedAddresses.Add(address);
                    RegisterLogicalLocation(resource, address);
                    RegisterLegacyAliases(
                        owner,
                        resource,
                        address);
                }
            }
            SetActiveOwner(activeOwnerStableId);
            _configured = true;
        }

        public static void SetActiveOwner(string ownerStableId)
        {
            _activeOwnerStableId =
                (ownerStableId ?? string.Empty).Trim();
        }

        /// <summary>
        /// Registers a logical override supplied by a MOD manifest. An empty
        /// owner key is a shared override; otherwise it only applies while the
        /// matching scene owner is active.
        /// </summary>
        public static void RegisterModOverride(
            string ownerStableId,
            JxqyLegacyResourceKind kind,
            string legacyReference,
            string address)
        {
            if (string.IsNullOrWhiteSpace(legacyReference))
                throw new ArgumentException(
                    "Legacy resource reference is empty.",
                    nameof(legacyReference));
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException(
                    "MOD resource address is empty.",
                    nameof(address));
            Overrides[OverrideKey(
                ownerStableId,
                kind,
                legacyReference)] = Normalize(address);
        }

        public static void RegisterAnimationAlias(
            string category,
            string legacyFileName,
            string metadataAddress)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException(
                    "Animation alias category is empty.",
                    nameof(category));
            if (string.IsNullOrWhiteSpace(legacyFileName))
                throw new ArgumentException(
                    "Animation alias file name is empty.",
                    nameof(legacyFileName));
            if (string.IsNullOrWhiteSpace(metadataAddress))
                throw new ArgumentException(
                    "Animation alias address is empty.",
                    nameof(metadataAddress));
            AddLegacyAlias(
                string.Empty,
                JxqyLegacyResourceKind.Animation,
                Path.GetFileName(legacyFileName.Replace('\\', '/')),
                category.Trim('/'),
                Normalize(metadataAddress));
        }

        public static void Clear()
        {
            Addresses.Clear();
            SharedAddresses.Clear();
            AddressesByOwner.Clear();
            Overrides.Clear();
            LegacyAliases.Clear();
            LocationsByLogicalKey.Clear();
            LogicalOverrides.Clear();
            ReportedMissing.Clear();
            _activeOwnerStableId = string.Empty;
            _activePackageName = JxqyResourceLocations.PackageName;
            _packageResolver = null;
            _configured = false;
        }

        public static bool TryResolvePackageName(
            string address,
            out string packageName)
        {
            packageName = string.Empty;
            if (_packageResolver == null ||
                !_packageResolver.TryResolveLoaded(
                    address,
                    out JxqyResolvedResourceLocation location))
            {
                return false;
            }
            packageName = location.PackageName;
            return !string.IsNullOrWhiteSpace(packageName);
        }

        public static string ResolvePackageNameOrActive(string address)
        {
            return TryResolvePackageName(address, out string packageName)
                ? packageName
                : ActivePackageName;
        }

        public static bool Contains(string address)
        {
            if (!_configured)
                return true;
            if (string.IsNullOrWhiteSpace(address))
                return false;
            string normalized = Normalize(address);
            return Addresses.Contains(normalized) ||
                   _packageResolver != null &&
                   _packageResolver.TryResolveLoaded(
                       normalized,
                       out JxqyResolvedResourceLocation _);
        }

        public static bool TryFindUniqueSharedFileAddress(
            string rootAddress,
            string fileStemPrefix,
            string fileExtension,
            out string address)
        {
            address = string.Empty;
            if (!_configured ||
                string.IsNullOrWhiteSpace(rootAddress) ||
                string.IsNullOrWhiteSpace(fileStemPrefix) ||
                string.IsNullOrWhiteSpace(fileExtension))
            {
                return false;
            }

            string root = Normalize(rootAddress).TrimEnd('/') + "/";
            string prefix = fileStemPrefix.Trim().ToLowerInvariant();
            string extension = fileExtension.Trim().ToLowerInvariant();
            foreach (string candidate in SharedAddresses)
            {
                if (!candidate.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase) ||
                    !candidate.EndsWith(
                        extension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = candidate.Substring(root.Length);
                if (relative.IndexOf('/') >= 0)
                    continue;
                string stem = Path.GetFileNameWithoutExtension(relative);
                if (!stem.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase) ||
                    stem.Length > prefix.Length &&
                    char.IsDigit(stem[prefix.Length]))
                {
                    continue;
                }

                if (address.Length > 0 &&
                    !string.Equals(
                        address,
                        candidate,
                        StringComparison.OrdinalIgnoreCase))
                {
                    address = string.Empty;
                    return false;
                }
                address = candidate;
            }
            return address.Length > 0;
        }

        public static bool TryResolve(
            JxqyResourceKey key,
            out JxqyResolvedResourceLocation location)
        {
            string logicalKey = key.ToString();
            if (LogicalOverrides.TryGetValue(
                    logicalKey,
                    out location))
            {
                return true;
            }
            return LocationsByLogicalKey.TryGetValue(
                logicalKey,
                out location);
        }

        /// <summary>
        /// Registers a MOD overlay against a logical key. The MOD may keep its
        /// assets in a separate YooAsset package and physical folder layout.
        /// </summary>
        public static void RegisterModOverride(
            JxqyResourceKey key,
            string address,
            string packageName = null)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException(
                    "MOD resource address is empty.",
                    nameof(address));
            string logicalKey = key.ToString();
            LogicalOverrides[logicalKey] =
                new JxqyResolvedResourceLocation(
                    logicalKey,
                    Normalize(address),
                    packageName);
        }

        public static bool TryResolveAnimationAddress(
            string fileName,
            out string address,
            params string[] categories)
        {
            address = string.Empty;
            string safeFileName = Path.GetFileName(
                (fileName ?? string.Empty).Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                categories == null)
            {
                return false;
            }

            if (TryGetOverride(
                    JxqyLegacyResourceKind.Animation,
                    safeFileName,
                    out address))
            {
                return true;
            }
            foreach (string category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;
                if (TryGetLegacyAlias(
                        JxqyLegacyResourceKind.Animation,
                        safeFileName,
                        category,
                        out address))
                    return true;
            }
            foreach (string category in categories)
            {
                string suffix =
                    $"/{category.Trim('/')}/{safeFileName}/animation.json";
                if (TryFindScopedAddress(suffix, out address))
                    return true;
            }
            // Runtime-profile animations (for example the frozen/poisoned/
            // petrified death visuals) are not referenced by legacy source
            // files and therefore may be absent from a composed Mod preload
            // manifest. Resolve their canonical address only through the
            // selected Mod's explicit package chain. This preserves package
            // isolation while allowing the shared base to supply implicit
            // runtime dependencies.
            foreach (string category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;
                string canonical = Normalize(
                    $"jxqy/animations/asf/{category.Trim('/')}/" +
                    $"{safeFileName}/animation.json");
                if (_packageResolver == null ||
                    !_packageResolver.TryResolveLoaded(
                        canonical,
                        out JxqyResolvedResourceLocation location))
                {
                    continue;
                }
                address = Normalize(location.Address);
                Addresses.Add(address);
                SharedAddresses.Add(address);
                return true;
            }
            // A large part of the original UI uses repeated file names such
            // as panel.asf. Prefer the requested scoped path before the
            // unqualified compatibility alias; otherwise littlemap/panel.asf
            // can silently resolve to an unrelated UI panel.
            if (TryGetLegacyAlias(
                    JxqyLegacyResourceKind.Animation,
                    safeFileName,
                    string.Empty,
                    out address))
                return true;
            return false;
        }

        public static bool TryResolveGeneratedAddress(
            JxqyLegacyResourceKind kind,
            string legacyReference,
            string generatedAddress,
            out string address)
        {
            if (TryGetOverride(kind, legacyReference, out address))
                return true;
            if (TryGetLegacyAlias(
                    kind,
                    legacyReference,
                    string.Empty,
                    out address))
                return true;
            address = Normalize(generatedAddress);
            if (!_configured || IsAvailableInActiveScope(address))
                return true;
            address = string.Empty;
            return false;
        }

        public static void ReportMissing(
            string usage,
            string legacyReference,
            string generatedAddress = null)
        {
            string key =
                $"{usage}|{legacyReference}|{generatedAddress}";
            if (!ReportedMissing.Add(key))
                return;
            Debug.LogWarning(
                $"JXQY-ASSET unavailable: usage={usage}; " +
                $"legacy={legacyReference}; " +
                $"generated={generatedAddress ?? "<no resolvable address>"}; " +
                "the source installation did not provide a converted asset.");
        }

        public static void ReportOptionalAudioMissing(
            string usage,
            string legacyReference,
            string generatedAddress = null)
        {
            string key =
                $"optional-audio|{usage}|{legacyReference}|" +
                generatedAddress;
            if (!ReportedMissing.Add(key))
                return;
            Debug.LogWarning(
                $"JXQY-AUDIO optional asset unavailable: usage={usage}; " +
                $"legacy={legacyReference}; " +
                $"generated={generatedAddress ?? "<no resolvable address>"}; " +
                "continuing silently as the legacy engine does.");
        }

        private static string Normalize(string address)
        {
            return address.Trim()
                .Replace('\\', '/')
                .ToLowerInvariant();
        }

        private static bool IsAvailableInActiveScope(string address)
        {
            if (!_configured)
                return true;
            if (!string.IsNullOrEmpty(_activeOwnerStableId) &&
                AddressesByOwner.TryGetValue(
                    _activeOwnerStableId,
                    out HashSet<string> ownerAddresses) &&
                ownerAddresses.Contains(address))
            {
                return true;
            }
            if (SharedAddresses.Contains(address))
                return true;
            // A selected DaoJian Mod configures its own preload manifest, so
            // assets that live only in the required shared fallback package
            // are intentionally absent from Addresses/SharedAddresses. Ask
            // the already-initialized explicit package chain before reporting
            // a synthesized legacy address as missing. The resolver never
            // consults the default package or a sibling Mod.
            return _packageResolver != null &&
                   _packageResolver.TryResolveLoaded(
                       address,
                       out JxqyResolvedResourceLocation _);
        }

        private static bool TryFindScopedAddress(
            string suffix,
            out string address)
        {
            if (!string.IsNullOrEmpty(_activeOwnerStableId) &&
                AddressesByOwner.TryGetValue(
                    _activeOwnerStableId,
                    out HashSet<string> ownerAddresses))
            {
                foreach (string candidate in ownerAddresses)
                {
                    if (candidate.EndsWith(
                            suffix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        address = candidate;
                        return true;
                    }
                }
            }
            foreach (string candidate in SharedAddresses)
            {
                if (candidate.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    address = candidate;
                    return true;
                }
            }
            address = string.Empty;
            return false;
        }

        private static bool TryGetOverride(
            JxqyLegacyResourceKind kind,
            string legacyReference,
            out string address)
        {
            if (Overrides.TryGetValue(
                    OverrideKey(
                        _activeOwnerStableId,
                        kind,
                        legacyReference),
                    out address))
            {
                return true;
            }
            return Overrides.TryGetValue(
                OverrideKey(
                    string.Empty,
                    kind,
                    legacyReference),
                out address);
        }

        private static void RegisterLogicalLocation(
            JxqyPreloadResource resource,
            string address)
        {
            if (string.IsNullOrWhiteSpace(resource.LogicalKey))
                return;
            string logicalKey = Normalize(resource.LogicalKey);
            var location = new JxqyResolvedResourceLocation(
                logicalKey,
                address,
                resource.PackageName);
            if (LocationsByLogicalKey.TryGetValue(
                    logicalKey,
                    out JxqyResolvedResourceLocation existing) &&
                !string.Equals(
                    existing.Address,
                    location.Address,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Logical resource key maps to multiple addresses: " +
                    $"{logicalKey}.");
            }
            LocationsByLogicalKey[logicalKey] = location;
        }

        private static void RegisterLegacyAliases(
            string owner,
            JxqyPreloadResource resource,
            string address)
        {
            if (!TryGetLegacyKind(
                    resource.ResourceKind,
                    out JxqyLegacyResourceKind kind))
            {
                return;
            }
            string source = resource.SourceStableId ?? string.Empty;
            string fileName = Path.GetFileName(
                source.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileName))
                return;
            string category = GetLegacyCategory(source);
            AddLegacyAlias(owner, kind, fileName, category, address);
            AddLegacyAlias(
                owner,
                kind,
                Path.GetFileNameWithoutExtension(fileName),
                category,
                address);
            AddLegacyAlias(owner, kind, fileName, string.Empty, address);
            AddLegacyAlias(
                owner,
                kind,
                Path.GetFileNameWithoutExtension(fileName),
                string.Empty,
                address);
        }

        private static void AddLegacyAlias(
            string owner,
            JxqyLegacyResourceKind kind,
            string legacyReference,
            string category,
            string address)
        {
            string key = LegacyAliasKey(
                owner,
                kind,
                legacyReference,
                category);
            if (!LegacyAliases.ContainsKey(key))
                LegacyAliases.Add(key, address);
        }

        private static bool TryGetLegacyAlias(
            JxqyLegacyResourceKind kind,
            string legacyReference,
            string category,
            out string address)
        {
            string fileName = Path.GetFileName(
                (legacyReference ?? string.Empty)
                .Replace('\\', '/'));
            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (TryGetLegacyAliasCore(
                    _activeOwnerStableId,
                    kind,
                    fileName,
                    category,
                    out address) ||
                (!string.Equals(
                     fileName,
                     stem,
                     StringComparison.OrdinalIgnoreCase) &&
                 TryGetLegacyAliasCore(
                     _activeOwnerStableId,
                     kind,
                     stem,
                     category,
                     out address)))
            {
                return true;
            }
            return TryGetLegacyAliasCore(
                       string.Empty,
                       kind,
                       fileName,
                       category,
                       out address) ||
                   (!string.Equals(
                        fileName,
                        stem,
                        StringComparison.OrdinalIgnoreCase) &&
                    TryGetLegacyAliasCore(
                        string.Empty,
                        kind,
                        stem,
                        category,
                        out address));
        }

        private static bool TryGetLegacyAliasCore(
            string owner,
            JxqyLegacyResourceKind kind,
            string legacyReference,
            string category,
            out string address)
        {
            return LegacyAliases.TryGetValue(
                LegacyAliasKey(
                    owner,
                    kind,
                    legacyReference,
                    category),
                out address);
        }

        private static bool TryGetLegacyKind(
            string resourceKind,
            out JxqyLegacyResourceKind kind)
        {
            switch (resourceKind)
            {
                case "AnimationMetadata":
                    kind = JxqyLegacyResourceKind.Animation;
                    return true;
                case "MusicClip":
                    kind = JxqyLegacyResourceKind.Music;
                    return true;
                case "SoundClip":
                    kind = JxqyLegacyResourceKind.Sound;
                    return true;
                case "VideoClip":
                    kind = JxqyLegacyResourceKind.Video;
                    return true;
                case "DynamicText":
                case "UiConfiguration":
                    kind = JxqyLegacyResourceKind.Text;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static string GetLegacyCategory(string sourceStableId)
        {
            string source = (sourceStableId ?? string.Empty)
                .Replace('\\', '/');
            int colon = source.IndexOf(':');
            if (colon >= 0)
                source = source.Substring(colon + 1);
            string[] segments = source.Split('/');
            if (segments.Length < 2)
                return string.Empty;
            int start = string.Equals(
                segments[0],
                "asf",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            return start < segments.Length - 1
                ? segments[start]
                : string.Empty;
        }

        private static string LegacyAliasKey(
            string owner,
            JxqyLegacyResourceKind kind,
            string legacyReference,
            string category)
        {
            string normalizedOwner = (owner ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            string normalizedLegacy = Path.GetFileName(
                    (legacyReference ?? string.Empty)
                    .Trim()
                    .Replace('\\', '/'))
                .ToLowerInvariant();
            string normalizedCategory = (category ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .ToLowerInvariant();
            return
                $"{normalizedOwner}|{kind}|{normalizedCategory}|" +
                $"{normalizedLegacy}";
        }

        private static string ResolveOwner(JxqyPreloadGroup group)
        {
            return string.IsNullOrWhiteSpace(group.SceneKey)
                ? group.OwnerStableId ?? string.Empty
                : group.SceneKey;
        }

        private static string OverrideKey(
            string ownerStableId,
            JxqyLegacyResourceKind kind,
            string legacyReference)
        {
            string owner = (ownerStableId ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            string legacy = Path.GetFileName(
                    (legacyReference ?? string.Empty)
                    .Trim()
                    .Replace('\\', '/'))
                .ToLowerInvariant();
            return $"{owner}|{kind}|{legacy}";
        }
    }
}
