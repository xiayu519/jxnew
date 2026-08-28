using System;
using System.Collections.Generic;
using Jxqy.Domain.Presentation;
using Jxqy.Domain.Simulation;
using Jxqy.UnityAdapters;
using TEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    public abstract class JxqySessionWindow : UIWindow
    {
        protected JxqyUiSession Session { get; private set; }
        protected virtual JxqyUiSound? DefaultButtonSound => null;

        protected sealed override void RegisterEvent()
        {
            AddUIEvent(IJxqyUI_Event.OnJxqyUiChanged, OnUiChanged);
        }

        protected override void OnCreate()
        {
            AttachSession();
            BindDefaultButtonSounds();
            RefreshView();
        }

        protected override void OnRefresh()
        {
            AttachSession();
            RefreshView();
        }

        protected abstract void RefreshView();

        private void AttachSession()
        {
            Session = UserData as JxqyUiSession;
        }

        private void OnUiChanged()
        {
            RefreshView();
        }

        protected void RequestUiSound(JxqyUiSound sound)
        {
            Session?.RequestSound(sound);
        }

        protected void BindButtonSound(
            Button button,
            JxqyUiSound sound)
        {
            if (button == null)
                return;
            button.onClick.AddListener(() => RequestUiSound(sound));
        }

        private void BindDefaultButtonSounds()
        {
            if (!DefaultButtonSound.HasValue)
                return;
            JxqyUiSound sound = DefaultButtonSound.Value;
            Button[] buttons = rectTransform == null
                ? Array.Empty<Button>()
                : rectTransform.GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                Button button = buttons[index];
                if (button == null ||
                    button.GetComponent<JxqyListSlotEventRelay>() != null)
                {
                    continue;
                }
                button.onClick.AddListener(
                    () => RequestUiSound(sound));
            }
        }

        protected static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
                button.gameObject.SetActive(visible);
        }

        protected static void ClearButton(Button button)
        {
            button?.onClick.RemoveAllListeners();
        }

        protected static T RequireStaticComponent<T>(
            GameObject target,
            string owner) where T : Component
        {
            T component = target == null ? null : target.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"{owner} requires static prefab component " +
                    $"{typeof(T).Name}.");
            }
            return component;
        }

        protected void BlockBackdropInside(string panelPath)
        {
            Graphic panel = FindChildComponent<Graphic>(panelPath);
            if (panel == null)
            {
                throw new InvalidOperationException(
                    $"Backdrop content panel is missing: {panelPath}");
            }
            panel.raycastTarget = true;
        }
    }

    [Window(
        UILayer.Backdrop,
        location: "jxqy/ui/prefabs/jxqysharedbackdropui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqySharedBackdropUI : JxqySessionWindow
    {
        private Button _mask;

        protected override void ScriptGenerator()
        {
            _mask = FindChildComponent<Button>("m_btn_Mask");
            if (_mask == null)
            {
                throw new InvalidOperationException(
                    "JxqySharedBackdropUI prefab mask is missing.");
            }
            _mask.onClick.AddListener(CloseCurrentScreen);
        }

        protected override void RefreshView()
        {
            if (_mask != null)
            {
                _mask.interactable =
                    Session?.SharedBackdropScreen.HasValue == true;
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_mask);
            _mask = null;
        }

        private void CloseCurrentScreen()
        {
            Session?.CloseSharedBackdropScreen();
        }
    }

    [Window(
        UILayer.System,
        location: "jxqy/ui/prefabs/jxqyfadeui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyFadeUI : JxqySessionWindow
    {
        private Image _overlay;

        public float Opacity => Session?.FadeOpacity ?? 0f;

        protected override void ScriptGenerator()
        {
            _overlay = FindChildComponent<Image>("m_image_Overlay");
            // Script state and modal windows own input gating. The fade is a
            // visual transition only; leaving it as a raycast target can make
            // a stale or deliberately opaque fade block the UI that must
            // advance the script.
            if (_overlay != null)
                _overlay.raycastTarget = false;
        }

        protected override void RefreshView()
        {
            ApplyOpacity();
            Session?.NotifyFadeUiReady();
        }

        protected override void OnUpdate()
        {
            ApplyOpacity();
        }

        private void ApplyOpacity()
        {
            if (_overlay == null)
                return;
            Color color = _overlay.color;
            color.r = 0f;
            color.g = 0f;
            color.b = 0f;
            color.a = Opacity;
            _overlay.color = color;
        }
    }

    [Window(
        UILayer.Tips,
        location: "jxqy/ui/prefabs/jxqynoticeui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyNoticeUI : JxqySessionWindow
    {
        private Text _notice;
        private float _hideAt;

        protected override void ScriptGenerator()
        {
            _notice = FindChildComponent<Text>("m_text_Notice");
        }

        protected override void RefreshView()
        {
            if (string.IsNullOrWhiteSpace(Session?.Notice))
            {
                _hideAt = Time.unscaledTime;
                return;
            }
            if (_notice != null)
                _notice.text = Session.Notice;
            _hideAt = Time.unscaledTime + 2f;
        }

        protected override void OnUpdate()
        {
            if (string.IsNullOrWhiteSpace(Session?.Notice) ||
                Time.unscaledTime >= _hideAt)
                GameModule.UI.CloseUI<JxqyNoticeUI>();
        }
    }

    [Window(
        UILayer.Bottom,
        location: "jxqy/ui/prefabs/jxqytargetlifeui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyTargetLifeUI : JxqySessionWindow
    {
        private GameObject _group;
        private RectTransform _fill;
        private Image _fillImage;
        private Text _text;

        protected override void ScriptGenerator()
        {
            _group = FindChild("m_group_TargetLife")?.gameObject;
            _fill = FindChildComponent<RectTransform>(
                "m_group_TargetLife/m_img_TargetLife");
            _fillImage = _fill?.GetComponent<Image>();
            _text = FindChildComponent<Text>(
                "m_group_TargetLife/m_text_TargetLife");
            if (_group == null || _fill == null || _fillImage == null ||
                _text == null)
                throw new InvalidOperationException(
                    "JxqyTargetLifeUI prefab hierarchy is incomplete.");
            _group.SetActive(false);
        }

        protected override void RefreshView()
        {
            if (_group == null)
                return;
            JxqyNpc target = Session?.ResolveTargetLifeNpc();
            bool visible = Session?.CurrentScreen != JxqyUiScreen.Title &&
                           target != null;
            _group.SetActive(visible);
            if (!visible)
                return;
            float percent = target.LifeMax <= 0
                ? 1f
                : Mathf.Clamp01(target.Life / (float)target.LifeMax);
            _fill.anchorMax = new Vector2(percent, 1f);
            _fillImage.color = GetLifeColor(target);
            _text.color = target.Relation == JxqyRelationType.Enemy &&
                          target.ExpBonus > 0
                ? new Color(200f / 255f, 200f / 255f, 10f / 255f) *
                  0.9f
                : Color.white * 0.8f;
            _text.text = target.Name ?? string.Empty;
        }

        private static Color GetLifeColor(JxqyNpc target)
        {
            if (target.Kind == JxqyCharacterKind.Fighter &&
                target.Relation == JxqyRelationType.Enemy)
            {
                return new Color(
                    163f / 255f,
                    18f / 255f,
                    21f / 255f) * 0.9f;
            }
            if ((target.Kind == JxqyCharacterKind.Fighter ||
                 target.Kind == JxqyCharacterKind.Follower) &&
                target.Relation == JxqyRelationType.Friend)
            {
                return new Color(
                    16f / 255f,
                    165f / 255f,
                    28f / 255f) * 0.9f;
            }
            return new Color(
                40f / 255f,
                30f / 255f,
                245f / 255f) * 0.9f;
        }

        protected override void OnUpdate() => RefreshView();
    }

    [Window(
        UILayer.Bottom,
        location: "jxqy/ui/prefabs/jxqytimerui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyTimerUI : JxqySessionWindow
    {
        private GameObject _group;
        private Text _text;
        private JxqyUiAnimationBinding _background;

        protected override void ScriptGenerator()
        {
            _group = FindChild("m_group_Timer")?.gameObject;
            RawImage image = _group?.GetComponent<RawImage>();
            _text = FindChildComponent<Text>(
                "m_group_Timer/m_text_Timer");
            if (_group == null || image == null || _text == null)
                throw new InvalidOperationException(
                    "JxqyTimerUI prefab hierarchy is incomplete.");
            _background = new JxqyUiAnimationBinding(image);
            _background.Set("timer", "window.asf");
            _group.SetActive(false);
        }

        protected override void RefreshView()
        {
            if (_group == null)
                return;
            bool visible = Session?.TimerVisible == true;
            _group.SetActive(visible);
            if (!visible || _text == null)
                return;
            int totalSeconds = Math.Max(0, Session.TimerSeconds);
            _text.text = $"{totalSeconds / 60:00}分" +
                         $"{totalSeconds % 60:00}秒";
        }

        protected override void OnUpdate()
        {
            _background?.Tick(Time.unscaledDeltaTime);
            RefreshView();
        }

        protected override void OnDestroy()
        {
            _background?.Dispose();
            _background = null;
        }
    }

    [Window(
        UILayer.Tips,
        location: "jxqy/ui/prefabs/jxqymessageui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyMessageUI : JxqySessionWindow
    {
        private GameObject _group;
        private Text _text;
        private JxqyPointerClickRelay _clickRelay;
        private JxqyUiAnimationBinding _background;
        private int _sequence = -1;
        private float _hideAt;

        protected override void ScriptGenerator()
        {
            _group = FindChild("m_group_Message")?.gameObject;
            RawImage image = _group?.GetComponent<RawImage>();
            _clickRelay = _group?.GetComponent<JxqyPointerClickRelay>();
            _text = FindChildComponent<Text>(
                "m_group_Message/m_text_Message");
            if (_group == null || image == null || _text == null ||
                _clickRelay == null)
                throw new InvalidOperationException(
                    "JxqyMessageUI prefab hierarchy is incomplete.");
            _clickRelay.Clicked = CloseFromBackgroundClick;
            _background = new JxqyUiAnimationBinding(image);
            _background.Set("message", "msgbox.asf");
            _group.SetActive(false);
        }

        protected override void RefreshView()
        {
            if (Session == null || _group == null ||
                Session.MessageSequence == _sequence)
                return;
            _sequence = Session.MessageSequence;
            _text.text = Session.Message;
            _group.SetActive(!string.IsNullOrWhiteSpace(_text.text));
            _hideAt = Time.unscaledTime + 2f;
        }

        protected override void OnUpdate()
        {
            _background?.Tick(Time.unscaledDeltaTime);
            if (_group != null && _group.activeSelf &&
                Time.unscaledTime >= _hideAt)
                _group.SetActive(false);
        }

        protected override void OnDestroy()
        {
            if (_clickRelay != null)
                _clickRelay.Clicked = null;
            _clickRelay = null;
            _background?.Dispose();
            _background = null;
        }

        private void CloseFromBackgroundClick(PointerEventData eventData)
        {
            if (eventData != null &&
                eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            _group?.SetActive(false);
        }
    }

    [Window(
        UILayer.Tips,
        location: "jxqy/ui/prefabs/jxqysystemmessageui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqySystemMessageUI : JxqySessionWindow
    {
        private sealed class Entry
        {
            public string Text;
            public float ExpiresAt;
        }

        private readonly List<Entry> _entries = new();
        private Text _text;
        private int _sequence = -1;

        protected override void ScriptGenerator()
        {
            _text = FindChildComponent<Text>("m_text_SystemMessages");
            if (_text == null)
                throw new InvalidOperationException(
                    "JxqySystemMessageUI prefab text is missing.");
        }

        protected override void RefreshView()
        {
            if (Session == null ||
                Session.SystemMessageSequence == _sequence)
                return;
            _sequence = Session.SystemMessageSequence;
            if (!string.IsNullOrWhiteSpace(Session.SystemMessage))
            {
                if (_entries.Count >= 15)
                    _entries.RemoveAt(0);
                _entries.Add(new Entry
                {
                    Text = Session.SystemMessage,
                    ExpiresAt = Time.unscaledTime +
                                Session.SystemMessageDurationMilliseconds /
                                1000f,
                });
            }
            RefreshText();
        }

        protected override void OnUpdate()
        {
            bool changed = false;
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                if (Time.unscaledTime < _entries[index].ExpiresAt)
                    continue;
                _entries.RemoveAt(index);
                changed = true;
            }
            if (changed)
                RefreshText();
        }

        private void RefreshText()
        {
            if (_text == null)
                return;
            var lines = new List<string>(_entries.Count);
            for (int index = 0; index < _entries.Count; index++)
                lines.Add(_entries[index].Text);
            _text.text = string.Join("\n", lines);
            _text.gameObject.SetActive(_entries.Count > 0);
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqytitleui.prefab",
        fullScreen: true,
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyTitleUI : JxqySessionWindow
    {
        private Button _newGame;
        private Button _loadGame;
        private Button _credits;
        private Button _exit;

        // Hover is emitted by the title-button relay. ConfirmIndex emits the
        // same configured original sound once before executing the click.
        protected override JxqyUiSound? DefaultButtonSound => null;

        protected override void ScriptGenerator()
        {
            _newGame = FindChildComponent<Button>("m_btn_NewGame");
            _loadGame = FindChildComponent<Button>("m_btn_LoadGame");
            _credits = FindChildComponent<Button>("m_btn_Credits");
            _exit = FindChildComponent<Button>("m_btn_Exit");
            _newGame?.onClick.AddListener(() => ConfirmIndex(0));
            _loadGame?.onClick.AddListener(() => ConfirmIndex(1));
            _credits?.onClick.AddListener(() => ConfirmIndex(2));
            _exit?.onClick.AddListener(() => ConfirmIndex(3));
            Button[] buttons = { _newGame, _loadGame, _credits, _exit };
            for (int index = 0; index < buttons.Length; index++)
            {
                ConfigureTitleButton(buttons[index]);
            }
        }

        protected override void RefreshView()
        {
            // The original title has no persistent/default selection frame.
            // Frame 1 is shown only while the pointer is over a button.
        }

        protected override void OnDestroy()
        {
            ClearButton(_newGame);
            ClearButton(_loadGame);
            ClearButton(_credits);
            ClearButton(_exit);
        }

        private void ConfigureTitleButton(Button button)
        {
            if (button == null)
                return;
            button.transition = Selectable.Transition.None;
            RawImage image = button.targetGraphic as RawImage ??
                             button.GetComponent<RawImage>();
            if (image == null)
                return;
            var relay = RequireStaticComponent<
                JxqyTitleButtonStateRelay>(
                button.gameObject,
                nameof(JxqyTitleUI));
            relay.Configure(
                image,
                () => RequestUiSound(JxqyUiSound.MainMenu));
        }

        private void ConfirmIndex(int index)
        {
            RequestUiSound(JxqyUiSound.MainMenu);
            Session?.Select(index);
            Session?.Confirm();
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqyhudui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyHudUI : JxqySessionWindow
    {
        private const int LegacyGoodsShortcutBegin = 221;
        private const int LegacyMagicShortcutBegin = 40;
        private readonly List<JxqyListSlotWidget> _shortcuts = new();
        private Button _status;
        private Button _equipment;
        private Button _training;
        private Button _inventory;
        private Button _skills;
        private Button _memo;
        private Button _menu;

        protected override JxqyUiSound? DefaultButtonSound =>
            JxqyUiSound.LargeButton;
        private RawImage _life;
        private RawImage _thew;
        private RawImage _mana;
        private JxqyUiAnimationBinding _lifeAnimation;
        private JxqyUiAnimationBinding _thewAnimation;
        private JxqyUiAnimationBinding _manaAnimation;
        private Text _lifeText;
        private Text _thewText;
        private Text _manaText;

        protected override void ScriptGenerator()
        {
            _status = FindChildComponent<Button>("m_btn_Status");
            _equipment = FindChildComponent<Button>("m_btn_Equipment");
            _training = FindChildComponent<Button>("m_btn_Training");
            _inventory = FindChildComponent<Button>("m_btn_Inventory");
            _skills = FindChildComponent<Button>("m_btn_Skills");
            _memo = FindChildComponent<Button>("m_btn_Memo");
            _menu = FindChildComponent<Button>("m_btn_Menu");
            _life = FindChildComponent<RawImage>("m_raw_Life");
            _thew = FindChildComponent<RawImage>("m_raw_Thew");
            _mana = FindChildComponent<RawImage>("m_raw_Mana");
            if (_life != null)
            {
                _lifeAnimation = new JxqyUiAnimationBinding(_life);
                _lifeAnimation.Set("column", "ColLife.asf");
            }
            if (_thew != null)
            {
                _thewAnimation = new JxqyUiAnimationBinding(_thew);
                _thewAnimation.Set("column", "ColThew.asf");
            }
            if (_mana != null)
            {
                _manaAnimation = new JxqyUiAnimationBinding(_mana);
                _manaAnimation.Set("column", "ColMana.asf");
            }
            _lifeText = FindChildComponent<Text>("m_text_Life");
            _thewText = FindChildComponent<Text>("m_text_Thew");
            _manaText = FindChildComponent<Text>("m_text_Mana");
            _status?.onClick.AddListener(
                () => Session?.Toggle(JxqyUiScreen.Status));
            _equipment?.onClick.AddListener(
                () => Session?.OpenPlayerEquipment());
            _inventory?.onClick.AddListener(
                () => Session?.Toggle(JxqyUiScreen.Inventory));
            _skills?.onClick.AddListener(
                () => Session?.Toggle(JxqyUiScreen.Skills));
            _memo?.onClick.AddListener(
                ToggleMemo);
            _menu?.onClick.AddListener(
                () => Session?.Open(JxqyUiScreen.Menu));

            for (int index = 0; index < 8; index++)
            {
                JxqyListSlotWidget widget =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Shortcut{index + 1}");
                if (widget != null)
                    _shortcuts.Add(widget);
            }
        }

        protected override void RefreshView()
        {
            RefreshMeters();

            for (int index = 0; index < _shortcuts.Count; index++)
            {
                bool itemSlot = index < 3;
                string name = string.Empty;
                string detail = string.Empty;
                string iconCategory = null;
                string iconFileName = null;
                float cooldownMilliseconds = 0f;
                if (itemSlot && Session.Inventory != null)
                {
                    JxqyInventoryEntry entry =
                        Session.Inventory.FindAtLegacyIndex(
                            LegacyGoodsShortcutBegin + index);
                    if (entry?.Definition?.Kind == JxqyItemKind.Drug)
                    {
                        name = entry.Definition.Name;
                        detail = entry.Count.ToString();
                        iconCategory = "goods";
                        iconFileName = entry.Definition.IconFileName;
                        cooldownMilliseconds =
                            entry.CooldownMilliseconds;
                    }
                }
                else if (!itemSlot && Session.Skills != null)
                {
                    JxqySkillEntry entry =
                        Session.Skills.FindAtLegacyIndex(
                            LegacyMagicShortcutBegin + index - 3);
                    if (entry != null)
                    {
                        name = string.IsNullOrWhiteSpace(entry.Magic.Name)
                            ? entry.Magic.Id
                            : entry.Magic.Name;
                        detail = string.Empty;
                        iconCategory = "magic";
                        iconFileName = entry.Magic.IconFileName;
                        cooldownMilliseconds =
                            entry.CooldownMilliseconds;
                    }
                }
                _shortcuts[index].Bind(
                    index,
                    name,
                    detail,
                    !itemSlot &&
                    ReferenceEquals(
                        Session.SelectedSkill,
                        Session.Skills?.FindAtLegacyIndex(
                            LegacyMagicShortcutBegin + index - 3)),
                    true,
                    null,
                    OnShortcut,
                    iconCategory: iconCategory,
                    iconFileName: iconFileName,
                    dragData: new JxqyListSlotWidget.DragData(
                        itemSlot
                            ? JxqyListSlotWidget.SlotKind.GoodsShortcut
                            : JxqyListSlotWidget.SlotKind.MagicShortcut,
                        itemSlot
                            ? LegacyGoodsShortcutBegin + index
                            : LegacyMagicShortcutBegin + index - 3),
                    dropped: OnShortcutDrop,
                    cooldownMilliseconds: cooldownMilliseconds,
                    soundRequested: RequestUiSound,
                    hoverExited: HideShortcutPreview,
                    anchoredHovered: PreviewShortcut);
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_status);
            ClearButton(_equipment);
            ClearButton(_training);
            ClearButton(_inventory);
            ClearButton(_skills);
            ClearButton(_memo);
            ClearButton(_menu);
            _lifeAnimation?.Dispose();
            _thewAnimation?.Dispose();
            _manaAnimation?.Dispose();
            _lifeAnimation = null;
            _thewAnimation = null;
            _manaAnimation = null;
            HideShortcutPreview();
        }

        protected override void OnUpdate()
        {
            float elapsedSeconds = Time.unscaledDeltaTime;
            _lifeAnimation?.Tick(elapsedSeconds);
            _thewAnimation?.Tick(elapsedSeconds);
            _manaAnimation?.Tick(elapsedSeconds);
            RefreshMeters();
        }

        private void RefreshMeters()
        {
            JxqyPlayer player = Session?.Player;
            if (player == null)
                return;
            SetMeter(_life, player.Life, player.LifeMax);
            SetMeter(_thew, player.Thew, player.ThewMax);
            SetMeter(_mana, player.Mana, player.ManaMax);
            if (_lifeText != null)
                _lifeText.text = $"{player.Life}/{player.LifeMax}";
            if (_thewText != null)
                _thewText.text = $"{player.Thew}/{player.ThewMax}";
            if (_manaText != null)
                _manaText.text = $"{player.Mana}/{player.ManaMax}";
        }


        private void OnShortcut(int index)
        {
            if (index < 3)
            {
                JxqyInventoryEntry entry =
                    Session?.Inventory?.FindAtLegacyIndex(
                        LegacyGoodsShortcutBegin + index);
                int inventoryIndex = FindInventoryIndex(entry);
                if (inventoryIndex >= 0)
                    Session.UseInventoryItem(inventoryIndex);
            }
            else
            {
                JxqySkillEntry entry =
                    Session?.Skills?.FindAtLegacyIndex(
                        LegacyMagicShortcutBegin + index - 3);
                int skillIndex = FindSkillIndex(entry);
                if (skillIndex >= 0)
                    Session.SelectSkill(skillIndex);
            }
        }

        private void PreviewShortcut(int index, RectTransform anchor)
        {
            ShowShortcutPreview(index, anchor);
        }

        private void ShowShortcutPreview(
            int index,
            RectTransform anchor)
        {
            if (index < 3)
            {
                JxqyInventoryEntry entry =
                    Session?.Inventory?.FindAtLegacyIndex(
                        LegacyGoodsShortcutBegin + index);
                if (entry != null)
                {
                    GameModule.UI.ShowUIAsync<JxqyItemDetailUI>(
                        JxqyLegacyDetailRequest.Preview(
                            entry.Definition,
                            anchor));
                }
                return;
            }
            JxqySkillEntry skill = Session?.Skills?.FindAtLegacyIndex(
                LegacyMagicShortcutBegin + index - 3);
            if (skill != null)
            {
                GameModule.UI.ShowUIAsync<JxqyMagicDetailUI>(
                    JxqyLegacyDetailRequest.Preview(skill, anchor));
            }
        }

        private static void HideShortcutPreview()
        {
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
            GameModule.UI.CloseUI<JxqyMagicDetailUI>();
        }

        private void ToggleMemo()
        {
            if (Session == null)
                return;
            Session.Toggle(JxqyUiScreen.Memo);
        }

        private void OnShortcutDrop(
            JxqyListSlotWidget.DragData source,
            JxqyListSlotWidget.DragData target)
        {
            if (source == null || target == null || Session == null)
                return;
            if (target.Kind ==
                    JxqyListSlotWidget.SlotKind.GoodsShortcut &&
                IsInventorySlot(source.Kind))
            {
                Session.MoveInventoryEntryToGoodsShortcut(
                    source.Index,
                    target.Index);
            }
            else if (target.Kind ==
                         JxqyListSlotWidget.SlotKind.MagicShortcut &&
                     IsSkillSlot(source.Kind))
            {
                int sourceIndex = FindSkillIndex(
                    Session.Skills?.FindAtLegacyIndex(source.Index));
                if (sourceIndex < 0)
                    return;
                Session.MoveSkillEntryToLegacyIndex(
                    sourceIndex,
                    target.Index);
            }
        }

        private static bool IsInventorySlot(
            JxqyListSlotWidget.SlotKind kind)
        {
            return kind == JxqyListSlotWidget.SlotKind.Inventory ||
                   kind == JxqyListSlotWidget.SlotKind.GoodsShortcut;
        }

        private static bool IsSkillSlot(
            JxqyListSlotWidget.SlotKind kind)
        {
            return kind == JxqyListSlotWidget.SlotKind.Skill ||
                   kind == JxqyListSlotWidget.SlotKind.MagicShortcut ||
                   kind == JxqyListSlotWidget.SlotKind.Cultivation;
        }

        private static void SetMeter(
            RawImage image,
            int value,
            int maximum)
        {
            if (image == null)
                return;
            float percent = maximum <= 0
                ? 0f
                : Mathf.Clamp01((float)value / maximum);
            if (image is JxqyFilledRawImage filled)
                filled.VerticalFill = percent;
        }

        private int FindInventoryIndex(JxqyInventoryEntry target)
        {
            if (target == null || Session?.Inventory == null)
                return -1;
            IReadOnlyList<JxqyInventoryEntry> entries =
                Session.Inventory.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index], target))
                    return index;
            }
            return -1;
        }

        private int FindSkillIndex(JxqySkillEntry target)
        {
            if (target == null || Session?.Skills == null)
                return -1;
            IReadOnlyList<JxqySkillEntry> entries =
                Session.Skills.Skills;
            for (int index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index], target))
                    return index;
            }
            return -1;
        }
    }

    [Window(
        UILayer.System,
        location: "jxqy/ui/prefabs/jxqydialogueui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyDialogueUI : JxqySessionWindow
    {
        private sealed class ChoiceButton
        {
            public GameObject GameObject;
            public RectTransform RectTransform;
            public Image Background;
            public Button Button;
            public Text Label;
            public JxqyChoiceButtonEventRelay HoverRelay;
        }

        private static readonly Color ChoiceNormalColor =
            new Color32(0, 0, 204, 255);
        private static readonly Color ChoiceHoverColor =
            new Color32(204, 0, 0, 255);
        private readonly List<ChoiceButton> _choices = new();
        private RawImage _portrait;
        protected override JxqyUiSound? DefaultButtonSound => null;
        private JxqyUiFrameBinding _portraitBinding;
        private Text _speaker;
        private Text _message;
        private Button _continue;
        private JxqyDialoguePage _messageSource;
        private string _messageSourceText = string.Empty;
        private IReadOnlyList<string> _messagePages =
            Array.Empty<string>();
        private int _messagePageIndex;

        protected override void ScriptGenerator()
        {
            _speaker = FindChildComponent<Text>("m_text_Speaker");
            _message = FindChildComponent<Text>("m_text_Message");
            _portrait =
                FindChildComponent<RawImage>("m_raw_Portrait");
            if (_portrait != null)
            {
                _portraitBinding =
                    new JxqyUiFrameBinding(
                        _portrait,
                        useOriginalFrameSize: true);
            }
            _continue = FindChildComponent<Button>("m_btn_Continue");
            _continue?.onClick.AddListener(Continue);
            for (int index = 0; index < 2; index++)
            {
                Transform root = FindChild($"m_item_Choice{index}");
                if (root == null)
                    continue;
                Button button = root.GetComponent<Button>();
                Text label = FindChild(root, "m_text_Name")
                    ?.GetComponent<Text>();
                if (button == null || label == null)
                    continue;
                int choiceIndex = index;
                button.onClick.AddListener(
                    () => SelectChoice(choiceIndex));
                label.raycastTarget = false;
                Image background = root.GetComponent<Image>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                JxqyChoiceButtonEventRelay hoverRelay =
                    RequireStaticComponent<JxqyChoiceButtonEventRelay>(
                        root.gameObject,
                        nameof(JxqyDialogueUI));
                hoverRelay.Configure(
                    label,
                    ChoiceNormalColor,
                    ChoiceHoverColor);
                _choices.Add(new ChoiceButton
                {
                    GameObject = root.gameObject,
                    RectTransform = root as RectTransform,
                    Background = background,
                    Button = button,
                    Label = label,
                    HoverRelay = hoverRelay,
                });
            }
        }

        protected override void RefreshView()
        {
            JxqyDialoguePage page = Session?.Dialogue?.Current;
            int count = page?.Choices.Count ?? 0;
            if (_speaker != null)
                _speaker.text = page?.Speaker ?? string.Empty;
            if (_message != null)
            {
                _message.supportRichText = true;
                string sourceText =
                    JxqyDialogueTextPaginator.ComposeVisibleText(
                        page?.Speaker,
                        page?.Text,
                        _speaker != null);
                if (!ReferenceEquals(_messageSource, page) ||
                    !string.Equals(
                        _messageSourceText,
                        sourceText,
                        StringComparison.Ordinal))
                {
                    _messageSource = page;
                    _messageSourceText = sourceText;
                    _messagePageIndex = 0;
                    _messagePages = count == 0
                        ? JxqyDialogueTextPaginator.Paginate(
                            _message,
                            sourceText)
                        : new[]
                        {
                            JxqyLegacyRichText.ToUnity(sourceText),
                        };
                }
                RenderMessagePage();
            }
            _portraitBinding?.Set(
                "portrait",
                page?.PortraitFileName);
            for (int index = 0; index < _choices.Count; index++)
            {
                bool visible = index < count;
                ChoiceButton choice = _choices[index];
                choice.GameObject.SetActive(visible);
                if (!visible)
                    continue;
                choice.Label.text = page.Choices[index].Text;
                choice.HoverRelay?.ResetVisual();
                choice.Button.interactable = true;
                if (choice.Background != null)
                    choice.Background.color = Color.clear;
            }
            if (_continue != null)
            {
                _continue.interactable = count == 0;
                _continue.gameObject.SetActive(count == 0);
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_continue);
            foreach (ChoiceButton choice in _choices)
                ClearButton(choice.Button);
            _choices.Clear();
            _portraitBinding?.Dispose();
            _portraitBinding = null;
        }

        private void Continue()
        {
            if (_messagePageIndex + 1 < _messagePages.Count)
            {
                _messagePageIndex++;
                RenderMessagePage();
                return;
            }
            Session?.Confirm();
        }

        private void RenderMessagePage()
        {
            if (_message == null)
                return;
            _message.text = _messagePageIndex >= 0 &&
                            _messagePageIndex < _messagePages.Count
                ? _messagePages[_messagePageIndex]
                : string.Empty;
        }

        private void SelectChoice(int index)
        {
            Session?.Select(index);
            Session?.Confirm();
        }
    }

    [Window(
        UILayer.System,
        location: "jxqy/ui/prefabs/jxqyselectionui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqySelectionUI : JxqySessionWindow
    {
        private sealed class ChoiceView
        {
            public GameObject Root;
            public Button Button;
            public Text Label;
            public JxqyChoiceButtonEventRelay Hover;
        }

        private readonly List<ChoiceView> _choices = new();
        private RectTransform _panel;
        private Text _message;

        protected override void ScriptGenerator()
        {
            _panel = FindChildComponent<RectTransform>(
                "m_group_Selection");
            _message = FindChildComponent<Text>(
                "m_group_Selection/m_text_Message");
            if (_panel == null || _message == null)
            {
                throw new InvalidOperationException(
                    "JxqySelectionUI prefab hierarchy is incomplete.");
            }
            BindStaticChoices();
        }

        protected override void RefreshView()
        {
            JxqyDialogue dialogue = Session?.Dialogue;
            JxqyDialoguePage page = dialogue?.Current;
            if (_message != null)
                _message.text = JxqyLegacyRichText.ToUnity(
                    page?.Text ?? string.Empty);
            int count = page?.Choices.Count ?? 0;
            if (count > _choices.Count)
            {
                throw new InvalidOperationException(
                    $"JxqySelectionUI requires {count} choices, but its " +
                    $"static prefab only contains {_choices.Count} slots.");
            }
            bool multiple = (page?.SelectionCount ?? 1) > 1;
            for (int index = 0; index < _choices.Count; index++)
            {
                ChoiceView view = _choices[index];
                bool visible = index < count;
                view.Root.SetActive(visible);
                if (!visible)
                    continue;
                JxqyDialogueChoice choice = page.Choices[index];
                bool selected = IsSelected(
                    dialogue.SelectedChoiceValues,
                    choice.Value);
                view.Label.text = multiple && selected
                    ? "● " + choice.Text
                    : choice.Text;
                view.Label.color = selected
                    ? Color.red
                    : Color.blue;
                Image selectionBackground =
                    view.Root.GetComponent<Image>();
                selectionBackground.color = selected
                    ? new Color(1f, 1f, 0f, 0.2f)
                    : Color.clear;
                view.Hover.Configure(
                    view.Label,
                    view.Label.color,
                    Color.red);
                view.Label.gameObject.SetActive(true);
                view.Label.enabled = true;
                view.Label.canvasRenderer.SetAlpha(1f);
                view.Label.SetAllDirty();
            }
            Canvas.ForceUpdateCanvases();
        }

        protected override void OnDestroy()
        {
            for (int index = 0; index < _choices.Count; index++)
                ClearButton(_choices[index].Button);
            _choices.Clear();
        }

        private void BindStaticChoices()
        {
            for (int index = 0; ; index++)
            {
                GameObject root = FindChild(
                    $"m_group_Selection/m_item_Choice{index}")?.gameObject;
                if (root == null)
                    break;
                Image image = root.GetComponent<Image>();
                Button button = root.GetComponent<Button>();
                Text label = FindChild(root.transform, "m_text_Name")?
                    .GetComponent<Text>();
                var hover =
                    root.GetComponent<JxqyChoiceButtonEventRelay>();
                if (image == null || button == null || label == null ||
                    hover == null)
                {
                    throw new InvalidOperationException(
                        $"Static selection choice {index} is incomplete.");
                }
                int choiceIndex = index;
                button.onClick.AddListener(
                    () => SelectChoice(choiceIndex));
                _choices.Add(new ChoiceView
                {
                    Root = root,
                    Button = button,
                    Label = label,
                    Hover = hover,
                });
                root.SetActive(false);
            }
            if (_choices.Count == 0)
            {
                throw new InvalidOperationException(
                    "JxqySelectionUI prefab contains no static choices.");
            }
        }

        private void SelectChoice(int index)
        {
            Session?.Select(index);
            Session?.Confirm();
        }

        private static bool IsSelected(
            IReadOnlyList<string> selectedValues,
            string value)
        {
            for (int index = 0; index < selectedValues.Count; index++)
            {
                if (string.Equals(
                        selectedValues[index],
                        value,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    [Window(
        UILayer.System,
        location: "jxqy/ui/prefabs/jxqygambleui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyGambleUI : JxqySessionWindow
    {
        private const float RollingDurationSeconds = 2.4f;
        private const float OpeningDurationSeconds = 0.66f;

        private RawImage _luFace;
        private RawImage _bossFace;
        private RawImage _rolling;
        private RawImage _opening;
        private RawImage _openBackground;
        private RawImage[] _dice;
        private JxqyUiAnimationBinding _rollingBinding;
        private JxqyUiAnimationBinding _openingBinding;
        private JxqyUiFrameBinding[] _diceBindings;
        private Button _big;
        private Button _small;
        private Button _stakeUp;
        private Button _stakeDown;
        private JxqyPointerHoldRelay _stakeUpHold;
        private JxqyPointerHoldRelay _stakeDownHold;
        private Button _placeBet;
        private Button _quit;
        private Button _messageButton;
        private GameObject _messageGroup;
        private Text _message;
        private Text _playerMoney;
        private Text _stake;
        private Text _opponentMoney;
        private JxqyGamblePhase _observedPhase =
            (JxqyGamblePhase)(-1);
        private JxqyDaoJianGamblePhase _observedDaoJianPhase =
            (JxqyDaoJianGamblePhase)(-1);
        private float _phaseElapsed;

        protected override void ScriptGenerator()
        {
            _luFace = FindChildComponent<RawImage>("m_raw_LuFace");
            _bossFace = FindChildComponent<RawImage>("m_raw_BossFace");
            _rolling = FindChildComponent<RawImage>("m_raw_Rolling");
            _opening = FindChildComponent<RawImage>("m_raw_Opening");
            _openBackground = FindChildComponent<RawImage>(
                "m_raw_OpenBackground");
            _dice = new[]
            {
                FindChildComponent<RawImage>("m_raw_Die1"),
                FindChildComponent<RawImage>("m_raw_Die2"),
                FindChildComponent<RawImage>("m_raw_Die3"),
            };
            _big = FindChildComponent<Button>("m_btn_Big");
            _small = FindChildComponent<Button>("m_btn_Small");
            _stakeUp = FindChildComponent<Button>("m_btn_StakeUp");
            _stakeDown = FindChildComponent<Button>("m_btn_StakeDown");
            _stakeUpHold = _stakeUp?.GetComponent<JxqyPointerHoldRelay>();
            _stakeDownHold =
                _stakeDown?.GetComponent<JxqyPointerHoldRelay>();
            _placeBet = FindChildComponent<Button>("m_btn_PlaceBet");
            _quit = FindChildComponent<Button>("m_btn_Quit");
            _messageButton = FindChildComponent<Button>(
                "m_group_Message/m_btn_Message");
            _messageGroup = FindChild("m_group_Message")?.gameObject;
            _message = FindChildComponent<Text>(
                "m_group_Message/m_text_Message");
            _playerMoney = FindChildComponent<Text>("m_text_PlayerMoney");
            _stake = FindChildComponent<Text>("m_text_Stake");
            _opponentMoney = FindChildComponent<Text>(
                "m_text_OpponentMoney");

            RequirePrefabContract();
            _rollingBinding = new JxqyUiAnimationBinding(_rolling);
            _openingBinding = new JxqyUiAnimationBinding(_opening);
            _diceBindings = new JxqyUiFrameBinding[3];
            for (int index = 0; index < _dice.Length; index++)
                _diceBindings[index] = new JxqyUiFrameBinding(_dice[index]);
            _big.onClick.AddListener(
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                        Session.CycleDaoJianGambleMultiplier();
                    else
                        Session?.SelectGambleChoice(JxqyGambleChoice.Big);
                });
            _small.onClick.AddListener(
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                        Session.CycleDaoJianGambleMultiplier();
                    else
                        Session?.SelectGambleChoice(JxqyGambleChoice.Small);
                });
            ConfigureStakeHold(
                _stakeUpHold,
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                        Session.AddDaoJianGambleBet();
                    else
                        Session?.IncreaseGambleStake();
                });
            ConfigureStakeHold(
                _stakeDownHold,
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                        Session.CycleDaoJianGambleMultiplier();
                    else
                        Session?.DecreaseGambleStake();
                });
            _placeBet.onClick.AddListener(
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                        Session.BeginDaoJianGambleRoll();
                    else
                        Session?.BeginGambleRoll();
                });
            _quit.onClick.AddListener(
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                        Session.RequestDaoJianGambleQuit();
                    else
                        Session?.RequestGambleQuit();
                });
            _messageButton.onClick.AddListener(
                () =>
                {
                    if (Session?.DaoJianGamble != null)
                    {
                        if (!Session.CompleteDaoJianGambleAfterResult())
                            Session.DismissDaoJianGambleResult();
                    }
                    else
                    {
                        Session?.DismissGambleMessage();
                    }
                });
        }

        protected override void RefreshView()
        {
            JxqyDaoJianGambleSession daoJian = Session?.DaoJianGamble;
            if (daoJian != null)
            {
                RefreshDaoJian(daoJian);
                return;
            }
            JxqyGambleSession gamble = Session?.Gamble;
            if (gamble == null)
                return;
            if (_observedPhase != gamble.Phase)
            {
                _observedPhase = gamble.Phase;
                _phaseElapsed = 0f;
                if (gamble.Phase == JxqyGamblePhase.Rolling)
                {
                    _rollingBinding.Dispose();
                    _rollingBinding = new JxqyUiAnimationBinding(_rolling);
                    _rollingBinding.Set("ui", "赌博动画摇骰子.asf");
                }
                else if (gamble.Phase == JxqyGamblePhase.Opening)
                {
                    _openingBinding.Dispose();
                    _openingBinding = new JxqyUiAnimationBinding(_opening);
                    _openingBinding.Set("ui", "赌博动画开盘.asf");
                }
            }

            bool awaiting =
                gamble.Phase == JxqyGamblePhase.AwaitingBet;
            bool rolling = gamble.Phase == JxqyGamblePhase.Rolling;
            bool opening = gamble.Phase == JxqyGamblePhase.Opening;
            bool showsDice = gamble.HasRolledDice &&
                             (gamble.Phase ==
                                  JxqyGamblePhase.ResultMessage ||
                              gamble.Phase ==
                                  JxqyGamblePhase.SettlementMessage);
            bool showsMessage = gamble.Phase ==
                                    JxqyGamblePhase.ResultMessage ||
                                gamble.Phase ==
                                    JxqyGamblePhase.NoticeMessage ||
                                gamble.Phase ==
                                    JxqyGamblePhase.SettlementMessage;

            _big.gameObject.SetActive(true);
            _small.gameObject.SetActive(true);
            _luFace.gameObject.SetActive(
                gamble.Opponent == JxqyGambleOpponent.LuWencai);
            _bossFace.gameObject.SetActive(
                gamble.Opponent == JxqyGambleOpponent.CasinoOwner);
            _rolling.gameObject.SetActive(rolling);
            _opening.gameObject.SetActive(opening);
            _openBackground.gameObject.SetActive(opening || showsDice);
            for (int index = 0; index < _dice.Length; index++)
                _dice[index].gameObject.SetActive(showsDice);
            if (showsDice)
            {
                _diceBindings[0].Set("ui", "骰子all.asf", gamble.Die1 - 1);
                _diceBindings[1].Set("ui", "骰子all.asf", gamble.Die2 - 1);
                _diceBindings[2].Set("ui", "骰子all.asf", gamble.Die3 - 1);
            }

            _big.interactable = awaiting;
            _small.interactable = awaiting;
            _big.targetGraphic.color = awaiting && gamble.HasChoice &&
                                       gamble.Choice ==
                                           JxqyGambleChoice.Big
                ? new Color(1f, 0.82f, 0.2f, 0.22f)
                : Color.clear;
            _small.targetGraphic.color = awaiting && gamble.HasChoice &&
                                         gamble.Choice ==
                                             JxqyGambleChoice.Small
                ? new Color(1f, 0.82f, 0.2f, 0.22f)
                : Color.clear;
            _stakeUp.interactable = awaiting &&
                                    gamble.AvailableMoney > 0 &&
                                    gamble.Stake < gamble.OpponentMoney;
            _stakeDown.interactable = awaiting && gamble.Stake > 1;
            _placeBet.interactable = awaiting;
            _quit.interactable = awaiting;
            _messageGroup.SetActive(showsMessage);
            _message.text = gamble.Message;
            _playerMoney.text = gamble.AvailableMoney.ToString();
            _stake.text = gamble.Stake.ToString();
            _opponentMoney.text = gamble.OpponentMoney.ToString();
        }

        protected override void OnUpdate()
        {
            JxqyDaoJianGambleSession daoJian = Session?.DaoJianGamble;
            if (daoJian != null)
            {
                UpdateDaoJian(daoJian);
                return;
            }
            JxqyGambleSession gamble = Session?.Gamble;
            if (gamble == null)
                return;
            float elapsed = Time.unscaledDeltaTime;
            if (gamble.Phase == JxqyGamblePhase.Rolling)
                _rollingBinding?.Tick(elapsed);
            else if (gamble.Phase == JxqyGamblePhase.Opening)
                _openingBinding?.Tick(elapsed);
            _phaseElapsed += elapsed;
            if (gamble.Phase == JxqyGamblePhase.Rolling &&
                _phaseElapsed >= RollingDurationSeconds)
            {
                Session.BeginGambleOpening();
            }
            else if (gamble.Phase == JxqyGamblePhase.Opening &&
                     _phaseElapsed >= OpeningDurationSeconds)
            {
                Session.RevealGambleDice();
            }
        }

        private void RefreshDaoJian(
            JxqyDaoJianGambleSession gamble)
        {
            if (_observedDaoJianPhase != gamble.Phase)
            {
                _observedDaoJianPhase = gamble.Phase;
                _phaseElapsed = 0f;
                if (gamble.Phase == JxqyDaoJianGamblePhase.Rolling)
                {
                    _rollingBinding.Dispose();
                    _rollingBinding = new JxqyUiAnimationBinding(_rolling);
                    _rollingBinding.Set(
                        "ui",
                        "璧屽崥鍔ㄧ敾鎽囬瀛?asf");
                }
            }

            bool awaiting =
                gamble.Phase == JxqyDaoJianGamblePhase.AwaitingBet;
            bool rolling =
                gamble.Phase == JxqyDaoJianGamblePhase.Rolling;
            bool result =
                gamble.Phase == JxqyDaoJianGamblePhase.Result;

            // DaoJian uses the same converted static art shell, but its
            // controls drive the independent player-vs-NPC dice-poker rules.
            _big.gameObject.SetActive(false);
            _small.gameObject.SetActive(false);
            _luFace.gameObject.SetActive(
                gamble.OpponentName.IndexOf(
                    "吕文才",
                    StringComparison.Ordinal) >= 0);
            _bossFace.gameObject.SetActive(!_luFace.gameObject.activeSelf);
            _rolling.gameObject.SetActive(rolling);
            _opening.gameObject.SetActive(false);
            _openBackground.gameObject.SetActive(result);
            for (int index = 0; index < _dice.Length; index++)
                _dice[index].gameObject.SetActive(result);
            if (result)
            {
                for (int index = 0; index < _diceBindings.Length; index++)
                {
                    _diceBindings[index].Set(
                        "ui",
                        "楠板瓙all.asf",
                        gamble.PlayerDice[index] - 1);
                }
            }

            _stakeUp.interactable = awaiting &&
                                    gamble.PlayerMoney - gamble.Bet >=
                                    100 * gamble.Multiplier;
            _stakeDown.interactable = awaiting;
            _placeBet.interactable = awaiting;
            _quit.interactable = awaiting;
            _messageGroup.SetActive(
                result || !string.IsNullOrWhiteSpace(gamble.Message));
            _message.text = gamble.Message;
            _playerMoney.text = gamble.PlayerMoney.ToString();
            _stake.text = gamble.Bet + "  x" + gamble.Multiplier;
            _opponentMoney.text = gamble.NpcMoney.ToString();
        }

        private void UpdateDaoJian(
            JxqyDaoJianGambleSession gamble)
        {
            float elapsed = Time.unscaledDeltaTime;
            if (gamble.Phase == JxqyDaoJianGamblePhase.Rolling)
                _rollingBinding?.Tick(elapsed);
            _phaseElapsed += elapsed;
            if (gamble.Phase == JxqyDaoJianGamblePhase.Rolling &&
                _phaseElapsed >= RollingDurationSeconds)
            {
                Session.ResolveDaoJianGambleRound();
            }
            else if (gamble.Phase == JxqyDaoJianGamblePhase.Result &&
                     gamble.ShouldAutoClose &&
                     _phaseElapsed >= OpeningDurationSeconds)
            {
                Session.CompleteDaoJianGambleAfterResult();
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_big);
            ClearButton(_small);
            ClearButton(_stakeUp);
            ClearButton(_stakeDown);
            ClearButton(_placeBet);
            ClearButton(_quit);
            ClearButton(_messageButton);
            _rollingBinding?.Dispose();
            _openingBinding?.Dispose();
            if (_diceBindings != null)
            {
                for (int index = 0; index < _diceBindings.Length; index++)
                    _diceBindings[index]?.Dispose();
            }
            _rollingBinding = null;
            _openingBinding = null;
            _diceBindings = null;
        }

        private void RequirePrefabContract()
        {
            if (_luFace == null || _bossFace == null || _rolling == null ||
                _opening == null || _openBackground == null ||
                Array.Exists(_dice, value => value == null) ||
                _big == null || _small == null || _stakeUp == null ||
                _stakeDown == null || _stakeUpHold == null ||
                _stakeDownHold == null || _placeBet == null ||
                _quit == null ||
                _messageButton == null || _messageGroup == null ||
                _message == null || _playerMoney == null || _stake == null ||
                _opponentMoney == null)
            {
                throw new InvalidOperationException(
                    "JxqyGambleUI static prefab hierarchy is incomplete.");
            }
        }

        private static void ConfigureStakeHold(
            JxqyPointerHoldRelay relay,
            Action changeStake)
        {
            relay.RepeatDelaySeconds = 25f / 60f;
            relay.RepeatIntervalSeconds = 1f / 60f;
            relay.Pressed = changeStake;
            relay.Held = changeStake;
        }
    }

    [Window(
        UILayer.UI,
        location: "jxqy/ui/prefabs/jxqystatusui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyStatusUI : JxqySessionWindow
    {
        private RawImage _panel;
        private Texture _defaultPanelTexture;
        private Rect _defaultPanelUv;
        private JxqyUiAnimationBinding _panelBinding;
        private int _panelPlayerIndex = -1;
        private Text _level;
        private Text _experience;
        private Text _levelUp;
        private Text _life;
        private Text _thew;
        private Text _mana;
        private Text _attack;
        private Text _defend;
        private Text _evade;
        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            _panel = FindChildComponent<RawImage>("m_raw_Panel");
            if (_panel != null)
            {
                _defaultPanelTexture = _panel.texture;
                _defaultPanelUv = _panel.uvRect;
            }
            _level = FindChildComponent<Text>("m_text_Level");
            _experience = FindChildComponent<Text>("m_text_Experience");
            _levelUp = FindChildComponent<Text>("m_text_LevelUp");
            _life = FindChildComponent<Text>("m_text_Life");
            _thew = FindChildComponent<Text>("m_text_Thew");
            _mana = FindChildComponent<Text>("m_text_Mana");
            _attack = FindChildComponent<Text>("m_text_Attack");
            _defend = FindChildComponent<Text>("m_text_Defend");
            _evade = FindChildComponent<Text>("m_text_Evade");
        }

        protected override void RefreshView()
        {
            RefreshPlayerPanel();
            JxqyPlayer player = Session?.Player;
            if (player == null)
                return;
            Set(_level, player.Level.ToString());
            Set(_experience, player.Experience.ToString());
            Set(_levelUp, player.LevelUpExperience.ToString());
            Set(_life, $"{player.Life}/{player.LifeMax}");
            Set(_thew, $"{player.Thew}/{player.ThewMax}");
            Set(_mana, player.ManaLimit
                ? "1/1"
                : $"{player.Mana}/{player.ManaMax}");
            Set(_attack, FormatCombatValue(
                player.Attack,
                player.Attack2,
                player.Attack3));
            Set(_defend, FormatCombatValue(
                player.Defend,
                player.Defend2,
                player.Defend3));
            Set(_evade, player.Evade.ToString());
        }

        protected override void OnUpdate()
        {
            _panelBinding?.Tick(Time.unscaledDeltaTime);
        }

        protected override void OnDestroy()
        {
            _panelBinding?.Dispose();
            _panelBinding = null;
        }

        private void RefreshPlayerPanel()
        {
            if (_panel == null)
                return;
            int playerIndex = Session?.PlayerIndex ?? 0;
            if (_panelPlayerIndex == playerIndex)
                return;
            _panelPlayerIndex = playerIndex;
            _panelBinding?.Dispose();
            _panelBinding = null;
            if (playerIndex <= 0)
            {
                _panel.texture = _defaultPanelTexture;
                _panel.uvRect = _defaultPanelUv;
                _panel.color = Color.white;
                return;
            }
            _panelBinding = new JxqyUiAnimationBinding(_panel);
            _panelBinding.Set(
                "common",
                $"panel5{(char)('a' + playerIndex)}.asf");
        }

        private static string FormatCombatValue(
            int primary,
            int secondary,
            int tertiary)
        {
            return secondary == 0 && tertiary == 0
                ? primary.ToString()
                : $"{primary}({secondary})({tertiary})";
        }

        private static void Set(Text text, string value)
        {
            if (text != null)
                text.text = value;
        }
    }

    [Window(
        UILayer.UI,
        location: "jxqy/ui/prefabs/jxqymemoui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyMemoUI : JxqySessionWindow
    {
        private const int VisibleLineCount = 10;
        private const int LegacyLineLength = 10;
        private readonly List<string> _lines = new();
        private Text _text;
        private JxqyLegacyVerticalScrollBinding _scroll;
        private int _topLine;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            _text = FindChildComponent<Text>("m_text_Memo");
            RectTransform track =
                FindChildComponent<RectTransform>("m_img_ScrollTrack");
            RectTransform thumb =
                FindChildComponent<RectTransform>("m_raw_ScrollThumb");
            if (track != null && thumb != null)
            {
                _scroll = new JxqyLegacyVerticalScrollBinding(
                    track,
                    thumb,
                    rectTransform,
                    OnScrolled);
            }
        }

        protected override void RefreshView()
        {
            BuildLines();
            _scroll?.SetRange(Math.Max(0, _lines.Count - 1));
            _topLine = _scroll?.Value ?? 0;
            RefreshText();
        }

        protected override void OnDestroy()
        {
            _scroll?.Dispose();
            _scroll = null;
        }

        private void BuildLines()
        {
            _lines.Clear();
            IReadOnlyList<string> memos = Session?.Memos;
            if (memos == null)
                return;
            // The original MemoListManager uses AddFirst, so index zero is
            // always the newest memo. Runtime saves keep entries in event
            // order; enumerate them backwards to preserve that presentation
            // without invalidating existing acceptance saves.
            for (int memoIndex = memos.Count - 1;
                 memoIndex >= 0;
                 memoIndex--)
            {
                string memo = memos[memoIndex];
                string value = (memo ?? string.Empty).Trim();
                if (value.Length == 0)
                    continue;
                value = value[0] == '●' ? value : $"●{value}";
                string[] paragraphs = value
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n');
                foreach (string paragraph in paragraphs)
                {
                    if (paragraph.Length == 0)
                    {
                        _lines.Add(string.Empty);
                        continue;
                    }
                    for (int offset = 0;
                         offset < paragraph.Length;
                         offset += LegacyLineLength)
                    {
                        _lines.Add(paragraph.Substring(
                            offset,
                            Math.Min(
                                LegacyLineLength,
                                paragraph.Length - offset)));
                    }
                }
            }
        }

        private void OnScrolled(int value)
        {
            _topLine = value;
            RefreshText();
        }

        private void RefreshText()
        {
            if (_text == null)
                return;
            var visible = new List<string>(VisibleLineCount);
            for (int index = 0; index < VisibleLineCount; index++)
            {
                int lineIndex = _topLine + index;
                visible.Add(lineIndex >= 0 && lineIndex < _lines.Count
                    ? _lines[lineIndex]
                    : string.Empty);
            }
            _text.text = string.Join("\n", visible);
        }

        private void Close()
        {
            Session?.Cancel();
        }
    }

    [Window(
        UILayer.UI,
        location: "jxqy/ui/prefabs/jxqyinventoryui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyInventoryUI : JxqySessionWindow
    {
        private const int PageSize = 9;
        private const int Capacity = 198;
        private const int Columns = 3;
        private const int VisibleRows = PageSize / Columns;
        private readonly List<JxqyListSlotWidget> _slots = new();
        private Text _money;
        private Text _description;
        private Button _previous;
        private Button _next;
        private Button _use;
        private JxqyLegacyVerticalScrollBinding _inventoryScroll;
        private int _topRow;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            _money = FindChildComponent<Text>("m_text_Money");
            _description = FindChildComponent<Text>("m_text_Description");
            _previous = FindChildComponent<Button>("m_btn_PreviousPage");
            _next = FindChildComponent<Button>("m_btn_NextPage");
            _use = FindChildComponent<Button>("m_btn_Use");
            SetActive(_previous, false);
            SetActive(_next, false);
            SetActive(_use, false);
            BuildInventoryScrollBar();
            for (int index = 0; index < PageSize; index++)
            {
                JxqyListSlotWidget slot =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Slot{index + 1}");
                if (slot != null)
                    _slots.Add(slot);
            }
        }

        protected override void RefreshView()
        {
            IReadOnlyList<JxqyInventoryEntry> entries =
                Session?.Inventory?.Entries;
            int count = GetInventoryStoreCount(entries);
            int selection = count == 0
                ? 0
                : Mathf.Clamp(Session.Selection, 0, count - 1);
            int maximumTopRow =
                Capacity / Columns - VisibleRows;
            _topRow = Mathf.Clamp(_topRow, 0, maximumTopRow);
            _inventoryScroll?.SetRange(maximumTopRow);
            _inventoryScroll?.SetValue(_topRow, false);
            int pageStart = _topRow * Columns;
            for (int index = 0; index < _slots.Count; index++)
            {
                int targetLegacyIndex = pageStart + index + 1;
                JxqyInventoryEntry entry =
                    Session?.Inventory?.FindAtLegacyIndex(
                        targetLegacyIndex);
                int dataIndex = FindInventoryIndex(entries, entry);
                bool occupied = dataIndex >= 0 &&
                                targetLegacyIndex <= Capacity;
                _slots[index].gameObject.SetActive(true);
                _slots[index].Bind(
                    dataIndex,
                    occupied ? entry.Definition.Name : string.Empty,
                    occupied ? entry.Count.ToString() : string.Empty,
                    occupied && dataIndex == selection,
                    occupied,
                    Select,
                    Activate,
                    iconCategory: "goods",
                    iconFileName: occupied
                        ? entry.Definition.ImageFileName
                        : null,
                    dragData: new JxqyListSlotWidget.DragData(
                        JxqyListSlotWidget.SlotKind.Inventory,
                        targetLegacyIndex),
                    dropped: OnInventoryDrop,
                    cooldownMilliseconds: occupied
                        ? entry.CooldownMilliseconds
                        : 0f,
                    soundRequested: RequestUiSound,
                    hoverExited: HideItemPreview,
                    anchoredHovered: PreviewItem);
            }
            if (_money != null)
                _money.text = (Session?.Player?.Money ?? 0).ToString();
            if (_description != null)
            {
                _description.text = count == 0
                    ? "（空）"
                    : entries[selection].Definition.Introduction;
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_previous);
            ClearButton(_next);
            ClearButton(_use);
            _inventoryScroll?.Dispose();
            _inventoryScroll = null;
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
        }

        private void Select(int index)
        {
            IReadOnlyList<JxqyInventoryEntry> entries =
                Session?.Inventory?.Entries;
            if (entries == null || index < 0 || index >= entries.Count)
                return;
            Session.Select(index);
        }

        private void PreviewItem(int index, RectTransform anchor)
        {
            IReadOnlyList<JxqyInventoryEntry> entries =
                Session?.Inventory?.Entries;
            if (entries == null || index < 0 || index >= entries.Count)
                return;
            GameModule.UI.ShowUIAsync<JxqyItemDetailUI>(
                JxqyLegacyDetailRequest.Preview(
                    entries[index].Definition,
                    anchor));
        }

        private static void HideItemPreview()
        {
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
        }

        private void BuildInventoryScrollBar()
        {
            RectTransform thumb =
                FindChildComponent<RectTransform>(
                    "m_raw_ScrollThumb");
            if (thumb == null)
                return;
            RectTransform track =
                FindChildComponent<RectTransform>(
                    "m_img_ScrollTrack");
            if (track == null)
                throw new InvalidOperationException(
                    "JxqyInventoryUI prefab scroll track is missing.");
            _inventoryScroll =
                new JxqyLegacyVerticalScrollBinding(
                    track,
                    thumb,
                    rectTransform,
                    OnInventoryScrolled);
            _inventoryScroll.SetRange(
                Capacity / Columns - VisibleRows);
        }

        private void OnInventoryScrolled(int topRow)
        {
            _topRow = topRow;
            RefreshView();
        }

        private void Activate(int index)
        {
            IReadOnlyList<JxqyInventoryEntry> entries =
                Session?.Inventory?.Entries;
            if (entries == null || index < 0 || index >= entries.Count)
                return;
            if (entries[index].Definition.Kind ==
                JxqyItemKind.Equipment)
            {
                Session.EquipInventoryItem(index);
            }
            else
            {
                Session.UseInventoryItem(index);
            }
        }

        private void OnInventoryDrop(
            JxqyListSlotWidget.DragData source,
            JxqyListSlotWidget.DragData target)
        {
            if (source?.Kind ==
                    JxqyListSlotWidget.SlotKind.Equipment &&
                target?.Kind ==
                    JxqyListSlotWidget.SlotKind.Inventory &&
                JxqyEquipmentManager.TryGetSlotByLegacyListIndex(
                    source.Index,
                    out JxqyEquipmentSlot equipmentSlot))
            {
                Session?.ExchangeEquipmentWithInventory(
                    equipmentSlot,
                    target.Index);
                return;
            }
            if (source?.Kind ==
                    JxqyListSlotWidget.SlotKind.Inventory ||
                source?.Kind ==
                    JxqyListSlotWidget.SlotKind.GoodsShortcut)
            {
                if (target?.Kind !=
                    JxqyListSlotWidget.SlotKind.Inventory)
                {
                    return;
                }
                int sourceIndex = FindInventoryIndex(
                    Session?.Inventory?.Entries,
                    Session?.Inventory?.FindAtLegacyIndex(source.Index));
                if (sourceIndex < 0)
                    return;
                Session?.MoveInventoryEntryToLegacyIndex(
                    sourceIndex,
                    target.Index);
            }
        }

        private static void SetActive(
            Component component,
            bool active)
        {
            if (component != null)
                component.gameObject.SetActive(active);
        }

        private static int GetInventoryStoreCount(
            IReadOnlyList<JxqyInventoryEntry> entries)
        {
            if (entries == null)
                return 0;
            int count = 0;
            while (count < entries.Count &&
                   entries[count].LegacyListIndex <= 198)
            {
                count++;
            }
            return count;
        }

        private static int FindInventoryIndex(
            IReadOnlyList<JxqyInventoryEntry> entries,
            JxqyInventoryEntry target)
        {
            if (entries == null || target == null)
                return -1;
            for (int index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index], target))
                    return index;
            }
            return -1;
        }
    }

    internal sealed class JxqyLegacyDetailRequest
    {
        private JxqyLegacyDetailRequest(
            object value,
            bool isPreview,
            RectTransform anchor)
        {
            Value = value;
            IsPreview = isPreview;
            Anchor = anchor;
        }

        public object Value { get; }
        public bool IsPreview { get; }
        public RectTransform Anchor { get; }

        public static JxqyLegacyDetailRequest Preview(
            object value,
            RectTransform anchor = null)
        {
            return new JxqyLegacyDetailRequest(value, true, anchor);
        }
    }

    public abstract class JxqyLegacyDetailWindow : UIWindow
    {
        private JxqyLegacyTooltipBinding _detail;
        private Button _mask;
        private Image _maskImage;
        private JxqyPointerClickRelay _maskClickRelay;
        private Graphic[] _graphics;
        private bool[] _defaultRaycastTargets;

        protected JxqyLegacyTooltipBinding Detail => _detail;
        protected object DetailData { get; private set; }

        protected override void ScriptGenerator()
        {
            _detail = new JxqyLegacyTooltipBinding(transform);
            _mask = FindChildComponent<Button>("m_btn_Mask");
            _maskImage = _mask?.targetGraphic as Image ??
                         _mask?.GetComponent<Image>();
            _mask?.onClick.AddListener(CloseDetail);
            if (_mask != null)
            {
                _maskClickRelay =
                    _mask.GetComponent<JxqyPointerClickRelay>();
                if (_maskClickRelay == null)
                {
                    throw new InvalidOperationException(
                        $"{GetType().Name} requires static prefab " +
                        $"component {nameof(JxqyPointerClickRelay)}.");
                }
                _maskClickRelay.Clicked = ForwardMaskRightClick;
            }
            _graphics = gameObject.GetComponentsInChildren<Graphic>(true);
            _defaultRaycastTargets = new bool[_graphics.Length];
            for (int index = 0; index < _graphics.Length; index++)
                _defaultRaycastTargets[index] =
                    _graphics[index].raycastTarget;
        }

        protected override void OnCreate()
        {
            RefreshRequest();
        }

        protected override void OnRefresh()
        {
            RefreshRequest();
        }

        protected abstract void RefreshDetail();

        protected override void OnDestroy()
        {
            ClearCloseButton();
            _detail?.Dispose();
            _detail = null;
            _graphics = null;
            _defaultRaycastTargets = null;
            _maskImage = null;
            DetailData = null;
        }

        private void CloseDetail()
        {
            GameModule.UI.CloseUI(GetType());
        }

        private void ForwardMaskRightClick(PointerEventData eventData)
        {
            if (eventData == null ||
                eventData.button != PointerEventData.InputButton.Right ||
                EventSystem.current == null ||
                _graphics == null)
            {
                return;
            }

            var currentRaycastTargets = new bool[_graphics.Length];
            var results = new List<RaycastResult>();
            try
            {
                for (int index = 0; index < _graphics.Length; index++)
                {
                    Graphic graphic = _graphics[index];
                    if (graphic == null)
                        continue;
                    currentRaycastTargets[index] = graphic.raycastTarget;
                    graphic.raycastTarget = false;
                }
                EventSystem.current.RaycastAll(eventData, results);
            }
            finally
            {
                for (int index = 0; index < _graphics.Length; index++)
                {
                    if (_graphics[index] != null)
                    {
                        _graphics[index].raycastTarget =
                            currentRaycastTargets[index];
                    }
                }
            }

            foreach (RaycastResult result in results)
            {
                JxqyListSlotEventRelay slot =
                    result.gameObject.GetComponentInParent<
                        JxqyListSlotEventRelay>();
                if (slot == null)
                    continue;
                slot.OnPointerClick(eventData);
                CloseDetail();
                return;
            }
        }

        private void RefreshRequest()
        {
            var request = UserData as JxqyLegacyDetailRequest;
            bool isPreview = request?.IsPreview == true;
            DetailData = request?.Value ?? UserData;
            RefreshDetail();
            // Original item/magic descriptions are passive cards. A
            // full-screen modal mask both darkens the game incorrectly and
            // intercepts the drag that assigns skills to HUD shortcuts.
            SetInteractionEnabled(false);
            if (isPreview && request?.Anchor != null)
                _detail?.PlaceBeside(request.Anchor, rectTransform);
            else
                _detail?.RestorePlacement();
        }

        private void SetInteractionEnabled(bool enabled)
        {
            if (_graphics != null && _defaultRaycastTargets != null)
            {
                int count = Math.Min(
                    _graphics.Length,
                    _defaultRaycastTargets.Length);
                for (int index = 0; index < count; index++)
                {
                    if (_graphics[index] != null)
                    {
                        _graphics[index].raycastTarget =
                            enabled && _defaultRaycastTargets[index];
                    }
                }
            }
            if (_mask != null)
                _mask.interactable = enabled;
            if (_maskImage != null)
                _maskImage.color = Color.clear;
        }

        private void ClearCloseButton()
        {
            _mask?.onClick.RemoveListener(CloseDetail);
            if (_maskClickRelay != null)
                _maskClickRelay.Clicked = null;
            _maskClickRelay = null;
            _mask = null;
        }

    }

    [Window(
        UILayer.Tips,
        location: "jxqy/ui/prefabs/jxqyitemdetailui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyItemDetailUI : JxqyLegacyDetailWindow
    {
        protected override void RefreshDetail()
        {
            Detail?.ShowItem(DetailData as JxqyItemDefinition);
        }
    }

    [Window(
        UILayer.Tips,
        location: "jxqy/ui/prefabs/jxqymagicdetailui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyMagicDetailUI : JxqyLegacyDetailWindow
    {
        protected override void RefreshDetail()
        {
            Detail?.ShowMagic(DetailData as JxqySkillEntry);
        }
    }

    [Window(
        UILayer.UI,
        location: "jxqy/ui/prefabs/jxqyequipmentui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyEquipmentUI : JxqySessionWindow
    {
        private static readonly JxqyEquipmentSlot[] EquipmentSlots =
        {
            // m_item_Equipped1..7 follow the original UI_Settings.ini coordinates,
            // not the enum declaration order.
            JxqyEquipmentSlot.Head,
            JxqyEquipmentSlot.Neck,
            JxqyEquipmentSlot.Wrist,
            JxqyEquipmentSlot.Body,
            JxqyEquipmentSlot.Hand,
            JxqyEquipmentSlot.Foot,
            JxqyEquipmentSlot.Back,
        };

        private readonly List<JxqyListSlotWidget> _equipped = new();
        private readonly List<JxqyListSlotWidget> _sheetMagic = new();
        private readonly List<Button> _characterButtons = new();
        private readonly List<RawImage> _characterImages = new();
        private readonly List<JxqyUiFrameBinding> _characterBindings = new();
        private RawImage _equipmentPanel;
        private Texture _defaultEquipmentPanelTexture;
        private Rect _defaultEquipmentPanelUv;
        private JxqyUiAnimationBinding _partnerEquipmentPanelBinding;
        private string _partnerEquipmentPanelFile = string.Empty;
        private Text _sheetLevel;
        private Text _sheetExperience;
        private Text _sheetLevelUp;
        private Text _sheetLife;
        private Text _sheetThew;
        private Text _sheetMana;
        private Text _sheetAttack;
        private Text _sheetDefend;
        private Text _sheetEvade;
        private JxqyCharacter _displayedSheetOwner;
        private int _displayedSheetLevel;
        private int _displayedSheetExperience;
        private int _displayedSheetLevelUpExperience;
        private int _displayedSheetLife;
        private int _displayedSheetLifeMax;
        private int _displayedSheetThew;
        private int _displayedSheetThewMax;
        private int _displayedSheetMana;
        private int _displayedSheetManaMax;
        private int _displayedSheetAttack;
        private int _displayedSheetAttack2;
        private int _displayedSheetAttack3;
        private int _displayedSheetDefend;
        private int _displayedSheetDefend2;
        private int _displayedSheetDefend3;
        private int _displayedSheetEvade;
        private bool _displayedSheetManaLimit;
        private bool _hasDisplayedSheetStats;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_EquipmentPanel");
            _equipmentPanel =
                FindChildComponent<RawImage>("m_raw_EquipmentPanel");
            if (_equipmentPanel != null)
            {
                _defaultEquipmentPanelTexture = _equipmentPanel.texture;
                _defaultEquipmentPanelUv = _equipmentPanel.uvRect;
            }
            for (int index = 0;
                 index < JxqyOriginalCharacterCatalog.Count;
                 index++)
            {
                int profileIndex = index;
                string path = $"m_btn_EquipmentCharacter{index}";
                Button button = FindChildComponent<Button>(path);
                RawImage image = FindChildComponent<RawImage>(path);
                if (button == null || image == null)
                    continue;
                button.onClick.AddListener(
                    () => Session?.SelectEquipmentProfile(profileIndex));
                var binding = new JxqyUiFrameBinding(image);
                _characterButtons.Add(button);
                _characterImages.Add(image);
                _characterBindings.Add(binding);
            }
            for (int index = 0; index < EquipmentSlots.Length; index++)
            {
                JxqyListSlotWidget slot =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Equipped{index + 1}");
                if (slot != null)
                    _equipped.Add(slot);
            }
            for (int index = 1; index <= 12; index++)
            {
                JxqyListSlotWidget slot =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_EquipMagic{index}");
                if (slot != null)
                    _sheetMagic.Add(slot);
            }
            _sheetLevel = FindChildComponent<Text>(
                "m_text_EquipLevel");
            _sheetExperience = FindChildComponent<Text>(
                "m_text_EquipExperience");
            _sheetLevelUp = FindChildComponent<Text>(
                "m_text_EquipLevelUp");
            _sheetLife = FindChildComponent<Text>(
                "m_text_EquipLife");
            _sheetThew = FindChildComponent<Text>(
                "m_text_EquipThew");
            _sheetMana = FindChildComponent<Text>(
                "m_text_EquipMana");
            _sheetAttack = FindChildComponent<Text>(
                "m_text_EquipAttack");
            _sheetDefend = FindChildComponent<Text>(
                "m_text_EquipDefend");
            _sheetEvade = FindChildComponent<Text>(
                "m_text_EquipEvade");
        }

        protected override void RefreshView()
        {
            RefreshCharacterSelectors();
            RefreshPartnerEquipmentPanel();
            for (int index = 0; index < _equipped.Count; index++)
            {
                JxqyEquipmentSlot slot = EquipmentSlots[index];
                JxqyItemDefinition item = null;
                bool hasItem = Session?.ActiveEquipment != null &&
                    Session.ActiveEquipment.Equipped.TryGetValue(
                        slot,
                        out item);
                _equipped[index].Bind(
                    index,
                    string.Empty,
                    string.Empty,
                    false,
                    hasItem,
                    null,
                    Unequip,
                    iconCategory: "goods",
                    iconFileName:
                        hasItem ? item.ImageFileName : null,
                    dragData: new JxqyListSlotWidget.DragData(
                        JxqyListSlotWidget.SlotKind.Equipment,
                        JxqyEquipmentManager.GetLegacyListIndex(slot)),
                    dropped: OnEquipmentDrop,
                    soundRequested: RequestUiSound,
                    hoverExited: HideEquipmentPreview,
                    anchoredHovered: PreviewEquippedDetail);
            }
            RefreshSheetStats(force: true);
            RefreshSheetMagic();
        }

        protected override void OnDestroy()
        {
            foreach (JxqyUiFrameBinding binding in _characterBindings)
                binding.Dispose();
            _characterBindings.Clear();
            _partnerEquipmentPanelBinding?.Dispose();
            _partnerEquipmentPanelBinding = null;
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
            GameModule.UI.CloseUI<JxqyMagicDetailUI>();
        }

        protected override void OnUpdate()
        {
            _partnerEquipmentPanelBinding?.Tick(Time.unscaledDeltaTime);
            RefreshSheetStats(force: false);
        }

        private void RefreshPartnerEquipmentPanel()
        {
            if (_equipmentPanel == null)
                return;
            string file;
            if (Session?.PartnerEquipmentTarget != null)
            {
                file = Session.PartnerEquipmentTarget
                    .EquipmentBackgroundFileName ?? string.Empty;
            }
            else
            {
                int playerIndex = Session?.PlayerIndex ?? 0;
                file = playerIndex <= 0
                    ? string.Empty
                    : $"panel7{(char)('a' + playerIndex)}.asf";
            }
            if (string.IsNullOrWhiteSpace(file) ||
                !file.EndsWith(".asf", StringComparison.OrdinalIgnoreCase))
            {
                if (_partnerEquipmentPanelBinding != null)
                {
                    _partnerEquipmentPanelBinding.Dispose();
                    _partnerEquipmentPanelBinding = null;
                }
                _partnerEquipmentPanelFile = string.Empty;
                _equipmentPanel.texture = _defaultEquipmentPanelTexture;
                _equipmentPanel.uvRect = _defaultEquipmentPanelUv;
                _equipmentPanel.color = Color.white;
                return;
            }
            if (string.Equals(
                    _partnerEquipmentPanelFile,
                    file,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _partnerEquipmentPanelFile = file;
            string normalized = file.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            string category = "common";
            if (slash > 0)
            {
                int previousSlash = normalized.LastIndexOf('/', slash - 1);
                category = normalized.Substring(previousSlash + 1,
                    slash - previousSlash - 1);
            }
            string safeFileName = slash >= 0
                ? normalized.Substring(slash + 1)
                : normalized;
            if (!JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                    safeFileName,
                    out _,
                    category))
            {
                _partnerEquipmentPanelBinding?.Dispose();
                _partnerEquipmentPanelBinding = null;
                _partnerEquipmentPanelFile = string.Empty;
                _equipmentPanel.texture = _defaultEquipmentPanelTexture;
                _equipmentPanel.uvRect = _defaultEquipmentPanelUv;
                _equipmentPanel.color = Color.white;
                return;
            }
            _partnerEquipmentPanelBinding?.Dispose();
            _partnerEquipmentPanelBinding =
                new JxqyUiAnimationBinding(_equipmentPanel);
            _partnerEquipmentPanelBinding.Set(
                category,
                safeFileName);
        }

        private void Unequip(int index)
        {
            if (index >= 0 && index < EquipmentSlots.Length)
                Session?.Unequip(EquipmentSlots[index]);
        }

        private void PreviewEquippedDetail(
            int index,
            RectTransform anchor)
        {
            if (Session?.ActiveEquipment == null ||
                index < 0 ||
                index >= EquipmentSlots.Length ||
                !Session.ActiveEquipment.Equipped.TryGetValue(
                    EquipmentSlots[index],
                    out JxqyItemDefinition item))
            {
                return;
            }
            GameModule.UI.ShowUIAsync<JxqyItemDetailUI>(
                JxqyLegacyDetailRequest.Preview(item, anchor));
        }

        private void RefreshCharacterSelectors()
        {
            if (Session?.PartnerEquipmentTarget != null)
            {
                int targetProfile =
                    JxqyOriginalCharacterCatalog.GetProfileIndex(
                        Session.PartnerEquipmentTarget.Name);
                if (!Session.IsEquipmentProfileAvailable(targetProfile))
                {
                    Session.SelectEquipmentProfile(Session.PlayerIndex);
                    return;
                }
            }
            int selectedProfile = Session?.PartnerEquipmentTarget == null
                ? Session?.PlayerIndex ?? 0
                : JxqyOriginalCharacterCatalog.GetProfileIndex(
                    Session.PartnerEquipmentTarget.Name);
            for (int index = 0; index < _characterButtons.Count; index++)
            {
                bool available =
                    Session?.IsEquipmentProfileAvailable(index) == true;
                Button button = _characterButtons[index];
                button.gameObject.SetActive(true);
                button.interactable = available && index != selectedProfile;
                _characterImages[index].raycastTarget = available;
                int frameIndex = !available
                    ? 0
                    : index == selectedProfile ? 2 : 1;
                _characterBindings[index].Set(
                    "equip",
                    $"name{index + 1}.asf",
                    frameIndex);
            }
        }

        private static void HideEquipmentPreview()
        {
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
        }

        private void RefreshSheetStats(bool force)
        {
            JxqyCharacter owner = Session?.EquipmentOwner;
            if (owner == null || _sheetLevel == null)
                return;
            bool manaLimit = owner is JxqyPlayer player && player.ManaLimit;
            if (!force &&
                _hasDisplayedSheetStats &&
                ReferenceEquals(_displayedSheetOwner, owner) &&
                _displayedSheetLevel == owner.Level &&
                _displayedSheetExperience == owner.Experience &&
                _displayedSheetLevelUpExperience == owner.LevelUpExperience &&
                _displayedSheetLife == owner.Life &&
                _displayedSheetLifeMax == owner.LifeMax &&
                _displayedSheetThew == owner.Thew &&
                _displayedSheetThewMax == owner.ThewMax &&
                _displayedSheetMana == owner.Mana &&
                _displayedSheetManaMax == owner.ManaMax &&
                _displayedSheetAttack == owner.Attack &&
                _displayedSheetAttack2 == owner.Attack2 &&
                _displayedSheetAttack3 == owner.Attack3 &&
                _displayedSheetDefend == owner.Defend &&
                _displayedSheetDefend2 == owner.Defend2 &&
                _displayedSheetDefend3 == owner.Defend3 &&
                _displayedSheetEvade == owner.Evade &&
                _displayedSheetManaLimit == manaLimit)
            {
                return;
            }
            _displayedSheetOwner = owner;
            _displayedSheetLevel = owner.Level;
            _displayedSheetExperience = owner.Experience;
            _displayedSheetLevelUpExperience = owner.LevelUpExperience;
            _displayedSheetLife = owner.Life;
            _displayedSheetLifeMax = owner.LifeMax;
            _displayedSheetThew = owner.Thew;
            _displayedSheetThewMax = owner.ThewMax;
            _displayedSheetMana = owner.Mana;
            _displayedSheetManaMax = owner.ManaMax;
            _displayedSheetAttack = owner.Attack;
            _displayedSheetAttack2 = owner.Attack2;
            _displayedSheetAttack3 = owner.Attack3;
            _displayedSheetDefend = owner.Defend;
            _displayedSheetDefend2 = owner.Defend2;
            _displayedSheetDefend3 = owner.Defend3;
            _displayedSheetEvade = owner.Evade;
            _displayedSheetManaLimit = manaLimit;
            _hasDisplayedSheetStats = true;
            SetSheetText(_sheetLevel, owner.Level.ToString());
            SetSheetText(_sheetExperience, owner.Experience.ToString());
            SetSheetText(
                _sheetLevelUp,
                owner.LevelUpExperience.ToString());
            SetSheetText(
                _sheetLife,
                $"{owner.Life}/{owner.LifeMax}");
            SetSheetText(
                _sheetThew,
                $"{owner.Thew}/{owner.ThewMax}");
            SetSheetText(
                _sheetMana,
                manaLimit
                    ? "1/1"
                    : $"{owner.Mana}/{owner.ManaMax}");
            SetSheetText(
                _sheetAttack,
                FormatSheetCombatValue(
                    owner.Attack,
                    owner.Attack2,
                    owner.Attack3));
            SetSheetText(
                _sheetDefend,
                FormatSheetCombatValue(
                    owner.Defend,
                    owner.Defend2,
                    owner.Defend3));
            SetSheetText(_sheetEvade, owner.Evade.ToString());
        }

        private void RefreshSheetMagic()
        {
            IReadOnlyList<JxqySkillEntry> skills =
                Session?.ActiveSkills?.Skills;
            for (int index = 0; index < _sheetMagic.Count; index++)
            {
                int legacyIndex = index + 1;
                JxqySkillEntry entry =
                    Session?.ActiveSkills?.FindAtLegacyIndex(legacyIndex);
                int dataIndex = FindSheetSkillIndex(skills, entry);
                bool occupied = dataIndex >= 0;
                _sheetMagic[index].Bind(
                    dataIndex,
                    string.Empty,
                    string.Empty,
                    false,
                    occupied,
                    null,
                    null,
                    iconCategory: "magic",
                    iconFileName: occupied
                        ? entry.Magic.ImageFileName
                        : null,
                    dragData: new JxqyListSlotWidget.DragData(
                        JxqyListSlotWidget.SlotKind.Skill,
                        legacyIndex),
                    dropped: OnSheetMagicDrop,
                    soundRequested: RequestUiSound,
                    hoverExited: HideSheetMagicPreview,
                    anchoredHovered: PreviewSheetMagicDetail);
            }
        }

        private void PreviewSheetMagicDetail(
            int index,
            RectTransform anchor)
        {
            IReadOnlyList<JxqySkillEntry> skills =
                Session?.ActiveSkills?.Skills;
            if (skills == null || index < 0 || index >= skills.Count)
                return;
            GameModule.UI.ShowUIAsync<JxqyMagicDetailUI>(
                JxqyLegacyDetailRequest.Preview(
                    skills[index],
                    anchor));
        }

        private static void HideSheetMagicPreview()
        {
            GameModule.UI.CloseUI<JxqyMagicDetailUI>();
        }

        private void OnSheetMagicDrop(
            JxqyListSlotWidget.DragData source,
            JxqyListSlotWidget.DragData target)
        {
            if (source == null ||
                target?.Kind != JxqyListSlotWidget.SlotKind.Skill ||
                Session?.ActiveSkills == null)
            {
                return;
            }
            bool sourceIsSheetSkill =
                source.Kind == JxqyListSlotWidget.SlotKind.Skill;
            bool sourceIsPlayerShortcut =
                source.Kind == JxqyListSlotWidget.SlotKind.MagicShortcut ||
                source.Kind == JxqyListSlotWidget.SlotKind.Cultivation;
            if (!sourceIsSheetSkill &&
                !(Session.PartnerEquipmentTarget == null &&
                  sourceIsPlayerShortcut))
            {
                return;
            }
            IReadOnlyList<JxqySkillEntry> skills =
                Session.ActiveSkills.Skills;
            int sourceIndex = FindSheetSkillIndex(
                skills,
                Session.ActiveSkills.FindAtLegacyIndex(source.Index));
            if (sourceIndex < 0)
                return;
            Session.MoveActiveSkillEntryToLegacyIndex(
                sourceIndex,
                target.Index);
        }

        private static int FindSheetSkillIndex(
            IReadOnlyList<JxqySkillEntry> skills,
            JxqySkillEntry target)
        {
            if (skills == null || target == null)
                return -1;
            for (int index = 0; index < skills.Count; index++)
            {
                if (ReferenceEquals(skills[index], target))
                    return index;
            }
            return -1;
        }

        private static string FormatSheetCombatValue(
            int primary,
            int secondary,
            int tertiary)
        {
            return secondary == 0 && tertiary == 0
                ? primary.ToString()
                : $"{primary}({secondary})({tertiary})";
        }

        private static void SetSheetText(Text text, string value)
        {
            if (text != null)
                text.text = value ?? string.Empty;
        }

        private void OnEquipmentDrop(
            JxqyListSlotWidget.DragData source,
            JxqyListSlotWidget.DragData target)
        {
            if (source == null || target == null || Session == null)
                return;
            if (source.Kind ==
                    JxqyListSlotWidget.SlotKind.Inventory &&
                target.Kind ==
                    JxqyListSlotWidget.SlotKind.Equipment)
            {
                JxqyInventoryEntry entry =
                    Session.Inventory?.FindAtLegacyIndex(source.Index);
                if (entry == null ||
                    !JxqyEquipmentManager.TryGetSlotByLegacyListIndex(
                        target.Index,
                        out JxqyEquipmentSlot targetSlot) ||
                    entry.Definition.Slot !=
                    targetSlot)
                {
                    return;
                }
                Session.ExchangeEquipmentWithInventory(
                    targetSlot,
                    source.Index);
            }
            else if (source.Kind ==
                         JxqyListSlotWidget.SlotKind.Equipment &&
                     target.Kind ==
                         JxqyListSlotWidget.SlotKind.Inventory &&
                     JxqyEquipmentManager.TryGetSlotByLegacyListIndex(
                         source.Index,
                         out JxqyEquipmentSlot sourceSlot))
            {
                Session.ExchangeEquipmentWithInventory(
                    sourceSlot,
                    target.Index);
            }
            else if (source.Kind ==
                         JxqyListSlotWidget.SlotKind.Inventory &&
                     target.Kind ==
                         JxqyListSlotWidget.SlotKind.Inventory)
            {
                int sourceDataIndex = FindInventoryIndex(
                    Session.Inventory?.Entries,
                    Session.Inventory?.FindAtLegacyIndex(source.Index));
                if (sourceDataIndex < 0)
                    return;
                Session.MoveInventoryEntryToLegacyIndex(
                    sourceDataIndex,
                    target.Index);
            }
        }

        private static int GetInventoryStoreCount(
            IReadOnlyList<JxqyInventoryEntry> entries)
        {
            if (entries == null)
                return 0;
            int count = 0;
            while (count < entries.Count &&
                   entries[count].LegacyListIndex <= 198)
            {
                count++;
            }
            return count;
        }

        private static int FindInventoryIndex(
            IReadOnlyList<JxqyInventoryEntry> entries,
            JxqyInventoryEntry target)
        {
            if (entries == null || target == null)
                return -1;
            for (int index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index], target))
                    return index;
            }
            return -1;
        }
    }

    [Window(
        UILayer.UI,
        location: "jxqy/ui/prefabs/jxqytrainingui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyTrainingUI : JxqySessionWindow
    {
        private const int CultivationLegacyListIndex = 49;
        private JxqyListSlotWidget _cultivation;
        private Text _level;
        private Text _experience;
        private Text _name;
        private Text _introduction;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            _cultivation =
                CreateWidget<JxqyListSlotWidget>(
                    "m_item_Cultivation");
            _level = FindChildComponent<Text>("m_text_Level");
            _experience =
                FindChildComponent<Text>("m_text_Experience");
            _name = FindChildComponent<Text>("m_text_MagicName");
            _introduction =
                FindChildComponent<Text>("m_text_Introduction");
        }

        protected override void RefreshView()
        {
            JxqySkillEntry entry =
                Session?.Skills?.FindAtLegacyIndex(
                    CultivationLegacyListIndex);
            int dataIndex = FindSkillIndex(entry);
            bool occupied = entry != null && dataIndex >= 0;
            _cultivation?.Bind(
                dataIndex,
                string.Empty,
                string.Empty,
                false,
                occupied,
                null,
                null,
                iconCategory: "magic",
                iconFileName: occupied
                    ? entry.Magic.ImageFileName
                    : null,
                dragData: new JxqyListSlotWidget.DragData(
                    JxqyListSlotWidget.SlotKind.Cultivation,
                    CultivationLegacyListIndex),
                dropped: OnCultivationDrop,
                soundRequested: RequestUiSound);

            if (!occupied)
            {
                Set(_level, "1/10");
                Set(_experience, "0/0");
                Set(_name, string.Empty);
                Set(_introduction, string.Empty);
                return;
            }

            int threshold =
                entry.Magic.GetLevelUpExperience(entry.Level);
            Set(
                _level,
                $"{entry.Level}/{entry.Magic.MaximumLevel}");
            Set(
                _experience,
                $"{entry.Experience}/{Math.Max(0, threshold)}");
            Set(
                _name,
                string.IsNullOrWhiteSpace(entry.Magic.Name)
                    ? entry.Magic.Id
                    : entry.Magic.Name);
            Set(_introduction, entry.Magic.Introduction);
        }

        private void OnCultivationDrop(
            JxqyListSlotWidget.DragData source,
            JxqyListSlotWidget.DragData target)
        {
            if (source == null ||
                target?.Kind !=
                    JxqyListSlotWidget.SlotKind.Cultivation ||
                !IsSkillSource(source.Kind))
            {
                return;
            }
            int sourceIndex = FindSkillIndex(
                Session?.Skills?.FindAtLegacyIndex(source.Index));
            if (sourceIndex < 0)
                return;
            Session?.MoveSkillEntryToLegacyIndex(
                sourceIndex,
                CultivationLegacyListIndex);
        }

        private int FindSkillIndex(JxqySkillEntry target)
        {
            IReadOnlyList<JxqySkillEntry> skills =
                Session?.Skills?.Skills;
            if (skills == null || target == null)
                return -1;
            for (int index = 0; index < skills.Count; index++)
            {
                if (ReferenceEquals(skills[index], target))
                    return index;
            }
            return -1;
        }

        private static bool IsSkillSource(
            JxqyListSlotWidget.SlotKind kind)
        {
            return kind == JxqyListSlotWidget.SlotKind.Skill ||
                   kind ==
                       JxqyListSlotWidget.SlotKind.MagicShortcut ||
                   kind ==
                       JxqyListSlotWidget.SlotKind.Cultivation;
        }

        private static void Set(Text text, string value)
        {
            if (text != null)
                text.text = value ?? string.Empty;
        }
    }

    [Window(
        UILayer.UI,
        location: "jxqy/ui/prefabs/jxqyskillsui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqySkillsUI : JxqySessionWindow
    {
        private const int Capacity = 36;
        private const int Columns = 3;
        private const int VisibleSlotCount = 9;
        private const int VisibleRows = VisibleSlotCount / Columns;
        private readonly List<JxqyListSlotWidget> _slots = new();
        private Text _description;
        private Text _level;
        private Button _select;
        private JxqyLegacyVerticalScrollBinding _skillScroll;
        private int _topRow;
        private int _lastSelectedLegacyIndex = -1;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            BuildSkillScrollBar();
            _description = FindChildComponent<Text>("m_text_Description");
            _level = FindChildComponent<Text>("m_text_Level");
            _select = FindChildComponent<Button>("m_btn_Select");
            if (_select != null)
                _select.gameObject.SetActive(false);
            for (int index = 0; index < VisibleSlotCount; index++)
            {
                JxqyListSlotWidget slot =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Slot{index + 1}");
                if (slot != null)
                    _slots.Add(slot);
            }
        }

        protected override void RefreshView()
        {
            IReadOnlyList<JxqySkillEntry> skills =
                Session?.Skills?.Skills;
            int count = GetSkillStoreCount(skills);
            int selection = count == 0
                ? 0
                : Mathf.Clamp(Session.Selection, 0, count - 1);
            int selectedLegacyIndex = count == 0
                ? 1
                : skills[selection].LegacyListIndex;
            int maximumTopRow = Capacity / Columns - VisibleRows;
            int selectedRow =
                (Mathf.Clamp(selectedLegacyIndex, 1, Capacity) - 1) /
                Columns;
            if (selectedLegacyIndex != _lastSelectedLegacyIndex)
            {
                if (selectedRow < _topRow)
                    _topRow = selectedRow;
                else if (selectedRow >= _topRow + VisibleRows)
                    _topRow = selectedRow - VisibleRows + 1;
                _lastSelectedLegacyIndex = selectedLegacyIndex;
            }
            _topRow = Mathf.Clamp(_topRow, 0, maximumTopRow);
            _skillScroll?.SetRange(maximumTopRow);
            _skillScroll?.SetValue(_topRow, false);

            for (int index = 0; index < _slots.Count; index++)
            {
                int targetLegacyIndex =
                    _topRow * Columns + index + 1;
                JxqySkillEntry entry =
                    Session?.Skills?.FindAtLegacyIndex(
                        targetLegacyIndex);
                int dataIndex = FindSkillIndex(skills, entry);
                bool occupied = dataIndex >= 0 &&
                                targetLegacyIndex <= Capacity;
                _slots[index].Bind(
                    dataIndex,
                    occupied
                        ? string.IsNullOrWhiteSpace(entry.Magic.Name)
                            ? entry.Magic.Id
                            : entry.Magic.Name
                        : string.Empty,
                    occupied ? $"Lv.{entry.Level}" : string.Empty,
                    occupied && dataIndex == selection,
                    occupied,
                    Select,
                    AssignFirstShortcut,
                    iconCategory: "magic",
                    iconFileName: occupied
                        ? entry.Magic.ImageFileName
                        : null,
                    dragData: new JxqyListSlotWidget.DragData(
                        JxqyListSlotWidget.SlotKind.Skill,
                        targetLegacyIndex),
                    dropped: OnSkillDrop,
                    soundRequested: RequestUiSound,
                    hoverExited: HideSkillPreview,
                    anchoredHovered: PreviewSkill);
            }
            if (_description != null)
            {
                _description.text = count == 0
                    ? "（尚未习得武功）"
                    : $"伤害 {skills[selection].Magic.Effect}  " +
                      $"内力 {skills[selection].Magic.ManaCost}  " +
                      $"体力 {skills[selection].Magic.ThewCost}  " +
                      $"范围 {skills[selection].Magic.Range:0}";
            }
            if (_level != null)
            {
                if (count == 0)
                {
                    _level.text = string.Empty;
                }
                else
                {
                    JxqySkillEntry selected = skills[selection];
                    int threshold =
                        selected.Magic.GetLevelUpExperience(
                            selected.Level);
                    string experience = threshold <= 0
                        ? "已满"
                        : $"{selected.Experience}/{threshold}";
                    _level.text =
                        $"等级 {selected.Level}  经验 {experience}";
                }
            }
            if (_select != null)
                _select.interactable = count > 0;
        }

        protected override void OnDestroy()
        {
            ClearButton(_select);
            _skillScroll?.Dispose();
            _skillScroll = null;
            GameModule.UI.CloseUI<JxqyMagicDetailUI>();
        }

        private void BuildSkillScrollBar()
        {
            RectTransform track =
                FindChildComponent<RectTransform>(
                    "m_img_ScrollTrack");
            RectTransform thumb =
                FindChildComponent<RectTransform>(
                    "m_raw_ScrollThumb");
            if (track == null || thumb == null)
                return;
            _skillScroll = new JxqyLegacyVerticalScrollBinding(
                track,
                thumb,
                rectTransform,
                OnSkillScrolled);
            _skillScroll.SetRange(Capacity / Columns - VisibleRows);
        }

        private void OnSkillScrolled(int topRow)
        {
            _topRow = topRow;
            RefreshView();
        }

        private void Select(int index)
        {
            IReadOnlyList<JxqySkillEntry> skills =
                Session?.Skills?.Skills;
            if (skills == null || index < 0 || index >= skills.Count)
                return;
            Session.Select(index);
        }

        private void PreviewSkill(int index, RectTransform anchor)
        {
            IReadOnlyList<JxqySkillEntry> skills =
                Session?.Skills?.Skills;
            if (skills == null || index < 0 || index >= skills.Count)
                return;
            GameModule.UI.ShowUIAsync<JxqyMagicDetailUI>(
                JxqyLegacyDetailRequest.Preview(
                    skills[index],
                    anchor));
        }

        private static void HideSkillPreview()
        {
            GameModule.UI.CloseUI<JxqyMagicDetailUI>();
        }

        private void SelectCurrent()
        {
            if (Session?.SelectSkill(Session.Selection) == true)
                Session.Cancel();
        }

        private void AssignFirstShortcut(int index)
        {
            if (Session?.Skills == null)
                return;
            for (int shortcut = 40; shortcut <= 44; shortcut++)
            {
                if (Session.Skills.FindAtLegacyIndex(shortcut) != null)
                    continue;
                Session.MoveSkillEntryToLegacyIndex(index, shortcut);
                return;
            }
        }

        private void OnSkillDrop(
            JxqyListSlotWidget.DragData source,
            JxqyListSlotWidget.DragData target)
        {
            if (source?.Kind ==
                    JxqyListSlotWidget.SlotKind.Skill ||
                source?.Kind ==
                    JxqyListSlotWidget.SlotKind.MagicShortcut ||
                source?.Kind ==
                    JxqyListSlotWidget.SlotKind.Cultivation)
            {
                if (target?.Kind !=
                    JxqyListSlotWidget.SlotKind.Skill)
                {
                    return;
                }
                int sourceIndex = FindSkillIndex(
                    Session?.Skills?.Skills,
                    Session?.Skills?.FindAtLegacyIndex(source.Index));
                if (sourceIndex < 0)
                    return;
                Session?.MoveSkillEntryToLegacyIndex(
                    sourceIndex,
                    target.Index);
            }
        }

        private void Close()
        {
            Session?.Cancel();
        }

        private static int GetSkillStoreCount(
            IReadOnlyList<JxqySkillEntry> skills)
        {
            if (skills == null)
                return 0;
            int count = 0;
            while (count < skills.Count &&
                   skills[count].LegacyListIndex <= 36)
            {
                count++;
            }
            return count;
        }

        private static int GetHighestSkillLegacyIndex(
            IReadOnlyList<JxqySkillEntry> skills)
        {
            int maximumLegacyIndex = 0;
            if (skills != null)
            {
                for (int index = 0; index < skills.Count; index++)
                {
                    int legacyIndex = skills[index].LegacyListIndex;
                    if (legacyIndex > 0 && legacyIndex <= Capacity)
                    {
                        maximumLegacyIndex = Math.Max(
                            maximumLegacyIndex,
                            legacyIndex);
                    }
                }
            }
            return maximumLegacyIndex;
        }

        private static int FindSkillIndex(
            IReadOnlyList<JxqySkillEntry> skills,
            JxqySkillEntry target)
        {
            if (skills == null || target == null)
                return -1;
            for (int index = 0; index < skills.Count; index++)
            {
                if (ReferenceEquals(skills[index], target))
                    return index;
            }
            return -1;
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqytradeui.prefab",
        fullScreen: true,
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyTradeUI : JxqySessionWindow
    {
        private const int PageSize = 9;
        private const int Columns = 3;
        private const int VisibleRows = PageSize / Columns;
        private const int OriginalRowCount = 27;
        private readonly List<JxqyListSlotWidget> _shopSlots = new();
        private int _shopSelection;
        private int _topRow;
        private Button _buy;
        private Button _close;
        private JxqyLegacyVerticalScrollBinding _shopScroll;

        protected override void ScriptGenerator()
        {
            _buy = FindChildComponent<Button>("m_btn_Buy");
            _close = FindChildComponent<Button>("m_btn_Close");
            BindButtonSound(_close, JxqyUiSound.LargeButton);
            _buy?.onClick.AddListener(Buy);
            _close?.onClick.AddListener(Close);
            RemoveGoodsPanel();
            BuildShopScrollBar();
            for (int index = 0; index < PageSize; index++)
            {
                JxqyListSlotWidget shop =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Shop{index + 1}");
                if (shop != null)
                    _shopSlots.Add(shop);
            }
        }

        protected override void RefreshView()
        {
            var stock = Session?.Shop == null
                ? new List<JxqyShopStock>()
                : new List<JxqyShopStock>(Session.Shop.Stock);
            _shopSelection = stock.Count == 0
                ? 0
                : Mathf.Clamp(_shopSelection, 0, stock.Count - 1);
            int maximumTopRow = OriginalRowCount - VisibleRows;
            _topRow = Mathf.Clamp(_topRow, 0, maximumTopRow);
            _shopScroll?.SetRange(maximumTopRow);
            _shopScroll?.SetValue(_topRow, false);
            int pageStart = _topRow * Columns;

            for (int index = 0; index < _shopSlots.Count; index++)
            {
                int dataIndex = pageStart + index;
                bool visible = dataIndex < stock.Count;
                _shopSlots[index].gameObject.SetActive(visible);
                if (visible)
                {
                    JxqyShopStock item = stock[dataIndex];
                    // The legacy buy panel only writes the finite stock count
                    // in the slot corner. Price remains in the item tooltip;
                    // combining them as "price / count" changes the original
                    // meaning and is especially ambiguous for unlimited stock.
                    string count = item.IsUnlimited
                        ? string.Empty
                        : item.Count.ToString();
                    _shopSlots[index].Bind(
                        dataIndex,
                        item.Item.Name,
                        count,
                        dataIndex == _shopSelection,
                        true,
                        SelectShop,
                        BuyIndex,
                        iconCategory: "goods",
                        iconFileName: item.Item.ImageFileName,
                        soundRequested: RequestUiSound,
                        hoverExited: HideTradeItemPreview,
                        anchoredHovered: PreviewShopItem);
                }
            }
            if (_buy != null)
                _buy.interactable = stock.Count > 0;
        }

        protected override void OnDestroy()
        {
            ClearButton(_buy);
            ClearButton(_close);
            _shopScroll?.Dispose();
            _shopScroll = null;
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
        }

        private void BuildShopScrollBar()
        {
            RectTransform thumb = FindChildComponent<RectTransform>(
                "m_raw_ShopScrollThumb");
            if (thumb == null)
                return;
            RectTransform track = FindChildComponent<RectTransform>(
                "m_img_ShopScrollTrack");
            if (track == null)
                throw new InvalidOperationException(
                    "JxqyTradeUI prefab scroll track is missing.");
            _shopScroll = new JxqyLegacyVerticalScrollBinding(
                track,
                thumb,
                rectTransform,
                OnShopScrolled);
            _shopScroll.SetRange(OriginalRowCount - VisibleRows);
        }

        private void OnShopScrolled(int topRow)
        {
            _topRow = topRow;
            RefreshView();
        }

        private void SelectShop(int index)
        {
            _shopSelection = index;
            RefreshView();
        }

        private void PreviewShopItem(int index, RectTransform anchor)
        {
            if (Session?.Shop == null)
                return;
            var stock = new List<JxqyShopStock>(Session.Shop.Stock);
            if (index < 0 || index >= stock.Count)
                return;
            GameModule.UI.ShowUIAsync<JxqyItemDetailUI>(
                JxqyLegacyDetailRequest.Preview(
                    stock[index].Item,
                    anchor));
        }

        private static void HideTradeItemPreview()
        {
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
        }

        private void Buy()
        {
            Session?.BuyShopItem(_shopSelection);
        }

        private void BuyIndex(int index)
        {
            _shopSelection = index;
            Session?.BuyShopItem(index);
        }

        private void Close()
        {
            Session?.Close(JxqyUiScreen.Trade);
        }

        private void RemoveGoodsPanel()
        {
            string[] names =
            {
                "m_raw_InventoryPanel",
                "m_img_InventoryScrollTrack",
                "m_raw_InventoryScrollThumb",
                "m_btn_Sell",
                "m_text_Money",
            };
            foreach (string name in names)
            {
                Transform child = FindChild(name);
                if (child != null)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
            for (int index = 1; index <= PageSize; index++)
            {
                Transform child = FindChild($"m_item_Inventory{index}");
                if (child != null)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqytradegoodsui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyTradeGoodsUI : JxqySessionWindow
    {
        private const int PageSize = 9;
        private const int Capacity = 198;
        private const int Columns = 3;
        private const int VisibleRows = PageSize / Columns;
        private readonly List<JxqyListSlotWidget> _slots = new();
        private int _selection;
        private int _topRow;
        private Button _sell;
        private Text _money;
        private JxqyLegacyVerticalScrollBinding _inventoryScroll;

        protected override void ScriptGenerator()
        {
            _sell = FindChildComponent<Button>("m_btn_Sell");
            _money = FindChildComponent<Text>("m_text_Money");
            _sell?.onClick.AddListener(Sell);
            RemoveBuyPanel();
            BuildInventoryScrollBar();
            for (int index = 0; index < PageSize; index++)
            {
                JxqyListSlotWidget slot =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Inventory{index + 1}");
                if (slot != null)
                    _slots.Add(slot);
            }
        }

        protected override void RefreshView()
        {
            IReadOnlyList<JxqyInventoryEntry> inventory =
                Session?.Inventory?.Entries;
            int storeCount = GetStoreCount(inventory);
            _selection = storeCount == 0
                ? 0
                : Mathf.Clamp(_selection, 0, (inventory?.Count ?? 1) - 1);
            if (storeCount > 0 &&
                !IsStoreEntry(inventory[_selection]))
            {
                _selection = FindFirstStoreIndex(inventory);
            }
            int maximumTopRow = Capacity / Columns - VisibleRows;
            _topRow = Mathf.Clamp(_topRow, 0, maximumTopRow);
            _inventoryScroll?.SetRange(maximumTopRow);
            _inventoryScroll?.SetValue(_topRow, false);
            int pageStart = _topRow * Columns;
            for (int index = 0; index < _slots.Count; index++)
            {
                int targetLegacyIndex = pageStart + index + 1;
                JxqyInventoryEntry entry =
                    Session?.Inventory?.FindAtLegacyIndex(targetLegacyIndex);
                int dataIndex = FindInventoryIndex(inventory, entry);
                bool occupied = dataIndex >= 0 &&
                                targetLegacyIndex <= Capacity;
                _slots[index].gameObject.SetActive(true);
                _slots[index].Bind(
                    dataIndex,
                    occupied ? entry.Definition.Name : string.Empty,
                    occupied ? entry.Count.ToString() : string.Empty,
                    occupied && dataIndex == _selection,
                    occupied && Session.Shop.CanSellPlayerGoods,
                    Select,
                    SellIndex,
                    iconCategory: "goods",
                    iconFileName: occupied
                        ? entry.Definition.ImageFileName
                        : null,
                    soundRequested: RequestUiSound,
                    hoverExited: HidePreview,
                    anchoredHovered: Preview);
            }
            if (_money != null)
                // GoodsGui in the original draws only the current numeric
                // money value in this fixed-width field.
                _money.text = (Session?.Player?.Money ?? 0).ToString();
            if (_sell != null)
            {
                _sell.interactable = storeCount > 0 &&
                                     Session.Shop.CanSellPlayerGoods;
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_sell);
            _inventoryScroll?.Dispose();
            _inventoryScroll = null;
            HidePreview();
        }

        private void BuildInventoryScrollBar()
        {
            RectTransform thumb = FindChildComponent<RectTransform>(
                "m_raw_InventoryScrollThumb");
            if (thumb == null)
                return;
            RectTransform track = FindChildComponent<RectTransform>(
                "m_img_InventoryScrollTrack");
            if (track == null)
                throw new InvalidOperationException(
                    "JxqyTradeGoodsUI prefab scroll track is missing.");
            _inventoryScroll = new JxqyLegacyVerticalScrollBinding(
                track,
                thumb,
                rectTransform,
                OnInventoryScrolled);
            _inventoryScroll.SetRange(
                Capacity / Columns - VisibleRows);
        }

        private void OnInventoryScrolled(int topRow)
        {
            _topRow = topRow;
            RefreshView();
        }

        private static int GetStoreCount(
            IReadOnlyList<JxqyInventoryEntry> entries)
        {
            int count = 0;
            if (entries == null)
                return count;
            foreach (JxqyInventoryEntry entry in entries)
            {
                if (IsStoreEntry(entry))
                    count++;
            }
            return count;
        }

        private static bool IsStoreEntry(JxqyInventoryEntry entry)
        {
            return entry != null && entry.LegacyListIndex >= 1 &&
                   entry.LegacyListIndex <= Capacity;
        }

        private static int FindFirstStoreIndex(
            IReadOnlyList<JxqyInventoryEntry> entries)
        {
            if (entries == null)
                return 0;
            for (int index = 0; index < entries.Count; index++)
            {
                if (IsStoreEntry(entries[index]))
                    return index;
            }
            return 0;
        }

        private static int FindInventoryIndex(
            IReadOnlyList<JxqyInventoryEntry> entries,
            JxqyInventoryEntry target)
        {
            if (entries == null || target == null)
                return -1;
            for (int index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index], target))
                    return index;
            }
            return -1;
        }

        private void Select(int index)
        {
            _selection = index;
            RefreshView();
        }

        private void Preview(int index, RectTransform anchor)
        {
            IReadOnlyList<JxqyInventoryEntry> entries =
                Session?.Inventory?.Entries;
            if (entries == null || index < 0 || index >= entries.Count)
                return;
            GameModule.UI.ShowUIAsync<JxqyItemDetailUI>(
                JxqyLegacyDetailRequest.Preview(
                    entries[index].Definition,
                    anchor));
        }

        private void Sell()
        {
            Session?.SellInventoryItem(_selection);
        }

        private void SellIndex(int index)
        {
            _selection = index;
            Session?.SellInventoryItem(index);
        }

        private static void HidePreview()
        {
            GameModule.UI.CloseUI<JxqyItemDetailUI>();
        }

        private void RemoveBuyPanel()
        {
            string[] names =
            {
                "m_raw_ShopPanel",
                "m_img_ShopScrollTrack",
                "m_raw_ShopScrollThumb",
                "m_btn_Buy",
            };
            foreach (string name in names)
            {
                Transform child = FindChild(name);
                if (child != null)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
            for (int index = 1; index <= PageSize; index++)
            {
                Transform child = FindChild($"m_item_Shop{index}");
                if (child != null)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqymenuui.prefab",
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyMenuUI : JxqySessionWindow
    {
        private Button _saveLoad;
        private Button _option;
        private Button _quit;
        private Button _return;
        private Text _message;

        protected override JxqyUiSound? DefaultButtonSound =>
            JxqyUiSound.LargeButton;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            _saveLoad = FindChildComponent<Button>("m_btn_SaveLoad");
            _option = FindChildComponent<Button>("m_btn_Option");
            _quit = FindChildComponent<Button>("m_btn_Quit");
            _return = FindChildComponent<Button>("m_btn_Return");
            _message = FindChildComponent<Text>("m_text_Message");
            _saveLoad?.onClick.AddListener(
                () => Session?.OpenSaveLoad(JxqySaveUiAction.Load));
            _option?.onClick.AddListener(
                () => Session?.OpenOptions());
            _quit?.onClick.AddListener(() => Session?.ReturnToTitle());
            _return?.onClick.AddListener(() => Session?.Cancel());
            ConfigureMenuButton(_saveLoad);
            ConfigureMenuButton(_option);
            ConfigureMenuButton(_quit);
            ConfigureMenuButton(_return);
        }

        protected override void RefreshView()
        {
            if (_message != null)
            {
                _message.text = Session?.Notice ?? string.Empty;
                _message.gameObject.SetActive(
                    !string.IsNullOrEmpty(_message.text));
            }
        }

        protected override void OnDestroy()
        {
            ClearButton(_saveLoad);
            ClearButton(_option);
            ClearButton(_quit);
            ClearButton(_return);
        }

        private static void ConfigureMenuButton(Button button)
        {
            if (button == null)
                return;
            button.transition = Selectable.Transition.None;
            RawImage image = button.targetGraphic as RawImage ??
                             button.GetComponent<RawImage>();
            if (image == null)
                return;
            var relay = RequireStaticComponent<JxqyMenuButtonStateRelay>(
                button.gameObject,
                nameof(JxqyOptionsUI));
            relay.Configure(image);
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqyoptionsui.prefab",
        fullScreen: true,
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqyOptionsUI : JxqySessionWindow
    {
        private Slider _music;
        private Slider _sound;
        private Slider _speed;
        private Button _return;

        protected override JxqyUiSound? DefaultButtonSound =>
            JxqyUiSound.LargeButton;

        protected override void ScriptGenerator()
        {
            BlockBackdropInside("m_raw_Panel");
            _music = FindChildComponent<Slider>("m_slider_Music");
            _sound = FindChildComponent<Slider>("m_slider_Sound");
            _speed = FindChildComponent<Slider>("m_slider_Speed");
            _return = FindChildComponent<Button>("m_btn_Return");
            ConfigureSlider(_music, 0f, 1f, false, OnMusicChanged);
            ConfigureSlider(_sound, 0f, 1f, false, OnSoundChanged);
            ConfigureSlider(_speed, 0f, 2f, true, OnSpeedChanged);
            _return?.onClick.AddListener(() => Session?.Cancel());
            ConfigureMenuButton(_return);
        }

        protected override void RefreshView()
        {
            if (Session == null)
                return;
            _music?.SetValueWithoutNotify(Session.MusicVolume);
            _sound?.SetValueWithoutNotify(Session.SoundVolume);
            _speed?.SetValueWithoutNotify(Session.GameSpeed);
        }

        protected override void OnDestroy()
        {
            _music?.onValueChanged.RemoveAllListeners();
            _sound?.onValueChanged.RemoveAllListeners();
            _speed?.onValueChanged.RemoveAllListeners();
            ClearButton(_return);
        }

        private void OnMusicChanged(float value) =>
            Session?.SetMusicVolume(value);

        private void OnSoundChanged(float value) =>
            Session?.SetSoundVolume(value);

        private void OnSpeedChanged(float value) =>
            Session?.SetGameSpeed(Mathf.RoundToInt(value));

        private static void ConfigureSlider(
            Slider slider,
            float minimum,
            float maximum,
            bool wholeNumbers,
            UnityEngine.Events.UnityAction<float> listener)
        {
            if (slider == null)
                throw new InvalidOperationException(
                    "JxqyOptionsUI slider is missing.");
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = wholeNumbers;
            slider.onValueChanged.AddListener(listener);
        }

        private static void ConfigureMenuButton(Button button)
        {
            if (button == null)
                return;
            button.transition = Selectable.Transition.None;
            RawImage image = button.targetGraphic as RawImage ??
                             button.GetComponent<RawImage>();
            if (image == null)
                return;
            var relay = RequireStaticComponent<JxqyMenuButtonStateRelay>(
                button.gameObject,
                nameof(JxqyMenuUI));
            relay.Configure(image);
        }
    }

    [Window(
        UILayer.Top,
        location: "jxqy/ui/prefabs/jxqysaveloadui.prefab",
        fullScreen: true,
        packageName: "JxMod_XinJianXia")]
    public sealed class JxqySaveLoadUI : JxqySessionWindow
    {
        private readonly List<JxqyListSlotWidget> _slots = new();
        private RawImage _snapshot;
        private Texture2D _snapshotTexture;
        private Text _description;
        private Text _savedAt;
        private Text _message;
        private Button _load;
        private Button _save;
        private Button _exit;

        protected override JxqyUiSound? DefaultButtonSound =>
            JxqyUiSound.LargeButton;

        protected override void ScriptGenerator()
        {
            _snapshot =
                FindChildComponent<RawImage>("m_raw_Snapshot");
            _description = FindChildComponent<Text>("m_text_Description");
            _savedAt = FindChildComponent<Text>("m_text_SavedAt");
            _message = FindChildComponent<Text>("m_text_Message");
            _load = FindChildComponent<Button>("m_btn_Load");
            _save = FindChildComponent<Button>("m_btn_Save");
            _exit = FindChildComponent<Button>("m_btn_Exit");
            _load?.onClick.AddListener(Load);
            _save?.onClick.AddListener(Save);
            _exit?.onClick.AddListener(Close);
            for (int index = 0; index < 7; index++)
            {
                JxqyListSlotWidget slot =
                    CreateWidget<JxqyListSlotWidget>(
                        $"m_item_Slot{index + 1}");
                if (slot != null)
                    _slots.Add(slot);
            }
        }

        protected override void RefreshView()
        {
            IReadOnlyList<JxqySaveSlotView> slots = Session?.SaveSlots;
            int count = slots?.Count ?? 0;
            int selection = count == 0
                ? 0
                : Mathf.Clamp(Session.Selection, 0, count - 1);
            for (int index = 0; index < _slots.Count; index++)
            {
                bool visible = index < count;
                _slots[index].gameObject.SetActive(visible);
                if (!visible)
                    continue;
                _slots[index].Bind(
                    index,
                    $"进度{ToChinese(index + 1)}",
                    slots[index].Exists ? "有存档" : "空",
                    index == selection,
                    true,
                    Select,
                    soundRequested: RequestUiSound);
            }
            JxqySaveSlotView selected = count == 0
                ? null
                : slots[selection];
            RefreshSnapshot(selected?.SnapshotPng);
            if (_description != null)
                _description.text = selected?.Description ?? "空存档";
            if (_savedAt != null)
                _savedAt.text = selected?.SavedAt ?? string.Empty;
            if (_message != null)
            {
                _message.text = !string.IsNullOrWhiteSpace(
                    Session?.Notice)
                    ? Session.Notice
                    : Session?.SaveAction == JxqySaveUiAction.Save
                        ? "请选择进度并保存"
                        : "请选择已有进度读取";
            }
            if (_load != null)
                _load.interactable = selected?.Exists == true;
            if (_save != null)
                _save.interactable =
                    count > 0 && Session?.IsSaveAllowed == true;
        }

        protected override void OnDestroy()
        {
            ReleaseSnapshot();
            ClearButton(_load);
            ClearButton(_save);
            ClearButton(_exit);
        }

        private void Select(int index)
        {
            Session?.Select(index);
        }

        private void Load()
        {
            Session?.RequestLoad(Session.Selection);
        }

        private void Save()
        {
            Session?.RequestSave(Session.Selection);
        }

        private void Close()
        {
            Session?.Cancel();
        }

        private void RefreshSnapshot(byte[] pngBytes)
        {
            ReleaseSnapshot();
            if (_snapshot == null ||
                pngBytes == null ||
                pngBytes.Length == 0)
            {
                if (_snapshot != null)
                    _snapshot.texture = null;
                return;
            }

            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGB24,
                false)
            {
                name = "JxqySaveSnapshot",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (!ImageConversion.LoadImage(texture, pngBytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                return;
            }
            _snapshotTexture = texture;
            _snapshot.texture = texture;
            _snapshot.color = Color.white;
        }

        private void ReleaseSnapshot()
        {
            if (_snapshot != null &&
                _snapshot.texture == _snapshotTexture)
                _snapshot.texture = null;
            if (_snapshotTexture != null)
                UnityEngine.Object.Destroy(_snapshotTexture);
            _snapshotTexture = null;
        }

        private static string ToChinese(int value)
        {
            string[] values =
            {
                "零", "一", "二", "三", "四", "五", "六", "七",
            };
            return value >= 0 && value < values.Length
                ? values[value]
                : value.ToString();
        }
    }

    public sealed class JxqyUiRouter : IDisposable
    {
        private readonly JxqyUiSession _session;
        private bool _sharedBackdropShown;
        private JxqyUiScreen? _shownModal;
        private JxqyUiScreen? _shownLeftPanel;
        private JxqyUiScreen? _shownRightPanel;
        private bool _fadeShown;
        private bool _gameplayChromeShown;
        private int _noticeSequence = -1;

        public JxqyUiRouter(JxqyUiSession session)
        {
            _session = session ??
                       throw new ArgumentNullException(nameof(session));
        }

        public void Start()
        {
            _session.Changed += Synchronize;
            GameModule.UI.ShowUIAsync<JxqyMessageUI>(_session);
            GameModule.UI.ShowUIAsync<JxqySystemMessageUI>(_session);
            Synchronize();
        }

        public void Dispose()
        {
            _session.Changed -= Synchronize;
            if (_sharedBackdropShown)
            {
                _sharedBackdropShown = false;
                GameModule.UI.CloseUI<JxqySharedBackdropUI>();
            }
            CloseModal(_shownModal);
            CloseModal(_shownLeftPanel);
            CloseModal(_shownRightPanel);
            GameModule.UI.CloseUI<JxqyNoticeUI>();
            GameModule.UI.CloseUI<JxqyFadeUI>();
            GameModule.UI.CloseUI<JxqySystemMessageUI>();
            GameModule.UI.CloseUI<JxqyMessageUI>();
            GameModule.UI.CloseUI<JxqyTimerUI>();
            GameModule.UI.CloseUI<JxqyTargetLifeUI>();
            GameModule.UI.CloseUI<JxqyPartnerHeadsUI>();
            GameModule.UI.CloseUI<JxqyHudUI>();
            _gameplayChromeShown = false;
        }

        private void Synchronize()
        {
            SynchronizeGameplayChrome();
            GameEvent.Get<IJxqyUI>().OnJxqyUiChanged();
            if (_session.FadeVisible && !_fadeShown)
            {
                _fadeShown = true;
                GameModule.UI.ShowUIAsync<JxqyFadeUI>(_session);
                // A parallel script can start a fade while dialogue is
                // already waiting for input. Re-push the interaction window
                // so System-layer insertion order cannot put the fade above
                // it. Sequential FadeOut -> Say is already ordered correctly.
                if (_shownModal == JxqyUiScreen.Dialogue)
                    GameModule.UI.ShowUIAsync<JxqyDialogueUI>(_session);
                else if (_shownModal == JxqyUiScreen.Selection)
                    GameModule.UI.ShowUIAsync<JxqySelectionUI>(_session);
            }
            else if (!_session.FadeVisible && _fadeShown)
            {
                _fadeShown = false;
                GameModule.UI.CloseUI<JxqyFadeUI>();
            }
            if (_session.NoticeSequence != _noticeSequence)
            {
                _noticeSequence = _session.NoticeSequence;
                if (string.IsNullOrWhiteSpace(_session.Notice))
                    GameModule.UI.CloseUI<JxqyNoticeUI>();
                else
                    GameModule.UI.ShowUIAsync<JxqyNoticeUI>(_session);
            }
            JxqyUiScreen? desiredModal =
                _session.ActiveModalScreen;
            SynchronizeSharedBackdrop(
                _session.SharedBackdropScreen.HasValue);
            bool modalVisible = desiredModal.HasValue;
            SynchronizeWindow(
                ref _shownLeftPanel,
                modalVisible ? null : _session.LeftPanelScreen);
            SynchronizeWindow(
                ref _shownRightPanel,
                modalVisible ? null : _session.RightPanelScreen);
            SynchronizeWindow(ref _shownModal, desiredModal);
        }

        private void SynchronizeGameplayChrome()
        {
            if (_gameplayChromeShown == _session.InterfaceVisible)
                return;
            _gameplayChromeShown = _session.InterfaceVisible;
            if (_gameplayChromeShown)
            {
                GameModule.UI.ShowUIAsync<JxqyHudUI>(_session);
                GameModule.UI.ShowUIAsync<JxqyPartnerHeadsUI>(_session);
                GameModule.UI.ShowUIAsync<JxqyTargetLifeUI>(_session);
                GameModule.UI.ShowUIAsync<JxqyTimerUI>(_session);
            }
            else
            {
                GameModule.UI.CloseUI<JxqyTimerUI>();
                GameModule.UI.CloseUI<JxqyTargetLifeUI>();
                GameModule.UI.CloseUI<JxqyPartnerHeadsUI>();
                GameModule.UI.CloseUI<JxqyHudUI>();
            }
        }

        private void SynchronizeSharedBackdrop(bool shouldShow)
        {
            if (_sharedBackdropShown == shouldShow)
                return;
            _sharedBackdropShown = shouldShow;
            if (shouldShow)
            {
                GameModule.UI.ShowUIAsync<JxqySharedBackdropUI>(_session);
            }
            else
            {
                GameModule.UI.CloseUI<JxqySharedBackdropUI>();
            }
        }

        private void SynchronizeWindow(
            ref JxqyUiScreen? shown,
            JxqyUiScreen? desired)
        {
            if (shown == desired)
                return;
            CloseModal(shown);
            shown = desired;
            if (desired.HasValue)
                ShowModal(desired.Value);
        }

        private void ShowModal(JxqyUiScreen screen)
        {
            switch (screen)
            {
                case JxqyUiScreen.Title:
                    GameModule.UI.ShowUIAsync<JxqyTitleUI>(_session);
                    break;
                case JxqyUiScreen.Dialogue:
                    GameModule.UI.ShowUIAsync<JxqyDialogueUI>(_session);
                    break;
                case JxqyUiScreen.Selection:
                    GameModule.UI.ShowUIAsync<JxqySelectionUI>(_session);
                    break;
                case JxqyUiScreen.Status:
                    GameModule.UI.ShowUIAsync<JxqyStatusUI>(_session);
                    break;
                case JxqyUiScreen.Inventory:
                    GameModule.UI.ShowUIAsync<JxqyInventoryUI>(_session);
                    break;
                case JxqyUiScreen.Equipment:
                    GameModule.UI.ShowUIAsync<JxqyEquipmentUI>(_session);
                    break;
                case JxqyUiScreen.Training:
                    GameModule.UI.ShowUIAsync<JxqyTrainingUI>(_session);
                    break;
                case JxqyUiScreen.Skills:
                    GameModule.UI.ShowUIAsync<JxqySkillsUI>(_session);
                    break;
                case JxqyUiScreen.Memo:
                    GameModule.UI.ShowUIAsync<JxqyMemoUI>(_session);
                    break;
                case JxqyUiScreen.Trade:
                    GameModule.UI.ShowUIAsync<JxqyTradeUI>(_session);
                    GameModule.UI.ShowUIAsync<JxqyTradeGoodsUI>(_session);
                    break;
                case JxqyUiScreen.Menu:
                    GameModule.UI.ShowUIAsync<JxqyMenuUI>(_session);
                    break;
                case JxqyUiScreen.Options:
                    GameModule.UI.ShowUIAsync<JxqyOptionsUI>(_session);
                    break;
                case JxqyUiScreen.SaveLoad:
                    GameModule.UI.ShowUIAsync<JxqySaveLoadUI>(_session);
                    break;
                case JxqyUiScreen.LittleMap:
                    GameModule.UI.ShowUIAsync<JxqyLittleMapUI>(_session);
                    break;
                case JxqyUiScreen.Gamble:
                    GameModule.UI.ShowUIAsync<JxqyGambleUI>(_session);
                    break;
            }
        }

        private static void CloseModal(JxqyUiScreen? screen)
        {
            if (!screen.HasValue)
                return;
            switch (screen.Value)
            {
                case JxqyUiScreen.Title:
                    GameModule.UI.CloseUI<JxqyTitleUI>();
                    break;
                case JxqyUiScreen.Dialogue:
                    GameModule.UI.CloseUI<JxqyDialogueUI>();
                    break;
                case JxqyUiScreen.Selection:
                    GameModule.UI.CloseUI<JxqySelectionUI>();
                    break;
                case JxqyUiScreen.Status:
                    GameModule.UI.CloseUI<JxqyStatusUI>();
                    break;
                case JxqyUiScreen.Inventory:
                    GameModule.UI.CloseUI<JxqyInventoryUI>();
                    break;
                case JxqyUiScreen.Equipment:
                    GameModule.UI.CloseUI<JxqyEquipmentUI>();
                    break;
                case JxqyUiScreen.Training:
                    GameModule.UI.CloseUI<JxqyTrainingUI>();
                    break;
                case JxqyUiScreen.Skills:
                    GameModule.UI.CloseUI<JxqySkillsUI>();
                    break;
                case JxqyUiScreen.Memo:
                    GameModule.UI.CloseUI<JxqyMemoUI>();
                    break;
                case JxqyUiScreen.Trade:
                    GameModule.UI.CloseUI<JxqyTradeGoodsUI>();
                    GameModule.UI.CloseUI<JxqyTradeUI>();
                    break;
                case JxqyUiScreen.Menu:
                    GameModule.UI.CloseUI<JxqyMenuUI>();
                    break;
                case JxqyUiScreen.Options:
                    GameModule.UI.CloseUI<JxqyOptionsUI>();
                    break;
                case JxqyUiScreen.SaveLoad:
                    GameModule.UI.CloseUI<JxqySaveLoadUI>();
                    break;
                case JxqyUiScreen.LittleMap:
                    GameModule.UI.CloseUI<JxqyLittleMapUI>();
                    break;
                case JxqyUiScreen.Gamble:
                    GameModule.UI.CloseUI<JxqyGambleUI>();
                    break;
            }
        }
    }
}
