using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameLogic;
using JxNewMod.Domain;
using Jxqy.UnityAdapters;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset.Editor;

namespace Jxqy.Editor.UI
{
    public static class JxqyUiPrefabContractValidationJob
    {
        private const string UiCatalogAddress =
            "jxqy/ui/ui-catalog.json";
        private const int FormalSelectionChoiceCount = 4;
        private const int DaoJianSelectionChoiceCount = 7;
        private static readonly FieldInfo WindowPanelField =
            typeof(UIWindow).GetField(
                "_panel",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [MenuItem(
            "TEngine/Jx New Mod/Validate Official Mod UI Prefab Contracts")]
        public static void Validate()
        {
            IReadOnlyList<UiContractValidationSummary> summaries =
                ValidateOrThrow();
            Debug.Log(
                "Official Mod UI prefab contract validation passed. " +
                string.Join(
                    "; ",
                    summaries.Select(summary =>
                        $"{summary.ModId}/{summary.PackageName}: " +
                        $"{summary.WindowCount} roles")));
        }

        public static IReadOnlyList<UiContractValidationSummary>
            ValidateOrThrow()
        {
            if (WindowPanelField == null)
            {
                throw new MissingFieldException(
                    typeof(UIWindow).FullName,
                    "_panel");
            }

            AssetBundleCollectorSetting setting =
                AssetBundleCollectorSettingData.Setting;
            OfficialModCatalog catalog = OfficialModCatalog.CreateBuiltIn();
            var summaries = new List<UiContractValidationSummary>();
            foreach (ModDescriptor descriptor in catalog.Mods.Where(
                         mod => mod.IsEnabled))
            {
                summaries.Add(ValidateMod(setting, descriptor));
            }
            return summaries;
        }

        private static UiContractValidationSummary ValidateMod(
            AssetBundleCollectorSetting setting,
            ModDescriptor descriptor)
        {
            CollectResult result = setting.BeginCollect(
                descriptor.PackageName,
                simulateBuild: false,
                useAssetDependencyDB: false);
            Dictionary<string, CollectAssetInfo> assetsByAddress =
                result.CollectAssets
                    .Where(asset =>
                        !string.IsNullOrWhiteSpace(asset.Address))
                    .ToDictionary(
                        asset => asset.Address,
                        StringComparer.OrdinalIgnoreCase);
            var resourceChain = new List<
                IReadOnlyDictionary<string, CollectAssetInfo>>
            {
                assetsByAddress,
            };
            foreach (ModResourcePackage fallback in
                     descriptor.ResourcePackages.Skip(1))
            {
                CollectResult fallbackResult = setting.BeginCollect(
                    fallback.PackageName,
                    simulateBuild: false,
                    useAssetDependencyDB: false);
                resourceChain.Add(fallbackResult.CollectAssets
                    .Where(asset =>
                        !string.IsNullOrWhiteSpace(asset.Address))
                    .ToDictionary(
                        asset => asset.Address,
                        StringComparer.OrdinalIgnoreCase));
            }
            if (!assetsByAddress.TryGetValue(
                    UiCatalogAddress,
                    out CollectAssetInfo catalogAsset))
            {
                Fail(
                    descriptor,
                    $"UiCatalog '{UiCatalogAddress}' is not collected.");
            }

            string catalogPath = catalogAsset.AssetInfo.AssetPath;
            TextAsset textAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(catalogPath);
            if (textAsset == null)
            {
                Fail(
                    descriptor,
                    $"UiCatalog is not a TextAsset: {catalogPath}.");
            }
            ModUiCatalogDocument document =
                JsonUtility.FromJson<ModUiCatalogDocument>(textAsset.text);
            if (document?.Windows == null || document.Windows.Count == 0)
                Fail(descriptor, "UiCatalog contains no window roles.");

            foreach (ModUiWindowRecord window in document.Windows)
            {
                ValidateWindow(
                    descriptor,
                    window,
                    resourceChain);
            }
            return new UiContractValidationSummary(
                descriptor.Id.Value,
                descriptor.PackageName,
                document.Windows.Count);
        }

        private static void ValidateWindow(
            ModDescriptor descriptor,
            ModUiWindowRecord contract,
            IReadOnlyList<IReadOnlyDictionary<
                string,
                CollectAssetInfo>> resourceChain)
        {
            if (contract == null || string.IsNullOrWhiteSpace(contract.Role))
                Fail(descriptor, "UiCatalog contains an empty role.");
            if (string.IsNullOrWhiteSpace(contract.PrefabAddress))
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' has no prefab address.");
            }
            CollectAssetInfo collected = null;
            foreach (IReadOnlyDictionary<string, CollectAssetInfo> package in
                     resourceChain)
            {
                if (package.TryGetValue(
                        contract.PrefabAddress,
                        out collected))
                {
                    break;
                }
            }
            if (collected == null)
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' prefab address " +
                    $"'{contract.PrefabAddress}' is not collected by its " +
                    "ordered Mod resource chain.");
            }

