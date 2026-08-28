using System;
using Jxqy.UnityAdapters;

namespace GameLogic
{
    /// <summary>
    /// Keeps shared UIWindow logic while resolving each Jxqy window prefab
    /// from the package selected for this process.
    /// </summary>
    public sealed class ActiveModWindowDescriptorResolver :
        IWindowDescriptorResolver
    {
        public static ActiveModWindowDescriptorResolver Instance { get; } =
            new();

        private ActiveModWindowDescriptorResolver()
        {
        }

        public bool TryResolve(
            Type windowType,
            out WindowDescriptor descriptor)
        {
            if (!WindowAttributeDescriptorResolver.Instance.TryResolve(
                    windowType,
                    out WindowDescriptor original))
            {
                descriptor = original;
                return false;
            }

            string packageName = original.PackageName;
            if (string.Equals(
                    original.PackageName,
                    JxqyResourceLocations.PackageName,
                    StringComparison.Ordinal))
            {
                packageName =
                    JxqyResourceAddressCatalog.TryResolvePackageName(
                        original.Location,
                        out string resolvedPackageName)
                        ? resolvedPackageName
                        : JxqyResourceAddressCatalog.ActivePackageName;
            }
            descriptor = new WindowDescriptor(
                original.WindowLayer,
                original.Location,
                original.FullScreen,
                original.FromResources,
                original.HideTimeToClose,
                packageName);
            return true;
        }
    }
}
