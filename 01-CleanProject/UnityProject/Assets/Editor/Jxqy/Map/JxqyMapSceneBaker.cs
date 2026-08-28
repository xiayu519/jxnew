using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jxqy.Domain.Content;
using Jxqy.Domain.World;
using Jxqy.UnityAdapters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Jxqy.Editor.Map
{
    public sealed class JxqyMapSceneBakeProfile
    {
        public JxqyMapSceneBakeProfile(
            string contentRoot,
            bool allowMissingMapAnimations = false,
            string sourceRelativePathPrefix = "map/")
        {
            ContentRoot = contentRoot?.Replace('\\', '/').TrimEnd('/') ??
                          throw new ArgumentNullException(nameof(contentRoot));
            AllowMissingMapAnimations = allowMissingMapAnimations;
            SourceRelativePathPrefix =
                sourceRelativePathPrefix?.Replace('\\', '/') ??
                string.Empty;
        }

        public string ContentRoot { get; }
        public bool AllowMissingMapAnimations { get; }
        public string SourceRelativePathPrefix { get; }
        public string MapRoot => ContentRoot + "/Maps";
        public string OutputRoot => ContentRoot + "/Scenes/Maps";
        public string CatalogPath =>
            ContentRoot + "/Scenes/map-scene-catalog.json";

        public static JxqyMapSceneBakeProfile XinJianXia { get; } =
            new(
                "Assets/Mods/XinJianXia/Content",
                allowMissingMapAnimations: true);
    }

    /// <summary>
    /// Generates one inspectable Unity scene for every converted MAP.
    /// Generated scenes are build artifacts; MAP data remains the source of truth.
    /// </summary>
    [InitializeOnLoad]
    public static class JxqyMapSceneBaker
    {
        public const string MapRoot = "Assets/Mods/XinJianXia/Content/Maps";
        public const string OutputRoot =
            "Assets/Mods/XinJianXia/Content/Scenes/Maps";
        public const string CatalogPath =
            "Assets/Mods/XinJianXia/Content/Scenes/map-scene-catalog.json";
        public const string GeneratorVersion =
            "0.4.0-explicit-scene-key";
        private const string AutomationRequestPath =
            "Temp/JxqyValidation/build-map-scenes.request";
        private static bool _automationBuildRunning;
        private static JxqyMapSceneBakeProfile _activeProfile =
            JxqyMapSceneBakeProfile.XinJianXia;
        private static readonly HashSet<string> MissingAnimationStableIds =
            new(StringComparer.OrdinalIgnoreCase);

        private static string ActiveMapRoot => _activeProfile.MapRoot;
        private static string ActiveOutputRoot => _activeProfile.OutputRoot;
        private static string ActiveCatalogPath => _activeProfile.CatalogPath;
        private static string ActiveContentRoot => _activeProfile.ContentRoot;

        static JxqyMapSceneBaker()
        {
            EditorApplication.update += PollRequestedBuild;
        }

        private static void PollRequestedBuild()
        {
            if (_automationBuildRunning ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }
            string requestPath = ToAbsolutePath(
                AutomationRequestPath);
            if (!File.Exists(requestPath))
                return;
            _automationBuildRunning = true;
            File.Delete(requestPath);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    BuildAll();
                }
                finally
                {
                    _automationBuildRunning = false;
                }
            };
        }

        [MenuItem("TEngine/Jxqy/Build Map Scenes")]
        public static void BuildAll()
        {
            BuildAll(JxqyMapSceneBakeProfile.XinJianXia);
        }

        public static void BuildAll(JxqyMapSceneBakeProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            Scene active = SceneManager.GetActiveScene();
            if (active.isDirty)
            {
                throw new InvalidOperationException(
                    "Save the active scene before generating Jxqy map scenes.");
            }

            JxqyMapSceneBakeProfile previous = _activeProfile;
            _activeProfile = profile;
            MissingAnimationStableIds.Clear();
            string previousScenePath = active.path;
            Directory.CreateDirectory(ToAbsolutePath(ActiveOutputRoot));
            var catalog = new JxqyMapSceneCatalog
            {
                GeneratorVersion = GeneratorVersion
            };

            try
            {
                string[] metadataPaths = Directory
                    .GetFiles(
                        ToAbsolutePath(ActiveMapRoot),
                        "map.json",
                        SearchOption.AllDirectories)
                    .Select(ToAssetPath)
                    .Where(IsCanonicalMapMetadata)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (string metadataPath in metadataPaths)
                    catalog.Maps.Add(BuildScene(metadataPath));

                WriteJson(ActiveCatalogPath, catalog);
                WriteJson(
                    ActiveContentRoot +
                    "/Reports/map-scene-bake-report.json",
                    new JxqyMapSceneBakeReport
                    {
                        GeneratorVersion = GeneratorVersion,
                        MapCount = catalog.Maps.Count,
                        MissingAnimationCount =
                            MissingAnimationStableIds.Count,
                        MissingAnimationStableIds =
                            MissingAnimationStableIds
                                .OrderBy(
                                    value => value,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                    });
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScenePath))
                    EditorSceneManager.OpenScene(previousScenePath);
                _activeProfile = previous;
            }

            Debug.Log(
                $"Generated {catalog.Maps.Count} Jxqy map scenes under " +
                $"{profile.OutputRoot}.");
        }

        private static bool IsCanonicalMapMetadata(string metadataPath)
        {
            if (string.IsNullOrWhiteSpace(
                    _activeProfile.SourceRelativePathPrefix))
            {
                return true;
            }
            JxqyMapMetadata metadata = JsonUtility.FromJson<JxqyMapMetadata>(
                File.ReadAllText(ToAbsolutePath(metadataPath)));
            return metadata != null &&
                   metadata.SourceRelativePath.StartsWith(
                       _activeProfile.SourceRelativePathPrefix,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static JxqyMapSceneEntry BuildScene(string metadataPath)
        {
            JxqyMapMetadata metadata = JsonUtility.FromJson<JxqyMapMetadata>(
                File.ReadAllText(ToAbsolutePath(metadataPath)));
            if (metadata == null ||
                string.IsNullOrWhiteSpace(metadata.SourceStableId))
            {
                throw new InvalidDataException(
                    $"Invalid converted map metadata: {metadataPath}");
            }

            string mapDataPath =
                Path.GetDirectoryName(metadataPath)?.Replace('\\', '/') +
                "/map.bytes";
            TextAsset mapData = AssetDatabase.LoadAssetAtPath<TextAsset>(
                mapDataPath);
            if (mapData == null)
                throw new FileNotFoundException(
                    "Converted map bytes are missing.",
                    mapDataPath);
            JxqyRuntimeMapData map = JxqyRuntimeMapData.Parse(
                mapData.bytes,
                metadata);

            string sceneName = Path.GetFileNameWithoutExtension(
                metadata.SourceRelativePath);
            string sceneDirectory =
                $"{ActiveOutputRoot}/{SanitizeFileName(sceneName)}";
            AssetDatabase.DeleteAsset(sceneDirectory);
            Directory.CreateDirectory(ToAbsolutePath(sceneDirectory));
            AssetDatabase.Refresh();
            string libraryPath =
                sceneDirectory + "/MapTileLibrary.asset";
            var tileLibrary =
                ScriptableObject.CreateInstance<JxqyMapTileLibrary>();
            tileLibrary.name = $"{sceneName}-TileLibrary";
            AssetDatabase.CreateAsset(tileLibrary, libraryPath);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var root = new GameObject($"JxqyMap-{sceneName}");
            root.AddComponent<JxqyMapSceneIdentity>()
                .ConfigureGeneratedScene(
                    metadata.SourceStableId,
                    metadata.SourceStableId,
                    metadata.SourceRelativePath);
            var grid = root.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSize = Vector3.one;

            Tilemap[] tilemaps = new Tilemap[3];
            TilemapRenderer[] renderers = new TilemapRenderer[3];
            string[] layerNames =
            {
                "GroundTilemap",
                "InterleavedTilemap",
                "OverlayTilemap"
            };
            for (int layer = 0; layer < tilemaps.Length; layer++)
            {
                var layerObject = new GameObject(layerNames[layer]);
                layerObject.transform.SetParent(root.transform, false);
                tilemaps[layer] = layerObject.AddComponent<Tilemap>();
                tilemaps[layer].tileAnchor = Vector3.zero;
                renderers[layer] =
                    layerObject.AddComponent<TilemapRenderer>();
                renderers[layer].mode = layer == 1
                    ? TilemapRenderer.Mode.Individual
                    : TilemapRenderer.Mode.Chunk;
                renderers[layer].sortingOrder = layer switch
                {
                    0 => -1000,
                    1 => 0,
                    _ => 1000,
                };
            }

            var tileCache =
                new Dictionary<TileKey, TileBase>(TileKey.Comparer);
            var spriteCache =
                new Dictionary<TileKey, Sprite>(TileKey.Comparer);
            var animationCache =
                new Dictionary<string, JxqyAnimationMetadata>(
                    StringComparer.OrdinalIgnoreCase);
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int row = 0; row < map.Rows; row++)
                {
                    for (int column = 0; column < map.Columns; column++)
                    {
                        JxqyRuntimeMapTile mapTile =
                            map.GetTile(column, row);
                        for (int layer = 0; layer < 3; layer++)
                        {
                            byte mpcNumber = mapTile.GetMpc(layer);
                            if (mpcNumber == 0)
                                continue;
                            int mpcIndex = mpcNumber - 1;
                            if (mpcIndex < 0 ||
                                mpcIndex >= metadata.MpcTable.Count)
                                continue;
                            JxqyMapMpcMetadata mpc =
                                metadata.MpcTable[mpcIndex];
                            if (string.IsNullOrWhiteSpace(mpc.FileName))
                                continue;

                            string stableId = CreateMpcStableId(
                                metadata.MpcDirectory,
                                mpc.FileName);
                            JxqyAnimationMetadata animation =
                                GetAnimation(stableId, animationCache);
                            if (animation == null)
                                continue;
                            JxqyAnimationFrameMetadata frame =
                                animation.Frames.FirstOrDefault(candidate =>
                                    candidate.SourceFrameIndex ==
                                    mapTile.GetFrame(layer)) ??
                                animation.Frames.FirstOrDefault();
                            if (frame == null)
                                continue;

                            var key = new TileKey(
                                stableId,
                                frame.SourceFrameIndex);
                            if (!tileCache.TryGetValue(
                                    key,
                                    out TileBase tile))
                            {
                                tile = CreateTileSubAsset(
                                    libraryPath,
                                    key,
                                    animation,
                                    frame,
                                    mpc.IsLooping,
                                    spriteCache);
                                tileCache.Add(key, tile);
                            }

                            var cell = new Vector3Int(column, row, 0);
                            tilemaps[layer].SetTile(cell, tile);
                            JxqyIntPoint world =
                                JxqyIsometricMapMath.TileToWorldPixel(
                                    column,
                                    row);
                            tilemaps[layer].SetTransformMatrix(
                                cell,
                                Matrix4x4.Translate(new Vector3(
                                    world.X - column,
                                    -world.Y - row,
                                    0f)));
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.ImportAsset(
                libraryPath,
                ImportAssetOptions.ForceSynchronousImport);

            CreateCamera(metadata);
            var lightObject = new GameObject("Directional Light");
            lightObject.AddComponent<Light>().type = LightType.Directional;

            string scenePath = sceneDirectory + "/" +
                               SanitizeFileName(sceneName) + ".unity";
            Directory.CreateDirectory(ToAbsolutePath(sceneDirectory));
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException($"Failed to save map scene: {scenePath}");

            return new JxqyMapSceneEntry
            {
                SceneKey = metadata.SourceStableId,
                MapStableId = metadata.SourceStableId,
                MapRelativePath = metadata.SourceRelativePath,
                SceneAddress = CreateAddress(scenePath),
                SceneAssetPath = scenePath,
                ColumnCount = metadata.ColumnCount,
                RowCount = metadata.RowCount
            };
        }

        private static TileBase CreateTileSubAsset(
            string libraryPath,
            TileKey key,
            JxqyAnimationMetadata animation,
            JxqyAnimationFrameMetadata frame,
            bool looping,
            IDictionary<TileKey, Sprite> spriteCache)
        {
            string id = Hash128.Compute(
                $"{key.StableId}|{key.FrameIndex}").ToString();
            if (looping && animation.Frames.Count > 1)
            {
                JxqyAnimationFrameMetadata[] orderedFrames =
                    animation.Frames
                        .OrderBy(candidate => candidate.SourceFrameIndex)
                        .ToArray();
                int startIndex = Array.FindIndex(
                    orderedFrames,
                    candidate =>
                        candidate.SourceFrameIndex ==
                        frame.SourceFrameIndex);
                if (startIndex < 0)
                    startIndex = 0;
                var sprites = new Sprite[orderedFrames.Length];
                for (int index = 0; index < sprites.Length; index++)
                {
                    JxqyAnimationFrameMetadata animatedFrame =
                        orderedFrames[
                            (startIndex + index) % orderedFrames.Length];
                    var spriteKey = new TileKey(
                        key.StableId,
                        animatedFrame.SourceFrameIndex);
                    sprites[index] = GetOrCreateSpriteSubAsset(
                        libraryPath,
                        spriteKey,
                        animation,
                        animatedFrame,
                        spriteCache);
                }
                var animatedTile =
                    ScriptableObject.CreateInstance<JxqyAnimatedTile>();
                animatedTile.name = id + "-AnimatedTile";
                animatedTile.Initialize(
                    sprites,
                    1000f / Math.Max(
                        1,
                        animation.IntervalMilliseconds));
                AssetDatabase.AddObjectToAsset(
                    animatedTile,
                    libraryPath);
                return animatedTile;
            }

            Sprite sprite = GetOrCreateSpriteSubAsset(
                libraryPath,
                key,
                animation,
                frame,
                spriteCache);
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = id + "-Tile";
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            AssetDatabase.AddObjectToAsset(tile, libraryPath);
            return tile;
        }

        private static Sprite GetOrCreateSpriteSubAsset(
            string libraryPath,
            TileKey key,
            JxqyAnimationMetadata animation,
            JxqyAnimationFrameMetadata frame,
            IDictionary<TileKey, Sprite> spriteCache)
        {
            if (spriteCache.TryGetValue(key, out Sprite cached))
                return cached;
            string id = Hash128.Compute(
                $"{key.StableId}|{key.FrameIndex}").ToString();
            string metadataPath = ResolveAnimationMetadataPath(
                animation.SourceRelativePath);
            string atlasPath =
                Path.GetDirectoryName(metadataPath)?.Replace('\\', '/') +
                $"/animation.atlas.{frame.AtlasPage:D3}.png";
            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (texture == null)
                throw new FileNotFoundException(
                    "Animation atlas is missing.",
                    atlasPath);

            float pivotX = frame.AtlasWidth == 0
                ? 0f
                : Mathf.Clamp01(
                    (float)frame.GetMapAnchorX() /
                    frame.AtlasWidth);
            float pivotY = frame.AtlasHeight == 0
                ? 0f
                : Mathf.Clamp01(
                    1f - (float)frame.GetMapAnchorY() /
                    frame.AtlasHeight);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(
                    frame.AtlasX,
                    frame.AtlasY,
                    frame.AtlasWidth,
                    frame.AtlasHeight),
                new Vector2(pivotX, pivotY),
                1f,
                0,
                SpriteMeshType.FullRect,
                Vector4.zero,
                false);
            sprite.name = id;
            AssetDatabase.AddObjectToAsset(sprite, libraryPath);
            spriteCache.Add(key, sprite);
            return sprite;
        }

        private static JxqyAnimationMetadata GetAnimation(
            string stableId,
            IDictionary<string, JxqyAnimationMetadata> cache)
        {
            if (cache.TryGetValue(stableId, out JxqyAnimationMetadata value))
                return value;
            string relativePath = stableId.Substring("mpc:".Length);
            string metadataPath = ResolveAnimationMetadataPath(relativePath);
            TextAsset metadataAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(metadataPath);
            if (metadataAsset == null)
            {
                if (!_activeProfile.AllowMissingMapAnimations)
                {
                    throw new FileNotFoundException(
                        $"Animation metadata is missing for {stableId}.",
                        metadataPath);
                }
                if (MissingAnimationStableIds.Add(stableId))
                {
                    Debug.LogWarning(
                        $"Skipping absent map animation '{stableId}'. " +
                        "The source archive does not contain this referenced file.");
                }
                cache.Add(stableId, null);
                return null;
            }
            value = JsonUtility.FromJson<JxqyAnimationMetadata>(
                metadataAsset.text);
            if (value == null)
                throw new InvalidDataException(
                    $"Animation metadata is invalid: {metadataPath}");
            cache.Add(stableId, value);
            return value;
        }

        private static string ResolveAnimationMetadataPath(
            string sourceRelativePath)
        {
            string normalized = sourceRelativePath
                .Replace('\\', '/')
                .TrimStart('/');
            string expected =
                $"{ActiveContentRoot}/Animations/{normalized}/animation.json";
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(expected) != null)
                return expected;

            string[] guids = AssetDatabase.FindAssets(
                "animation t:TextAsset",
                new[] { ActiveContentRoot + "/Animations" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(
                        "/animation.json",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                TextAsset candidate =
                    AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                JxqyAnimationMetadata metadata =
                    JsonUtility.FromJson<JxqyAnimationMetadata>(
                        candidate.text);
                if (metadata != null &&
                    string.Equals(
                        metadata.SourceRelativePath,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return null;
        }

        [Serializable]
        private sealed class JxqyMapSceneBakeReport
        {
            public string GeneratorVersion = string.Empty;
            public int MapCount;
            public int MissingAnimationCount;
            public List<string> MissingAnimationStableIds = new();
        }

        private static void CreateCamera(JxqyMapMetadata metadata)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 240f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                camera.cullingMask &= ~(1 << uiLayer);
            camera.transparencySortMode = TransparencySortMode.CustomAxis;
            camera.transparencySortAxis = new Vector3(0f, 1f, 0f);
            cameraObject.transform.position = new Vector3(
                metadata.MapPixelWidth * 0.5f,
                -metadata.MapPixelHeight * 0.5f,
                -100f);
        }

        private static string CreateMpcStableId(
            string directory,
            string fileName)
        {
            string path =
                $"{directory.Trim('/', '\\')}/{fileName.TrimStart('/', '\\')}"
                    .Replace('\\', '/')
                    .ToLowerInvariant();
            return "mpc:" + path;
        }

        private static string CreateAddress(string assetPath)
        {
            string root = ActiveContentRoot.TrimEnd('/') + "/";
            if (!assetPath.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Jxqy scene must stay below the Jxqy asset root.",
                    nameof(assetPath));
            return "jxqy/" + assetPath.Substring(root.Length)
                .Replace('\\', '/')
                .ToLowerInvariant();
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char character in Path.GetInvalidFileNameChars())
                value = value.Replace(character, '_');
            return value;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    ".."))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            return Path.GetFullPath(absolutePath)
                .Substring(projectRoot.Length + 1)
                .Replace('\\', '/');
        }

        private static void WriteJson<T>(string assetPath, T value)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllText(
                absolutePath,
                JsonUtility.ToJson(value, true),
                new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private readonly struct TileKey : IEquatable<TileKey>
        {
            public static readonly IEqualityComparer<TileKey> Comparer =
                EqualityComparer<TileKey>.Default;

            public TileKey(string stableId, int frameIndex)
            {
                StableId = stableId;
                FrameIndex = frameIndex;
            }

            public string StableId { get; }
            public int FrameIndex { get; }

            public bool Equals(TileKey other)
            {
                return FrameIndex == other.FrameIndex &&
                       string.Equals(
                           StableId,
                           other.StableId,
                           StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is TileKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return StringComparer.OrdinalIgnoreCase.GetHashCode(
                               StableId) * 397 ^
                           FrameIndex;
                }
            }
        }
    }

}
