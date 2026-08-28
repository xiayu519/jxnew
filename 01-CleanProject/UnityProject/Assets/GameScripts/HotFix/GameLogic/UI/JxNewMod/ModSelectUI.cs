using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using JxNewMod.Domain;
using JxNewMod.Runtime;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(
        UILayer.System,
        location: "ModSelectUI",
        fullScreen: true,
        hideTimeToClose: 0)]
    public sealed class ModSelectUI : UIWindow
    {
        private RectTransform _safeArea;
        private Text _title;
        private RectTransform _listContent;
        private GameObject _itemTemplate;
        private Text _status;
        private GameObject _actions;
        private Button _confirm;
        private Button _cancel;
        private readonly List<ModSelectItemWidget> _items = new();
        private ModRuntimeCoordinator _coordinator;
        private CancellationTokenSource _lifetime;
        private ModDescriptor _selectedDescriptor;
        private bool _activationStarted;

        protected override void ScriptGenerator()
        {
            _safeArea = Require<RectTransform>("m_rect_SafeArea");
            _title = Require<Text>("m_text_Title");
            _listContent = Require<RectTransform>("m_rect_ModListContent");
            _itemTemplate = Require<RectTransform>(
                "m_widget_ModItemTemplate").gameObject;
            _status = Require<Text>("m_text_Status");
            _actions = Require<RectTransform>("m_group_Actions").gameObject;
            _confirm = Require<Button>("m_btn_Confirm");
            _cancel = Require<Button>("m_btn_Cancel");
            _confirm.onClick.AddListener(ConfirmSelection);
            _cancel.onClick.AddListener(CancelSelection);
        }

        protected override void OnCreate()
        {
            _coordinator = UserData as ModRuntimeCoordinator ??
                throw new InvalidOperationException(
                    "ModSelectUI requires a ModRuntimeCoordinator.");
            _lifetime = new CancellationTokenSource();
            SetUIFit(_safeArea);

            _title.text = "选择 Mod";
            BuildItems();
            SetStatus("请选择一个 Mod");
            SelectDefaultItem();
        }

        protected override void OnDestroy()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            _confirm?.onClick.RemoveListener(ConfirmSelection);
            _cancel?.onClick.RemoveListener(CancelSelection);
            _confirm = null;
            _cancel = null;
            _actions = null;
            _selectedDescriptor = null;
            _coordinator = null;
            _items.Clear();
        }

        private void BuildItems()
        {
            _itemTemplate.SetActive(false);
            foreach (ModDescriptor descriptor in _coordinator.Catalog.Mods
                         .Where(mod => mod.IsEnabled))
            {
                ModSelectItemWidget item =
                    CreateWidgetByPrefab<ModSelectItemWidget>(
                        _itemTemplate,
                        _listContent);
                if (item == null)
                    throw new InvalidOperationException(
                        $"Failed to create selector row for '{descriptor.Id}'.");

                item.Bind(descriptor, Select);
                _items.Add(item);
            }
        }

        private void Select(ModDescriptor descriptor)
        {
            if (_activationStarted || descriptor?.IsEnabled != true)
                return;

            _selectedDescriptor = descriptor;
            RefreshSelection();
        }

        private void SelectDefaultItem()
        {
            foreach (ModSelectItemWidget item in _items)
            {
                if (item.Descriptor?.IsEnabled != true)
                    continue;
                _selectedDescriptor = item.Descriptor;
                break;
            }
            RefreshSelection();
        }

        private void ConfirmSelection()
        {
            if (_selectedDescriptor != null)
                ActivateAsync(_selectedDescriptor).Forget();
        }

        private void CancelSelection()
        {
            if (_activationStarted)
                return;
            _selectedDescriptor = null;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (ModSelectItemWidget item in _items)
            {
                item.SetSelected(ReferenceEquals(
                    item.Descriptor,
                    _selectedDescriptor));
            }
            if (_actions != null)
                _actions.SetActive(_selectedDescriptor != null);
        }

        private async UniTask ActivateAsync(ModDescriptor descriptor)
        {
            if (_activationStarted ||
                _coordinator == null ||
                _lifetime == null)
            {
                return;
            }

            _activationStarted = true;
            SetItemsInteractable(false);
            SetStatus($"正在加载 {descriptor.DisplayName}…");

            CancellationToken cancellationToken = _lifetime.Token;
            ModActivationResult result = await _coordinator.ActivateAsync(
                descriptor.Id,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            if (result.Succeeded)
            {
                SetStatus($"已进入 {result.ActiveContext.Descriptor.DisplayName}");
                GameModule.UI.CloseUI<ModSelectUI>();
                return;
            }

            _activationStarted = false;
            SetItemsInteractable(true);
            RefreshSelection();
            SetStatus(result.Message);
        }

        private void SetItemsInteractable(bool interactable)
        {
            foreach (ModSelectItemWidget item in _items)
                item.SetInteractable(interactable);
        }

        private void SetStatus(string message)
        {
            _status.text = message ?? string.Empty;
        }

        private T Require<T>(string nodeName) where T : Component
        {
            T component = FindChildComponent<T>(nodeName);
            if (component == null)
                throw new InvalidOperationException(
                    $"ModSelectUI prefab is missing {typeof(T).Name} '{nodeName}'.");
            return component;
        }
    }
}