            Type roleType = typeof(UIWindow).Assembly.GetType(
                $"GameLogic.{contract.Role}",
                throwOnError: false,
                ignoreCase: false);
            roleType ??= typeof(JxqyCombatFloatTextView).Assembly.GetType(
                $"Jxqy.UnityAdapters.{contract.Role}",
                throwOnError: false,
                ignoreCase: false);
            if (roleType == null)
            {
                Fail(
                    descriptor,
                    $"UI role type '{contract.Role}' is missing from " +
                    "the shared UIWindow and runtime-view assemblies.");
            }

            string prefabPath = collected.AssetInfo.AssetPath;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (root.GetComponent<RectTransform>() == null)
                {
                    Fail(
                        descriptor,
                        $"UI role '{contract.Role}' prefab root has no " +
                        "RectTransform.");
                }

                ValidateReadableTextRects(
                    descriptor,
                    contract,
                    root);
                if (roleType == typeof(JxqySelectionUI))
                {
                    ValidateStaticSelectionChoices(
                        descriptor,
                        contract,
                        root);
                }
                if (roleType == typeof(JxqyDialogueUI))
                {
                    ValidateDialoguePortrait(
                        descriptor,
                        contract,
                        root);
                }
                if (roleType == typeof(JxqyLittleMapUI))
                {
                    ValidateLittleMapPresentation(
                        descriptor,
                        contract,
                        root);
                }

                if (typeof(UIWindow).IsAssignableFrom(roleType))
                {
                    ValidateWindowContract(
                        descriptor,
                        contract,
                        roleType,
                        root);
                }
                else if (typeof(MonoBehaviour).IsAssignableFrom(roleType))
                {
                    bool containsRuntimeView =
                        root.GetComponentInChildren(roleType, true) != null;
                    bool usesPoolAttachedFloatTextView =
                        roleType == typeof(JxqyCombatFloatTextView) &&
                        root.transform.Find("m_text_Value")?.GetComponent<
                            UnityEngine.UI.Text>() != null;
                    if (!containsRuntimeView &&
                        !usesPoolAttachedFloatTextView)
                    {
                        Fail(
                            descriptor,
                            $"UI role '{contract.Role}' prefab does not " +
                            $"contain component '{roleType.FullName}'.");
                    }
                }
                else
                {
                    Fail(
                        descriptor,
                        $"UI role '{contract.Role}' type is neither a " +
                        "UIWindow nor a MonoBehaviour view.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidateReadableTextRects(
            ModDescriptor descriptor,
            ModUiWindowRecord contract,
            GameObject root)
        {
            // SharedLogic windows are the immutable, already accepted formal
            // XinJianXia support prefabs. Keep validating their addresses,
            // static nodes, components and binding contracts, but apply the
            // generic localization sizing heuristic only to Mod-owned UI.
            if (contract.SharedLogic)
                return;

            foreach (UnityEngine.UI.Text text in
                     root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                float requiredHeight = text.fontSize + 10f;
                float actualHeight = Math.Abs(text.rectTransform.rect.height);
                if (actualHeight + 0.01f >= requiredHeight)
                    continue;
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' text '{text.name}' has " +
                    $"height {actualHeight:0.##} for font size " +
                    $"{text.fontSize}; localized text requires at least " +
                    $"{requiredHeight:0.##}.");
            }
        }

