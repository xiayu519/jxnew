using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jxqy.UnityAdapters
{
    public sealed class JxqyMaterialCache : IDisposable
    {
        private static readonly int StencilMaskId =
            Shader.PropertyToID("_StencilMask");
        private static readonly string[] MaterialKeyValues =
        {
            "default",
            "grayscale",
            "transparent",
            "alphatest",
            "magic-occluded-outline",
            "outedge",
            "outedgewide",
            "refraction",
            "waterfall",
            "occluder",
            "occluder-grayscale",
            "map-occluder",
            "map-occluder-grayscale",
            "map-magic-occluder",
            "map-magic-occluder-grayscale",
            "magic-stencil-clear",
            "player-opaque",
            "player-occluded",
            "player-opaque-grayscale",
            "player-occluded-grayscale",
        };
        private readonly IReadOnlyDictionary<string, Material> _materials;
        private readonly Dictionary<StencilMaterialKey, Material>
            _stencilMaterials = new();
        private readonly Material _defaultMaterial;

        public JxqyMaterialCache(
            IReadOnlyDictionary<string, Material> materials)
        {
            _materials = materials ??
                         throw new ArgumentNullException(nameof(materials));
            if (!_materials.TryGetValue(
                    "default",
                    out _defaultMaterial) ||
                _defaultMaterial == null)
            {
                throw new InvalidOperationException(
                    "The static Jxqy default material is missing.");
            }
        }

        public static IReadOnlyList<string> MaterialKeys =>
            MaterialKeyValues;

        public Material Get(string materialKey)
        {
            string normalized = string.IsNullOrWhiteSpace(materialKey)
                ? "default"
                : materialKey;
            return _materials.TryGetValue(
                       normalized,
                       out Material material) &&
                   material != null
                ? material
                : _defaultMaterial;
        }

        public Material Get(string materialKey, int stencilMask)
        {
            Material source = Get(materialKey);
            if (stencilMask == 0 || !source.HasProperty(StencilMaskId))
                return source;
            var key = new StencilMaterialKey(source, stencilMask);
            if (_stencilMaterials.TryGetValue(key, out Material material))
                return material;
            material = new Material(source)
            {
                name = $"{source.name}-stencil-{stencilMask}",
                hideFlags = HideFlags.DontSave,
            };
            material.SetInt(StencilMaskId, stencilMask);
            _stencilMaterials.Add(key, material);
            return material;
        }

        public void Dispose()
        {
            foreach (Material material in _stencilMaterials.Values)
            {
                if (material == null)
                    continue;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(material);
                else
                    UnityEngine.Object.DestroyImmediate(material);
            }
            _stencilMaterials.Clear();
        }

        private readonly struct StencilMaterialKey :
            IEquatable<StencilMaterialKey>
        {
            private readonly Material _material;
            private readonly int _stencilMask;

            public StencilMaterialKey(Material material, int stencilMask)
            {
                _material = material;
                _stencilMask = stencilMask;
            }

            public bool Equals(StencilMaterialKey other)
            {
                return ReferenceEquals(_material, other._material) &&
                       _stencilMask == other._stencilMask;
            }

            public override bool Equals(object obj)
            {
                return obj is StencilMaterialKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_material, _stencilMask);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class JxqyWaterRefractionEffect : MonoBehaviour
    {
        private static readonly int DisplacementTextureId =
            Shader.PropertyToID("_DisplacementTex");
        private static readonly int DisplacementScrollId =
            Shader.PropertyToID("_DisplacementScroll");
        private static readonly int StrengthId =
            Shader.PropertyToID("_Strength");

        private Material _material;

        public bool EffectEnabled { get; set; }
        public bool IsInitialized => _material != null;

        public void Initialize(
            Material sourceMaterial,
            Texture2D displacementTexture)
        {
            if (sourceMaterial == null)
                throw new ArgumentNullException(nameof(sourceMaterial));
            if (displacementTexture == null)
            {
                throw new ArgumentNullException(
                    nameof(displacementTexture));
            }
            if (_material == null ||
                _material.shader != sourceMaterial.shader)
            {
                if (_material != null)
                    Destroy(_material);
                _material = new Material(sourceMaterial)
                {
                    name = "Jxqy-WaterRefraction",
                    hideFlags = HideFlags.DontSave,
                };
            }
            _material.SetTexture(
                DisplacementTextureId,
                displacementTexture);
            _material.SetFloat(StrengthId, 0.015f);
        }

        private void OnRenderImage(
            RenderTexture source,
            RenderTexture destination)
        {
            if (!EffectEnabled || _material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            float time = Time.unscaledTime * 0.2f;
            _material.SetVector(
                DisplacementScrollId,
                new Vector4(
                    Mathf.Cos(time),
                    Mathf.Sin(time),
                    0,
                    0));
            Graphics.Blit(source, destination, _material);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            _material = null;
        }
    }
}
