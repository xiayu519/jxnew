using System;

namespace GameLogic
{
    public readonly struct WindowDescriptor
    {
        public WindowDescriptor(
            int windowLayer,
            string location,
            bool fullScreen,
            bool fromResources,
            int hideTimeToClose,
            string packageName)
        {
            WindowLayer = windowLayer;
            Location = location ?? string.Empty;
            FullScreen = fullScreen;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
            PackageName = packageName ?? string.Empty;
        }

        public int WindowLayer { get; }
        public string Location { get; }
        public bool FullScreen { get; }
        public bool FromResources { get; }
        public int HideTimeToClose { get; }
        public string PackageName { get; }
    }

    /// <summary>
    /// Allows the active Mod composition root to redirect a window to another
    /// package or Prefab while the UIWindow logic remains reusable.
    /// </summary>
    public interface IWindowDescriptorResolver
    {
        bool TryResolve(Type windowType, out WindowDescriptor descriptor);
    }

    public sealed class WindowAttributeDescriptorResolver :
        IWindowDescriptorResolver
    {
        public static WindowAttributeDescriptorResolver Instance { get; } =
            new();

        private WindowAttributeDescriptorResolver()
        {
        }

        public bool TryResolve(
            Type windowType,
            out WindowDescriptor descriptor)
        {
            if (windowType == null)
                throw new ArgumentNullException(nameof(windowType));
            WindowAttribute attribute = Attribute.GetCustomAttribute(
                windowType,
                typeof(WindowAttribute)) as WindowAttribute;
            if (attribute == null)
            {
                descriptor = new WindowDescriptor(
                    (int)UILayer.UI,
                    windowType.Name,
                    fullScreen: false,
                    fromResources: false,
                    hideTimeToClose: 10,
                    packageName: string.Empty);
                return true;
            }

            descriptor = new WindowDescriptor(
                attribute.WindowLayer,
                string.IsNullOrWhiteSpace(attribute.Location)
                    ? windowType.Name
                    : attribute.Location,
                attribute.FullScreen,
                attribute.FromResources,
                attribute.HideTimeToClose,
                attribute.PackageName);
            return true;
        }
    }
}
