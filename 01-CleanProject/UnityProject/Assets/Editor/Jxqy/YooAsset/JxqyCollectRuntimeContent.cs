using System;
using YooAsset.Editor;

namespace Jxqy.Editor.YooAsset
{
    /// <summary>
    /// Collects playable Mod content while keeping conversion and validation
    /// reports in the repository only. Reports change after every verification
    /// and must not invalidate an otherwise identical runtime package.
    /// </summary>
    [DisplayName("Jxqy: 仅运行时内容")]
    public sealed class JxqyCollectRuntimeContent : IFilterRule
    {
        public string FindAssetType => EAssetSearchType.All.ToString();

        public bool IsCollectAsset(FilterRuleData data)
        {
            string assetPath = Normalize(data.AssetPath);
            string collectPath = Normalize(data.CollectPath).TrimEnd('/');
            return !assetPath.StartsWith(
                collectPath + "/Reports/",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}
