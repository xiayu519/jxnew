using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 资源组件拓展。
    /// </summary>
    internal partial class ResourceExtComponent
    {
        private readonly Dictionary<string, SubAssetsHandle> _subAssetsHandles = new Dictionary<string, SubAssetsHandle>();
        private readonly Dictionary<string, int> _subSpriteReferences = new Dictionary<string, int>();

        public async UniTask SetSubSprite(Image image, string location, string spriteName, bool setNativeSize = false, CancellationToken cancellationToken = default, string packageName = "")
        {
            var subSprite = await GetSubSpriteImp(location, spriteName, cancellationToken, packageName);

            if (image == null)
            {
                Log.Warning($"SetSubAssets Image is null");
                return;
            }

            image.sprite = subSprite;
            if (setNativeSize)
            {
                image.SetNativeSize();
            }
            AddReference(
                image.gameObject,
                GetResourceCacheKey(location, packageName));
        }
        
        public async UniTask SetSubSprite(SpriteRenderer spriteRenderer, string location, string spriteName, CancellationToken cancellationToken = default, string packageName = "")
        {
            var subSprite = await GetSubSpriteImp(location, spriteName, cancellationToken, packageName);

            if (spriteRenderer == null)
            {
                Log.Warning($"SetSubAssets Image is null");
                return;
            }

            spriteRenderer.sprite = subSprite;
            AddReference(
                spriteRenderer.gameObject,
                GetResourceCacheKey(location, packageName));
        }

        private async UniTask<Sprite> GetSubSpriteImp(string location, string spriteName, CancellationToken cancellationToken = default, string packageName = "")
        {
            var assetInfo = _resourceModule.GetAssetInfo(location, packageName);
            if (assetInfo.IsInvalid)
            {
                throw new GameFrameworkException(
                    $"Invalid location '{location}' in package " +
                    $"'{packageName}'.");
            }

            string resourceKey = GetResourceCacheKey(location, packageName);
            await TryWaitingLoading(resourceKey);

            if (!_subAssetsHandles.TryGetValue(resourceKey, out var subAssetsHandle))
            {
                subAssetsHandle = string.IsNullOrEmpty(packageName)
                    ? YooAssets.LoadSubAssetsAsync<Sprite>(location)
                    : YooAssets.GetPackage(packageName)
                        .LoadSubAssetsAsync<Sprite>(location);
                await subAssetsHandle.ToUniTask(cancellationToken: cancellationToken);
                _subAssetsHandles[resourceKey] = subAssetsHandle;
            }

            var subSprite = subAssetsHandle.GetSubAssetObject<Sprite>(spriteName);
            if (subSprite == null)
            {
                throw new GameFrameworkException($"Invalid sprite name: {spriteName}");
            }
            return subSprite;
        }

        private void AddReference(GameObject target, string resourceKey)
        {
            var subSpriteReference = target.GetComponent<SubSpriteReference>();
            if (subSpriteReference == null)
            {
                subSpriteReference = target.AddComponent<SubSpriteReference>();
            }
            if (!subSpriteReference.Reference(resourceKey))
                return;
            _subSpriteReferences[resourceKey] =
                _subSpriteReferences.TryGetValue(resourceKey, out var count)
                    ? count + 1
                    : 1;
        }
        
        internal void DeleteReference(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey) ||
                !_subSpriteReferences.TryGetValue(
                    resourceKey,
                    out var count))
            {
                return;
            }
            _subSpriteReferences[resourceKey] = count - 1;
            if (_subSpriteReferences[resourceKey] <= 0)
            {
                if (_subAssetsHandles.TryGetValue(
                        resourceKey,
                        out var subAssetsHandle))
                {
                    subAssetsHandle.Dispose();
                    _subAssetsHandles.Remove(resourceKey);
                }
                _subSpriteReferences.Remove(resourceKey);
            }
        }
    }
    
    [DisallowMultipleComponent]
    public class SubSpriteReference : MonoBehaviour
    {
        private string _resourceKey;
        
        public bool Reference(string resourceKey)
        {
            if (_resourceKey == resourceKey)
                return false;
            if (_resourceKey != null)
            {
                ResourceExtComponent.Instance?.DeleteReference(_resourceKey);
            }
            _resourceKey = resourceKey;
            return true;
        }
        
        private void OnDestroy()
        {
            if (_resourceKey != null)
            {
                ResourceExtComponent.Instance?.DeleteReference(_resourceKey);
            }
        }
    }
}
