using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using YooAsset;

namespace Jxqy.UnityAdapters
{
    public sealed class JxqyRuntimeResourcePackage
    {
        public JxqyRuntimeResourcePackage(
            string packageName,
            bool requiredOnActivation)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException(
                    "Runtime resource package name is required.",
                    nameof(packageName));
            PackageName = packageName.Trim();
            RequiredOnActivation = requiredOnActivation;
        }

        public string PackageName { get; }
        public bool RequiredOnActivation { get; }
    }

    public sealed class JxqyResourcePackageChain
    {
        public JxqyResourcePackageChain(
            string primaryPackageName,
            IEnumerable<JxqyRuntimeResourcePackage> fallbackPackages = null)
        {
            if (string.IsNullOrWhiteSpace(primaryPackageName))
                throw new ArgumentException(
                    "Primary resource package name is required.",
                    nameof(primaryPackageName));
            PrimaryPackageName = primaryPackageName.Trim();
            JxqyRuntimeResourcePackage[] packages = new[]
                {
                    new JxqyRuntimeResourcePackage(
                        PrimaryPackageName,
                        requiredOnActivation: true),
                }
                .Concat(fallbackPackages ??
                        Enumerable.Empty<JxqyRuntimeResourcePackage>())
                .ToArray();
            string duplicate = packages
                .GroupBy(
                    package => package.PackageName,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicate != null)
                throw new ArgumentException(
                    $"Runtime resource package '{duplicate}' is duplicated.",
                    nameof(fallbackPackages));
            Packages = packages;
        }

        public string PrimaryPackageName { get; }
        public IReadOnlyList<JxqyRuntimeResourcePackage> Packages { get; }
    }

    /// <summary>
    /// Resolves an address only inside the selected Mod's explicit package
    /// chain. It never consults the YooAsset default package or sibling Mods.
    /// </summary>
    public sealed class JxqyYooAssetPackageResolver : IDisposable
    {
        private readonly JxqyResourcePackageChain _chain;
        private readonly Dictionary<string, SemaphoreSlim> _initializationGates =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JxqyResolvedResourceLocation>
            _resolved = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _hitCounts =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public JxqyYooAssetPackageResolver(
            JxqyResourcePackageChain chain)
        {
            _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        }

        public JxqyResourcePackageChain Chain => _chain;
        public int FallbackHitCount { get; private set; }

        public int GetHitCount(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return 0;
            return _hitCounts.TryGetValue(
                packageName.Trim(),
                out int count)
                ? count
                : 0;
        }

        public async UniTask EnsureRequiredPackagesAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            foreach (JxqyRuntimeResourcePackage package in
                     _chain.Packages.Where(
                         candidate => candidate.RequiredOnActivation))
            {
                await EnsureInitializedAsync(
                    package.PackageName,
                    cancellationToken);
            }
        }

        public async UniTask<JxqyResolvedResourceLocation> ResolveAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException(
                    "Resource address is empty.",
                    nameof(address));
            string normalizedAddress = address.Trim();
            if (_resolved.TryGetValue(
                    normalizedAddress,
                    out JxqyResolvedResourceLocation cached) &&
                IsAvailable(cached.PackageName, normalizedAddress))
            {
                if (!string.Equals(
                        cached.PackageName,
                        _chain.PrimaryPackageName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    FallbackHitCount++;
                }
                RecordHit(cached.PackageName);
                return cached;
            }

            for (int index = 0; index < _chain.Packages.Count; index++)
            {
                JxqyRuntimeResourcePackage candidate =
                    _chain.Packages[index];
                ResourcePackage package = await EnsureInitializedAsync(
                    candidate.PackageName,
                    cancellationToken);
                if (!package.CheckLocationValid(normalizedAddress))
                    continue;

                var resolved = new JxqyResolvedResourceLocation(
                    normalizedAddress,
                    normalizedAddress,
                    candidate.PackageName);
                _resolved[normalizedAddress] = resolved;
                if (index > 0)
                    FallbackHitCount++;
                RecordHit(candidate.PackageName);
                return resolved;
            }

            string packageChain = string.Join(
                " -> ",
                _chain.Packages.Select(package => package.PackageName));
            throw new InvalidOperationException(
                $"Resource '{normalizedAddress}' was not found in the " +
                $"selected Mod package chain: " +
                $"{packageChain}.");
        }

        public bool TryResolveLoaded(
            string address,
            out JxqyResolvedResourceLocation location)
        {
            ThrowIfDisposed();
            location = default;
            if (string.IsNullOrWhiteSpace(address))
                return false;

            string normalizedAddress = address.Trim();
            for (int index = 0; index < _chain.Packages.Count; index++)
            {
                string packageName = _chain.Packages[index].PackageName;
                if (!IsAvailable(packageName, normalizedAddress))
                    continue;

                location = new JxqyResolvedResourceLocation(
                    normalizedAddress,
                    normalizedAddress,
                    packageName);
                _resolved[normalizedAddress] = location;
                if (index > 0)
                    FallbackHitCount++;
                RecordHit(packageName);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (SemaphoreSlim gate in _initializationGates.Values)
                gate.Dispose();
            _initializationGates.Clear();
            _resolved.Clear();
            _hitCounts.Clear();
        }

        private async UniTask<ResourcePackage> EnsureInitializedAsync(
            string packageName,
            CancellationToken cancellationToken)
        {
            ResourcePackage initialized = GetInitialized(packageName);
            if (initialized != null)
                return initialized;

            SemaphoreSlim gate = GetInitializationGate(packageName);
            await gate.WaitAsync(cancellationToken);
            try
            {
                initialized = GetInitialized(packageName);
                if (initialized != null)
                    return initialized;

                IResourceModule resources =
                    ModuleSystem.GetModule<IResourceModule>() ??
                    throw new InvalidOperationException(
                        "TEngine resource module is unavailable.");
                InitializationOperation operation =
                    await resources.InitPackage(
                        packageName,
                        needInitMainFest: true);
                cancellationToken.ThrowIfCancellationRequested();
                if (operation == null ||
                    operation.Status != EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException(
                        $"YooAsset package '{packageName}' failed to " +
                        $"initialize: {operation?.Error ?? "no operation"}");
                }
                return YooAssets.TryGetPackage(packageName) ??
                       throw new InvalidOperationException(
                           $"YooAsset package '{packageName}' initialized " +
                           "without a registered package instance.");
            }
            finally
            {
                gate.Release();
            }
        }

        private SemaphoreSlim GetInitializationGate(string packageName)
        {
            if (_initializationGates.TryGetValue(
                    packageName,
                    out SemaphoreSlim gate))
            {
                return gate;
            }
            gate = new SemaphoreSlim(1, 1);
            _initializationGates.Add(packageName, gate);
            return gate;
        }

        private static ResourcePackage GetInitialized(string packageName)
        {
            ResourcePackage package = YooAssets.TryGetPackage(packageName);
            return package?.InitializeStatus == EOperationStatus.Succeed
                ? package
                : null;
        }

        private static bool IsAvailable(
            string packageName,
            string address)
        {
            ResourcePackage package = GetInitialized(packageName);
            return package != null && package.CheckLocationValid(address);
        }

        private void RecordHit(string packageName)
        {
            _hitCounts.TryGetValue(packageName, out int count);
            _hitCounts[packageName] = count + 1;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(JxqyYooAssetPackageResolver));
        }
    }
}