        private static void ValidateStaticSelectionChoices(
            ModDescriptor descriptor,
            ModUiWindowRecord contract,
            GameObject root)
        {
            int requiredChoiceCount = descriptor.Id == ModId.XinJianXia
                ? FormalSelectionChoiceCount
                : DaoJianSelectionChoiceCount;
            Transform group = root.transform.Find("m_group_Selection");
            if (group == null)
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' has no selection group.");
            }
            if (group.Find("m_item_ChoiceTemplate") != null)
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' still contains a runtime " +
                    "choice template; choices must be static prefab nodes.");
            }
            ValidateSelectionRect(
                descriptor,
                contract,
                group,
                "m_raw_SelectionPanel",
                new Vector2(-280f, -50f),
                new Vector2(
                    560f,
                    requiredChoiceCount > FormalSelectionChoiceCount
                        ? 140f
                        : 80f));
            ValidateSelectionRect(
                descriptor,
                contract,
                group,
                "m_text_Message",
                new Vector2(10f, -72.25214f),
                new Vector2(380f, 24.5043f),
                expectedPivot: new Vector2(0.5f, 0.5f));
            for (int index = 0;
                 index < requiredChoiceCount;
                 index++)
            {
                Transform choice = group.Find($"m_item_Choice{index}");
                bool complete = choice != null &&
                                choice.GetComponent<RectTransform>() != null &&
                                choice.GetComponent<UnityEngine.UI.Image>() != null &&
                                choice.GetComponent<UnityEngine.UI.Button>() != null &&
                                choice.GetComponent<JxqyChoiceButtonEventRelay>() != null &&
                                choice.Find("m_text_Name")?.GetComponent<
                                    UnityEngine.UI.Text>() != null;
                if (!complete)
                {
                    Fail(
                        descriptor,
                        $"UI role '{contract.Role}' static choice " +
                        $"m_item_Choice{index} is missing or incomplete.");
                }
                ValidateSelectionRect(
                    descriptor,
                    contract,
                    group,
                    $"m_item_Choice{index}",
                    new Vector2(
                        -180f + 220f * (index % 2),
                        -83f - 23f * (index / 2)),
                    new Vector2(160f, 28f));
            }
            if (group.Find($"m_item_Choice{requiredChoiceCount}") != null)
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' contains more than the " +
                    $"required {requiredChoiceCount} static choices.");
            }
        }

        private static void ValidateDialoguePortrait(
            ModDescriptor descriptor,
            ModUiWindowRecord contract,
            GameObject root)
        {
            bool hasDedicatedSpeakerField =
                root.transform.Find("m_text_Speaker") != null;
            string visibleText =
                JxqyDialogueTextPaginator.ComposeVisibleText(
                    "刘轻舟",
                    "为师这次下山……",
                    hasDedicatedSpeakerField);
            string expectedText = hasDedicatedSpeakerField
                ? "为师这次下山……"
                : "刘轻舟：为师这次下山……";
            if (!string.Equals(
                    visibleText,
                    expectedText,
                    StringComparison.Ordinal))
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' cannot present the " +
                    "separate speaker argument used by DaoJian Say.");
            }
            string embeddedSpeakerText =
                "张琳心：近日临安多名女子失踪……";
            if (!string.Equals(
                    JxqyDialogueTextPaginator.ComposeVisibleText(
                        string.Empty,
                        embeddedSpeakerText,
                        hasDedicatedSpeakerField),
                    embeddedSpeakerText,
                    StringComparison.Ordinal))
            {
                Fail(
                    descriptor,
                    $"UI role '{contract.Role}' duplicates a speaker " +
                    "already embedded in legacy dialogue text.");
            }

            RectTransform rect =
                root.transform.Find("m_raw_Portrait") as RectTransform;
            bool valid = rect != null &&
                         Approximately(
                             rect.anchorMin,
                             new Vector2(0.5f, 0.5f)) &&
                         Approximately(
                             rect.anchorMax,
                             new Vector2(0.5f, 0.5f)) &&
                         Approximately(rect.pivot, new Vector2(0f, 1f)) &&
                         Approximately(
                             rect.anchoredPosition,
                             new Vector2(-250f, 150f)) &&
                         Approximately(
                             rect.sizeDelta,
                             new Vector2(500f, 200f));
            if (valid)
                return;

            Fail(
                descriptor,
                $"UI role '{contract.Role}' portrait must preserve the " +
                "original 500x200 frame at logical position (70,90); " +
                "the frame's transparent pixels encode left/right " +
                "portrait placement.");
        }

        private static void ValidateLittleMapPresentation(
            ModDescriptor descriptor,
            ModUiWindowRecord contract,
            GameObject root)
        {
            Transform group = root.transform.Find("m_group_LittleMap");
            RectTransform map = group?.Find("m_raw_Map") as RectTransform;
            RectTransform panel = group?.Find("m_raw_Panel") as RectTransform;
            UnityEngine.UI.RawImage panelImage =
                panel?.GetComponent<UnityEngine.UI.RawImage>();
            string panelPath = panelImage?.texture == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(panelImage.texture);

            bool valid = group is RectTransform groupRect &&
                         Approximately(
                             groupRect.sizeDelta,
                             new Vector2(640f, 480f)) &&
                         HasTopLeftRect(
                             map,
                             new Vector2(160f, -120f),
                             new Vector2(320f, 240f)) &&
                         HasTopLeftRect(
                             panel,
                             Vector2.zero,
                             new Vector2(640f, 480f)) &&
                         map.GetSiblingIndex() < panel.GetSiblingIndex() &&
                         panelImage != null &&
                         !panelImage.raycastTarget &&
                         panelPath.EndsWith(
                             "/littlemap/window-littlemap.asf/" +
                             "animation.atlas.000.png",
                             StringComparison.OrdinalIgnoreCase) &&
                         HasTopLeftRect(
                             group.Find("m_text_MapName") as RectTransform,
                             new Vector2(210f, -110f),
                             new Vector2(220f, 30f)) &&
                         HasTopLeftRect(
                             group.Find("m_text_MapTip") as RectTransform,
                             new Vector2(160f, -370f),
                             new Vector2(260f, 30f)) &&
                         HasTopLeftRect(
                             group.Find("m_btn_Left") as RectTransform,
                             new Vector2(480f, -271f),
                             new Vector2(34f, 32f)) &&
                         HasTopLeftRect(
                             group.Find("m_btn_Right") as RectTransform,
                             new Vector2(544f, -271f),
                             new Vector2(28f, 34f)) &&
                         HasTopLeftRect(
                             group.Find("m_btn_Up") as RectTransform,
                             new Vector2(514f, -249f),
                             new Vector2(30f, 38f)) &&
                         HasTopLeftRect(
                             group.Find("m_btn_Down") as RectTransform,
                             new Vector2(514f, -287f),
                             new Vector2(30f, 40f)) &&
                         HasTopLeftRect(
                             group.Find("m_btn_Close") as RectTransform,
                             new Vector2(424f, -383f),
                             new Vector2(168f, 26f));
            if (valid)
                return;

            Fail(
                descriptor,
                $"UI role '{contract.Role}' little-map presentation " +
                "does not match the Xin Jian Xia 640x480 resource and " +
                "widget contracts.");
        }

        private static bool HasTopLeftRect(
            RectTransform rect,
            Vector2 expectedPosition,
            Vector2 expectedSize)
        {
            return rect != null &&
                   Approximately(rect.anchorMin, new Vector2(0f, 1f)) &&
                   Approximately(rect.anchorMax, new Vector2(0f, 1f)) &&
                   Approximately(rect.pivot, new Vector2(0f, 1f)) &&
                   Approximately(rect.anchoredPosition, expectedPosition) &&
                   Approximately(rect.sizeDelta, expectedSize);
        }

        private static void ValidateSelectionRect(
            ModDescriptor descriptor,
            ModUiWindowRecord contract,
            Transform group,
            string path,
            Vector2 expectedPosition,
            Vector2 expectedSize,
            bool allowWider = false,
            Vector2? expectedPivot = null)
        {
            RectTransform rect = group.Find(path) as RectTransform;
            bool validSize = rect != null &&
                             (allowWider
                                 ? rect.sizeDelta.x + 0.01f >= expectedSize.x &&
                                   Mathf.Abs(
                                       rect.sizeDelta.y - expectedSize.y) <= 0.01f
                                 : Approximately(rect.sizeDelta, expectedSize));
            bool valid = rect != null &&
                         Approximately(rect.anchorMin, new Vector2(0.5f, 0.5f)) &&
                         Approximately(rect.anchorMax, new Vector2(0.5f, 0.5f)) &&
                         Approximately(
                             rect.pivot,
                             expectedPivot ?? new Vector2(0f, 1f)) &&
                         Approximately(rect.anchoredPosition, expectedPosition) &&
                         validSize;
            if (valid)
                return;

            Fail(
                descriptor,
                $"UI role '{contract.Role}' selection node '{path}' is " +
                "not authored in the centered 640x480 viewport.");
        }

        private static bool Approximately(Vector2 actual, Vector2 expected)
        {
            return Mathf.Abs(actual.x - expected.x) <= 0.01f &&
                   Mathf.Abs(actual.y - expected.y) <= 0.01f;
        }

        private static void ValidateWindowContract(
            ModDescriptor mod,
            ModUiWindowRecord contract,
            Type windowType,
            GameObject root)
        {
            if (!WindowAttributeDescriptorResolver.Instance.TryResolve(
                    windowType,
                    out WindowDescriptor descriptor))
            {
                Fail(
                    mod,
                    $"UIWindow '{contract.Role}' has no Window descriptor.");
            }
            if (!string.Equals(
                    descriptor.Location,
                    contract.PrefabAddress,
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail(
                    mod,
                    $"UIWindow '{contract.Role}' descriptor address " +
                    $"'{descriptor.Location}' differs from UiCatalog " +
                    $"'{contract.PrefabAddress}'.");
            }
            if (!string.Equals(
                    descriptor.PackageName,
                    JxqyResourceLocations.PackageName,
                    StringComparison.Ordinal))
            {
                Fail(
                    mod,
                    $"UIWindow '{contract.Role}' package placeholder " +
                    $"'{descriptor.PackageName}' cannot be resolved through " +
                    "the Active Mod package.");
            }

            var window = (UIWindow)Activator.CreateInstance(windowType);
            window.Init(
                windowType.FullName,
                descriptor.WindowLayer,
                descriptor.FullScreen,
                descriptor.Location,
                descriptor.FromResources,
                descriptor.HideTimeToClose,
                mod.PackageName);
            WindowPanelField.SetValue(window, root);
            MethodInfo generator = windowType.GetMethod(
                "ScriptGenerator",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (generator == null)
            {
                Fail(
                    mod,
                    $"UIWindow '{contract.Role}' has no ScriptGenerator " +
                    "contract method.");
            }
            try
            {
                generator.Invoke(window, null);
            }
            catch (TargetInvocationException exception)
            {
                Exception contractFailure = exception.InnerException ??
                                            exception;
                throw new InvalidOperationException(
                    $"Mod '{mod.Id.Value}' UIWindow '{contract.Role}' " +
                    $"prefab contract failed at '{contract.PrefabAddress}'.",
                    contractFailure);
            }
            finally
            {
                WindowPanelField.SetValue(window, null);
            }
        }

        private static void Fail(ModDescriptor descriptor, string message)
        {
            throw new InvalidOperationException(
                $"Mod '{descriptor.Id.Value}' UI contract validation " +
                $"failed: {message}");
        }

        [Serializable]
        private sealed class ModUiCatalogDocument
        {
            public List<ModUiWindowRecord> Windows = new();
        }

        [Serializable]
        private sealed class ModUiWindowRecord
        {
            public string Role = string.Empty;
            public string PrefabAddress = string.Empty;
            public bool SharedLogic;
        }
    }

    public readonly struct UiContractValidationSummary
    {
        public UiContractValidationSummary(
            string modId,
            string packageName,
            int windowCount)
        {
            ModId = modId;
            PackageName = packageName;
            WindowCount = windowCount;
        }

        public string ModId { get; }
        public string PackageName { get; }
        public int WindowCount { get; }
    }

    public sealed class JxqyUiPrefabContractBuildValidator :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => -850;

        public void OnPreprocessBuild(
            UnityEditor.Build.Reporting.BuildReport report)
        {
            JxqyUiPrefabContractValidationJob.ValidateOrThrow();
        }
    }
}
