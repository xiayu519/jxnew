using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using JxNewMod.Domain;
using Jxqy.Bootstrap;
using Jxqy.UnityAdapters;

namespace JxNewMod.Runtime
{
    /// <summary>
    /// Shared adapter for official Jxqy-family content packages. Per-Mod
    /// differences are immutable data supplied by ModDescriptor.
    /// </summary>
    public sealed class JxqyOfficialModEntryPoint : IModEntryPoint
    {
        private static readonly TimeSpan ActivationTimeout =
            TimeSpan.FromMinutes(2);

        private readonly JxqyRuntimeContentProfile _contentProfile;

        public JxqyOfficialModEntryPoint(
            ModId modId,
            JxqyRuntimeContentProfile contentProfile)
        {
            if (!modId.IsValid)
                throw new ArgumentException("A valid Mod id is required.", nameof(modId));
            ModId = modId;
            _contentProfile = contentProfile ??
                              throw new ArgumentNullException(
                                  nameof(contentProfile));
        }

        public ModId ModId { get; }

        public async UniTask ActivateAsync(
            ModDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.Id != ModId)
                throw new ArgumentException(
                    "Official Jxqy entry point received the wrong Mod descriptor.",
                    nameof(descriptor));

            JxqyGameBootstrap.Start(CreateContentContext(
                descriptor,
                _contentProfile));
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!JxqyGameBootstrap.IsContentReady)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(JxqyGameBootstrap.LastError))
                    throw new InvalidOperationException(JxqyGameBootstrap.LastError);
                if (stopwatch.Elapsed > ActivationTimeout)
                {
                    throw new TimeoutException(
                        $"“{descriptor.DisplayName}”资源初始化超过两分钟，" +
                        "请检查本地资源包后重试。");
                }
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        public void Shutdown()
        {
            JxqyGameBootstrap.Shutdown();
        }

        public void Dispose()
        {
        }

        private static JxqyRuntimeContentContext CreateContentContext(
            ModDescriptor descriptor,
            JxqyRuntimeContentProfile contentProfile)
        {
            string contentRoot =
                JxqyRuntimeContentContext.GetContentRootAddress(
                    descriptor.Content.PreloadManifestAddress);
            return new JxqyRuntimeContentContext(
                descriptor.PackageName,
                descriptor.Content.PreloadManifestAddress,
                descriptor.Content.ScriptCatalogAddress,
                descriptor.Content.PlayerProfileAddress,
                descriptor.Content.EntryScriptAddress,
                descriptor.Content.InitialMapAddress,
                descriptor.SaveNamespace,
                contentProfile,
                JxqyLegacyMediaAddressResolver.ForContentRoot(contentRoot),
                CreateAnimationAliases(
                    descriptor.UiAnimationAliases,
                    contentRoot),
                fallbackPackages: descriptor.ResourcePackages
                    .Skip(1)
                    .Select(package =>
                        new JxqyRuntimeResourcePackage(
                            package.PackageName,
                            package.IsRequiredOnActivation)),
                scriptDialectId: descriptor.ScriptDialectId,
                snapshotTemplateRelativeDirectory:
                    descriptor.Content.SnapshotTemplateRelativeDirectory);
        }

        private static IReadOnlyList<JxqyLegacyAnimationAlias>
            CreateAnimationAliases(
                IEnumerable<ModUiAnimationAlias> aliases,
                string contentRootAddress)
        {
            return (aliases ?? Enumerable.Empty<ModUiAnimationAlias>())
                .Select(alias =>
                {
                    string requested = alias.RequestedRelativePath;
                    string category = Path.GetDirectoryName(requested)
                        ?.Replace('\\', '/') ?? string.Empty;
                    string fileName = Path.GetFileName(requested);
                    string metadataAddress =
                        contentRootAddress + "/animations/asf/ui/" +
                        alias.ActualRelativePath +
                        "/animation.json";
                    return new JxqyLegacyAnimationAlias(
                        category,
                        fileName,
                        metadataAddress);
                })
                .ToArray();
        }

    }
}
