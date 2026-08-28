using System;
using JxNewMod.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// A reusable row in the official Mod selector. The catalog owns which
    /// Mods exist; this widget only presents one descriptor and reports clicks.
    /// </summary>
    public sealed class ModSelectItemWidget : UIWidget
    {
        private Button _button;
        private GameObject _selectionFrame;
        private Text _name;
        private ModDescriptor _descriptor;
        private Action<ModDescriptor> _selected;

        protected override void ScriptGenerator()
        {
            _button = gameObject.GetComponent<Button>() ??
                throw new InvalidOperationException(
                    "Mod selector item requires a Button on its root.");
            _selectionFrame = Require<RectTransform>(
                "m_group_Selection").gameObject;
            _name = Require<Text>("m_text_Name");
            _button.onClick.AddListener(Select);
        }

        protected override void OnDestroy()
        {
            _button?.onClick.RemoveListener(Select);
            _descriptor = null;
            _selected = null;
        }

        public void Bind(
            ModDescriptor descriptor,
            Action<ModDescriptor> selected)
        {
            _descriptor = descriptor ??
                throw new ArgumentNullException(nameof(descriptor));
            _selected = selected ??
                throw new ArgumentNullException(nameof(selected));

            _name.text = descriptor.DisplayName;
            _button.interactable = descriptor.IsEnabled;
            SetSelected(false);
            gameObject.name = $"m_widget_Mod_{descriptor.Id.Value}";
        }

        public ModDescriptor Descriptor => _descriptor;

        public void SetSelected(bool selected)
        {
            if (_selectionFrame != null)
                _selectionFrame.SetActive(selected);
        }

        public void SetInteractable(bool interactable)
        {
            _button.interactable = interactable &&
                                   _descriptor?.IsEnabled == true;
        }

        private void Select()
        {
            if (_descriptor != null && _button.interactable)
                _selected?.Invoke(_descriptor);
        }

        private T Require<T>(string nodeName) where T : Component
        {
            T component = FindChildComponent<T>(nodeName);
            if (component == null)
                throw new InvalidOperationException(
                    $"Mod selector item is missing {typeof(T).Name} " +
                    $"'{nodeName}'.");
            return component;
        }

    }
}
