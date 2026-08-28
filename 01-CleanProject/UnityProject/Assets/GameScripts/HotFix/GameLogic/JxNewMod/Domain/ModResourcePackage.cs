using System;

namespace JxNewMod.Domain
{
    public enum ModPackageLoadPolicy
    {
        RequiredOnActivation = 0,
        OnFirstUse = 1,
    }

    /// <summary>
    /// One explicitly allowed YooAsset package in a Mod resource chain.
    /// Chain order is authoritative and prevents fallback into sibling Mods.
    /// </summary>
    public sealed class ModResourcePackage
    {
        public ModResourcePackage(
            string packageName,
            ModPackageLoadPolicy loadPolicy)
        {
            PackageName = RequirePackageName(packageName);
            LoadPolicy = loadPolicy;
        }

        public string PackageName { get; }
        public ModPackageLoadPolicy LoadPolicy { get; }
        public bool IsRequiredOnActivation =>
            LoadPolicy == ModPackageLoadPolicy.RequiredOnActivation;

        internal static string RequirePackageName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Package name is required.",
                    nameof(value));
            string packageName = value.Trim();
            if (packageName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException(
                    "Package name cannot contain path separators.",
                    nameof(value));
            return packageName;
        }
    }
}
