using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset;

namespace JxNewMod.Editor.CleanSetup
{
    [InitializeOnLoad]
    public static class JxNewModCleanProjectSetup
    {
        private const string RequestRelativePath =
            "Library/JxNewCleanSetup/setup.request";
        private const string ResultRelativePath =
            "Library/JxNewCleanSetup/setup.result";
        private const string ProgressRelativePath =
            "Library/JxNewCleanSetup/setup.progress";

        private static readonly string[] ResourceRoots =
        {
            "Assets/Mods/XinJianXia/Content",
            "Assets/Shared/JxShared_XinJianXiaBase/Content",
            "Assets/Shared/JxShared_DaoJian543Base/Content",
            "Assets/Mods/LengJianHanMei/Content",
            "Assets/Mods/MengLiHuiMou/Content",
        };

        private static readonly string[] RequiredModFiles =
        {
            "Assets/Mods/XinJianXia/Content/Manifests/preload-manifest.json",
            "Assets/Mods/XinJianXia/Content/Manifests/script-catalog.json",
            "Assets/Mods/XinJianXia/Content/UI/ui-catalog.json",
            "Assets/Mods/XinJianXia/Content/Text/ini/save/player0.ini/content.txt",
            "Assets/Mods/XinJianXia/Content/Text/script/common/newgame.txt/content.txt",
            "Assets/Mods/LengJianHanMei/Content/Manifests/preload-manifest.json",
            "Assets/Mods/LengJianHanMei/Content/Manifests/script-catalog.json",
            "Assets/Mods/LengJianHanMei/Content/UI/ui-catalog.json",
            "Assets/Mods/LengJianHanMei/Content/Text/save/rpg0/Player0.ini/content.txt",
            "Assets/Mods/LengJianHanMei/Content/Text/script/common/newgame.txt/content.txt",
            "Assets/Mods/MengLiHuiMou/Content/Manifests/preload-manifest.json",
            "Assets/Mods/MengLiHuiMou/Content/Manifests/script-catalog.json",
            "Assets/Mods/MengLiHuiMou/Content/UI/ui-catalog.json",
            "Assets/Mods/MengLiHuiMou/Content/Text/ini/save/player0.ini/content.txt",
            "Assets/Mods/MengLiHuiMou/Content/Text/script/common/newgame.txt/content.txt",
            "Assets/Shared/JxShared_XinJianXiaBase/Content/Maps/map/map001_衡山.map/map.json",
        };

        private static readonly string[] PackageNames =
        {
            "DefaultPackage",
            "JxMod_XinJianXia",
            "JxShared_XinJianXiaBase",
            "JxShared_DaoJian543Base",
            "JxMod_LengJianHanMei",
            "JxMod_MengLiHuiMou",
        };

        private static bool _running;

        static JxNewModCleanProjectSetup()
        {
            EditorApplication.update += Poll;
        }

        [MenuItem("TEngine/Jx New Mod/Configure Clean Project Resources")]
        public static void ConfigureFromMenu()
        {
            Configure(writeResult: true);
        }

        public static void ConfigureFromCommandLine()
        {
            Configure(writeResult: true);
        }

        private static void Poll()
        {
            if (_running || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlaying)
                return;

            string request = ProjectPath(RequestRelativePath);
            if (!File.Exists(request))
                return;

            File.Delete(request);
            EditorApplication.delayCall += () => Configure(writeResult: true);
        }

        private static void Configure(bool writeResult)
        {
            if (_running)
                return;

            _running = true;
            string resultPath = ProjectPath(ResultRelativePath);
            string progressPath = ProjectPath(ProgressRelativePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                WriteProgress(progressPath, "Checking imported Mod resources...");
                ValidateResourceTrees();

                const string startupScene = "Assets/Scenes/main.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(startupScene) == null)
                    throw new FileNotFoundException(
                        "Startup scene is missing.",
                        startupScene);

                EditorPrefs.SetInt(
                    "EditorPlayMode",
                    (int)EPlayMode.EditorSimulateMode);

                var packageRoots = new List<string>();
                foreach (string packageName in PackageNames)
                {
                    WriteProgress(
                        progressPath,
                        "Refreshing " + packageName + " simulation manifest...");
                    PackageInvokeBuildResult result =
                        EditorSimulateModeHelper.SimulateBuild(packageName);
                    ValidateSimulationResult(packageName, result);
                    packageRoots.Add(
                        packageName + "Manifest=" + result.PackageRootDirectory);
                }

                string resultText = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "SUCCESS",
                        "UnityVersion=" + Application.unityVersion,
                        "EditorPlayMode=" + EPlayMode.EditorSimulateMode,
                        "StartupScene=" + startupScene,
                    }
                    .Concat(packageRoots)
                    .Concat(new[]
                    {
                        "SimulationManifest=Refreshed",
                        "BundleBuild=Skipped",
                    }));
                if (writeResult)
                    File.WriteAllText(
                        resultPath,
                        resultText + Environment.NewLine);
                Debug.Log(
                    "[JxNewModCleanProjectSetup] " + resultText.Replace(
                        Environment.NewLine,
                        "; "));
                WriteProgress(progressPath, "SUCCESS");
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                if (writeResult)
                {
                    File.WriteAllText(
                        resultPath,
                        "FAILED" + Environment.NewLine + exception +
                        Environment.NewLine);
                }
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                _running = false;
            }
        }

        private static void ValidateResourceTrees()
        {
            foreach (string root in ResourceRoots)
            {
                string path = ProjectPath(root);
                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException(
                        "Import JxNewResources-20260828.unitypackage before " +
                        "setup. Missing: " + root);
                if (!Directory.EnumerateFileSystemEntries(path).Any())
                    throw new InvalidOperationException(
                        "Resource package is incomplete. Empty: " + root);
            }

            foreach (string requiredFile in RequiredModFiles)
            {
                if (!File.Exists(ProjectPath(requiredFile)))
                    throw new FileNotFoundException(
                        "Resource package is incomplete.",
                        requiredFile);
            }

            string[] forbiddenExtensions =
            {
                ".cs",
                ".dll",
                ".asmdef",
                ".rsp",
            };
            foreach (string root in ResourceRoots)
            {
                string forbidden = Directory.EnumerateFiles(
                        ProjectPath(root),
                        "*",
                        SearchOption.AllDirectories)
                    .FirstOrDefault(path => forbiddenExtensions.Contains(
                        Path.GetExtension(path),
                        StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(forbidden))
                    throw new InvalidOperationException(
                        "Resource package contains forbidden code: " + forbidden);
            }
        }

        private static void ValidateSimulationResult(
            string packageName,
            PackageInvokeBuildResult result)
        {
            if (result == null ||
                string.IsNullOrWhiteSpace(result.PackageRootDirectory) ||
                !Directory.Exists(result.PackageRootDirectory))
            {
                throw new InvalidOperationException(
                    packageName + " simulation manifest refresh failed.");
            }
        }

        private static void WriteProgress(string path, string message)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, message + Environment.NewLine);
        }

        private static string ProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                relativePath));
        }
    }
}
