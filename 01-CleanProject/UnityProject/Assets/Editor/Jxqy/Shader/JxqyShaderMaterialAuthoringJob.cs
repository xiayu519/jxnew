using System;
using Jxqy.Editor.YooAsset;
using Jxqy.UnityAdapters;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Jxqy.Editor.Shader
{
    public static class JxqyShaderMaterialAuthoringJob
    {
        public const string ShaderRoot =
            "Assets/Mods/XinJianXia/Content/Shaders";
        public const string MaterialRoot =
            "Assets/Mods/XinJianXia/Content/Materials";

        [MenuItem("TEngine/Jxqy/Create Ported Shader Materials")]
        public static void Create()
        {
            CreateForContentRoot("Assets/Mods/XinJianXia/Content");
        }

        public static void CreateForContentRoot(string contentRoot)
        {
            string shaderRoot = Root(contentRoot, "Shaders");
            string materialRoot = Root(contentRoot, "Materials");
            EnsureAssetFolder(materialRoot);
            foreach (string key in JxqyMaterialCache.MaterialKeys)
                CreateOrUpdateMaterial(key, shaderRoot, materialRoot);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateOrThrow(contentRoot);
            Debug.Log(
                $"Jxqy static shader materials are ready. " +
                $"Materials={JxqyMaterialCache.MaterialKeys.Count}; " +
                $"Root={materialRoot}.");
        }

        public static void ValidateOrThrow()
        {
            ValidateOrThrow("Assets/Mods/XinJianXia/Content");
        }

        public static void ValidateOrThrow(string contentRoot)
        {
            string shaderRoot = Root(contentRoot, "Shaders");
            string materialRoot = Root(contentRoot, "Materials");
            foreach (string key in JxqyMaterialCache.MaterialKeys)
            {
                string shaderPath = GetShaderPath(key, shaderRoot);
                string materialPath = GetMaterialPath(key, materialRoot);
                UnityEngine.Shader shader =
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(
                        shaderPath);
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (shader == null)
                {
                    throw new BuildFailedException(
                        $"Jxqy shader asset is missing: {shaderPath}");
                }
                if (material == null)
                {
                    throw new BuildFailedException(
                        $"Jxqy static material is missing: {materialPath}");
                }
                if (material.shader != shader)
                {
                    throw new BuildFailedException(
                        $"Jxqy material '{materialPath}' does not " +
                        $"reference '{shaderPath}'.");
                }

                string address =
                    JxqyAddressByRelativePath.CreateAddress(
                        materialPath,
                        contentRoot);
                string expectedAddress =
                    $"jxqy/materials/{key}.mat";
                if (!string.Equals(
                        address,
                        expectedAddress,
                        StringComparison.Ordinal))
                {
                    throw new BuildFailedException(
                        $"Jxqy material address mismatch: " +
                        $"expected '{expectedAddress}', got '{address}'.");
                }
            }
        }

        public static string GetMaterialPath(string key)
        {
            return GetMaterialPath(key, MaterialRoot);
        }

        public static string GetShaderPath(string key)
        {
            return GetShaderPath(key, ShaderRoot);
        }

        private static string GetMaterialPath(
            string key,
            string materialRoot)
        {
            return $"{materialRoot}/{key}.mat";
        }

        private static string GetShaderPath(
            string key,
            string shaderRoot)
        {
            return $"{shaderRoot}/{key}.shader";
        }

        private static void CreateOrUpdateMaterial(
            string key,
            string shaderRoot,
            string materialRoot)
        {
            string shaderPath = GetShaderPath(key, shaderRoot);
            UnityEngine.Shader shader =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(
                    shaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Jxqy shader asset is missing: {shaderPath}");
            }

            string materialPath = GetMaterialPath(key, materialRoot);
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = key,
                };
                AssetDatabase.CreateAsset(material, materialPath);
                return;
            }

            if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string[] segments = assetPath.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static string Root(string contentRoot, string child)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root is empty.", nameof(contentRoot));
            return contentRoot.Replace('\\', '/').TrimEnd('/') + "/" + child;
        }
    }

    public sealed class JxqyShaderMaterialBuildValidator :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            JxqyShaderMaterialAuthoringJob.ValidateOrThrow();
            const string xinJianXiaRoot =
                "Assets/Mods/XinJianXia/Content";
            if (AssetDatabase.IsValidFolder(xinJianXiaRoot + "/Shaders"))
                JxqyShaderMaterialAuthoringJob.ValidateOrThrow(xinJianXiaRoot);
        }
    }
}
