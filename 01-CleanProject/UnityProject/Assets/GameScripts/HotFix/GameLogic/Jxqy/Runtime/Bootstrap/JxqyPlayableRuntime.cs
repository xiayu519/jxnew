using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Jxqy.Domain.Animation;
using Jxqy.Domain.Content;
using Jxqy.Domain.Input;
using Jxqy.Domain.Persistence;
using Jxqy.Domain.Presentation;
using Jxqy.Domain.Simulation;
using Jxqy.Domain.Scripting;
using Jxqy.Domain.World;
using Jxqy.Ports;
using Jxqy.UnityAdapters;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Jxqy.Bootstrap
{
    internal sealed class JxqyRuntimeActorVisual
    {
        public JxqyWorldVisual Visual;
        public JxqyAnimationMetadata Stand;
        public JxqyAnimationMetadata Walk;
        public readonly Dictionary<int, JxqyAnimationMetadata> Actions =
            new();
        public readonly Dictionary<int, string> ActionFileNames = new();
        public readonly HashSet<int> LoadingActions = new();
        public readonly Dictionary<int, string> StateSounds = new();
        public JxqyAnimationMetadata Current;
        public JxqyCharacterState CurrentState =
            (JxqyCharacterState)(-1);
        public int CurrentStateVersion = -1;
        public JxqyAnimationPlayer SpecialAction;
        public int OffsetX;
        public int OffsetY;
        public bool ObjectOpenState;
        public bool ObjectTransition;
        public bool SpecialActionOnly;
        public string ActiveStateSoundId = string.Empty;
    }

    internal sealed class JxqyRuntimeMagicAssets
    {
        public JxqyAnimationMetadata Flying;
        public JxqyAnimationMetadata Vanish;
        public JxqyAnimationMetadata SuperMode;
    }

    internal sealed class JxqyLoadedMapResource
    {
        public JxqyPreloadResource Resource;
        public JxqyAssetLease<TextAsset> TextLease;
        public JxqyAssetLease<Texture2D> TextureLease;
    }

    internal sealed class JxqyRuntimeMagicVisual
    {
        public JxqyWorldVisual Visual;
        public JxqyMagicDefinition Magic;
        public JxqyMagicProjectile Projectile;
        public JxqyCharacter FollowTarget;
        public bool IsVanish;
        public float RemainingSeconds;
    }

    internal sealed class JxqyPendingMagicCast
    {
        public int SkillIndex;
        public JxqySkillEntry Skill;
        public JxqyFloat2 Destination;
        public JxqyNpc Target;
    }

    internal sealed class JxqyPendingBasicAttack
    {
        public JxqyCharacter Target;
        public JxqyMagicDefinition Magic;
        public JxqyFloat2 Destination;
        public int MaximumTileDistance;
    }

    /// <summary>
    /// Live Unity composition root for the migrated game. Unlike the validation
    /// models, this component loads the packaged assets and submits draw commands
    /// every Play Mode frame.
    /// </summary>
    internal sealed partial class JxqyPlayableRuntime : MonoBehaviour
    {
        private static readonly ProfilerMarker ScriptTickMarker =
            new("Jxqy.ScriptTick");
        private static readonly ProfilerMarker NpcTickMarker =
            new("Jxqy.NpcTick");
        private static readonly ProfilerMarker CombatTickMarker =
            new("Jxqy.CombatTick");
        private static readonly ProfilerMarker ActorVisualTickMarker =
            new("Jxqy.ActorVisualTick");
        private static readonly ProfilerMarker FrameBuildMarker =
            new("Jxqy.FrameBuild");
        private static readonly ProfilerMarker FrameSubmitMarker =
            new("Jxqy.FrameSubmit");
        private int _logicalWidth = JxqyLogicalViewport.OriginalWidth;
        private int _logicalHeight = JxqyLogicalViewport.OriginalHeight;
        private int LogicalWidth => _logicalWidth;
        private int LogicalHeight => _logicalHeight;
        private const int LegacyMagicBaseSpeed = 100;
        private const int OriginalMobileAutoTargetRadius = 15;
        private const string MusicVolumePreference =
            "Jxqy.Options.MusicVolume";
        private const string SoundVolumePreference =
            "Jxqy.Options.SoundVolume";
        private const string GameSpeedPreference =
            "Jxqy.Options.GameSpeed";
        private float _gameSpeedScale = 1f;
        private readonly List<IDisposable> _leases = new();
        private readonly List<IDisposable> _activeMapLeases = new();
        private readonly List<string> _activeMapTextureAddresses = new();
        private readonly List<string> _activeMapAnimationStableIds = new();
        private readonly List<JxqyDrawCommand> _frameCommands = new(512);
        private readonly List<JxqyDrawCommand> _actorCommands = new(16);
        private readonly List<int> _characterOcclusionRows = new(4);
        private readonly List<JxqyDrawCommand> _presentationCommands =
            new(128);
        private readonly List<JxqyWeatherParticle> _weatherParticles =
            new(128);
        private readonly List<Texture2D> _ownedTextures = new(6);
        private readonly List<GameObject> _activeSceneRoots = new(16);
        private readonly List<Camera> _activeSceneCameras = new(4);
        private readonly List<Camera> _rootCameras = new(2);
        private readonly List<Tilemap> _mapTilemaps = new(8);
        private readonly List<Tilemap> _rootTilemaps = new(4);
        private readonly List<JxqyWorldVisual> _frameVisuals = new(64);
        private readonly Dictionary<JxqyNpc, JxqyRuntimeActorVisual>
            _npcVisuals = new();
        private readonly Dictionary<JxqyWorldObject, JxqyRuntimeActorVisual>
            _objectVisuals = new();
        private readonly Dictionary<string, JxqyAnimationMetadata> _animations =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JxqyAnimationMetadata>
            _dynamicAnimationCache =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _dynamicTextCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AsyncLazy<string>>
            _dynamicTextLoadCache =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AsyncLazy<JxqyAnimationMetadata>>
            _dynamicAnimationLoadTasks =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AsyncLazy<Texture2D>>
            _dynamicTextureLoadTasks =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JxqyItemDefinition>
            _itemDefinitionCache =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JxqyRuntimeMagicAssets>
            _magicVisualAssets =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly List<JxqyRuntimeMagicVisual> _magicVisuals =
            new(16);
        private readonly Dictionary<
                JxqyMagicProjectile,
                JxqyRuntimeMagicVisual>
            _projectileVisuals =
                new();
        private readonly Dictionary<int, JxqyAnimationPlayer>
            _playerScriptActions = new();
        private readonly Dictionary<int, JxqyAnimationPlayer>
            _playerStateActions = new();
        private readonly Dictionary<int, string> _playerStateSounds = new();
        private readonly Dictionary<JxqyStatusKind, JxqyAnimationMetadata>
            _statusDeathAnimations = new();
        private readonly Dictionary<JxqyStatusKind, JxqyAnimationPlayer>
            _playerStatusDeathPlayers = new();
        private readonly Dictionary<string, Material> _renderMaterials =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _talkTexts = new();
        private readonly List<JxqyLegacyTalkLine> _talkLines = new();
        private readonly List<string> _memoEntries = new();
        private readonly List<string> _rainThunderSoundFileNames = new();
        private readonly Dictionary<string, string> _mapDisplayNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<JxqyUiSound, string> _uiSoundFileNames =
            new();
        private readonly Dictionary<int, JxqyLevelEntry> _levelEntries =
            new();
        private readonly Dictionary<int, JxqyLevelEntry> _npcLevelEntries =
            new();
        private readonly Dictionary<int, string>
            _originalCharacterProfileIniTexts = new();
        private readonly Dictionary<int, Dictionary<int, JxqyLevelEntry>>
            _originalCharacterLevelEntries = new();
        private readonly Dictionary<string, JxqyMagicDefinition>
            _levelRewardMagics =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JxqyItemDefinition>
            _levelRewardItems =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<JxqyNpc>>
            _savedNpcSnapshots =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<JxqyWorldObject>>
            _savedObjectSnapshots =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, JxqySavePlayerProfileState>
            _playerProfiles = new();
        private readonly Dictionary<int, JxqySkillManager>
            _originalCharacterSkills = new();
        private readonly Dictionary<JxqyNpc, JxqyAutoAttackController>
            _npcAutoAttacks = new();
        private readonly Dictionary<JxqyNpc, float>
            _npcKeepAttackCooldowns = new();
        private readonly Dictionary<JxqyWorldObject, float>
            _objectTimerElapsedMilliseconds = new();
        private readonly List<JxqyWorldObject> _expiredWorldObjects = new();
        private readonly Dictionary<JxqyCharacter, JxqyPendingBasicAttack>
            _pendingBasicAttacks = new();
        private readonly Dictionary<JxqyCharacter, float>
            _transientCombatStates = new();
        private readonly List<JxqyCharacter> _combatCollisionTargets =
            new(32);
        private readonly HashSet<JxqyNpc> _processedNpcDeaths = new();
        private readonly HashSet<JxqyNpc> _finalizedNpcDeaths = new();
        private readonly List<JxqyNpc> _npcDeathsReadyToFinalize = new();
        private readonly JxqyDeterministicRandom _legacyRandom =
            new(20260727);
        private string _playerActiveStateSoundId = string.Empty;
        private string _initialPlayerIniText = string.Empty;
        private string _playerResourceFileName = string.Empty;

        private IJxqyInputPort _input;
        private JxqyMapPreloadCoordinator _mapCoordinator;
        private JxqyPreloadManifest _preloadManifest;
        private JxqyYooAssetResourcePort _resources;
        private JxqyRuntimeContentContext _contentContext;
        private JxqyResourceScope _mapScope;
        private JxqyResourceScope _activeMapAssetScope;
        private JxqyTextureRegistry _textures;
        private JxqySpriteRendererWorldRenderer _renderer;
        private JxqySafeAreaViewport _safeAreaViewport;
        private JxqyWaterRefractionEffect _waterRefractionEffect;
        private Camera _worldCamera;
        private Texture2D _waterDisplacementTexture;
        private JxqyWorldDrawCommandBuilder _worldCommands;
        private JxqyMapDrawCommandBuilder _mapCommands;
        private JxqyMapMetadata _mapMetadata;
        private JxqyRuntimeMapData _map;
        private JxqyPlayer _player;
        private JxqyObjectManager _objects;
        private JxqyNpcManager _npcs;
        private bool _npcAiDisabled;
        private JxqyAnimationPlayer _playerStand;
        private JxqyAnimationPlayer _playerWalk;
        private JxqyAnimationPlayer _playerRun;
        private JxqyWorldVisual _playerVisual;
        private IJxqyAudioPort _audio;
        private IJxqyVideoPort _video;
        private JxqyPresentationEffects _presentationEffects;
        private JxqyPresentationDrawCommandBuilder _presentationBuilder;
        private Scene _boundMapScene;
        private JxqyInventory _inventory;
        private JxqyEquipmentManager _equipment;
        private JxqySkillManager _skills;
        private JxqyShop _shop;
        private JxqyCombatSystem _combat;
        private JxqyAutoAttackController _playerAutoAttack;
        private bool _playerAutoAttackRunRequested;
        private JxqyUiSession _uiSession;
        private JxqyCombatFloatTextPool _combatFloatTextPool;
        private bool _fadeOutPendingUiReady;
        private bool _fadeInPendingUiReady;
        private bool _closeFadeUiWhenTransparent;
        private JxqySaveRepository _saveRepository;
        private JxqyPlayableScriptSession _scriptSession;
        private JxqyTrapRegistry _trapRegistry = new();
        private JxqyTrapRegistry _savedTrapRegistry = new();
        private JxqyAnimationPlayer _playerSpecialAction;
        private JxqyPendingMagicCast _pendingPlayerMagicCast;
        private JxqyPendingMagicCast _queuedPlayerMagicCast;
        private bool _manualPlayerMovementActive;
        private JxqyCharacterState _playerVisualState =
            (JxqyCharacterState)(-1);
        private int _playerVisualStateVersion = -1;
        private JxqyIntRect _camera;
        private JxqyFloat2 _lastCameraPlayerPosition;
        private JxqyCharacter _lastCameraPlayerCharacter;
        private bool _cameraPlayerTracked;
        private string _levelFileName = string.Empty;
        private string _backgroundMusicAddress = string.Empty;
        private string _activeNpcFileName = string.Empty;
        private string _activeObjectFileName = string.Empty;
        private int _playerIndex;
        private int _playerNpcIniIndex = 1;
        private int _lastMenuMove;
        private int _inputIntentCoverageMask;
        private bool _legacyKeyboardMovementThisFrame;
        private bool _legacyInputDisabled;
        private bool _saveDisabled;
        private bool _dropGoodWhenDefeatEnemyDisabled;
        private bool _showMapPosition;
        private bool _timerWindowVisible;
        private float _timeLimitRemainingSeconds;
        private float _timeScriptTriggerSeconds;
        private string _timeScriptFileName = string.Empty;
        private bool _timeScriptFired;
        private int _lastTimerNoticeSecond = -1;
        private object _pendingInteractionOwner;
        private string _pendingInteractionScript = string.Empty;
        private bool _gameStarted;
        private bool _startingNewGame;
        private bool _newGameOpeningVideoPending;
        private bool _scriptFaultReported;
        private bool _saveOperationInProgress;
        private bool _mapSwitchInProgress;
        private bool _worldAssetsLoaded;
        private bool _ready;
        private JxqyIntPoint _lastTrapObservedTile =
            new(-1, -1);
#if UNITY_EDITOR
        internal const string AcceptanceDrugId =
            "acceptance-runtime-drug";
        internal const string AcceptanceEquipmentId =
            "acceptance-runtime-equipment";
        internal const string AcceptanceShopItemId =
            "acceptance-runtime-shop-item";
        internal const string AcceptanceHotkeyItemId =
            "acceptance-runtime-hotkey-item";
        private int _acceptanceLastPointerAcceptedFrame = -1;
        private bool _acceptanceLastPointerTurnedImmediately;
        private string _acceptanceMapSwitchError = string.Empty;
        private double _acceptanceLastMapSwitchMilliseconds;
        private string _acceptanceActorLoadError = string.Empty;
        private bool _acceptanceActorLoadFinished;
        private bool _acceptanceSuppressTraps;
        private bool _acceptanceTrapTransitionFinished;
        private string _acceptanceTrapTransitionError = string.Empty;
        private JxqyWorldObject _acceptanceInteractionTarget;
        private bool _acceptanceInteractionStarted;
        private int _acceptanceInteractionMoneyBefore;
        private string _acceptanceInteractionScript = string.Empty;
        private JxqyNpc _acceptanceCombatTarget;
        private int _acceptanceCombatTargetLifeBefore;
        private int _acceptanceCombatExperienceBefore;
        private int _acceptanceCombatLevelBefore;
        private int _acceptanceCombatLevelUpExperience;
        private int _acceptanceCombatObjectCountBefore;
        private bool _acceptanceSaveLoadFinished;
        private bool _acceptanceSaveLoadPassed;
        private string _acceptanceSaveLoadError = string.Empty;
        private bool _acceptanceDaoJianCommandProbeFinished;
        private bool _acceptanceDaoJianCommandProbePassed;
        private string _acceptanceDaoJianCommandProbeError = string.Empty;
        private JxqyNpc _acceptanceMagicTarget;
        private int _acceptanceMagicTargetLifeBefore;
        private int _acceptanceMagicManaBefore;
        private int _acceptanceMagicResolveCount;
        private int _acceptanceItemLifeBefore;
        private int _acceptanceEquipmentAttackBefore;
        private int _acceptanceShopMoneyBefore;
        private int _acceptanceShopInventoryBefore;
        private JxqyFloat2 _acceptancePresentationCameraBefore;
        private bool _acceptancePresentationCommandsAccepted;
        private int _acceptanceHotkeyItemLifeBefore;
        private long _acceptanceManagedBytesLastUpdate;
        private long _acceptanceManagedBytesLastActorVisualTick;
        private long _acceptanceManagedBytesLastFrameBuild;
        private long _acceptanceManagedBytesLastFrameSubmit;
        private int _acceptanceNewGameInventoryEntryCount;
        private int _acceptanceNewGameEquipmentEntryCount;
        private int _acceptanceNewGameSkillCount;
#endif

        public string ActiveMapStableId { get; private set; } = string.Empty;
        public bool IsReady => _ready;
        public int InputIntentCoverageMask => _inputIntentCoverageMask;
#if UNITY_EDITOR
        public JxqyIntPoint AcceptancePlayerTile =>
            _player?.TilePosition ?? new JxqyIntPoint(-1, -1);
        public JxqyIntPoint AcceptancePlayerDestination =>
            _player?.DestinationTilePosition ??
            new JxqyIntPoint(-1, -1);
        public bool AcceptancePlayerHasPath => _player?.HasPath == true;
        public JxqyFloat2 AcceptancePlayerPosition =>
            _player?.PositionInWorld ?? JxqyFloat2.Zero;
        public int AcceptancePlayerDirection =>
            _player?.CurrentDirection ?? -1;
        public bool AcceptancePlayerIsRunning =>
            _player?.IsRunning == true;
        public int AcceptanceLastPointerAcceptedFrame =>
            _acceptanceLastPointerAcceptedFrame;
        public bool AcceptanceLastPointerTurnedImmediately =>
            _acceptanceLastPointerTurnedImmediately;
        public string AcceptanceMapSwitchError =>
            _acceptanceMapSwitchError;
        public bool AcceptanceMapSwitchInProgress =>
            _mapSwitchInProgress;
        public double AcceptanceLastMapSwitchMilliseconds =>
            _acceptanceLastMapSwitchMilliseconds;
        public bool AcceptanceScriptFaulted =>
            _scriptSession?.IsFaulted == true;
        public bool AcceptanceScriptRunning =>
            _scriptSession?.IsRunning == true;
        public bool AcceptanceTrapTransitionFinished =>
            _acceptanceTrapTransitionFinished;
        public string AcceptanceTrapTransitionError =>
            _acceptanceTrapTransitionError;
        public string AcceptanceActiveNpcFileName =>
            _activeNpcFileName;
        public string AcceptanceActiveObjectFileName =>
            _activeObjectFileName;
        public string AcceptanceActorLoadError =>
            _acceptanceActorLoadError;
        public bool AcceptanceActorLoadFinished =>
            _acceptanceActorLoadFinished;
        public int AcceptanceNpcCount => _npcs?.Npcs.Count ?? 0;
        public int AcceptanceObjectCount =>
            _objects?.Objects.Count ?? 0;
        public bool AcceptanceFirstActTombLoaded =>
            _objects?.Objects.Any(value =>
                value.TilePosition.Equals(new JxqyIntPoint(23, 37)) &&
                !string.IsNullOrWhiteSpace(value.ResourceFileName)) == true;
        public int AcceptanceFirstActChestCount =>
            _objects?.Objects.Count(value =>
                (value.TilePosition.Equals(new JxqyIntPoint(25, 58)) ||
                 value.TilePosition.Equals(new JxqyIntPoint(12, 25))) &&
                !string.IsNullOrWhiteSpace(value.ScriptAddress)) ?? 0;
        public int AcceptanceInitialSkillCount =>
            _skills?.Skills.Count ?? 0;
        public int AcceptanceInventoryEntryCount =>
            _inventory?.Entries.Count ?? 0;
        public int AcceptanceEquipmentEntryCount =>
            _equipment?.EquippedEntries.Count ?? 0;
        public int AcceptancePlayerLevel => _player?.Level ?? 0;
        public string AcceptanceLevelFileName =>
            _levelFileName ?? string.Empty;
        public int AcceptanceNewGameInventoryEntryCount =>
            _acceptanceNewGameInventoryEntryCount;
        public int AcceptanceNewGameEquipmentEntryCount =>
            _acceptanceNewGameEquipmentEntryCount;
        public int AcceptanceNewGameSkillCount =>
            _acceptanceNewGameSkillCount;
        public bool AcceptanceInitialShortcutsEmpty =>
            _inventory?.FindAtLegacyIndex(221) == null &&
            _inventory?.FindAtLegacyIndex(222) == null &&
            _inventory?.FindAtLegacyIndex(223) == null &&
            _skills?.FindAtLegacyIndex(40) == null &&
            _skills?.FindAtLegacyIndex(41) == null &&
            _skills?.FindAtLegacyIndex(42) == null &&
            _skills?.FindAtLegacyIndex(43) == null &&
            _skills?.FindAtLegacyIndex(44) == null;
        public int AcceptanceWorldSoundCount =>
            (_audio as JxqyUnityAudioPort)?.RegisteredWorldSoundCount ?? 0;
        public bool AcceptanceInteractionStarted =>
            _acceptanceInteractionStarted;
        public bool AcceptanceInteractionApplied =>
            _acceptanceInteractionStarted &&
            _acceptanceInteractionTarget != null &&
            _acceptanceInteractionTarget.IsOpen &&
            string.IsNullOrWhiteSpace(
                _acceptanceInteractionTarget.ScriptAddress) &&
            _player.Money > _acceptanceInteractionMoneyBefore;
        public string AcceptanceInteractionScript =>
            _acceptanceInteractionScript;
        public int AcceptanceInteractionMoneyDelta =>
            _player == null
                ? 0
                : _player.Money - _acceptanceInteractionMoneyBefore;
        public bool AcceptanceCombatApplied =>
            _acceptanceCombatTarget?.IsDead == true &&
            _player.Experience > _acceptanceCombatExperienceBefore &&
            _objects.Objects.Count >
            _acceptanceCombatObjectCountBefore;
        public bool AcceptanceCombatDropSpawned =>
            _objects != null &&
            _objects.Objects.Count >
            _acceptanceCombatObjectCountBefore;
        public int AcceptanceCombatDamage =>
            _acceptanceCombatTarget == null
                ? 0
                : _acceptanceCombatTargetLifeBefore -
                  _acceptanceCombatTarget.Life;
        public int AcceptanceCombatExperienceDelta =>
            _player == null
                ? 0
                : _player.Experience -
                  _acceptanceCombatExperienceBefore;
        public bool AcceptanceCombatLevelUpApplied =>
            _acceptanceCombatTarget?.IsDead == true &&
            _player != null &&
            _player.Level > _acceptanceCombatLevelBefore &&
            _player.Experience == _acceptanceCombatLevelUpExperience;
        public bool AcceptanceCombatLevelUpNoticeShown =>
            _uiSession != null &&
            _player != null &&
            string.Equals(
                _uiSession.Notice,
                $"{_player.Name}的等级提升了",
                StringComparison.Ordinal);
        public bool AcceptanceSaveLoadFinished =>
            _acceptanceSaveLoadFinished;
        public bool AcceptanceSaveLoadPassed =>
            _acceptanceSaveLoadPassed;
        public string AcceptanceSaveLoadError =>
            _acceptanceSaveLoadError;
        public bool AcceptanceDaoJianCommandProbeFinished =>
            _acceptanceDaoJianCommandProbeFinished;
        public bool AcceptanceDaoJianCommandProbePassed =>
            _acceptanceDaoJianCommandProbePassed;
        public string AcceptanceDaoJianCommandProbeError =>
            _acceptanceDaoJianCommandProbeError;
        public bool AcceptanceMagicApplied =>
            _acceptanceMagicTarget != null &&
            _acceptanceMagicTarget.Life <
            _acceptanceMagicTargetLifeBefore &&
            _player.Mana < _acceptanceMagicManaBefore &&
            !_player.HasPath;
        public int AcceptanceMagicDamage =>
            _acceptanceMagicTarget == null
                ? 0
                : _acceptanceMagicTargetLifeBefore -
                  _acceptanceMagicTarget.Life;
        public bool AcceptanceRepeatedMagicFinished =>
            _acceptanceMagicResolveCount >= 2 &&
            _pendingPlayerMagicCast == null &&
            _player?.CanPerformAction == true;
        public bool AcceptanceItemUsed =>
            _player != null &&
            _player.Life > _acceptanceItemLifeBefore &&
            _inventory.Count(AcceptanceDrugId) == 0;
        public bool AcceptanceEquipmentEquipped =>
            _player != null &&
            _player.Attack > _acceptanceEquipmentAttackBefore &&
            _equipment.Equipped.TryGetValue(
                JxqyEquipmentSlot.Hand,
                out JxqyItemDefinition equipped) &&
            string.Equals(
                equipped.Id,
                AcceptanceEquipmentId,
                StringComparison.Ordinal);
        public bool AcceptanceShopBought =>
            _player != null &&
            _inventory.Count(AcceptanceShopItemId) ==
            _acceptanceShopInventoryBefore + 1 &&
            _player.Money == _acceptanceShopMoneyBefore - 20;
        public bool AcceptanceShopSold =>
            _player != null &&
            _inventory.Count(AcceptanceShopItemId) ==
            _acceptanceShopInventoryBefore &&
            _player.Money == _acceptanceShopMoneyBefore - 10;
        public bool AcceptancePresentationApplied =>
            _acceptancePresentationCommandsAccepted &&
            _renderer != null &&
            _presentationEffects.IsRaining &&
            _presentationEffects.IsSnowing &&
            _presentationEffects.WaterEffectEnabled &&
            _presentationEffects.MapTime == 7 &&
            _presentationEffects.FadeOpacity > 0.2f &&
            _uiSession?.FadeVisible == true &&
            _uiSession.FadeOpacity > 0.2f &&
            _camera.X >=
            Mathf.RoundToInt(
                _acceptancePresentationCameraBefore.X + 80f) &&
            _renderer.LastWeatherCommandCount > 0 &&
            _renderer.LastFadeCommandCount == 0;
        public int AcceptanceWeatherCommandCount =>
            _renderer?.LastWeatherCommandCount ?? 0;
        public int AcceptanceFadeCommandCount =>
            _uiSession?.FadeVisible == true &&
            _uiSession.FadeOpacity > 0f ? 1 : 0;
        public bool AcceptanceHotkeyItemUsed =>
            _player != null &&
            _player.Life > _acceptanceHotkeyItemLifeBefore &&
            _inventory.Count(AcceptanceHotkeyItemId) == 0;
        public int AcceptanceCrowdCount =>
            _npcs?.Npcs.Count ?? 0;
        public int AcceptancePathPlansLastTick =>
            _npcs?.PathPlansLastTick ?? 0;
        public long AcceptancePathPlansTotal =>
            _npcs?.PathPlansTotal ?? 0;
        public int AcceptanceRendererCount =>
            _renderer?.ActiveRendererCount ?? 0;
        public int AcceptanceRendererSpawnsLastFrame =>
            _renderer?.LastPoolSpawnCount ?? 0;
        public int AcceptanceRendererUnspawnsLastFrame =>
            _renderer?.LastPoolUnspawnCount ?? 0;
        public long AcceptanceManagedBytesLastUpdate =>
            _acceptanceManagedBytesLastUpdate;
        public long AcceptanceManagedBytesLastActorVisualTick =>
            _acceptanceManagedBytesLastActorVisualTick;
        public long AcceptanceManagedBytesLastFrameBuild =>
            _acceptanceManagedBytesLastFrameBuild;
        public long AcceptanceManagedBytesLastFrameSubmit =>
            _acceptanceManagedBytesLastFrameSubmit;
#endif
        internal JxqyPresentationScriptCommandPort PresentationCommands
        {
            get;
            private set;
        }

        public async UniTask InitializeAsync(
            JxqyMapPreloadCoordinator mapCoordinator,
            IJxqyInputPort input,
            Action<string> reportStatus,
            IJxqyAudioPort audio = null,
            IJxqyVideoPort video = null,
            JxqyRuntimeContentContext contentContext = null,
            JxqyYooAssetPackageResolver packageResolver = null,
            CancellationToken cancellationToken = default)
        {
            _mapCoordinator = mapCoordinator ??
                              throw new ArgumentNullException(
                                  nameof(mapCoordinator));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _contentContext = contentContext ??
                throw new ArgumentNullException(nameof(contentContext));
            _audio = audio;
            _video = video;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (_video is JxqyUnityVideoPort unityVideo)
                unityVideo.PlaybackStarted += OnVideoPlaybackStarted;
            _resources = new JxqyYooAssetResourcePort(
                packageResolver ??
                throw new ArgumentNullException(nameof(packageResolver)));
            _mapScope = new JxqyResourceScope(
                $"playable-map:{Guid.NewGuid():N}");
            _textures = new JxqyTextureRegistry();

            reportStatus?.Invoke("正在读取地图预载清单...");
            JxqyPreloadManifest manifest = await LoadManifestAsync(
                cancellationToken);
            _preloadManifest = manifest;
            JxqyPreloadGroup group = manifest.Groups.SingleOrDefault(
                candidate =>
                    string.Equals(
                        candidate.Kind,
                        "Map",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        candidate.OwnerStableId,
                        _contentContext.InitialMapStableId,
                        StringComparison.OrdinalIgnoreCase));
            if (group == null)
                throw new InvalidOperationException(
                    $"预载清单缺少新游戏地图：" +
                    $"{_contentContext.InitialMapStableId}");

            string sceneKey = string.IsNullOrWhiteSpace(group.SceneKey)
                ? group.OwnerStableId
                : group.SceneKey;
            JxqyResourceAddressCatalog.Configure(
                manifest,
                string.Empty,
                _contentContext.PackageName,
                packageResolver);
            foreach (JxqyLegacyAnimationAlias alias in
                     _contentContext.AnimationAliases)
            {
                JxqyResourceAddressCatalog.RegisterAnimationAlias(
                    alias.Category,
                    alias.FileName,
                    alias.MetadataAddress);
            }
            await LoadOriginalCharacterNamesAsync(cancellationToken);
            await LoadOriginalCharacterLevelEntriesAsync(cancellationToken);
            await LoadNpcLevelEntriesAsync(cancellationToken);
            await LoadMapDisplayNamesAsync(cancellationToken);
            await LoadUiSoundConfigurationAsync(cancellationToken);
            await LoadTalkIndexAsync(cancellationToken);
            CreatePresentation();
            CreateUiSession();
            CreateScriptSession();
            reportStatus?.Invoke("正在加载原版脚本目录...");
            await _scriptSession.InitializeAsync(cancellationToken);
            _combatFloatTextPool = new JxqyCombatFloatTextPool();
            await _combatFloatTextPool.InitializeAsync(
                transform,
                cancellationToken,
                packageResolver,
                _contentContext.CombatFloatTextPrefabAddress);
            JxqyGameBootstrap.NotifyUiSessionReady(_uiSession);
            _ready = true;
            SubmitFrame();
            reportStatus?.Invoke(
                "Jxqy title menu is ready; no map is loaded.");
        }

        private async UniTask EnsureWorldAssetsLoadedAsync(
            CancellationToken cancellationToken)
        {
            if (_worldAssetsLoaded)
                return;
            await LoadPlayerVisualsAsync(null, cancellationToken);
            await LoadStatusDeathAnimationsAsync(cancellationToken);
            await LoadRenderAssetsAsync(cancellationToken);
            _worldAssetsLoaded = true;
        }

        private async UniTask<JxqyPreloadManifest> LoadManifestAsync(
            CancellationToken cancellationToken)
        {
            JxqyAssetLease<TextAsset> lease =
                await _resources.LoadAsync<TextAsset>(
                    _contentContext.PreloadManifestAddress,
                    _mapScope,
                    cancellationToken);
            _leases.Add(lease);
            JxqyPreloadManifest manifest =
                JsonUtility.FromJson<JxqyPreloadManifest>(lease.Asset.text);
            if (manifest == null ||
                manifest.Errors == null ||
                manifest.Errors.Count != 0)
                throw new InvalidOperationException(
                    "地图预载清单无效或包含转换错误。");
            return manifest;
        }

        private async UniTask LoadMapGroupAsync(
            JxqyPreloadGroup group,
            Action<string> reportStatus,
            CancellationToken cancellationToken)
        {
            var candidateScope = new JxqyResourceScope(
                $"playable-map-assets:{Guid.NewGuid():N}");
            var candidateLeases = new List<IDisposable>();
            var candidateTextures =
                new List<(string Address, Texture2D Texture,
                    IDisposable Lease)>();
            var candidateAnimations =
                new Dictionary<string, JxqyAnimationMetadata>(
                    StringComparer.OrdinalIgnoreCase);
            JxqyMapMetadata candidateMetadata = null;
            TextAsset candidateMapData = null;
            try
            {
                for (int batchStart = 0;
                     batchStart < group.Resources.Count;
                     batchStart +=
                         JxqyMapPreloadCoordinator.ResourceLoadBatchSize)
                {
                    int batchCount = Math.Min(
                        JxqyMapPreloadCoordinator.ResourceLoadBatchSize,
                        group.Resources.Count - batchStart);
                    var tasks =
                        new UniTask<JxqyLoadedMapResource>[batchCount];
                    for (int offset = 0;
                         offset < batchCount;
                         offset++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int resourceIndex = batchStart + offset;
                        JxqyPreloadResource resource =
                            group.Resources[resourceIndex];
                        reportStatus?.Invoke(
                            $"加载地图资源 " +
                            $"{resourceIndex + 1}/{group.Resources.Count}：" +
                            resource.ResourceKind);
                        tasks[offset] = LoadMapResourceAsync(
                            resource,
                            candidateScope,
                            cancellationToken);
                    }

                    Exception firstException = null;
                    for (int offset = 0;
                         offset < tasks.Length;
                         offset++)
                    {
                        try
                        {
                            JxqyLoadedMapResource loaded =
                                await tasks[offset];
                            JxqyPreloadResource resource = loaded.Resource;
                            switch (resource.ResourceKind)
                            {
                                case "MapMetadata":
                                    candidateLeases.Add(loaded.TextLease);
                                    candidateMetadata =
                                        JsonUtility.FromJson<
                                            JxqyMapMetadata>(
                                            loaded.TextLease.Asset.text);
                                    break;
                                case "MapData":
                                    candidateLeases.Add(loaded.TextLease);
                                    candidateMapData =
                                        loaded.TextLease.Asset;
                                    break;
                                case "AnimationMetadata":
                                {
                                    candidateLeases.Add(loaded.TextLease);
                                    JxqyAnimationMetadata animation =
                                        JsonUtility.FromJson<
                                            JxqyAnimationMetadata>(
                                            loaded.TextLease.Asset.text);
                                    if (animation == null ||
                                        string.IsNullOrWhiteSpace(
                                            animation.SourceStableId))
                                    {
                                        throw new InvalidOperationException(
                                            $"动画元数据无效：" +
                                            resource.Address);
                                    }
                                    candidateAnimations[
                                        animation.SourceStableId] =
                                        animation;
                                    break;
                                }
                                case "AnimationAtlas":
                                    candidateTextures.Add((
                                        resource.Address,
                                        loaded.TextureLease.Asset,
                                        loaded.TextureLease));
                                    break;
                            }
                        }
                        catch (Exception exception)
                        {
                            firstException ??= exception;
                        }
                    }

                    // Let every operation started in this batch settle before
                    // rollback can release the scope. A late successful task
                    // must never register a lease after scope cleanup.
                    if (firstException != null)
                        throw firstException;
                }

                if (candidateMetadata == null ||
                    candidateMapData == null)
                {
                    throw new InvalidOperationException(
                        "地图缺少地图元数据或二进制数据。");
                }

                JxqyRuntimeMapData candidateMap =
                    JxqyRuntimeMapData.Parse(
                        candidateMapData.bytes,
                        candidateMetadata);
                await ReleaseActiveMapAssetsAsync();
                _activeMapAssetScope = candidateScope;
                _activeMapLeases.AddRange(candidateLeases);
                candidateLeases.Clear();
                foreach ((string address, Texture2D texture,
                             IDisposable lease) in candidateTextures)
                {
                    _textures.Register(address, texture, lease);
                    _activeMapTextureAddresses.Add(address);
                }
                candidateTextures.Clear();
                foreach (KeyValuePair<string, JxqyAnimationMetadata> entry
                         in candidateAnimations)
                {
                    _animations[entry.Key] = entry.Value;
                    _activeMapAnimationStableIds.Add(entry.Key);
                }
                _mapMetadata = candidateMetadata;
                _map = candidateMap;
            }
            catch
            {
                foreach (IDisposable lease in candidateLeases)
                    lease.Dispose();
                foreach ((string _, Texture2D __, IDisposable lease)
                         in candidateTextures)
                {
                    lease.Dispose();
                }
                await _resources.ReleaseScopeAsync(
                    candidateScope,
                    CancellationToken.None);
                throw;
            }

            _mapCommands = new JxqyMapDrawCommandBuilder(
                _mapMetadata,
                _map,
                _animations);
            _worldCommands =
                new JxqyWorldDrawCommandBuilder(_map.Columns);
        }

        private async UniTask<JxqyLoadedMapResource>
            LoadMapResourceAsync(
                JxqyPreloadResource resource,
                JxqyResourceScope scope,
                CancellationToken cancellationToken)
        {
            var loaded = new JxqyLoadedMapResource
            {
                Resource = resource,
            };
            switch (resource.ResourceKind)
            {
                case "MapMetadata":
                case "MapData":
                case "AnimationMetadata":
                    loaded.TextLease =
                        await _resources.LoadAsync<TextAsset>(
                            resource.Address,
                            scope,
                            cancellationToken);
                    break;
                case "AnimationAtlas":
                    loaded.TextureLease =
                        await _resources.LoadAsync<Texture2D>(
                            resource.Address,
                            scope,
                            cancellationToken);
                    break;
            }
            return loaded;
        }

        private async UniTask ReleaseActiveMapAssetsAsync()
        {
            for (int index = 0;
                 index < _activeMapTextureAddresses.Count;
                 index++)
            {
                _textures.Unregister(
                    _activeMapTextureAddresses[index]);
            }
            _activeMapTextureAddresses.Clear();
            for (int index = 0;
                 index < _activeMapAnimationStableIds.Count;
                 index++)
            {
                _animations.Remove(
                    _activeMapAnimationStableIds[index]);
            }
            _activeMapAnimationStableIds.Clear();
            foreach (IDisposable lease in _activeMapLeases)
                lease.Dispose();
            _activeMapLeases.Clear();
            if (_activeMapAssetScope != null)
            {
                JxqyResourceScope scope = _activeMapAssetScope;
                _activeMapAssetScope = null;
                await _resources.ReleaseScopeAsync(
                    scope,
                    CancellationToken.None);
            }
        }

        private async UniTask<JxqyAssetLease<TextAsset>> LoadTextAsync(
            string address,
            CancellationToken cancellationToken)
        {
            JxqyAssetLease<TextAsset> lease =
                await _resources.LoadAsync<TextAsset>(
                    address,
                    _mapScope,
                    cancellationToken);
            _leases.Add(lease);
            return lease;
        }

        private void CreateRenderer()
        {
            var rendererObject = new GameObject("JxqyWorldRenderer");
            rendererObject.transform.SetParent(transform, false);
            _renderer =
                rendererObject.AddComponent<JxqySpriteRendererWorldRenderer>();
            _safeAreaViewport =
                rendererObject.AddComponent<JxqySafeAreaViewport>();
            RefreshActiveMapBindings(true);
        }

        private async UniTask LoadStatusDeathAnimationsAsync(
            CancellationToken cancellationToken)
        {
            foreach (KeyValuePair<JxqyStatusKind, string> entry in
                     _contentContext.ContentProfile
                         .StatusDeathAnimationFiles)
            {
                JxqyAnimationMetadata metadata =
                    await LoadDynamicAnimationAsync(
                        entry.Value,
                        cancellationToken,
                        "interlude");
                _statusDeathAnimations[entry.Key] = metadata;
                _playerStatusDeathPlayers[entry.Key] =
                    new JxqyAnimationPlayer(metadata)
                    {
                        IsLooping = false,
                    };
            }
        }

        private void RefreshActiveMapBindings(bool force = false)
        {
            Scene mapScene = SceneManager.GetActiveScene();
            if (!mapScene.IsValid() || !mapScene.isLoaded)
                throw new InvalidOperationException(
                    "Active Jxqy map scene is invalid or not loaded.");
            if (!force &&
                _worldCamera != null &&
                _boundMapScene == mapScene)
                return;

            _activeSceneRoots.Clear();
            _activeSceneCameras.Clear();
            _mapTilemaps.Clear();
            mapScene.GetRootGameObjects(_activeSceneRoots);
            for (int index = 0;
                 index < _activeSceneRoots.Count;
                 index++)
            {
                GameObject root = _activeSceneRoots[index];
                _rootCameras.Clear();
                root.GetComponentsInChildren(
                    true,
                    _rootCameras);
                _activeSceneCameras.AddRange(_rootCameras);
                _rootTilemaps.Clear();
                root.GetComponentsInChildren(
                    true,
                    _rootTilemaps);
                _mapTilemaps.AddRange(_rootTilemaps);
            }

            Camera worldCamera = null;
            int enabledMainCameraCount = 0;
            for (int index = 0;
                 index < _activeSceneCameras.Count;
                 index++)
            {
                Camera candidate = _activeSceneCameras[index];
                if (!candidate.enabled ||
                    !candidate.gameObject.activeInHierarchy ||
                    !candidate.CompareTag("MainCamera"))
                    continue;
                worldCamera = candidate;
                enabledMainCameraCount++;
            }
            if (enabledMainCameraCount != 1)
            {
                throw new InvalidOperationException(
                    $"Active map scene '{mapScene.path}' must contain " +
                    $"exactly one enabled MainCamera; found " +
                    $"{enabledMainCameraCount}.");
            }

            _worldCamera = worldCamera;
            _boundMapScene = mapScene;
            _waterRefractionEffect =
                worldCamera.GetComponent<JxqyWaterRefractionEffect>() ??
                worldCamera.gameObject
                    .AddComponent<JxqyWaterRefractionEffect>();
            _waterRefractionEffect.Initialize(
                _renderMaterials["refraction"],
                _waterDisplacementTexture);
            worldCamera.depth = -1;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                worldCamera.cullingMask &= ~(1 << uiLayer);
            _renderer.Initialize(
                worldCamera,
                _textures,
                _renderMaterials);
            _safeAreaViewport.Initialize(worldCamera);
            RefreshLogicalViewport(true);
            if (_video is JxqyUnityVideoPort unityVideo)
                unityVideo.BindCamera(worldCamera);
            for (int index = 0; index < _mapTilemaps.Count; index++)
            {
                TilemapRenderer tilemapRenderer =
                    _mapTilemaps[index].GetComponent<TilemapRenderer>();
                if (tilemapRenderer != null)
                    tilemapRenderer.enabled = false;
            }
            ApplyPresentationColors();
        }

        private void RefreshLogicalViewport(bool force)
        {
            if (_safeAreaViewport == null || _renderer == null)
                return;
            _safeAreaViewport.Refresh(force);
            JxqyViewportLayout layout = _safeAreaViewport.Layout;
            int width = Math.Max(
                JxqyLogicalViewport.OriginalWidth,
                layout.LogicalWidth);
            int height = Math.Max(
                JxqyLogicalViewport.OriginalHeight,
                layout.LogicalHeight);
            if (!force &&
                _logicalWidth == width &&
                _logicalHeight == height)
            {
                return;
            }
            _logicalWidth = width;
            _logicalHeight = height;
            _renderer.SetLogicalResolution(width, height);
            _presentationEffects?.SetViewportSize(width, height);
            if (_mapMetadata != null)
            {
                _camera = JxqyIsometricMapMath.ClampCamera(
                    _camera.X,
                    _camera.Y,
                    width,
                    height,
                    _mapMetadata.MapPixelWidth,
                    _mapMetadata.MapPixelHeight);
                _presentationEffects?.SetCameraPositionPreservingMove(
                    new JxqyFloat2(_camera.X, _camera.Y));
            }
        }

        private async UniTask LoadPlayerVisualsAsync(
            Action<string> reportStatus,
            CancellationToken cancellationToken)
        {
            JxqyAssetLease<TextAsset> playerLease = await LoadTextAsync(
                _contentContext.PlayerProfileAddress,
                cancellationToken);
            _initialPlayerIniText = playerLease.Asset.text;
            string resourceFileName = GetPlayerResourceFileName(
                playerLease.Asset.text);
            string resourceText = await LoadDynamicTextAsync(
                "ini/npcres",
                resourceFileName,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(resourceText);
            string standFileName = GetStateImage(sections, "Stand");
            string walkFileName = GetStateImage(sections, "Walk");
            string runFileName = GetStateImage(sections, "Run");
            if (string.IsNullOrWhiteSpace(standFileName) ||
                string.IsNullOrWhiteSpace(walkFileName))
            {
                throw new InvalidOperationException(
                    $"主角资源定义缺少 Stand/Walk 动画：{resourceFileName}");
            }
            if (string.IsNullOrWhiteSpace(runFileName))
                runFileName = walkFileName;

            reportStatus?.Invoke("加载当前 Mod 主角动画...");
            JxqyAnimationMetadata stand = await LoadDynamicAnimationAsync(
                standFileName,
                cancellationToken,
                "character");
            JxqyAnimationMetadata walk = await LoadDynamicAnimationAsync(
                walkFileName,
                cancellationToken,
                "character");
            JxqyAnimationMetadata run = await LoadDynamicAnimationAsync(
                runFileName,
                cancellationToken,
                "character");
            _playerStand = new JxqyAnimationPlayer(stand);
            _playerWalk = new JxqyAnimationPlayer(walk);
            _playerRun = new JxqyAnimationPlayer(run);
        }

        private async UniTask LoadRenderAssetsAsync(
            CancellationToken cancellationToken)
        {
            foreach (string materialKey in
                     JxqyMaterialCache.MaterialKeys)
            {
                string address =
                    _contentContext.MaterialAddress(materialKey);
                JxqyAssetLease<Material> materialLease =
                    await _resources.LoadAsync<Material>(
                        address,
                        _mapScope,
                        cancellationToken);
                _leases.Add(materialLease);
                _renderMaterials.Add(
                    materialKey,
                    materialLease.Asset);
            }
            JxqyAssetLease<Texture2D> textureLease =
                await _resources.LoadAsync<Texture2D>(
                    _contentContext.WaterDisplacementTextureAddress,
                    _mapScope,
                    cancellationToken);
            _leases.Add(textureLease);
            _waterDisplacementTexture = textureLease.Asset;
        }

        private async UniTask<JxqyAnimationMetadata> LoadAnimationAsync(
            JxqyPreloadGroup group,
            string stableId,
            CancellationToken cancellationToken)
        {
            JxqyPreloadResource metadataResource =
                group.Resources.SingleOrDefault(resource =>
                    string.Equals(
                        resource.SourceStableId,
                        stableId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        resource.ResourceKind,
                        "AnimationMetadata",
                        StringComparison.OrdinalIgnoreCase));
            if (metadataResource == null)
                throw new InvalidOperationException(
                    $"角色动画元数据缺失：{stableId}");
            JxqyAssetLease<TextAsset> metadataLease =
                await LoadTextAsync(
                    metadataResource.Address,
                    cancellationToken);
            JxqyAnimationMetadata metadata =
                JsonUtility.FromJson<JxqyAnimationMetadata>(
                    metadataLease.Asset.text);
            if (metadata == null)
                throw new InvalidOperationException(
                    $"角色动画元数据无效：{stableId}");

            foreach (string atlasAddress in metadata.AtlasAddresses)
            {
                JxqyAssetLease<Texture2D> atlasLease =
                    await _resources.LoadAsync<Texture2D>(
                        atlasAddress,
                        _mapScope,
                        cancellationToken);
                _textures.Register(
                    atlasAddress,
                    atlasLease.Asset,
                    atlasLease);
            }
            return metadata;
        }

#if false // Retired MeshRenderer UI resource path; UIWindow owns these resources.
        private async UniTask LoadTitleUiAsync(
            JxqyPreloadManifest manifest,
            Action<string> reportStatus,
            CancellationToken cancellationToken)
        {
            _sharedUiGroup = manifest.Groups.SingleOrDefault(
                candidate => string.Equals(
                    candidate.Kind,
                    "UI",
                    StringComparison.OrdinalIgnoreCase));
            if (_sharedUiGroup == null)
                throw new InvalidOperationException(
                    "预加载清单缺少原版 UI 资源组。");

            reportStatus?.Invoke("加载原版标题画面和菜单按钮...");
            JxqyAssetLease<Texture2D> background =
                await _resources.LoadAsync<Texture2D>(
                    TitleBackgroundAddress,
                    _mapScope,
                    cancellationToken);
            _textures.Register(
                TitleBackgroundAddress,
                background.Asset,
                background);
            string[] titleButtonIds =
            {
                TitleNewGameStableId,
                TitleLoadStableId,
                TitleCreditsStableId,
                TitleExitStableId,
            };
            UniTask<JxqyAnimationMetadata>[] titleButtonTasks =
                titleButtonIds
                    .Select(stableId => LoadAnimationAsync(
                        _sharedUiGroup,
                        stableId,
                        cancellationToken))
                    .ToArray();
            _titleButtons = await UniTask.WhenAll(titleButtonTasks);
        }

        private async UniTask LoadLegacyGameUiInBackgroundAsync(
            CancellationToken cancellationToken)
        {
            if (_legacyUiReady || _legacyUiLoading)
                return;
            _legacyUiLoading = true;
            _legacyUiLoadError = null;
            try
            {
                await LoadLegacyGameUiAsync(cancellationToken);
                _legacyUiReady = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _legacyUiLoadError = exception;
                Debug.LogException(exception, this);
            }
            finally
            {
                _legacyUiLoading = false;
            }
        }

        private async UniTask LoadLegacyGameUiAsync(
            CancellationToken cancellationToken)
        {
            if (_sharedUiGroup == null)
                throw new InvalidOperationException(
                    "原版 UI 资源组尚未完成初始化。");
            string[] legacyUiAnimations =
            {
                "asf:asf/ui/top/window.asf",
                "asf:asf/ui/top/btnstate.asf",
                "asf:asf/ui/top/btnequip.asf",
                "asf:asf/ui/top/btnxiulian.asf",
                "asf:asf/ui/top/btngoods.asf",
                "asf:asf/ui/top/btnmagic.asf",
                "asf:asf/ui/top/btnnotes.asf",
                "asf:asf/ui/top/btnoption.asf",
                "asf:asf/ui/bottom/window.asf",
                "asf:asf/ui/column/panel9.asf",
                "asf:asf/ui/column/collife.asf",
                "asf:asf/ui/column/colthew.asf",
                "asf:asf/ui/column/colmana.asf",
                "asf:asf/ui/common/panel.asf",
                "asf:asf/ui/common/panel2.asf",
                "asf:asf/ui/common/panel3.asf",
                "asf:asf/ui/common/panel5b.asf",
                "asf:asf/ui/common/panel7b.asf",
                "asf:asf/ui/common/panel8.asf",
                "asf:asf/ui/system/saveload.asf",
                "asf:asf/ui/system/option.asf",
                "asf:asf/ui/system/quit.asf",
                "asf:asf/ui/system/return.asf",
                "asf:asf/ui/saveload/panel.asf",
                "asf:asf/ui/saveload/btnload.asf",
                "asf:asf/ui/saveload/btnsave.asf",
                "asf:asf/ui/saveload/btnexit.asf",
                "asf:asf/ui/goods/slidebtn.asf",
                "asf:asf/ui/buysell/closebtn.asf",
                "asf:asf/ui/dialog/panel.asf",
            };
            const int batchSize = 4;
            for (int offset = 0;
                 offset < legacyUiAnimations.Length;
                 offset += batchSize)
            {
                int count = Math.Min(
                    batchSize,
                    legacyUiAnimations.Length - offset);
                var animationTasks =
                    new UniTask<JxqyAnimationMetadata>[count];
                for (int index = 0; index < count; index++)
                {
                    animationTasks[index] = LoadAnimationAsync(
                        _sharedUiGroup,
                        legacyUiAnimations[offset + index],
                        cancellationToken);
                }
                JxqyAnimationMetadata[] animations =
                    await UniTask.WhenAll(animationTasks);
                foreach (JxqyAnimationMetadata animation in animations)
                    _animations[animation.SourceStableId] = animation;
            }
            await LoadUiFontAsync(_sharedUiGroup, cancellationToken);
        }

        private async UniTask LoadUiFontAsync(
            JxqyPreloadGroup sharedUi,
            CancellationToken cancellationToken)
        {
            JxqyPreloadResource metadataResource =
                sharedUi.Resources.FirstOrDefault(resource =>
                    string.Equals(
                        resource.ResourceKind,
                        "FontMetadata",
                        StringComparison.OrdinalIgnoreCase) &&
                    resource.Address.EndsWith(
                        "_12.xnb/font.json",
                        StringComparison.OrdinalIgnoreCase));
            if (metadataResource == null)
                throw new InvalidOperationException(
                    "原版 12 号中文点阵字体元数据缺失。");
            JxqyAssetLease<TextAsset> metadataLease =
                await LoadTextAsync(
                    metadataResource.Address,
                    cancellationToken);
            JxqySpriteFontMetadata metadata =
                JsonUtility.FromJson<JxqySpriteFontMetadata>(
                    metadataLease.Asset.text);
            if (metadata == null ||
                string.IsNullOrWhiteSpace(metadata.TextureAddress))
                throw new InvalidOperationException(
                    "原版 12 号中文点阵字体元数据无效。");
            JxqyAssetLease<Texture2D> textureLease =
                await _resources.LoadAsync<Texture2D>(
                    metadata.TextureAddress,
                    _mapScope,
                    cancellationToken);
            _textures.Register(
                metadata.TextureAddress,
                textureLease.Asset,
                textureLease);
            _uiFont = new JxqySpriteFontDrawCommandBuilder(metadata);
        }

#endif

        private void CreatePlayer()
        {
            // Original Npc.IsAIDisabled is reset for a fresh game/runtime,
            // then remains global while scripted map loads replace NPC data.
            _npcAiDisabled = false;
            if (_player != null)
            {
                _player.Died -= OnPlayerDied;
                _player.Revived -= OnPlayerRevived;
            }
            _playerStateActions.Clear();
            _playerStateSounds.Clear();
            RemoveActiveCharacterStateSound(
                ref _playerActiveStateSoundId);
            _player = new JxqyPlayer();
            if (!string.IsNullOrWhiteSpace(_initialPlayerIniText))
            {
                ApplyOriginalPlayerIni(
                    _initialPlayerIniText,
                    updateVisual: false);
            }
            _player.Died += OnPlayerDied;
            _player.Revived += OnPlayerRevived;
            _playerVisualState = (JxqyCharacterState)(-1);
            _playerVisualStateVersion = -1;
            _playerVisual = new JxqyWorldVisual
            {
                Id = "player",
                Kind = JxqyWorldVisualKind.Player,
                CharacterOcclusionStencilBit =
                    JxqyCharacterOcclusionPolicy.GetTargetStencilBit(0),
                Animation = _playerStand
            };
            _frameVisuals.Clear();
            _frameVisuals.Add(_playerVisual);
            _objects = new JxqyObjectManager();
            _npcs = new JxqyNpcManager(
                _player,
                _objects,
                new JxqyRuntimeCollisionMap(_map),
                _legacyRandom);
            _combat = new JxqyCombatSystem(_legacyRandom);
            _combat.ProjectileSpawned += OnMagicProjectileSpawned;
            _combat.ProjectileResolved += OnMagicProjectileResolved;
            _combat.ProjectileExpired += OnMagicProjectileExpired;
            _combat.MagicContacted += OnMagicContacted;
            _combat.MagicUsed += OnMagicUsed;
            _playerAutoAttack = new JxqyAutoAttackController
            {
                IntervalSeconds = 0f,
                Range = 96f,
                MaximumTileDistance = 1,
            };
            _npcAutoAttacks.Clear();
            _npcKeepAttackCooldowns.Clear();
            _pendingBasicAttacks.Clear();
            _transientCombatStates.Clear();
            _processedNpcDeaths.Clear();
            _finalizedNpcDeaths.Clear();
            _npcDeathsReadyToFinalize.Clear();
            _pendingPlayerMagicCast = null;
            _queuedPlayerMagicCast = null;
            ClearMagicVisuals();
            _worldCommands =
                new JxqyWorldDrawCommandBuilder(_map.Columns);
            UpdatePlayerVisual();
        }

        private void CreatePresentation()
        {
            _presentationEffects = new JxqyPresentationEffects(
                new JxqyDeterministicRandom(20260725),
                LogicalWidth,
                LogicalHeight);
            _presentationEffects.Thunder += PlayThunder;
            _presentationEffects.RainStarted += PlayRainAmbient;
            _presentationEffects.RainEnded += StopRainAmbient;
            _presentationBuilder =
                new JxqyPresentationDrawCommandBuilder(
                    _contentContext.ContentAddress(
                        _contentContext.ContentProfile
                            .RainTextureRelativeAddress),
                    _contentContext.ContentProfile
                        .SnowTextureRelativeAddresses
                        .Select(_contentContext.ContentAddress)
                        .ToArray(),
                    _contentContext.ContentAddress(
                        _contentContext.ContentProfile
                            .WhiteTextureRelativeAddress));
            PresentationCommands =
                new JxqyPresentationScriptCommandPort(
                    _presentationEffects,
                    _audio,
                    _video,
                    tileCameraPosition:
                        ResolveTileCameraPosition,
                     backgroundMusicChanged:
                         address =>
                             _backgroundMusicAddress =
                                 address ?? string.Empty,
                     fadeOutRequested: RequestScreenFadeOut,
                     fadeInRequested: RequestScreenFadeIn,
                     fadeOutCompleted: IsScreenFadeOutComplete,
                     fadeInCompleted: IsScreenFadeInComplete,
                     mediaAddresses:
                         _contentContext.MediaAddressResolver);
            RegisterPresentationTexture(
                _presentationBuilder.WhiteTextureAddress,
                CreateSolidTexture(
                    LogicalWidth,
                    LogicalHeight,
                    Color.white,
                    "JxqyPresentationWhite"));
            RegisterPresentationTexture(
                _presentationBuilder.RainTextureAddress,
                CreateRainTexture());
            for (int index = 0;
                 index <
                 _presentationBuilder.SnowTextureAddresses.Count;
                 index++)
            {
                RegisterPresentationTexture(
                    _presentationBuilder
                        .SnowTextureAddresses[index],
                    CreateSnowTexture(index));
            }
            ApplyPresentationColors();
        }

        private void RequestScreenFadeOut()
        {
            _closeFadeUiWhenTransparent = false;
            _fadeInPendingUiReady = false;
            _uiSession?.ShowFade(0f);
            if (_uiSession?.FadeUiReady == true)
                _presentationEffects.FadeOut();
            else
                _fadeOutPendingUiReady = true;
        }

        private void RequestScreenFadeIn()
        {
            _closeFadeUiWhenTransparent = true;
            _fadeOutPendingUiReady = false;
            _uiSession?.ShowFade(1f);
            if (_uiSession?.FadeUiReady == true)
                _presentationEffects.FadeIn();
            else
                _fadeInPendingUiReady = true;
        }

        private bool IsScreenFadeOutComplete()
        {
            return _uiSession?.FadeUiReady == true &&
                   !_presentationEffects.IsFadingOut &&
                   _presentationEffects.FadeOpacity >= 1f;
        }

        private bool IsScreenFadeInComplete()
        {
            return _uiSession?.FadeVisible == false &&
                   !_presentationEffects.IsFadingIn &&
                   _presentationEffects.FadeOpacity <= 0f;
        }

        private void UpdateScreenFadeUi()
        {
            if (_uiSession == null)
                return;
            if (_uiSession.FadeUiReady && _fadeOutPendingUiReady)
            {
                _fadeOutPendingUiReady = false;
                _presentationEffects.FadeOut();
            }
            if (_uiSession.FadeUiReady && _fadeInPendingUiReady)
            {
                _fadeInPendingUiReady = false;
                _presentationEffects.FadeIn();
            }
            _uiSession.SetFadeOpacity(_presentationEffects.FadeOpacity);
            if (!_closeFadeUiWhenTransparent ||
                _presentationEffects.IsFadingIn ||
                _presentationEffects.FadeOpacity > 0f)
            {
                return;
            }
            _uiSession.HideFade();
            _closeFadeUiWhenTransparent = false;
        }

        private void RecoverOrphanedOpaqueFade()
        {
            if (!_gameStarted ||
                _startingNewGame ||
                _mapSwitchInProgress ||
                (_scriptSession?.IsRunning ?? true) ||
                _uiSession?.FadeVisible != true ||
                _presentationEffects == null ||
                _presentationEffects.IsFadingIn ||
                _presentationEffects.IsFadingOut ||
                _presentationEffects.FadeOpacity < 1f ||
                _video is JxqyUnityVideoPort video &&
                video.IsPresentationActive)
            {
                return;
            }

            Debug.LogWarning(
                "JXQY-SCRIPT recovered an opaque FadeOut with no active " +
                "script, video, map transition, or title transition.",
                this);
            RequestScreenFadeIn();
        }

        private void CreateUiSession()
        {
            _inventory = new JxqyInventory();
            _equipment = new JxqyEquipmentManager();
            _skills = new JxqySkillManager();
            _shop = new JxqyShop();
            _saveRepository = new JxqySaveRepository(
                new JxqyFilePersistencePort(
                    JxqyFilePersistencePort.RootForSaveNamespace(
                        _contentContext.SaveNamespace)));
            _uiSession = new JxqyUiSession
            {
                Player = _player,
                LayoutKind = _contentContext.CombinedCharacterSheet
                    ? JxqyUiLayoutKind.CombinedCharacterSheet
                    : JxqyUiLayoutKind.SeparatePanels,
                Inventory = _inventory,
                Equipment = _equipment,
                Skills = _skills,
                Shop = _shop,
                Memos = _memoEntries,
                CanSave = CanSaveGame,
                Npcs = _npcs?.Npcs ?? Array.Empty<JxqyNpc>(),
                TryMoveFromLittleMap = TryMovePlayerFromLittleMap,
                IsRunModifierHeld = () =>
                    IsRunRequested(_input.CaptureFrame()),
                ResolveCombatTargetNpc = () =>
                    _playerAutoAttack?.Target as JxqyNpc,
            };
            float musicVolume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(MusicVolumePreference, 1f));
            float soundVolume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(SoundVolumePreference, 1f));
            int gameSpeed = Mathf.Clamp(
                PlayerPrefs.GetInt(GameSpeedPreference, 2),
                0,
                2);
            _uiSession.SetOptionValues(
                musicVolume,
                soundVolume,
                gameSpeed);
            ApplyMusicVolume(musicVolume, false);
            ApplySoundVolume(soundVolume, false);
            ApplyGameSpeed(gameSpeed, false);
            for (int slot = 0; slot < 7; slot++)
            {
                _uiSession.SaveSlots.Add(new JxqySaveSlotView
                {
                    Slot = slot,
                    Exists = false,
                    Description = "空存档",
                });
            }
            _uiSession.NewGameRequested += StartNewGame;
            _uiSession.CreditsRequested += StartCredits;
            _uiSession.QuitRequested += ReturnToTitle;
            _uiSession.ExitApplicationRequested += ExitApplication;
            _uiSession.SaveRequested += OnSaveRequested;
            _uiSession.LoadRequested += OnLoadRequested;
            _uiSession.ItemScriptRequested += OnItemScriptRequested;
            _uiSession.SoundRequested += OnUiSoundRequested;
            _uiSession.MusicVolumeChanged += OnMusicVolumeChanged;
            _uiSession.SoundVolumeChanged += OnSoundVolumeChanged;
            _uiSession.GameSpeedChanged += OnGameSpeedChanged;
            _uiSession.ShowTitle();
            RefreshSaveSlotsAsync(
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private void CreateScriptSession()
        {
            _scriptSession = new JxqyPlayableScriptSession(
                _resources,
                _mapScope,
                _uiSession,
                PresentationCommands,
                new JxqyPlayableScriptBindings
                {
                    GetActiveMapName = () =>
                        GetMapDisplayName(ActiveMapStableId),
                    GetPlayer = () => _player,
                    GetPlayerKindCharacter = ResolvePlayerKindCharacter,
                    GetNpcs = () => _npcs,
                    GetObjects = () => _objects,
                    GetInventory = () => _inventory,
                    GetSkills = () => _skills,
                    GetCollisionMap = CreateLiveCollisionMap,
                    LoadNewGameAsync = LoadOriginalNewGameAsync,
                    SetCharacterActionFileAsync =
                        SetCharacterActionFileAsync,
                    PlayPlayerSpecialActionAsync =
                        PlayPlayerSpecialActionAsync,
                    LoadMapAsync = SwitchMapFromScriptAsync,
                    LoadNpcAsync = LoadNpcsFromScriptAsync,
                    LoadOneNpcAsync = LoadOneNpcsFromScriptAsync,
                    LoadObjAsync = LoadObjectsFromScriptAsync,
                    AddNpcAsync = AddNpcFromScriptAsync,
                    AddObjAsync = AddObjectFromScriptAsync,
                    DeleteNpc = DeleteNpcFromScript,
                    DeleteObj = DeleteObjectFromScript,
                    DeleteObjectInstance =
                        DeleteObjectInstanceFromScript,
                    ClearBodies = ClearBodiesFromScript,
                    SaveNpcSnapshot = SaveNpcSnapshot,
                    SaveObjectSnapshot = SaveObjectSnapshot,
                    LoadItemDefinitionAsync =
                        LoadItemDefinitionAsync,
                    LoadRandomItemDefinitionAsync =
                        LoadRandomItemDefinitionAsync,
                    LoadMagicDefinitionAsync =
                        LoadMagicDefinitionAsync,
                    LoadMagicDefinitionAtLevelAsync =
                        LoadMagicDefinitionAsync,
                    MergeNpcAsync = MergeNpcsFromScriptAsync,
                    SetCharacterResourceAsync =
                        SetCharacterResourceAsync,
                    PlayCharacterSpecialActionAsync =
                        PlayCharacterSpecialActionAsync,
                    OpenShopAsync = OpenShopAsync,
                    ChangePlayerAsync = ChangePlayerAsync,
                    SetInputDisabled =
                        value => _legacyInputDisabled = value,
                    SetNpcAiDisabled = SetNpcAiDisabled,
                    SetMapPosition = SetMapPosition,
                    SetNamedMapTrap = SetNamedMapTrap,
                    SaveMapTrapSnapshot =
                        () => _savedTrapRegistry =
                            _trapRegistry.Clone(),
                    FreeMap = FreeMapFromScript,
                    OpenTimeLimit = OpenTimeLimit,
                    CloseTimeLimit = CloseTimeLimit,
                    HideTimerWindow = HideTimerWindow,
                    SetTimeScript = SetTimeScript,
                    GetTalkText = GetTalkText,
                    ShowMessage = text =>
                        _uiSession.ShowMessage(text),
                    ShowSystemMessage = (text, duration) =>
                        _uiSession.ShowSystemMessage(text, duration),
                    CenterCameraOnPlayer = CenterCameraOnPlayer,
                    CenterCameraOnCharacter = CenterCameraOnCharacter,
                    HandleScriptedPlayerPositionSet =
                        HandleScriptedPlayerPositionSet,
                    RefreshActorVisual = RefreshActorVisual,
                    GetTalkLines = GetTalkLines,
                    SetMapTrap = SetMapTrap,
                    AddMemo = AddMemo,
                    AddMemoText = AddMemoText,
                    DeleteMemo = DeleteMemo,
                    EquipGoods = EquipGoods,
                    AddPlayerExperienceAsync =
                        AddPlayerExperienceFromScriptAsync,
                    SetLevelFileAsync = LoadLevelFileAsync,
                    SetPlayerLevelAsync = SetPlayerLevelAsync,
                    SetNpcKind = SetNpcKind,
                    SetNpcLevel = SetNpcLevel,
                    SetPartnerLevel = SetPartnerLevel,
                    NpcSkillsChanged = CacheOriginalCharacterSkills,
                    SetSaveDisabled = value => _saveDisabled = value,
                    ClearAllSavesAsync = ClearAllSavesAsync,
                    SetDropDisabled = value =>
                        _dropGoodWhenDefeatEnemyDisabled = value,
                    SetShowMapPosition = value =>
                        _showMapPosition = value,
                    StopSounds = () => _audio?.StopSounds(),
                    UsePlayerMagic = UsePlayerMagicFromScript,
                    PerformNpcAttack = BeginForcedScriptedAttackAt,
                    PerformNpcMagic = BeginScriptedMagicAt,
                    ReturnToTitle = ReturnToTitle,
                },
                _contentContext.ScriptCatalogAddress,
                _contentContext.PortraitsAddress,
                _contentContext.ScriptDialectId);
        }

        private void StartNewGame()
        {
            if (_startingNewGame)
                return;
            _startingNewGame = true;
            _newGameOpeningVideoPending = true;
            // Keep a real opaque fade beneath the topmost video overlay. When
            // the movie is skipped or ends, its overlay can disappear without
            // exposing the player while LoadGame is still creating visuals.
            _presentationEffects.HoldFadeOpaque();
            if (_video is JxqyUnityVideoPort unityVideo)
                unityVideo.ShowBlackTransition();
            _uiSession.ShowFade(1f);
            StartNewGameAsync(
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private void StartCredits()
        {
            _gameStarted = false;
            _scriptFaultReported = false;
            _uiSession.Open(JxqyUiScreen.Hud);
            _input.ResetTransientState();
            _scriptSession.StartAsync(
                    _contentContext.ContentProfile.CreditsScriptFileName,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private async UniTask StartNewGameAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                _scriptFaultReported = false;
                _gameStarted = true;
                _uiSession.Open(JxqyUiScreen.Hud);
                _input.ResetTransientState();
                await _scriptSession.StartAsync(
                    _contentContext.EntryScriptFileName,
                    cancellationToken,
                    resetVariables: true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ReturnToTitle();
            }
            finally
            {
                _startingNewGame = false;
            }
        }

#if false // Retired eager loading of all legacy UI atlases.
        private void StartNewGameLegacy()
        {
            if (_startingNewGame)
                return;
            _startingNewGame = true;
            _uiSession.ShowTitle();
            StartNewGameAsync().Forget();
        }

        private async UniTask StartNewGameAsync()
        {
            try
            {
                if (!_legacyUiReady && !_legacyUiLoading)
                {
                    LoadLegacyGameUiInBackgroundAsync(
                            this.GetCancellationTokenOnDestroy())
                        .Forget();
                }
                await UniTask.WaitUntil(
                    () => _legacyUiReady ||
                          _legacyUiLoadError != null,
                    cancellationToken:
                        this.GetCancellationTokenOnDestroy());
                if (_legacyUiLoadError != null)
                {
                    throw new InvalidOperationException(
                        "原版游戏 UI 资源加载失败。",
                        _legacyUiLoadError);
                }
                _gameStarted = true;
                _uiSession.Open(JxqyUiScreen.Hud);
                _input.ResetTransientState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ReturnToTitle();
            }
            finally
            {
                _startingNewGame = false;
            }
        }

#endif

        private async UniTask LoadOriginalNewGameAsync()
        {
            CancellationToken cancellationToken =
                this.GetCancellationTokenOnDestroy();
            _savedTrapRegistry = new JxqyTrapRegistry();
            JxqyAssetLease<TextAsset> stateLease =
                await LoadTextAsync(
                    _contentContext.NewGameStateAddress,
                    cancellationToken);
            Dictionary<string, Dictionary<string, string>> stateSections =
                JxqyLegacySaveImporter.ParseIni(stateLease.Asset.text);
            if (!stateSections.TryGetValue(
                    "State",
                    out Dictionary<string, string> state))
            {
                throw new InvalidOperationException(
                    "The original new-game Game.ini has no State section.");
            }
            string mapFileName = GetIniValue(state, "Map");
            if (string.IsNullOrWhiteSpace(ActiveMapStableId) ||
                !string.Equals(
                    GetLegacyMapFileName(ActiveMapStableId),
                    mapFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                await SwitchMapFromScriptAsync(mapFileName);
            }

            JxqyAssetLease<TextAsset> playerLease =
                await LoadTextAsync(
                    _contentContext.PlayerProfileAddress,
                    cancellationToken);
            CreatePlayer();
            ApplyOriginalPlayerIni(playerLease.Asset.text);
            _playerScriptActions.Clear();
            await SetCharacterResourceAsync(
                _player,
                GetPlayerResourceFileName(playerLease.Asset.text));
            await PrepareCharacterBasicMagicsAsync(
                _player,
                cancellationToken);
            _playerSpecialAction = null;
            _playerIndex = 0;
            _uiSession?.SetPlayerIndex(0, notify: false);
            _playerProfiles.Clear();
            _originalCharacterSkills.Clear();
            _levelFileName = string.Empty;
            _backgroundMusicAddress = string.Empty;
            _saveDisabled = false;
            _dropGoodWhenDefeatEnemyDisabled = false;
            _showMapPosition = ParseIniInteger(
                state,
                "ScriptShowMapPos",
                0) != 0;
            _levelEntries.Clear();
            _levelRewardMagics.Clear();
            _levelRewardItems.Clear();
            _memoEntries.Clear();
            _inventory = new JxqyInventory();
            _equipment = new JxqyEquipmentManager();
            _skills = new JxqySkillManager();
            _shop = new JxqyShop();
            _uiSession.Player = _player;
            _uiSession.Inventory = _inventory;
            _uiSession.Equipment = _equipment;
            _uiSession.Skills = _skills;
            _uiSession.Shop = _shop;

            JxqyAssetLease<TextAsset> goodsLease =
                await LoadTextAsync(
                    _contentContext.NewGameGoodsAddress,
                    cancellationToken);
            await LoadInitialGoodsAsync(
                goodsLease.Asset.text,
                cancellationToken);
            JxqyAssetLease<TextAsset> magicLease =
                await LoadTextAsync(
                    _contentContext.NewGameMagicAddress,
                    cancellationToken);
            await LoadInitialMagicAsync(
                magicLease.Asset.text,
                cancellationToken);
            CacheCurrentPlayerSkills();
#if UNITY_EDITOR
            _acceptanceNewGameInventoryEntryCount =
                _inventory.Entries.Count;
            _acceptanceNewGameEquipmentEntryCount =
                _equipment.EquippedEntries.Count;
            _acceptanceNewGameSkillCount = _skills.Skills.Count;
#endif

            string npcFileName = GetIniValue(state, "Npc");
            if (HasDynamicText(
                    _contentContext.SnapshotTemplateRelativeDirectory,
                    npcFileName))
                await LoadNpcsFromScriptAsync(npcFileName);
            else
            {
                ClearNpcActors();
                // Game.ini still establishes the active legacy snapshot name
                // even when the corresponding initial .npc payload is absent.
                // Trap scripts immediately call parameterless SaveNpc(), which
                // must snapshot the resulting empty/follower-only actor state.
                _activeNpcFileName =
                    SafeLegacyFileName(npcFileName, ".npc");
            }
            string objectFileName = GetIniValue(state, "Obj");
            await LoadObjectsFromScriptAsync(objectFileName);
            ApplyOriginalGameOptions(stateSections);

            JxqyAssetLease<TextAsset> trapsLease =
                await LoadTextAsync(
                    _contentContext.NewGameTrapsAddress,
                    cancellationToken);
            ParseTraps(trapsLease.Asset.text);
            CenterCameraOnPlayer();
            SubmitFrame();
            if (Application.isEditor && !Application.isBatchMode)
                await ValidateWorldRenderingAsync(cancellationToken);
        }

        private void ApplyOriginalPlayerIni(
            string playerIniText,
            bool updateVisual = true)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(playerIniText);
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    "The original new-game Player0.ini is invalid.");
            }
            _player.Name = GetIniValue(init, "Name");
            _player.CurrentDirection =
                ParseIniInteger(init, "Dir", 0);
            _player.TilePosition = new JxqyIntPoint(
                ParseIniInteger(init, "MapX", 0),
                ParseIniInteger(init, "MapY", 0));
            _player.Relation =
                (JxqyRelationType)ParseIniInteger(
                    init,
                    "Relation",
                    0);
            _player.LifeMax =
                ParseIniInteger(init, "LifeMax", 0);
            _player.ThewMax =
                ParseIniInteger(init, "ThewMax", 0);
            _player.ManaMax =
                ParseIniInteger(init, "ManaMax", 0);
            _player.Life =
                ParseIniInteger(init, "Life", _player.LifeMax);
            _player.Thew =
                ParseIniInteger(init, "Thew", _player.ThewMax);
            _player.Mana =
                ParseIniInteger(init, "Mana", _player.ManaMax);
            _player.Attack =
                ParseIniInteger(init, "Attack", 0);
            _player.Attack2 =
                ParseIniInteger(init, "Attack2", 0);
            _player.Attack3 =
                ParseIniInteger(init, "Attack3", 0);
            _player.Defend =
                ParseIniInteger(init, "Defend", 0);
            _player.Defend2 =
                ParseIniInteger(init, "Defend2", 0);
            _player.Defend3 =
                ParseIniInteger(init, "Defend3", 0);
            _player.Evade =
                ParseIniInteger(init, "Evade", 0);
            _player.Experience =
                ParseIniInteger(init, "Exp", 0);
            _player.ExpBonus =
                ParseIniInteger(init, "ExpBonus", 0);
            _player.LevelUpExperience =
                ParseIniInteger(init, "LevelUpExp", 0);
            _player.Level =
                ParseIniInteger(init, "Level", 1);
            _player.AttackLevel =
                ParseIniInteger(init, "AttackLevel", 1);
            _player.DialogRadius =
                ParseIniInteger(init, "DialogRadius", 1);
            _player.Money =
                ParseIniInteger(init, "Money", 0);
            _player.MagicFileName =
                GetIniValue(init, "FlyIni");
            _player.MagicFileName2 =
                GetIniValue(init, "FlyIni2");
            _player.DeathScriptAddress =
                GetIniValue(init, "DeathScript");
            _player.RetaliationMagicFileName =
                GetIniValue(init, "MagicToUseWhenBeAttacked");
            _player.MagicDirectionWhenBeAttacked =
                ParseIniInteger(
                    init,
                    "MagicDirectionWhenBeAttacked",
                    0);
            _player.WalkIsRun =
                ParseIniInteger(init, "WalkIsRun", 0) != 0;
            _player.ManaLimit =
                ParseIniInteger(init, "ManaLimit", 0) != 0;
            _player.IsRunDisabled =
                ParseIniInteger(init, "IsRunDisabled", 0) != 0;
            _player.IsJumpDisabled =
                ParseIniInteger(init, "IsJumpDisabled", 0) != 0;
            _player.IsFightDisabled =
                ParseIniInteger(init, "IsFightDisabled", 0) != 0;
            _player.IsNotUseThewWhenRun = false;
            _player.IsManaRestore = false;
            _player.IsMovementDisabled = false;
            _player.IsInTransport = false;
            _player.Invincible =
                ParseIniInteger(init, "Invincible", 0) != 0;
            _player.AdditionalBasicMagics.Clear();
            _player.AddLifeRestorePercent =
                ParseIniInteger(
                    init,
                    "AddLifeRestorePercent",
                    0);
            _player.AddThewRestorePercent =
                ParseIniInteger(
                    init,
                    "AddThewRestorePercent",
                    0);
            _player.AddManaRestorePercent =
                ParseIniInteger(
                    init,
                    "AddManaRestorePercent",
                    0);
            ParseAdditionalBasicMagicFiles(
                _player,
                GetIniValue(init, "FlyInis"));
            _player.SetFighting(
                ParseIniInteger(init, "Fight", 0) != 0);
            _player.SetState(
                (JxqyCharacterState)ParseIniInteger(
                    init,
                    "State",
                    0));
            if (updateVisual)
                UpdatePlayerVisual();
        }

        private static string GetPlayerResourceFileName(
            string playerIniText)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(playerIniText);
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    "The original player INI is invalid.");
            }
            return GetIniValue(init, "NpcIni");
        }

        private static string GetPlayerLevelFileName(
            string playerIniText)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(playerIniText);
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    "The original player INI is invalid.");
            }
            return GetIniValue(init, "LevelIni");
        }

        private async UniTask LoadInitialGoodsAsync(
            string goodsListText,
            CancellationToken cancellationToken)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(goodsListText);
            bool usesDaoJianSaveSlots = UsesDaoJianSaveSlotSemantics();
            int count = 0;
            bool hasDeclaredCount = sections.TryGetValue(
                "Head",
                out Dictionary<string, string> head);
            hasDeclaredCount = hasDeclaredCount &&
                head.TryGetValue("Count", out string countText) &&
                int.TryParse(
                    countText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out count);
            if (!hasDeclaredCount && !usesDaoJianSaveSlots)
            {
                throw new InvalidOperationException(
                    "The original new-game Goods0.ini is invalid.");
            }
            IEnumerable<KeyValuePair<int, Dictionary<string, string>>>
                entries = usesDaoJianSaveSlots
                    ? EnumerateNumericIniSections(sections)
                    : Enumerable.Range(1, count)
                        .Select(index =>
                        {
                            sections.TryGetValue(
                                index.ToString(
                                    CultureInfo.InvariantCulture),
                                out Dictionary<string, string> entry);
                            return new KeyValuePair<int,
                                Dictionary<string, string>>(index, entry);
                        });

            foreach (KeyValuePair<int, Dictionary<string, string>> slot in
                     entries)
            {
                int index = slot.Key;
                Dictionary<string, string> entry = slot.Value;
                if (entry == null ||
                    !entry.TryGetValue(
                        "IniFile",
                        out string iniFileName))
                {
                    throw new InvalidOperationException(
                        $"The original new-game goods entry {index} is invalid.");
                }
                int quantity = ParseIniInteger(entry, "Number", 1);
                string itemAddress = _contentContext.TextAddress(
                    "ini/goods",
                    iniFileName);
                JxqyAssetLease<TextAsset> itemLease =
                    await LoadTextAsync(
                        itemAddress,
                        cancellationToken);
                JxqyItemDefinition definition =
                    ParseItemDefinition(
                        iniFileName,
                        itemLease.Asset.text);
                bool restored =
                    usesDaoJianSaveSlots &&
                    JxqyEquipmentManager.TryGetSlotByLegacyListIndex(
                        index,
                        out _)
                        ? _equipment.RestoreEquippedEntry(
                            definition,
                            quantity,
                            0f,
                            index)
                        : _inventory.Add(
                            definition,
                            quantity,
                            index);
                if (!restored)
                {
                    throw new InvalidOperationException(
                        $"Cannot add original new-game item '{iniFileName}'.");
                }
            }
        }

        private async UniTask LoadInitialMagicAsync(
            string magicListText,
            CancellationToken cancellationToken,
            JxqySkillManager targetSkills = null,
            string sourceLabel = "Magic0.ini")
        {
            targetSkills ??= _skills;
            if (targetSkills == null)
            {
                throw new InvalidOperationException(
                    $"Cannot load original magic list '{sourceLabel}'.");
            }
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(magicListText);
            bool usesDaoJianSaveSlots = UsesDaoJianSaveSlotSemantics();
            int count = 0;
            bool hasDeclaredCount = sections.TryGetValue(
                    "Head",
                    out Dictionary<string, string> head) &&
                head.TryGetValue("Count", out string countText) &&
                int.TryParse(
                    countText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out count);
            if (!hasDeclaredCount && !usesDaoJianSaveSlots)
            {
                throw new InvalidOperationException(
                    $"The original magic list '{sourceLabel}' is invalid.");
            }
            int loaded = 0;
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!int.TryParse(
                        section.Key,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int legacyListIndex))
                {
                    continue;
                }
                string iniFileName =
                    GetIniValue(section.Value, "IniFile");
                if (string.IsNullOrWhiteSpace(iniFileName))
                    continue;
                int level =
                    ParseIniInteger(section.Value, "Level", 1);
                JxqyMagicDefinition definition =
                    await LoadMagicDefinitionAsync(
                        iniFileName,
                        level);
                if (!targetSkills.RestoreEntry(
                        definition,
                        level,
                        ParseIniInteger(section.Value, "Exp", 0),
                        0,
                        ParseIniInteger(section.Value, "HideCount", 0),
                        legacyListIndex))
                {
                    throw new InvalidOperationException(
                        $"Cannot add original magic from '{sourceLabel}': " +
                        $"'{iniFileName}'.");
                }
                loaded++;
            }
            if (!usesDaoJianSaveSlots && loaded != count)
            {
                throw new InvalidOperationException(
                    $"The original '{sourceLabel}' declares {count} " +
                    $"entries but {loaded} were loaded.");
            }
        }

        private bool UsesDaoJianSaveSlotSemantics()
        {
            return JxqyLegacyScriptCommands.IsDaoJian543Dialect(
                _contentContext.ScriptDialectId);
        }

        private static IReadOnlyList<KeyValuePair<int,
            Dictionary<string, string>>> EnumerateNumericIniSections(
            IReadOnlyDictionary<string, Dictionary<string, string>> sections)
        {
            var result = new List<KeyValuePair<int,
                Dictionary<string, string>>>();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!int.TryParse(
                        section.Key,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int legacyIndex) ||
                    legacyIndex <= 0)
                {
                    continue;
                }
                result.Add(new KeyValuePair<int,
                    Dictionary<string, string>>(
                    legacyIndex,
                    section.Value));
            }
            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result;
        }

        private void ApplyOriginalGameOptions(
            Dictionary<string, Dictionary<string, string>> sections)
        {
            if (!sections.TryGetValue(
                    "Option",
                    out Dictionary<string, string> option))
            {
                return;
            }
            _presentationEffects.EndRain();
            string rainFile = GetIniValue(option, "RainFile");
            if (!string.IsNullOrWhiteSpace(rainFile))
                _presentationEffects.BeginRain(rainFile);
            _presentationEffects.ShowSnow(
                ParseIniInteger(option, "SnowShow", 0) != 0);
            _presentationEffects.MapTime =
                ParseIniInteger(option, "MapTime", 0);
            _presentationEffects.WaterEffectEnabled =
                ParseIniInteger(option, "Water", 0) != 0;
            _saveDisabled =
                ParseIniInteger(option, "SaveDisabled", 0) != 0;
            _dropGoodWhenDefeatEnemyDisabled =
                ParseIniInteger(
                    option,
                    "IsDropGoodWhenDefeatEnemyDisabled",
                    0) != 0;
        }

        private static JxqyItemDefinition ParseItemDefinition(
            string iniFileName,
            string iniText)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(iniText);
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    $"Original item '{iniFileName}' has no Init section.");
            }
            int legacyKind = ParseIniInteger(init, "Kind", 0);
            var item = new JxqyItemDefinition
            {
                Id = iniFileName,
                Name = GetIniValue(init, "Name"),
                Introduction = GetIniValue(init, "Intro"),
                ImageFileName = GetIniValue(init, "Image"),
                IconFileName = GetIniValue(init, "Icon"),
                Kind = legacyKind switch
                {
                    1 => JxqyItemKind.Equipment,
                    2 => JxqyItemKind.Event,
                    _ => JxqyItemKind.Drug,
                },
                Life = ParseIniInteger(init, "Life", 0),
                Thew = ParseIniInteger(init, "Thew", 0),
                Mana = ParseIniInteger(init, "Mana", 0),
                MinimumUserLevel =
                    ParseIniInteger(init, "MinUserLevel", 0),
                ExplicitCost = ParseIniInteger(init, "Cost", 0),
                ExplicitSellPrice =
                    ParseIniInteger(init, "SellPrice", 0),
                CooldownMilliseconds =
                    ParseIniInteger(init, "ColdMilliSeconds", 0),
                NoNeedToEquip =
                    ParseIniInteger(init, "NoNeedToEquip", 0) != 0,
                UseScript = GetIniValue(init, "Script"),
            };
            string part = GetIniValue(init, "Part");
            if (!string.IsNullOrWhiteSpace(part) &&
                Enum.TryParse(
                    part,
                    true,
                    out JxqyEquipmentSlot slot))
            {
                item.Slot = slot;
            }
            item.Modifiers.LifeMax =
                ParseIniInteger(init, "LifeMax", 0);
            item.Modifiers.ThewMax =
                ParseIniInteger(init, "ThewMax", 0);
            item.Modifiers.ManaMax =
                ParseIniInteger(init, "ManaMax", 0);
            item.Modifiers.Attack =
                ParseIniInteger(init, "Attack", 0);
            item.Modifiers.Attack2 =
                ParseIniInteger(init, "Attack2", 0);
            item.Modifiers.Attack3 =
                ParseIniInteger(init, "Attack3", 0);
            item.Modifiers.Defend =
                ParseIniInteger(init, "Defend", 0);
            item.Modifiers.Defend2 =
                ParseIniInteger(init, "Defend2", 0);
            item.Modifiers.Defend3 =
                ParseIniInteger(init, "Defend3", 0);
            item.Modifiers.Evade =
                ParseIniInteger(init, "Evade", 0);
            item.Modifiers.MoveSpeedPercent =
                ParseIniInteger(init, "ChangeMoveSpeedPercent", 0);
            item.EffectKind = ParseItemEffectKind(
                item.Kind,
                item.Slot,
                ParseIniInteger(init, "EffectType", 0));
            return item;
        }

        private static JxqyItemEffectKind ParseItemEffectKind(
            JxqyItemKind kind,
            JxqyEquipmentSlot slot,
            int effectType)
        {
            if (kind == JxqyItemKind.Drug)
            {
                return effectType switch
                {
                    1 => JxqyItemEffectKind.ClearFrozen,
                    2 => JxqyItemEffectKind.ClearPoison,
                    3 => JxqyItemEffectKind.ClearPetrifaction,
                    _ => JxqyItemEffectKind.None,
                };
            }
            if (kind != JxqyItemKind.Equipment)
                return JxqyItemEffectKind.None;
            if (effectType == 1)
            {
                return slot switch
                {
                    JxqyEquipmentSlot.Foot =>
                        JxqyItemEffectKind.ThewNotLoseWhenRun,
                    JxqyEquipmentSlot.Neck =>
                        JxqyItemEffectKind.ManaRestore,
                    JxqyEquipmentSlot.Hand =>
                        JxqyItemEffectKind.EnemyFrozen,
                    _ => JxqyItemEffectKind.None,
                };
            }
            if (slot != JxqyEquipmentSlot.Hand)
                return JxqyItemEffectKind.None;
            return effectType switch
            {
                2 => JxqyItemEffectKind.EnemyPoisoned,
                3 => JxqyItemEffectKind.EnemyPetrified,
                _ => JxqyItemEffectKind.None,
            };
        }

        private async UniTask LoadOriginalCharacterNamesAsync(
            CancellationToken cancellationToken)
        {
            _originalCharacterProfileIniTexts.Clear();
            var names = new List<string>(
                JxqyUiSession.LastOriginalPlayerIndex + 1);
            for (int index = 0;
                 index <= JxqyUiSession.LastOriginalPlayerIndex;
                 index++)
            {
                JxqyAssetLease<TextAsset> lease = await LoadTextAsync(
                    _contentContext.PlayerAddress(index),
                    cancellationToken);
                Dictionary<string, Dictionary<string, string>> sections =
                    JxqyLegacySaveImporter.ParseIni(lease.Asset.text);
                _originalCharacterProfileIniTexts[index] =
                    lease.Asset.text;
                if (!sections.TryGetValue(
                        "Init",
                        out Dictionary<string, string> init) ||
                    string.IsNullOrWhiteSpace(GetIniValue(init, "Name")))
                {
                    throw new InvalidDataException(
                        $"Player{index}.ini has no configured character name.");
                }
                names.Add(GetIniValue(init, "Name"));
            }
            JxqyOriginalCharacterCatalog.Configure(names);
        }

        private async UniTask LoadOriginalCharacterLevelEntriesAsync(
            CancellationToken cancellationToken)
        {
            _originalCharacterLevelEntries.Clear();
            var loadedByFileName = new Dictionary<
                string,
                Dictionary<int, JxqyLevelEntry>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, string> profile in
                     _originalCharacterProfileIniTexts)
            {
                string fileName = GetPlayerLevelFileName(profile.Value);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "level-hard.ini";
                string safeFileName = Path.GetFileName(
                    fileName.Replace('\\', '/'));
                if (!loadedByFileName.TryGetValue(
                        safeFileName,
                        out Dictionary<int, JxqyLevelEntry> entries))
                {
                    JxqyAssetLease<TextAsset> lease = await LoadTextAsync(
                        _contentContext.TextAddress(
                            "ini/level",
                            safeFileName),
                        cancellationToken);
                    entries = ParsePlayerLevelEntries(
                        lease.Asset.text,
                        safeFileName);
                    loadedByFileName[safeFileName] = entries;
                }
                _originalCharacterLevelEntries[profile.Key] = entries;
            }
        }

        private static Dictionary<int, JxqyLevelEntry>
            ParsePlayerLevelEntries(string text, string fileName)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            var entries = new Dictionary<int, JxqyLevelEntry>();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!section.Key.StartsWith(
                        "Level",
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(
                        section.Key.Substring("Level".Length),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int level))
                {
                    continue;
                }
                Dictionary<string, string> values = section.Value;
                entries[level] = new JxqyLevelEntry
                {
                    LevelUpExperience =
                        ParseIniInteger(values, "LevelUpExp", 0),
                    LifeMax = ParseIniInteger(values, "LifeMax", 0),
                    ThewMax = ParseIniInteger(values, "ThewMax", 0),
                    ManaMax = ParseIniInteger(values, "ManaMax", 0),
                    Attack = ParseIniInteger(values, "Attack", 0),
                    Attack2 = ParseIniInteger(values, "Attack2", 0),
                    Attack3 = ParseIniInteger(values, "Attack3", 0),
                    Defend = ParseIniInteger(values, "Defend", 0),
                    Defend2 = ParseIniInteger(values, "Defend2", 0),
                    Defend3 = ParseIniInteger(values, "Defend3", 0),
                    Evade = ParseIniInteger(values, "Evade", 0),
                    NewMagic = GetIniValue(values, "NewMagic"),
                    NewGood = GetIniValue(values, "NewGood"),
                };
            }
            if (entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Legacy level file '{fileName}' contains no levels.");
            }
            return entries;
        }

        private async UniTask LoadNpcLevelEntriesAsync(
            CancellationToken cancellationToken)
        {
            JxqyAssetLease<TextAsset> lease = await LoadTextAsync(
                _contentContext.NpcLevelsAddress,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(lease.Asset.text);
            _npcLevelEntries.Clear();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!section.Key.StartsWith(
                        "Level",
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(
                        section.Key.Substring("Level".Length),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int level))
                {
                    continue;
                }
                Dictionary<string, string> values = section.Value;
                _npcLevelEntries[level] = new JxqyLevelEntry
                {
                    LevelUpExperience = ParseIniInteger(values, "Exp", 0),
                    LifeMax = ParseIniInteger(values, "Life", 0),
                    ThewMax = ParseIniInteger(values, "Thew", 0),
                    ManaMax = ParseIniInteger(values, "Mana", 0),
                    Attack = ParseIniInteger(values, "Attack", 0),
                    Attack2 = ParseIniInteger(values, "Attack2", 0),
                    Attack3 = ParseIniInteger(values, "Attack3", 0),
                    Defend = ParseIniInteger(values, "Defend", 0),
                    Defend2 = ParseIniInteger(values, "Defend2", 0),
                    Defend3 = ParseIniInteger(values, "Defend3", 0),
                    Evade = ParseIniInteger(values, "Evade", 0),
                };
            }
            if (_npcLevelEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    "Legacy NPC level file contains no levels.");
            }
        }

        private UniTask LoadLevelFileAsync(string fileName)
        {
            return LoadLevelFileAsync(fileName, applyCurrentLevel: true);
        }

        private async UniTask LoadLevelFileAsync(
            string fileName,
            bool applyCurrentLevel)
        {
            string safeFileName = Path.GetFileName(
                (fileName ?? string.Empty).Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                !safeFileName.EndsWith(
                    ".ini",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Invalid legacy level file '{fileName}'.",
                    nameof(fileName));
            }
            string address = _contentContext.TextAddress(
                "ini/level",
                safeFileName);
            JxqyAssetLease<TextAsset> lease =
                await LoadTextAsync(
                    address,
                    this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(lease.Asset.text);
            _levelEntries.Clear();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!section.Key.StartsWith(
                        "Level",
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(
                        section.Key.Substring("Level".Length),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int level))
                {
                    continue;
                }
                Dictionary<string, string> values = section.Value;
                _levelEntries[level] = new JxqyLevelEntry
                {
                    LevelUpExperience =
                        ParseIniInteger(values, "LevelUpExp", 0),
                    LifeMax =
                        ParseIniInteger(values, "LifeMax", 0),
                    ThewMax =
                        ParseIniInteger(values, "ThewMax", 0),
                    ManaMax =
                        ParseIniInteger(values, "ManaMax", 0),
                    Attack =
                        ParseIniInteger(values, "Attack", 0),
                    Attack2 =
                        ParseIniInteger(values, "Attack2", 0),
                    Attack3 =
                        ParseIniInteger(values, "Attack3", 0),
                    Defend =
                        ParseIniInteger(values, "Defend", 0),
                    Defend2 =
                        ParseIniInteger(values, "Defend2", 0),
                    Defend3 =
                        ParseIniInteger(values, "Defend3", 0),
                    Evade =
                        ParseIniInteger(values, "Evade", 0),
                    NewMagic = GetIniValue(values, "NewMagic"),
                    NewGood = GetIniValue(values, "NewGood"),
                };
            }
            if (_levelEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Legacy level file '{fileName}' contains no levels.");
            }
            await LoadLevelRewardDefinitionsAsync();
            _levelFileName = safeFileName;
            // SetLevelFile is an active difficulty/ruleset change in the
            // original scripts. Merely swapping the table leaves the current
            // level's maxima, defence and next threshold from the old table.
            if (applyCurrentLevel && _player != null)
                SetPlayerLevel(_player.Level);
        }

        private async UniTask LoadLevelRewardDefinitionsAsync()
        {
            _levelRewardMagics.Clear();
            _levelRewardItems.Clear();
            foreach (string fileName in _levelEntries.Values
                         .Select(entry => entry.NewMagic)
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _levelRewardMagics[fileName] =
                    await LoadMagicDefinitionAsync(fileName);
            }
            foreach (string fileName in _levelEntries.Values
                         .Select(entry => entry.NewGood)
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _levelRewardItems[fileName] =
                    await LoadItemDefinitionAsync(fileName);
            }
        }

        private UniTask SetPlayerLevelAsync(int level)
        {
            // Character.SetLevelTo assigns the requested level and copies
            // table values when present; unlike natural LevelUpTo it does
            // not grant NewMagic/NewGood rewards.
            SetPlayerLevel(level);
            return UniTask.CompletedTask;
        }

        private JxqyLevelEntry SetPlayerLevel(int level)
        {
            if (!_levelEntries.TryGetValue(
                    level,
                    out JxqyLevelEntry entry))
            {
                _player.Level = level;
                return null;
            }
            _player.Level = level;
            _player.LifeMax = entry.LifeMax;
            _player.ThewMax = entry.ThewMax;
            _player.ManaMax = entry.ManaMax;
            _player.Life = _player.LifeMax;
            _player.Thew = _player.ThewMax;
            _player.Mana = _player.ManaMax;
            _player.Attack = entry.Attack;
            _player.Attack2 = entry.Attack2;
            _player.Attack3 = entry.Attack3;
            _player.Defend = entry.Defend;
            _player.Defend2 = entry.Defend2;
            _player.Defend3 = entry.Defend3;
            _player.Evade = entry.Evade;
            _player.LevelUpExperience =
                entry.LevelUpExperience;
            return entry;
        }

        private void SetNpcLevel(JxqyNpc npc, int level)
        {
            if (npc == null)
                throw new ArgumentNullException(nameof(npc));
            if (!_npcLevelEntries.TryGetValue(
                    level,
                    out JxqyLevelEntry entry))
            {
                npc.Level = level;
                return;
            }
            npc.Level = level;
            npc.LifeMax = entry.LifeMax;
            npc.ThewMax = entry.ThewMax;
            npc.ManaMax = entry.ManaMax;
            npc.Life = npc.LifeMax;
            npc.Thew = npc.ThewMax;
            npc.Mana = npc.ManaMax;
            npc.Attack = entry.Attack;
            npc.Attack2 = entry.Attack2;
            npc.Attack3 = entry.Attack3;
            npc.Defend = entry.Defend;
            npc.Defend2 = entry.Defend2;
            npc.Defend3 = entry.Defend3;
            npc.Evade = entry.Evade;
            npc.LevelUpExperience = entry.LevelUpExperience;
        }

        private void SetNpcKind(
            JxqyNpc npc,
            JxqyCharacterKind kind)
        {
            if (npc == null)
                throw new ArgumentNullException(nameof(npc));
            if (npc.Kind != JxqyCharacterKind.Follower &&
                kind == JxqyCharacterKind.Follower &&
                npc.CanLevelUp <= 0)
            {
                ApplyOriginalPartnerProfile(npc);
            }
            npc.Kind = kind;
            if (kind == JxqyCharacterKind.Follower)
            {
                SynchronizeOriginalFollowerInitialAttackRadius(npc);
                CacheOriginalCharacterSkills(npc);
            }
            RefreshActorVisual(npc);
        }

        private void ApplyOriginalPartnerProfile(JxqyNpc npc)
        {
            int profileIndex =
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name);
            if (profileIndex < 0 ||
                !_originalCharacterProfileIniTexts.TryGetValue(
                    profileIndex,
                    out string profileText))
            {
                return;
            }
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(profileText);
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    $"Player{profileIndex}.ini has no Init section.");
            }

            // Original SetNpcKind(..., 3) loads the named PlayerN.ini profile
            // into the existing map NPC. Identity, position, relation, scripts
            // and visuals stay owned by that NPC; only the original partner's
            // combat/progression profile is replaced here.
            npc.LifeMax = ParseIniInteger(init, "LifeMax", npc.LifeMax);
            npc.ThewMax = ParseIniInteger(init, "ThewMax", npc.ThewMax);
            npc.ManaMax = ParseIniInteger(init, "ManaMax", npc.ManaMax);
            npc.Life = ParseIniInteger(init, "Life", npc.LifeMax);
            npc.Thew = ParseIniInteger(init, "Thew", npc.ThewMax);
            npc.Mana = ParseIniInteger(init, "Mana", npc.ManaMax);
            npc.Attack = ParseIniInteger(init, "Attack", npc.Attack);
            npc.Attack2 = ParseIniInteger(init, "Attack2", npc.Attack2);
            npc.Attack3 = ParseIniInteger(init, "Attack3", npc.Attack3);
            npc.Defend = ParseIniInteger(init, "Defend", npc.Defend);
            npc.Defend2 = ParseIniInteger(init, "Defend2", npc.Defend2);
            npc.Defend3 = ParseIniInteger(init, "Defend3", npc.Defend3);
            npc.Evade = ParseIniInteger(init, "Evade", npc.Evade);
            npc.Experience =
                ParseIniInteger(init, "Exp", npc.Experience);
            npc.ExpBonus =
                ParseIniInteger(init, "ExpBonus", npc.ExpBonus);
            npc.LevelUpExperience = ParseIniInteger(
                init,
                "LevelUpExp",
                npc.LevelUpExperience);
            npc.Level = ParseIniInteger(init, "Level", npc.Level);
            npc.AttackLevel = ParseIniInteger(
                init,
                "AttackLevel",
                npc.AttackLevel);
            npc.IdleFrames = ParseIniInteger(
                init,
                "Idle",
                npc.IdleFrames);
            npc.AttackRadius = ParseIniInteger(
                init,
                "AttackRadius",
                npc.AttackRadius);
            npc.MagicFileName = GetIniValue(init, "FlyIni");
            npc.MagicFileName2 = GetIniValue(init, "FlyIni2");
            string deathScript = GetIniValue(init, "DeathScript");
            if (!string.IsNullOrWhiteSpace(deathScript))
                npc.DeathScriptAddress = deathScript;
            npc.RetaliationMagicFileName =
                GetIniValue(init, "MagicToUseWhenBeAttacked");
            npc.MagicDirectionWhenBeAttacked = ParseIniInteger(
                init,
                "MagicDirectionWhenBeAttacked",
                npc.MagicDirectionWhenBeAttacked);
            npc.Invincible =
                ParseIniInteger(init, "Invincible", 0) != 0;
            npc.CanLevelUp = 1;
        }

        private bool RepairOriginalPartnerProfileAfterRestore(JxqyNpc npc)
        {
            if (npc == null ||
                npc.Kind != JxqyCharacterKind.Follower ||
                npc.CanLevelUp > 0 ||
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name) < 0)
            {
                return false;
            }

            // Saves written before original partner profiles were restored
            // contain the map-NPC defaults and no level threshold. Repair
            // only that unmistakable invalid state. A valid older profile
            // merely missing the Unity-side growth flag keeps all its stats.
            if (npc.LevelUpExperience <= 0)
                ApplyOriginalPartnerProfile(npc);
            else
                npc.CanLevelUp = 1;
            return true;
        }

        private void SetPartnerLevel(JxqyNpc npc, int level)
        {
            if (npc == null)
                throw new ArgumentNullException(nameof(npc));
            int profileIndex =
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name);
            if (npc.Kind != JxqyCharacterKind.Follower ||
                profileIndex < 0 ||
                !_originalCharacterLevelEntries.TryGetValue(
                    profileIndex,
                    out Dictionary<int, JxqyLevelEntry> entries))
            {
                return;
            }
            SetPartnerLevelFromEntries(npc, level, entries);
        }

        private static void SetPartnerLevelFromEntries(
            JxqyNpc npc,
            int level,
            IReadOnlyDictionary<int, JxqyLevelEntry> entries)
        {
            if (!entries.TryGetValue(
                    npc.Level,
                    out JxqyLevelEntry current) ||
                !entries.TryGetValue(level, out JxqyLevelEntry target))
            {
                npc.Level = level;
                return;
            }
            // PlayerN.ini contains the currently equipped totals. Applying
            // only the level-table delta preserves those equipment bonuses,
            // matching the original partner SetLevelTo path.
            npc.LifeMax += target.LifeMax - current.LifeMax;
            npc.ThewMax += target.ThewMax - current.ThewMax;
            npc.ManaMax += target.ManaMax - current.ManaMax;
            npc.Attack += target.Attack - current.Attack;
            npc.Attack2 += target.Attack2 - current.Attack2;
            npc.Attack3 += target.Attack3 - current.Attack3;
            npc.Defend += target.Defend - current.Defend;
            npc.Defend2 += target.Defend2 - current.Defend2;
            npc.Defend3 += target.Defend3 - current.Defend3;
            npc.Evade += target.Evade - current.Evade;
            npc.Level = level;
            npc.Life = npc.LifeMax;
            npc.Thew = npc.ThewMax;
            npc.Mana = npc.ManaMax;
            npc.LevelUpExperience = target.LevelUpExperience;
        }

        private void AddPlayerExperience(int amount)
        {
            JxqyLevelEntry reward = AddPlayerExperienceCore(amount);
            if (reward != null)
                GrantPlayerLevelRewards(reward);
        }

        private UniTask AddPlayerExperienceFromScriptAsync(int amount)
        {
            AddPlayerExperience(amount);
            return UniTask.CompletedTask;
        }

        private JxqyLevelEntry AddPlayerExperienceCore(int amount)
        {
            if (_player.LevelUpExperience <= 0)
                return null;

            _player.Experience = checked(
                _player.Experience + amount);
            if (_player.Experience <= _player.LevelUpExperience)
                return null;

            int targetLevel = FindLevelForExperience(
                _player.Experience);
            if (!LevelPlayerTo(targetLevel, out JxqyLevelEntry reward))
                return null;
            _uiSession?.SetNotice(
                $"{_player.Name}的等级提升了");
            return reward;
        }

        private int FindLevelForExperience(int experience)
        {
            return JxqyExperienceRules.FindLevelForExperience(
                _levelEntries
                    .OrderBy(value => value.Key)
                    .Select(value => new KeyValuePair<int, int>(
                        value.Key,
                        value.Value.LevelUpExperience)),
                experience);
        }

        private bool LevelPlayerTo(
            int level,
            out JxqyLevelEntry reward)
        {
            reward = null;
            if (!_levelEntries.TryGetValue(
                    _player.Level,
                    out JxqyLevelEntry current))
            {
                return false;
            }
            if (!_levelEntries.TryGetValue(
                    level,
                    out JxqyLevelEntry target))
            {
                JxqyExperienceRules.ApplyTerminalLevel(
                    _player,
                    level);
                return true;
            }

            _player.LifeMax += target.LifeMax - current.LifeMax;
            _player.ThewMax += target.ThewMax - current.ThewMax;
            _player.ManaMax += target.ManaMax - current.ManaMax;
            _player.Life = _player.LifeMax;
            _player.Thew = _player.ThewMax;
            _player.Mana = _player.ManaMax;
            _player.Attack += target.Attack - current.Attack;
            _player.Attack2 += target.Attack2 - current.Attack2;
            _player.Attack3 += target.Attack3 - current.Attack3;
            _player.Defend += target.Defend - current.Defend;
            _player.Defend2 += target.Defend2 - current.Defend2;
            _player.Defend3 += target.Defend3 - current.Defend3;
            _player.Evade += target.Evade - current.Evade;
            _player.LevelUpExperience =
                target.LevelUpExperience;
            _player.Level = level;
            reward = target;
            return true;
        }

        private void AddNpcExperience(JxqyNpc npc, int amount)
        {
            if (npc == null)
                throw new ArgumentNullException(nameof(npc));
            if (npc.LevelUpExperience <= 0)
                return;

            npc.Experience = checked(npc.Experience + amount);
            if (npc.Experience <= npc.LevelUpExperience)
                return;

            IReadOnlyDictionary<int, JxqyLevelEntry> levelEntries =
                GetNpcLevelEntries(npc);
            int targetLevel = JxqyExperienceRules.FindLevelForExperience(
                levelEntries
                    .OrderBy(value => value.Key)
                    .Select(value => new KeyValuePair<int, int>(
                        value.Key,
                        value.Value.LevelUpExperience)),
                npc.Experience);
            if (!LevelNpcTo(npc, targetLevel))
                return;
            _uiSession?.SetNotice(
                $"{npc.Name}\u7684\u7b49\u7ea7\u63d0\u5347\u4e86");
        }

        private bool LevelNpcTo(JxqyNpc npc, int level)
        {
            IReadOnlyDictionary<int, JxqyLevelEntry> levelEntries =
                GetNpcLevelEntries(npc);
            if (npc == null ||
                !levelEntries.TryGetValue(
                    npc.Level,
                    out JxqyLevelEntry current))
            {
                return false;
            }

            bool isTerminalLevel = false;
            if (!levelEntries.TryGetValue(
                    level,
                    out JxqyLevelEntry target))
            {
                int highestLevel = levelEntries.Keys.Max();
                if (level <= highestLevel ||
                    !levelEntries.TryGetValue(
                        highestLevel,
                        out target))
                {
                    return false;
                }
                isTerminalLevel = true;
            }

            npc.LifeMax += target.LifeMax - current.LifeMax;
            npc.ThewMax += target.ThewMax - current.ThewMax;
            npc.ManaMax += target.ManaMax - current.ManaMax;
            npc.Life = npc.LifeMax;
            npc.Thew = npc.ThewMax;
            npc.Mana = npc.ManaMax;
            npc.Attack += target.Attack - current.Attack;
            npc.Attack2 += target.Attack2 - current.Attack2;
            npc.Attack3 += target.Attack3 - current.Attack3;
            npc.Defend += target.Defend - current.Defend;
            npc.Defend2 += target.Defend2 - current.Defend2;
            npc.Defend3 += target.Defend3 - current.Defend3;
            npc.Evade += target.Evade - current.Evade;
            npc.LevelUpExperience = target.LevelUpExperience;
            if (isTerminalLevel)
                JxqyExperienceRules.ApplyTerminalLevel(npc, level);
            else
                npc.Level = level;
            if (JxqyExperienceRules.AdvancePartnerBaseSkills(npc) > 0)
            {
                SynchronizeNpcSkillCombatDefinitions(npc);
                CacheOriginalCharacterSkills(npc);
            }
            return true;
        }

        private IReadOnlyDictionary<int, JxqyLevelEntry>
            GetNpcLevelEntries(JxqyNpc npc)
        {
            int profileIndex =
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc?.Name);
            if (npc?.Kind == JxqyCharacterKind.Follower &&
                profileIndex >= 0 &&
                _originalCharacterLevelEntries.TryGetValue(
                    profileIndex,
                    out Dictionary<int, JxqyLevelEntry> entries))
            {
                return entries;
            }
            return _npcLevelEntries;
        }

        private void GrantPlayerLevelRewards(
            JxqyLevelEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.NewMagic))
            {
                if (_levelRewardMagics.TryGetValue(
                        entry.NewMagic,
                        out JxqyMagicDefinition magic) &&
                    _skills.Find(magic.Id) == null)
                {
                    // Player.AddMagic/ MagicListManager silently leaves the
                    // reward unadded when the original skill list is full.
                    _skills.Learn(magic);
                }
            }
            if (!string.IsNullOrWhiteSpace(entry.NewGood))
            {
                if (_levelRewardItems.TryGetValue(
                        entry.NewGood,
                        out JxqyItemDefinition item))
                {
                    // ScriptExecuter.AddGoods also treats a full inventory as
                    // a soft failure instead of aborting level-up processing.
                    _inventory.Add(item);
                }
            }
        }

        private async UniTask SetPlayerActionFileAsync(
            int state,
            string fileName)
        {
            var characterState = (JxqyCharacterState)state;
            if (string.IsNullOrEmpty(fileName))
            {
                _player.SetActionEnabled(characterState, false);
                _playerScriptActions.Remove(state);
                return;
            }
            if (!TryResolveLegacyCharacterAnimation(
                    fileName,
                    out string metadataAddress))
            {
                _player.SetActionEnabled(characterState, false);
                _playerScriptActions.Remove(state);
                JxqyResourceAddressCatalog.ReportMissing(
                    "SetNpcActionFile",
                    fileName);
                return;
            }
            JxqyAnimationMetadata metadata =
                await LoadLegacyCharacterAnimationAsync(
                    metadataAddress,
                    fileName,
                    this.GetCancellationTokenOnDestroy());
            _playerScriptActions[state] =
                new JxqyAnimationPlayer(metadata)
                {
                    IsLooping = IsLoopingCharacterState(
                        (JxqyCharacterState)state),
                };
            _player.SetActionEnabled(characterState, true);
        }

        private async UniTask SetCharacterActionFileAsync(
            JxqyCharacter character,
            int state,
            string fileName)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));
            if (ReferenceEquals(character, _player))
            {
                await SetPlayerActionFileAsync(state, fileName);
                return;
            }
            if (character is not JxqyNpc npc ||
                !_npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual visual))
            {
                throw new InvalidOperationException(
                    $"SetNpcActionFile target '{character.Name}' " +
                    "has no live visual.");
            }
            var characterState = (JxqyCharacterState)state;
            if (string.IsNullOrEmpty(fileName))
            {
                npc.SetActionEnabled(characterState, false);
                visual.Actions.Remove(state);
                visual.ActionFileNames.Remove(state);
                visual.CurrentState = (JxqyCharacterState)(-1);
                visual.CurrentStateVersion = -1;
                return;
            }
            if (!TryResolveLegacyCharacterAnimation(
                    fileName,
                    out string metadataAddress))
            {
                npc.SetActionEnabled(characterState, false);
                visual.Actions.Remove(state);
                visual.ActionFileNames.Remove(state);
                JxqyResourceAddressCatalog.ReportMissing(
                    "SetNpcActionFile",
                    fileName);
                return;
            }
            JxqyAnimationMetadata metadata =
                await LoadLegacyCharacterAnimationAsync(
                    metadataAddress,
                    fileName,
                    this.GetCancellationTokenOnDestroy());
            visual.Actions[state] = metadata;
            visual.ActionFileNames[state] = fileName;
            npc.SetActionEnabled(characterState, true);
            if (state == (int)JxqyCharacterState.Stand)
                visual.Stand = metadata;
            else if (state == (int)JxqyCharacterState.Walk)
                visual.Walk = metadata;
            visual.CurrentState = (JxqyCharacterState)(-1);
            visual.CurrentStateVersion = -1;
        }

        private async UniTask PlayPlayerSpecialActionAsync(
            string fileName)
        {
            if (!TryResolveLegacyCharacterAnimation(
                    fileName,
                    out string metadataAddress))
            {
                JxqyResourceAddressCatalog.ReportMissing(
                    "NpcSpecialAction",
                    fileName);
                return;
            }
            JxqyAnimationMetadata metadata =
                await LoadLegacyCharacterAnimationAsync(
                    metadataAddress,
                    fileName,
                    this.GetCancellationTokenOnDestroy());
            _playerSpecialAction = new JxqyAnimationPlayer(metadata)
            {
                IsLooping = false,
            };
            _playerSpecialAction.Restart();
        }

        private async UniTask<JxqyAnimationMetadata>
            LoadLegacyCharacterAnimationAsync(
                string metadataAddress,
                string legacyFileName,
                CancellationToken cancellationToken)
        {
            JxqyAssetLease<TextAsset> metadataLease =
                await LoadTextAsync(
                    metadataAddress,
                    cancellationToken);
            JxqyAnimationMetadata metadata =
                JsonUtility.FromJson<JxqyAnimationMetadata>(
                    metadataLease.Asset.text);
            if (metadata == null ||
                metadata.AtlasAddresses == null ||
                metadata.AtlasAddresses.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Legacy character animation " +
                    $"'{legacyFileName}' is invalid.");
            }
            foreach (string atlasAddress in metadata.AtlasAddresses)
            {
                await LoadDynamicTextureAsync(
                    atlasAddress,
                    cancellationToken);
            }
            return metadata;
        }

        private UniTask<Texture2D> LoadDynamicTextureAsync(
            string address,
            CancellationToken cancellationToken)
        {
            if (_textures.TryGet(address, out Texture2D cached))
                return UniTask.FromResult(cached);
            if (_dynamicTextureLoadTasks.TryGetValue(
                    address,
                    out AsyncLazy<Texture2D> pending))
            {
                return AwaitDynamicTextureLoadAsync(address, pending);
            }
            var task = new AsyncLazy<Texture2D>(() =>
                LoadAndRegisterDynamicTextureAsync(
                        address,
                        cancellationToken));
            _dynamicTextureLoadTasks.Add(address, task);
            return AwaitDynamicTextureLoadAsync(address, task);
        }

        private async UniTask<Texture2D> AwaitDynamicTextureLoadAsync(
            string address,
            AsyncLazy<Texture2D> task)
        {
            try
            {
                return await task.Task;
            }
            finally
            {
                if (_dynamicTextureLoadTasks.TryGetValue(
                        address,
                        out AsyncLazy<Texture2D> current) &&
                    ReferenceEquals(current, task))
                {
                    _dynamicTextureLoadTasks.Remove(address);
                }
            }
        }

        private async UniTask<Texture2D>
            LoadAndRegisterDynamicTextureAsync(
                string address,
                CancellationToken cancellationToken)
        {
            JxqyAssetLease<Texture2D> lease =
                await _resources.LoadAsync<Texture2D>(
                    address,
                    _mapScope,
                    cancellationToken);
            if (_textures.TryGet(address, out Texture2D cached))
            {
                lease.Dispose();
                return cached;
            }
            _textures.Register(address, lease.Asset, lease);
            return lease.Asset;
        }

        private static bool TryResolveLegacyCharacterAnimation(
            string fileName,
            out string metadataAddress)
        {
            string safeFileName = Path.GetFileName(
                (fileName ?? string.Empty).Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                !safeFileName.EndsWith(
                    ".asf",
                    StringComparison.OrdinalIgnoreCase))
            {
                metadataAddress = string.Empty;
                return false;
            }
            return JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                safeFileName,
                out metadataAddress,
                "interlude",
                "character",
                "object");
        }

        private void EquipGoods(int oneBasedIndex, int part)
        {
            int index = oneBasedIndex - 1;
            if (index < 0 || index >= _inventory.Entries.Count)
            {
                throw new InvalidOperationException(
                    $"Original inventory slot {oneBasedIndex} does not exist.");
            }
            JxqyInventoryEntry entry = _inventory.Entries[index];
            if ((int)entry.Definition.Slot != part)
            {
                throw new InvalidOperationException(
                    $"Item '{entry.Definition.Id}' cannot equip to part {part}.");
            }
            if (!_equipment.Equip(
                    _player,
                    _inventory,
                    entry.Definition.Id))
            {
                throw new InvalidOperationException(
                    $"Failed to equip original item '{entry.Definition.Id}'.");
            }
        }

        private void AddMemo(int textId)
        {
            if (!_talkTexts.TryGetValue(textId, out string text))
            {
                throw new InvalidOperationException(
                    $"Talk text {textId} was not loaded.");
            }
            _memoEntries.Add(text);
            _uiSession?.Refresh();
        }

        private void AddMemoText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            _memoEntries.Add(text.Trim());
            _uiSession?.Refresh();
        }

        private void DeleteMemo(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            // Entries are stored chronologically and presented in reverse.
            // The original newest-first list deletes the first matching
            // memo, which corresponds to the last match in this storage.
            int index = _memoEntries.FindLastIndex(entry =>
                string.Equals(
                    entry,
                    text.Trim(),
                    StringComparison.Ordinal));
            if (index < 0)
                return;
            _memoEntries.RemoveAt(index);
            _uiSession?.Refresh();
        }

        private void ParseTalkIndex(string text)
        {
            _talkTexts.Clear();
            _talkLines.Clear();
            string normalized = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            foreach (string line in normalized.Split('\n'))
            {
                if (!line.StartsWith("[", StringComparison.Ordinal))
                    continue;
                int comma = line.IndexOf(',');
                int close = line.IndexOf(']');
                if (comma <= 1 || close <= comma ||
                    !int.TryParse(
                        line.Substring(1, comma - 1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int id) ||
                    !int.TryParse(
                        line.Substring(
                            comma + 1,
                            close - comma - 1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int portraitIndex))
                {
                    continue;
                }
                string value = line.Substring(close + 1).Trim();
                _talkLines.Add(new JxqyLegacyTalkLine
                {
                    Index = id,
                    PortraitIndex = portraitIndex,
                    Text = value,
                });
                if (!_talkTexts.ContainsKey(id))
                    _talkTexts.Add(id, value);
            }
        }

        private async UniTask LoadTalkIndexAsync(
            CancellationToken cancellationToken)
        {
            JxqyAssetLease<TextAsset> talkLease =
                await LoadTextAsync(
                    _contentContext.TalkIndexAddress,
                    cancellationToken);
            ParseTalkIndex(talkLease.Asset.text);
            if (_talkTexts.Count == 0)
            {
                throw new InvalidOperationException(
                    "The original talk index contains no valid entries.");
            }
        }

        private IReadOnlyList<JxqyLegacyTalkLine> GetTalkLines(
            int from,
            int to)
        {
            if (to < from)
                return Array.Empty<JxqyLegacyTalkLine>();
            int first = _talkLines.FindIndex(line =>
                line.Index == from);
            if (first < 0)
                return Array.Empty<JxqyLegacyTalkLine>();
            var result = new List<JxqyLegacyTalkLine>();
            for (int index = first;
                 index < _talkLines.Count;
                 index++)
            {
                JxqyLegacyTalkLine line = _talkLines[index];
                if (line.Index > to)
                    break;
                result.Add(line);
            }
            return result;
        }

        private void ParseTraps(string text)
        {
            if (_savedTrapRegistry.HasEntries)
            {
                _trapRegistry = _savedTrapRegistry.Clone();
                _lastTrapObservedTile = new JxqyIntPoint(-1, -1);
                return;
            }
            _trapRegistry = new JxqyTrapRegistry();
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            foreach (KeyValuePair<
                         string,
                         Dictionary<string, string>> section in sections)
            {
                foreach (KeyValuePair<string, string> entry
                         in section.Value)
                {
                    if (!int.TryParse(
                            entry.Key,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int index) ||
                        index <= 0)
                    {
                        continue;
                    }
                    _trapRegistry.SetTrap(
                        section.Key,
                        index,
                        entry.Value,
                        activate: false);
                }
            }
            _lastTrapObservedTile = new JxqyIntPoint(-1, -1);
        }

        private async UniTask LoadNpcsFromScriptAsync(string fileName)
        {
            // The original NpcManager.Load keeps Kind=3 followers. Scripts
            // rely on this to carry companions across LoadMap + LoadNpc.
            ClearNpcActors(keepFollowers: true);
            string safeFileName = SafeLegacyFileName(fileName, ".npc");
            _activeNpcFileName = safeFileName;
            if (safeFileName.Length == 0)
                return;
            if (_savedNpcSnapshots.TryGetValue(
                    safeFileName,
                    out List<JxqyNpc> saved))
            {
                var loaded = new List<JxqyNpc>(saved.Count);
                foreach (JxqyNpc source in saved)
                {
                    JxqyNpc npc = CloneNpc(source);
                    await PrepareNpcEquipmentAsync(npc);
                    _npcs.Add(npc);
                    if (npc.IsDead)
                    {
                        _processedNpcDeaths.Add(npc);
                        if (npc.IsBodyCreated)
                            _finalizedNpcDeaths.Add(npc);
                    }
                    loaded.Add(npc);
                }
                await CreateNpcVisualsAsync(
                    loaded,
                    this.GetCancellationTokenOnDestroy());
                return;
            }
            string text;
            try
            {
                text = await LoadDynamicTextAsync(
                    _contentContext.SnapshotTemplateRelativeDirectory,
                    safeFileName,
                    this.GetCancellationTokenOnDestroy());
            }
            catch (FileNotFoundException exception)
            {
                // Original NpcManager.Load logs a missing snapshot and returns
                // after clearing ordinary NPCs, while the script keeps running.
                Debug.LogWarning(
                    $"JXQY-NPC snapshot '{safeFileName}' is unavailable. " +
                    "Original behavior leaves ordinary NPCs empty and " +
                    $"continues the script. {exception.Message}",
                    this);
                return;
            }
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            var loadedNpcs = new List<JxqyNpc>();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!section.Key.StartsWith(
                        "NPC",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                JxqyNpc npc = CreateNpc(section.Value);
                await PrepareNpcEquipmentAsync(npc);
                _npcs.Add(npc);
                loadedNpcs.Add(npc);
            }
            await CreateNpcVisualsAsync(
                loadedNpcs,
                this.GetCancellationTokenOnDestroy());
        }

        private async UniTask LoadOneNpcsFromScriptAsync(
            IReadOnlyList<string> fileNames)
        {
            ClearNpcActors(keepFollowers: true);
            _activeNpcFileName = string.Empty;
            if (fileNames == null)
                return;
            foreach (string fileName in fileNames)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                await MergeNpcsFromScriptAsync(fileName);
            }
        }

        private async UniTask LoadObjectsFromScriptAsync(string fileName)
        {
            ClearObjectActors();
            string safeFileName = SafeLegacyFileName(fileName, ".obj");
            _activeObjectFileName = safeFileName;
            if (safeFileName.Length == 0)
                return;
            if (_savedObjectSnapshots.TryGetValue(
                    safeFileName,
                    out List<JxqyWorldObject> saved))
            {
                var loaded = new List<JxqyWorldObject>(saved.Count);
                for (int index = 0; index < saved.Count; index++)
                {
                    JxqyWorldObject worldObject =
                        CloneWorldObject(saved[index]);
                    _objects.Add(worldObject);
                    loaded.Add(worldObject);
                }
                await PreloadObjectResourcesAsync(
                    loaded,
                    this.GetCancellationTokenOnDestroy());
                for (int index = 0; index < loaded.Count; index++)
                {
                    await RestoreObjectPresentationAsync(
                        safeFileName,
                        index,
                        loaded[index],
                        this.GetCancellationTokenOnDestroy());
                }
                return;
            }
            string text;
            try
            {
                text = await LoadDynamicTextAsync(
                    _contentContext.SnapshotTemplateRelativeDirectory,
                    safeFileName,
                    this.GetCancellationTokenOnDestroy());
            }
            catch (Exception exception) when (
                !(exception is OperationCanceledException))
            {
                // Original ObjManager.Load clears the current objects,
                // catches file/read failures and lets the script
                // continue. Several shipped late-game scripts intentionally
                // reference absent .obj files such as yaowanggu.obj.
                Debug.LogWarning(
                    $"JXQY-OBJ snapshot '{safeFileName}' is unavailable. " +
                    "Original behavior leaves objects empty and continues " +
                    $"the script. {exception.Message}",
                    this);
                return;
            }
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            var loadedObjects = new List<JxqyWorldObject>();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!section.Key.StartsWith(
                        "OBJ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                JxqyWorldObject worldObject =
                    CreateWorldObject(section.Value);
                _objects.Add(worldObject);
                loadedObjects.Add(worldObject);
            }
            await PreloadObjectResourcesAsync(
                loadedObjects,
                this.GetCancellationTokenOnDestroy());
            for (int objectIndex = 0;
                 objectIndex < loadedObjects.Count;
                 objectIndex++)
            {
                await RestoreObjectPresentationAsync(
                    safeFileName,
                    objectIndex,
                    loadedObjects[objectIndex],
                    this.GetCancellationTokenOnDestroy());
            }
        }

        private async UniTask RestoreObjectPresentationAsync(
            string snapshotFileName,
            int index,
            JxqyWorldObject worldObject,
            CancellationToken cancellationToken)
        {
            try
            {
                await CreateObjectVisualAsync(
                    worldObject,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Legacy object snapshot '{snapshotFileName}' entry " +
                    $"{index} failed: name='{worldObject?.Name}', " +
                    $"kind={worldObject?.Kind}, " +
                    $"resource='{worldObject?.ResourceFileName}', " +
                    $"sound='{worldObject?.WavFileName}'.",
                    exception);
            }
        }

        private async UniTask MergeNpcsFromScriptAsync(string fileName)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".npc");
            if (_savedNpcSnapshots.TryGetValue(
                    safeFileName,
                    out List<JxqyNpc> saved))
            {
                var loaded = new List<JxqyNpc>(saved.Count);
                foreach (JxqyNpc source in saved)
                {
                    JxqyNpc npc = CloneNpc(source);
                    await PrepareNpcEquipmentAsync(npc);
                    _npcs.Add(npc);
                    if (npc.IsDead)
                    {
                        _processedNpcDeaths.Add(npc);
                        if (npc.IsBodyCreated)
                            _finalizedNpcDeaths.Add(npc);
                    }
                    loaded.Add(npc);
                }
                await CreateNpcVisualsAsync(
                    loaded,
                    this.GetCancellationTokenOnDestroy());
                return;
            }
            string text = await LoadDynamicTextAsync(
                _contentContext.SnapshotTemplateRelativeDirectory,
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            var loadedNpcs = new List<JxqyNpc>();
            foreach (KeyValuePair<string, Dictionary<string, string>>
                     section in sections)
            {
                if (!section.Key.StartsWith(
                        "NPC",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                JxqyNpc npc = CreateNpc(section.Value);
                await PrepareNpcEquipmentAsync(npc);
                _npcs.Add(npc);
                loadedNpcs.Add(npc);
            }
            await CreateNpcVisualsAsync(
                loadedNpcs,
                this.GetCancellationTokenOnDestroy());
        }

        private async UniTask AddNpcFromScriptAsync(
            string fileName,
            int tileX,
            int tileY,
            int direction,
            int onlyOne)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".ini");
            string text = await LoadDynamicTextAsync(
                "ini/npc",
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            if (!sections.TryGetValue(
                    "INIT",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    $"NPC definition '{safeFileName}' has no INIT section.");
            }
            JxqyNpc npc = CreateNpc(init);
            if (onlyOne != 0 && _npcs.Find(npc.Name) != null)
                return;
            await PrepareNpcEquipmentAsync(npc);
            npc.TilePosition = new JxqyIntPoint(tileX, tileY);
            npc.CurrentDirection = direction;
            _npcs.Add(npc);
            await CreateNpcVisualAsync(
                npc,
                this.GetCancellationTokenOnDestroy());
        }

        private async UniTask AddObjectFromScriptAsync(
            string fileName,
            int tileX,
            int tileY,
            int direction)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".ini");
            string text = await LoadDynamicTextAsync(
                "ini/obj",
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            if (!sections.TryGetValue(
                    "INIT",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    $"Object definition '{safeFileName}' has no INIT section.");
            }
            JxqyWorldObject worldObject = CreateWorldObject(init);
            worldObject.TilePosition = new JxqyIntPoint(tileX, tileY);
            worldObject.CurrentDirection = direction;
            _objects.Add(worldObject);
            await CreateObjectVisualAsync(
                worldObject,
                this.GetCancellationTokenOnDestroy());
        }

        private static JxqyNpc CreateNpc(
            Dictionary<string, string> values)
        {
            int lifeMax = ParseIniInteger(values, "LifeMax", 1000);
            int thewMax = ParseIniInteger(values, "ThewMax", 1000);
            int manaMax = ParseIniInteger(values, "ManaMax", 1000);
            var npc = new JxqyNpc
            {
                Name = GetIniValue(values, "Name"),
                NpcIniFileName = GetIniValue(values, "NpcIni"),
                Kind = (JxqyCharacterKind)ParseIniInteger(
                    values,
                    "Kind",
                    0),
                Relation = (JxqyRelationType)ParseIniInteger(
                    values,
                    "Relation",
                    2),
                Action = ParseIniInteger(values, "Action", 0),
                PathFinderMode =
                    ParseIniInteger(values, "PathFinder", 0),
                FixedPositionData = GetIniValue(values, "FixedPos"),
                CurrentFixedPositionIndex = ParseIniInteger(
                    values,
                    "CurrentFixedPosIndex",
                    0),
                WalkSpeed = ParseIniInteger(values, "WalkSpeed", 1),
                VisionRadius = ParseIniInteger(
                    values,
                    "VisionRadius",
                    9),
                AttackRadius = ParseIniInteger(
                    values,
                    "AttackRadius",
                    1),
                IdleFrames = ParseIniInteger(values, "Idle", 0),
                LightRadius = ParseIniInteger(values, "Lum", 0),
                DialogRadius = ParseIniInteger(
                    values,
                    "DialogRadius",
                    1),
                Group = ParseIniInteger(values, "Group", 0),
                ScriptAddress = GetIniValue(values, "ScriptFile"),
                DeathScriptAddress =
                    GetIniValue(values, "DeathScript"),
                MagicFileName = GetIniValue(values, "FlyIni"),
                MagicFileName2 = GetIniValue(values, "FlyIni2"),
                RetaliationMagicFileName =
                    GetIniValue(
                        values,
                        "MagicToUseWhenBeAttacked"),
                MagicDirectionWhenBeAttacked =
                    ParseIniInteger(
                        values,
                        "MagicDirectionWhenBeAttacked",
                        0),
                NoAutoAttackPlayer =
                    ParseIniInteger(
                        values,
                        "NoAutoAttackPlayer",
                        0) != 0,
                StopFindingTarget =
                    ParseIniInteger(
                        values,
                        "StopFindingTarget",
                        0) != 0,
                ActionType =
                    ParseIniInteger(values, "ActionType", 0),
                DestinationMapPosX =
                    ParseIniInteger(
                        values,
                        "DestinationMapPosX",
                        0),
                DestinationMapPosY =
                    ParseIniInteger(
                        values,
                        "DestinationMapPosY",
                        0),
                KeepAttackX =
                    ParseIniInteger(values, "KeepAttackX", 0),
                KeepAttackY =
                    ParseIniInteger(values, "KeepAttackY", 0),
                CanEquip = ParseIniInteger(values, "CanEquip", 0),
                CanLevelUp = ParseIniInteger(values, "CanLevelUp", 0),
                BodyFileName = GetIniValue(values, "BodyIni"),
                ReviveDelaySeconds = Math.Max(
                    0,
                    ParseIniInteger(
                        values,
                        "ReviveMilliseconds",
                        0) / 1000f),
                EquipmentBackgroundFileName =
                    GetIniValue(values, "BackgroundTextureEquip"),
                LifeMax = lifeMax,
                ThewMax = thewMax,
                ManaMax = manaMax,
                Attack = ParseIniInteger(values, "Attack", 100),
                Evade = ParseIniInteger(values, "Evade", 10),
                Attack2 = ParseIniInteger(values, "Attack2", 0),
                Attack3 = ParseIniInteger(values, "Attack3", 0),
                Defend = ParseFirstIniInteger(
                    values,
                    10,
                    "Defend",
                    "Defence"),
                Defend2 = ParseIniInteger(values, "Defend2", 0),
                Defend3 = ParseIniInteger(values, "Defend3", 0),
                Level = ParseIniInteger(values, "Level", 1),
                AttackLevel = ParseIniInteger(
                    values,
                    "AttackLevel",
                    1),
                Experience = ParseIniInteger(values, "Exp", 0),
                LevelUpExperience = ParseIniInteger(
                    values,
                    "LevelUpExp",
                    0),
                ExpBonus = ParseIniInteger(values, "ExpBonus", 0),
                DropIni = GetIniValue(values, "DropIni"),
                NoDropWhenDead =
                    ParseIniInteger(values, "NoDropWhenDead", 0) != 0,
                TilePosition = new JxqyIntPoint(
                    ParseIniInteger(values, "MapX", 0),
                    ParseIniInteger(values, "MapY", 0)),
                CurrentDirection =
                    ParseIniInteger(values, "Dir", 0),
            };
            npc.SetState((JxqyCharacterState)ParseIniInteger(
                values,
                "State",
                0));
            npc.Life = ParseIniInteger(values, "Life", lifeMax);
            npc.Thew = ParseIniInteger(values, "Thew", thewMax);
            npc.Mana = ParseIniInteger(values, "Mana", manaMax);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Head,
                GetIniValue(values, "HeadEquip"));
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Neck,
                GetIniValue(values, "NeckEquip"));
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Body,
                GetIniValue(values, "BodyEquip"));
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Back,
                GetIniValue(values, "BackEquip"));
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Hand,
                GetIniValue(values, "HandEquip"));
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Wrist,
                GetIniValue(values, "WristEquip"));
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Foot,
                GetIniValue(values, "FootEquip"));
            ParseAdditionalBasicMagicFiles(
                npc,
                GetIniValue(values, "FlyInis"));
            return npc;
        }

        private static void AddNpcEquipmentFileName(
            JxqyNpc npc,
            JxqyEquipmentSlot slot,
            string fileName)
        {
            if (npc == null || string.IsNullOrWhiteSpace(fileName))
                return;
            npc.EquipmentFileNames[slot] = fileName.Trim();
        }

        private async UniTask PrepareNpcEquipmentAsync(JxqyNpc npc)
        {
            await PrepareOriginalNpcSkillsAsync(npc);
            if (npc == null ||
                (npc.CanEquip <= 0 &&
                 JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name) < 0) ||
                npc.EquipmentFileNames.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<JxqyEquipmentSlot, string> entry in
                     npc.EquipmentFileNames)
            {
                JxqyItemDefinition item =
                    await LoadItemDefinitionAsync(entry.Value);
                if (item.Slot != entry.Key)
                {
                    throw new InvalidDataException(
                        $"NPC '{npc.Name}' equipment '{entry.Value}' " +
                        $"belongs to {item.Slot}, expected {entry.Key}.");
                }
                npc.Equipment.RestoreEquipped(item);
            }
        }

        private async UniTask PrepareOriginalNpcSkillsAsync(JxqyNpc npc)
        {
            if (npc == null)
                return;
            int playerIndex =
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name);
            if (playerIndex < 0)
                return;
            CancellationToken cancellationToken =
                this.GetCancellationTokenOnDestroy();
            npc.Skills ??= new JxqySkillManager();
            if (npc.Skills.Skills.Count > 0)
            {
                // Save data owns learned levels/experience, but serialized
                // magic definitions do not load their ASF presentation
                // leases. The player restore path already rehydrates those
                // definitions; original companions need the same step before
                // their AI can visibly emit the selected martial art.
                await PrepareRestoredMagicVisualsAsync(
                    npc.Skills,
                    cancellationToken);
                if (npc.Kind == JxqyCharacterKind.Follower ||
                    !_originalCharacterSkills.ContainsKey(playerIndex))
                {
                    CacheOriginalCharacterSkills(npc);
                }
                SynchronizeOriginalFollowerInitialAttackRadius(npc);
                return;
            }
            if (_originalCharacterSkills.TryGetValue(
                    playerIndex,
                    out JxqySkillManager cached))
            {
                npc.Skills = CloneSkillManager(cached);
                await PrepareRestoredMagicVisualsAsync(
                    npc.Skills,
                    cancellationToken);
                SynchronizeOriginalFollowerInitialAttackRadius(npc);
                return;
            }

            JxqyAssetLease<TextAsset> magicLease = await LoadTextAsync(
                _contentContext.MagicAddress(playerIndex),
                cancellationToken);
            await LoadInitialMagicAsync(
                magicLease.Asset.text,
                cancellationToken,
                npc.Skills,
                $"Magic{playerIndex}.ini");
            CacheOriginalCharacterSkills(npc);
            SynchronizeOriginalFollowerInitialAttackRadius(npc);
        }

        private static void
            SynchronizeOriginalFollowerInitialAttackRadius(JxqyNpc npc)
        {
            if (npc == null ||
                npc.Kind != JxqyCharacterKind.Follower ||
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name) < 0)
            {
                return;
            }

            JxqyMagicDefinition magic =
                npc.Skills?.FindAtLegacyIndex(1)?.Magic ??
                npc.Skills?.FindAtLegacyIndex(2)?.Magic;
            if (magic != null)
                npc.AttackRadius = Math.Max(1, magic.AttackRadius);
        }

        private void CacheOriginalCharacterSkills(JxqyNpc npc)
        {
            int playerIndex =
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc?.Name);
            if (playerIndex < 0 || npc?.Skills == null)
                return;
            _originalCharacterSkills[playerIndex] =
                CloneSkillManager(npc.Skills);
        }

        private void CacheCurrentPlayerSkills()
        {
            if (_skills == null || _playerIndex < 0 ||
                _playerIndex > JxqyUiSession.LastOriginalPlayerIndex)
            {
                return;
            }
            _originalCharacterSkills[_playerIndex] =
                CloneSkillManager(_skills);
        }

        private static JxqySkillManager CloneSkillManager(
            JxqySkillManager source)
        {
            return JxqyRuntimeSaveCodec.RestoreSkills(
                JxqyRuntimeSaveCodec.CaptureSkills(
                    source ?? new JxqySkillManager()));
        }

        private static void ParseAdditionalBasicMagicFiles(
            JxqyCharacter character,
            string value)
        {
            if (character == null || string.IsNullOrWhiteSpace(value))
                return;
            foreach (string raw in value.Split(
                         new[] { ';', '；' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = raw.Split(new[] { ':', '：' }, 2);
                string fileName = parts[0].Trim();
                if (fileName.Length == 0)
                    continue;
                int distance = 0;
                if (parts.Length > 1)
                {
                    int.TryParse(
                        parts[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out distance);
                }
                character.AdditionalBasicMagics.Add(
                    new JxqyRangedMagicReference
                    {
                        Magic = new JxqyMagicDefinition
                        {
                            Id = fileName,
                        },
                        Distance = distance,
                    });
            }
        }

        private static JxqyWorldObject CreateWorldObject(
            Dictionary<string, string> values)
        {
            return new JxqyWorldObject
            {
                Name = GetIniValue(values, "ObjName"),
                ResourceFileName = GetIniValue(values, "ObjFile"),
                WavFileName = GetIniValue(values, "WavFile"),
                Kind = (JxqyObjectKind)ParseIniInteger(
                    values,
                    "Kind",
                    0),
                Damage = ParseIniInteger(values, "Damage", 0),
                LightRadius = ParseIniInteger(values, "Lum", 0),
                OffsetX = ParseIniInteger(values, "OffX", 0),
                OffsetY = ParseIniInteger(values, "OffY", 0),
                Height = ParseIniInteger(values, "Height", 0),
                Frame = ParseIniInteger(values, "Frame", 0),
                ScriptAddress = GetIniValue(values, "ScriptFile"),
                RightScriptAddress =
                    GetFirstIniValue(
                        values,
                        "ScriptFileRight",
                        "RightScript"),
                TimerScriptAddress =
                    GetFirstIniValue(
                        values,
                        "TimerScriptFile",
                        "TimerScript"),
                TimerScriptIntervalMilliseconds =
                    Math.Max(
                        1,
                        ParseIniInteger(
                            values,
                            "TimerScriptInterval",
                            1000)),
                ReviveNpcFileName =
                    GetIniValue(values, "ReviveNpcIni"),
                MillisecondsToRemove = Math.Max(
                    0,
                    ParseIniInteger(
                        values,
                        "MillisecondsToRemove",
                        0)),
                TilePosition = new JxqyIntPoint(
                    ParseIniInteger(values, "MapX", 0),
                    ParseIniInteger(values, "MapY", 0)),
                CurrentDirection =
                    ParseIniInteger(values, "Dir", 0),
            };
        }

        private async UniTask CreateNpcVisualsAsync(
            IReadOnlyList<JxqyNpc> npcs,
            CancellationToken cancellationToken)
        {
            if (npcs == null || npcs.Count == 0)
                return;
            string[] resourceFiles = npcs
                .Select(npc => SafeLegacyFileName(
                    npc.NpcIniFileName,
                    ".ini"))
                .Where(fileName => fileName.Length != 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await PreloadInBatchesAsync(
                resourceFiles,
                PreloadNpcStandResourceAsync,
                cancellationToken);
            for (int index = 0; index < npcs.Count; index++)
            {
                await CreateNpcVisualAsync(
                    npcs[index],
                    cancellationToken);
            }
        }

        private async UniTask PreloadNpcStandResourceAsync(
            string iniFile,
            CancellationToken cancellationToken)
        {
            string text = await LoadDynamicTextAsync(
                "ini/npcres",
                iniFile,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            string imageFile = GetStateImage(
                sections,
                JxqyCharacterState.Stand.ToString());
            if (string.IsNullOrWhiteSpace(imageFile) ||
                !JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                    imageFile,
                    out _,
                    "character",
                    "object"))
            {
                return;
            }
            await LoadDynamicAnimationAsync(
                imageFile,
                cancellationToken,
                "character",
                "object");
        }

        private async UniTask PreloadObjectResourcesAsync(
            IReadOnlyList<JxqyWorldObject> objects,
            CancellationToken cancellationToken)
        {
            if (objects == null || objects.Count == 0)
                return;
            string[] resourceFiles = objects
                .Where(worldObject =>
                    worldObject.Kind != JxqyObjectKind.LoopingSound &&
                    worldObject.Kind != JxqyObjectKind.RandomSound)
                .Select(worldObject =>
                    worldObject.ResourceFileName ?? string.Empty)
                .Where(reference =>
                    SafeLegacyFileName(reference, ".ini").Length != 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await PreloadInBatchesAsync(
                resourceFiles,
                PreloadObjectResourceAsync,
                cancellationToken);
        }

        private async UniTask PreloadObjectResourceAsync(
            string resourceReference,
            CancellationToken cancellationToken)
        {
            string text = await LoadObjectResourceTextAsync(
                resourceReference,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            string imageFile = GetStateImage(sections, "Common");
            if (string.IsNullOrWhiteSpace(imageFile) ||
                IsInvisibleObjectPlaceholder(imageFile))
            {
                return;
            }
            await LoadDynamicAnimationAsync(
                imageFile,
                cancellationToken,
                "object",
                "character");
        }

        private async UniTask<string> LoadObjectResourceTextAsync(
            string resourceReference,
            CancellationToken cancellationToken)
        {
            string fileName = SafeLegacyFileName(
                resourceReference,
                ".ini");
            if (fileName.Length == 0)
                return string.Empty;

            string normalized = (resourceReference ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/');
            string[] directories;
            if (normalized.StartsWith(
                    "ini/obj/",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(
                    "obj/",
                    StringComparison.OrdinalIgnoreCase))
            {
                directories = new[] { "ini/obj" };
            }
            else if (normalized.StartsWith(
                         "ini/objres/",
                         StringComparison.OrdinalIgnoreCase) ||
                     normalized.StartsWith(
                         "objres/",
                         StringComparison.OrdinalIgnoreCase))
            {
                directories = new[] { "ini/objres" };
            }
            else
            {
                // Snapshot ObjFile normally contains a bare resource name,
                // while objects added by script are definitions under
                // ini/obj.  Supporting both roots is a path capability, not
                // a Mod-specific alias.
                directories = new[] { "ini/objres", "ini/obj" };
            }

            FileNotFoundException missing = null;
            for (int index = 0; index < directories.Length; index++)
            {
                try
                {
                    return await LoadDynamicTextAsync(
                        directories[index],
                        fileName,
                        cancellationToken);
                }
                catch (FileNotFoundException exception)
                {
                    missing = exception;
                }
            }
            throw missing ?? new FileNotFoundException(
                $"Object resource '{resourceReference}' is missing.");
        }

        private static async UniTask PreloadInBatchesAsync(
            IReadOnlyList<string> resources,
            Func<string, CancellationToken, UniTask> load,
            CancellationToken cancellationToken)
        {
            for (int batchStart = 0;
                 batchStart < resources.Count;
                 batchStart +=
                     JxqyMapPreloadCoordinator.ResourceLoadBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int batchCount = Math.Min(
                    JxqyMapPreloadCoordinator.ResourceLoadBatchSize,
                    resources.Count - batchStart);
                var tasks = new UniTask[batchCount];
                for (int offset = 0; offset < batchCount; offset++)
                {
                    tasks[offset] = load(
                        resources[batchStart + offset],
                        cancellationToken);
                }
                await UniTask.WhenAll(tasks);
            }
        }

        private async UniTask CreateNpcVisualAsync(
            JxqyNpc npc,
            CancellationToken cancellationToken)
        {
            await PrepareCharacterBasicMagicsAsync(
                npc,
                cancellationToken);
            string iniFile = SafeLegacyFileName(
                npc.NpcIniFileName,
                ".ini");
            if (iniFile.Length == 0)
                return;
            string text = await LoadDynamicTextAsync(
                "ini/npcres",
                iniFile,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            Dictionary<int, string> actionFileNames =
                ParseCharacterStateImages(sections);
            Dictionary<int, string> sounds =
                ParseCharacterStateSounds(sections);
            if (!actionFileNames.TryGetValue(
                    (int)JxqyCharacterState.Stand,
                    out string standFileName))
            {
                // Some original event actors are intentionally visual-less
                // placeholders. Their script later supplies the one-shot
                // animation through NpcSpecialAction.
                if (actionFileNames.Count == 0)
                {
                    return;
                }
                standFileName = actionFileNames.Values.First();
                Debug.LogWarning(
                    $"JXQY-NPC resource '{iniFile}' has no Stand " +
                    "animation; using its first converted state.",
                    this);
            }
            JxqyAnimationMetadata stand =
                await LoadDynamicAnimationAsync(
                    standFileName,
                    cancellationToken,
                    "character",
                    "object");
            var state = new JxqyRuntimeActorVisual
            {
                Visual = new JxqyWorldVisual
                {
                    Id = $"npc:{npc.Name}:{_npcVisuals.Count}",
                    Kind = npc.Kind == JxqyCharacterKind.Flyer
                        ? JxqyWorldVisualKind.FlyingNpc
                        : JxqyWorldVisualKind.Npc,
                    Animation = new JxqyAnimationPlayer(stand),
                },
                Stand = stand,
                Walk = stand,
                Current = stand,
            };
            foreach (KeyValuePair<int, string> action in actionFileNames)
                state.ActionFileNames[action.Key] = action.Value;
            state.Actions[(int)JxqyCharacterState.Stand] = stand;
            foreach (KeyValuePair<int, string> sound in sounds)
                state.StateSounds[sound.Key] = sound.Value;
            state.Visual.Animation.SetDirection(npc.CurrentDirection);
            _npcVisuals.Add(npc, state);
            _frameVisuals.Add(state.Visual);
            RefreshActorVisual(npc);
        }

        private async UniTaskVoid LoadNpcActionOnDemandAsync(
            JxqyNpc npc,
            JxqyRuntimeActorVisual visual,
            int state,
            string fileName)
        {
            try
            {
                JxqyAnimationMetadata metadata =
                    await LoadDynamicAnimationAsync(
                        fileName,
                        this.GetCancellationTokenOnDestroy(),
                        "character",
                        "object");
                if (!_npcVisuals.TryGetValue(
                        npc,
                        out JxqyRuntimeActorVisual current) ||
                    !ReferenceEquals(current, visual) ||
                    !visual.ActionFileNames.TryGetValue(
                        state,
                        out string currentFileName) ||
                    !string.Equals(
                        currentFileName,
                        fileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                visual.Actions[state] = metadata;
                if (state == (int)JxqyCharacterState.Stand)
                    visual.Stand = metadata;
                else if (state == (int)JxqyCharacterState.Walk)
                    visual.Walk = metadata;
                visual.CurrentState = (JxqyCharacterState)(-1);
                visual.CurrentStateVersion = -1;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"JXQY-ACTION failed to lazy-load '{fileName}' " +
                    $"for NPC '{npc?.Name}': " +
                    exception.GetBaseException().Message,
                    this);
            }
            finally
            {
                visual?.LoadingActions.Remove(state);
            }
        }

        private bool EnsureNpcActionLoading(
            JxqyNpc npc,
            JxqyRuntimeActorVisual visual,
            JxqyCharacterState state)
        {
            if (visual == null ||
                !TryResolveCharacterActionFile(
                    visual.ActionFileNames,
                    state,
                    out int actionState,
                    out string fileName) ||
                visual.Actions.ContainsKey(actionState))
            {
                return false;
            }
            if (visual.LoadingActions.Add(actionState))
            {
                LoadNpcActionOnDemandAsync(
                    npc,
                    visual,
                    actionState,
                    fileName).Forget();
            }
            return true;
        }

        private static bool TryResolveCharacterActionFile(
            IReadOnlyDictionary<int, string> actionFileNames,
            JxqyCharacterState state,
            out int actionState,
            out string fileName)
        {
            actionState = (int)state;
            if (actionFileNames.TryGetValue(actionState, out fileName))
                return true;
            JxqyCharacterState fallback = state switch
            {
                JxqyCharacterState.Stand1 => JxqyCharacterState.Stand,
                JxqyCharacterState.FightStand => JxqyCharacterState.Stand,
                JxqyCharacterState.FightWalk => JxqyCharacterState.Walk,
                JxqyCharacterState.FightRun => JxqyCharacterState.Run,
                JxqyCharacterState.FightJump => JxqyCharacterState.Jump,
                JxqyCharacterState.Attack1 => JxqyCharacterState.Attack,
                JxqyCharacterState.Attack2 => JxqyCharacterState.Attack,
                _ => JxqyCharacterState.Stand,
            };
            actionState = (int)fallback;
            return actionFileNames.TryGetValue(actionState, out fileName);
        }

        private async UniTask CreateObjectVisualAsync(
            JxqyWorldObject worldObject,
            CancellationToken cancellationToken)
        {
            if (worldObject.Kind == JxqyObjectKind.LoopingSound ||
                worldObject.Kind == JxqyObjectKind.RandomSound)
            {
                await CreateObjectSoundAsync(
                    worldObject,
                    cancellationToken);
                return;
            }
            string iniFile = SafeLegacyFileName(
                worldObject.ResourceFileName,
                ".ini");
            if (iniFile.Length == 0)
                return;
            string text = await LoadObjectResourceTextAsync(
                worldObject.ResourceFileName,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            string imageFile = GetStateImage(sections, "Common");
            // Original logical obstacles may deliberately have no image.
            // They still participate in collision and script lookup, but do
            // not need a renderer.
            if (string.IsNullOrWhiteSpace(imageFile) ||
                IsInvisibleObjectPlaceholder(imageFile))
                return;
            JxqyAnimationMetadata metadata =
                await LoadDynamicAnimationAsync(
                    imageFile,
                    cancellationToken,
                    "object",
                    "character");
            var animation = new JxqyAnimationPlayer(metadata)
            {
                IsLooping = IsAutoPlayObject(worldObject),
            };
            animation.SetDirection(worldObject.CurrentDirection);
            if (worldObject.IsOpen && !animation.IsLooping)
                animation.SeekFrame(int.MaxValue);
            else
                animation.SeekFrame(worldObject.Frame);
            var state = new JxqyRuntimeActorVisual
            {
                Visual = new JxqyWorldVisual
                {
                    Id =
                        $"obj:{worldObject.Name}:{_objectVisuals.Count}",
                    Kind = worldObject.Kind == JxqyObjectKind.Body
                        ? JxqyWorldVisualKind.BodyObject
                        : JxqyWorldVisualKind.Object,
                    Animation = animation,
                },
                Stand = metadata,
                Current = metadata,
                OffsetX = worldObject.OffsetX,
                OffsetY = worldObject.OffsetY,
                ObjectOpenState = worldObject.IsOpen,
            };
            _objectVisuals.Add(worldObject, state);
            _frameVisuals.Add(state.Visual);
            RefreshActorVisual(worldObject);
        }

        private async UniTask CreateObjectSoundAsync(
            JxqyWorldObject worldObject,
            CancellationToken cancellationToken)
        {
            if (_audio is not IJxqyWorldAudioPort worldAudio ||
                string.IsNullOrWhiteSpace(worldObject.WavFileName))
            {
                return;
            }
            string generated = _contentContext.MediaAddressResolver
                .ResolveSound(worldObject.WavFileName);
            if (!JxqyResourceAddressCatalog.TryResolveGeneratedAddress(
                    JxqyLegacyResourceKind.Sound,
                    worldObject.WavFileName,
                    generated,
                    out string address))
            {
                JxqyResourceAddressCatalog.ReportOptionalAudioMissing(
                    "WorldObjectSound",
                    worldObject.WavFileName,
                    generated);
                return;
            }
            try
            {
                await worldAudio.RegisterWorldSoundAsync(
                    WorldSoundId(worldObject),
                    address,
                    worldObject.Kind == JxqyObjectKind.LoopingSound,
                    worldObject.PositionInWorld,
                    1f,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Ambient/random sounds are optional presentation. A stale
                // or unavailable audio agent must not abort a map transition.
                Debug.LogWarning(
                    $"JXQY-OBJ optional world sound skipped: " +
                    $"object='{worldObject.Name}', " +
                    $"sound='{worldObject.WavFileName}', " +
                    $"error={exception.GetBaseException().Message}",
                    this);
            }
        }

        private UniTask<JxqyAnimationMetadata>
            LoadDynamicAnimationAsync(
                string fileName,
                CancellationToken cancellationToken,
                params string[] categories)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(
                    "A visible actor has no animation image.");
            }
            string[] searchCategories =
                categories == null || categories.Length == 0
                    ? new[] { "character", "object" }
                    : categories;
            if (!JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                    fileName,
                    out string metadataAddress,
                    searchCategories))
            {
                throw new InvalidOperationException(
                    $"Actor animation '{fileName}' was not converted.");
            }
            if (_dynamicAnimationCache.TryGetValue(
                    metadataAddress,
                    out JxqyAnimationMetadata cached))
            {
                return UniTask.FromResult(cached);
            }
            if (_dynamicAnimationLoadTasks.TryGetValue(
                    metadataAddress,
                    out AsyncLazy<JxqyAnimationMetadata> pending))
            {
                return AwaitDynamicAnimationLoadAsync(
                    metadataAddress,
                    pending);
            }
            var task = new AsyncLazy<JxqyAnimationMetadata>(() =>
                LoadAndCacheDynamicAnimationAsync(
                        metadataAddress,
                        fileName,
                        cancellationToken));
            _dynamicAnimationLoadTasks.Add(metadataAddress, task);
            return AwaitDynamicAnimationLoadAsync(metadataAddress, task);
        }

        private async UniTask<JxqyAnimationMetadata>
            AwaitDynamicAnimationLoadAsync(
                string metadataAddress,
                AsyncLazy<JxqyAnimationMetadata> task)
        {
            try
            {
                return await task.Task;
            }
            finally
            {
                if (_dynamicAnimationLoadTasks.TryGetValue(
                        metadataAddress,
                        out AsyncLazy<JxqyAnimationMetadata> current) &&
                    ReferenceEquals(current, task))
                {
                    _dynamicAnimationLoadTasks.Remove(metadataAddress);
                }
            }
        }

        private async UniTask<JxqyAnimationMetadata>
            LoadAndCacheDynamicAnimationAsync(
                string metadataAddress,
                string fileName,
                CancellationToken cancellationToken)
        {
            JxqyAnimationMetadata metadata =
                await LoadLegacyCharacterAnimationAsync(
                    metadataAddress,
                    fileName,
                    cancellationToken);
            _dynamicAnimationCache[metadataAddress] = metadata;
            return metadata;
        }

        private UniTask<string> LoadDynamicTextAsync(
            string relativeDirectory,
            string safeFileName,
            CancellationToken cancellationToken)
        {
            string address = _contentContext.TextAddress(
                relativeDirectory,
                safeFileName);
            if (_dynamicTextCache.TryGetValue(
                    address,
                    out string cached))
            {
                return UniTask.FromResult(cached);
            }
            if (!JxqyResourceAddressCatalog.Contains(address))
            {
                throw new FileNotFoundException(
                    $"Converted legacy text asset is missing: {address}");
            }
            if (_dynamicTextLoadCache.TryGetValue(
                    address,
                    out AsyncLazy<string> pending))
            {
                return AwaitDynamicTextLoadAsync(address, pending);
            }
            var task = new AsyncLazy<string>(() =>
                LoadDynamicTextCoreAsync(
                    address,
                    cancellationToken));
            _dynamicTextLoadCache.Add(address, task);
            return AwaitDynamicTextLoadAsync(address, task);
        }

        private async UniTask<string> AwaitDynamicTextLoadAsync(
            string address,
            AsyncLazy<string> task)
        {
            try
            {
                return await task.Task;
            }
            finally
            {
                if (_dynamicTextLoadCache.TryGetValue(
                        address,
                        out AsyncLazy<string> current) &&
                    ReferenceEquals(current, task))
                {
                    _dynamicTextLoadCache.Remove(address);
                }
            }
        }

        private async UniTask<string> LoadDynamicTextCoreAsync(
            string address,
            CancellationToken cancellationToken)
        {
            JxqyResourceScope scope = _activeMapAssetScope ?? _mapScope;
            JxqyAssetLease<TextAsset> lease =
                await _resources.LoadAsync<TextAsset>(
                    address,
                    scope,
                    cancellationToken);
            _activeMapLeases.Add(lease);
            string text = lease.Asset.text;
            _dynamicTextCache[address] = text;
            return text;
        }

        private async UniTask LoadMapDisplayNamesAsync(
            CancellationToken cancellationToken)
        {
            JxqyLegacyTextSource source =
                _contentContext.ContentProfile.MapNames;
            string text = await LoadDynamicTextAsync(
                source.RelativeDirectory,
                source.FileName,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            _mapDisplayNames.Clear();
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> names))
            {
                return;
            }
            foreach (KeyValuePair<string, string> entry in names)
                _mapDisplayNames[entry.Key] = entry.Value;
        }

        private async UniTask LoadUiSoundConfigurationAsync(
            CancellationToken cancellationToken)
        {
            _uiSoundFileNames.Clear();
            foreach (JxqyUiSoundSource source in
                     _contentContext.ContentProfile.UiSoundSources)
            {
                string text = await LoadDynamicTextAsync(
                    source.Text.RelativeDirectory,
                    source.Text.FileName,
                    cancellationToken);
                Dictionary<string, Dictionary<string, string>> sections =
                    JxqyLegacySaveImporter.ParseIni(text);
                if (!sections.TryGetValue(
                        source.Section,
                        out Dictionary<string, string> section) ||
                    !section.TryGetValue(source.Key, out string configured) ||
                    string.IsNullOrWhiteSpace(configured))
                {
                    throw new InvalidDataException(
                        $"UI sound '{source.Sound}' is missing from " +
                        $"{source.Text.RelativeDirectory}/" +
                        $"{source.Text.FileName} " +
                        $"[{source.Section}] {source.Key}.");
                }
                string fileName = Path.GetFileName(
                    configured.Trim().Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new InvalidDataException(
                        $"UI sound '{source.Sound}' has an invalid path.");
                }
                _uiSoundFileNames[source.Sound] = fileName;
            }
        }

        private bool HasDynamicText(
            string relativeDirectory,
            string fileName)
        {
            string safeFileName = Path.GetFileName(
                (fileName ?? string.Empty)
                .Trim()
                .Replace('\\', '/'));
            if (safeFileName.Length == 0)
                return false;
            string address = _contentContext.TextAddress(
                relativeDirectory,
                safeFileName);
            return JxqyResourceAddressCatalog.Contains(
                address);
        }

        private static string SafeLegacyFileName(
            string fileName,
            string requiredExtension)
        {
            string safe = Path.GetFileName(
                (fileName ?? string.Empty)
                .Trim()
                .Replace('\\', '/'));
            if (safe.Length == 0)
                return string.Empty;
            if (!safe.EndsWith(
                    requiredExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Legacy file '{fileName}' must end with " +
                    $"'{requiredExtension}'.");
            }
            return safe;
        }

        private static string GetStateImage(
            Dictionary<string, Dictionary<string, string>> sections,
            string state)
        {
            return sections.TryGetValue(
                       state,
                       out Dictionary<string, string> values)
                ? GetIniValue(values, "Image")
                : string.Empty;
        }

        private static Dictionary<int, string> ParseCharacterStateSounds(
            Dictionary<string, Dictionary<string, string>> sections)
        {
            var sounds = new Dictionary<int, string>();
            foreach (JxqyCharacterState state in
                     Enum.GetValues(typeof(JxqyCharacterState)))
            {
                if (!sections.TryGetValue(
                        state.ToString(),
                        out Dictionary<string, string> values))
                {
                    continue;
                }
                string sound = GetIniValue(values, "Sound");
                if (!string.IsNullOrWhiteSpace(sound))
                    sounds[(int)state] = sound;
            }
            return sounds;
        }

        private static Dictionary<int, string> ParseCharacterStateImages(
            Dictionary<string, Dictionary<string, string>> sections)
        {
            var images = new Dictionary<int, string>();
            foreach (JxqyCharacterState state in
                     Enum.GetValues(typeof(JxqyCharacterState)))
            {
                string fileName = GetStateImage(
                    sections,
                    state.ToString());
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                if (!JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                        fileName,
                        out _,
                        "character",
                        "object"))
                {
                    Debug.LogWarning(
                        $"JXQY-ACTION optional state animation " +
                        $"'{fileName}' for {state} was not converted; " +
                        "using the original fallback state.");
                    continue;
                }
                images[(int)state] = fileName;
            }
            return images;
        }

        private async UniTask<Dictionary<int, JxqyAnimationMetadata>>
            LoadCharacterStateAnimationsAsync(
                Dictionary<string, Dictionary<string, string>> sections,
                CancellationToken cancellationToken)
        {
            var actions =
                new Dictionary<int, JxqyAnimationMetadata>();
            foreach (JxqyCharacterState state in
                     Enum.GetValues(typeof(JxqyCharacterState)))
            {
                string fileName = GetStateImage(
                    sections,
                    state.ToString());
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                if (!JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                        fileName,
                        out _,
                        "character",
                        "object"))
                {
                    Debug.LogWarning(
                        $"JXQY-ACTION optional state animation " +
                        $"'{fileName}' for {state} was not converted; " +
                        "using the original fallback state.",
                        this);
                    continue;
                }
                actions[(int)state] =
                    await LoadDynamicAnimationAsync(
                        fileName,
                        cancellationToken);
            }
            return actions;
        }

        private static JxqyAnimationMetadata ResolveCharacterAnimation(
            IReadOnlyDictionary<int, JxqyAnimationMetadata> actions,
            JxqyCharacterState state,
            JxqyAnimationMetadata stand)
        {
            if (actions != null &&
                actions.TryGetValue((int)state, out var exact))
            {
                return exact;
            }
            JxqyCharacterState fallback = state switch
            {
                JxqyCharacterState.Stand1 =>
                    JxqyCharacterState.Stand,
                JxqyCharacterState.Run =>
                    JxqyCharacterState.Walk,
                JxqyCharacterState.FightStand =>
                    JxqyCharacterState.Stand,
                JxqyCharacterState.FightWalk =>
                    JxqyCharacterState.Walk,
                JxqyCharacterState.FightRun =>
                    JxqyCharacterState.Run,
                JxqyCharacterState.FightJump =>
                    JxqyCharacterState.Jump,
                JxqyCharacterState.Attack1 =>
                    JxqyCharacterState.Attack,
                JxqyCharacterState.Attack2 =>
                    JxqyCharacterState.Attack,
                _ => JxqyCharacterState.Stand,
            };
            return actions != null &&
                   actions.TryGetValue((int)fallback, out var resolved)
                ? resolved
                : stand;
        }

        private static bool IsLoopingCharacterState(
            JxqyCharacterState state)
        {
            return state == JxqyCharacterState.Stand ||
                   state == JxqyCharacterState.Stand1 ||
                   state == JxqyCharacterState.Walk ||
                   state == JxqyCharacterState.Run ||
                   state == JxqyCharacterState.FightStand ||
                   state == JxqyCharacterState.FightWalk ||
                   state == JxqyCharacterState.FightRun;
        }

        private static bool ShouldHoldFinishedCharacterPose(
            JxqyCharacterState state)
        {
            return state == JxqyCharacterState.Sit;
        }

        private void DeleteNpcFromScript(string name)
        {
            IReadOnlyList<JxqyNpc> matches =
                _npcs.FindAll(name).ToArray();
            foreach (JxqyNpc npc in matches)
            {
                CacheOriginalCharacterSkills(npc);
                if (npc.Kind == JxqyCharacterKind.Follower)
                    continue;
                RemoveNpcVisual(npc);
                _npcs.Remove(npc);
                _processedNpcDeaths.Remove(npc);
                _finalizedNpcDeaths.Remove(npc);
            }
        }

        private void DeleteObjectFromScript(string name)
        {
            while (_objects.Find(name) is JxqyWorldObject worldObject)
            {
                RemoveObjectVisual(worldObject);
                _objects.Remove(name);
            }
        }

        private void DeleteObjectInstanceFromScript(
            JxqyWorldObject worldObject)
        {
            if (worldObject == null)
                throw new ArgumentNullException(nameof(worldObject));
            RemoveObjectVisual(worldObject);
            worldObject.IsRemoved = true;
            _objects.Remove(worldObject);
        }

        private void ClearBodiesFromScript()
        {
            JxqyWorldObject[] bodies = _objects.Objects
                .Where(item => item.Kind == JxqyObjectKind.Body)
                .ToArray();
            foreach (JxqyWorldObject body in bodies)
                DeleteObjectInstanceFromScript(body);
        }

        private void SaveNpcSnapshot(string fileName)
        {
            string key = ResolveAndActivateSnapshotFileName(
                fileName,
                ref _activeNpcFileName,
                ".npc");
            _savedNpcSnapshots[key] =
                _npcs.Npcs
                    .Where(npc =>
                        npc.Kind != JxqyCharacterKind.Follower &&
                        !npc.IsMagicSummon)
                    .Select(CloneNpc)
                    .ToList();
        }

        private void SaveObjectSnapshot(string fileName)
        {
            string key = ResolveAndActivateSnapshotFileName(
                fileName,
                ref _activeObjectFileName,
                ".obj");
            _savedObjectSnapshots[key] =
                _objects.Objects
                    .Where(item => !item.IsRemoved)
                    .Select(CloneWorldObject)
                    .ToList();
        }

        private static string ResolveAndActivateSnapshotFileName(
            string requested,
            ref string active,
            string extension)
        {
            string resolved = ResolveSnapshotFileName(
                requested,
                active,
                extension);
            if (!string.IsNullOrWhiteSpace(requested))
                active = resolved;
            return resolved;
        }

        private static string ResolveSnapshotFileName(
            string requested,
            string active,
            string extension)
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return SafeLegacyFileName(requested, extension);
            if (!string.IsNullOrWhiteSpace(active))
                return active;
            throw new InvalidOperationException(
                $"Cannot save legacy '{extension}' snapshot before " +
                "a source file has been loaded.");
        }

        private async UniTask<JxqyItemDefinition>
            LoadItemDefinitionAsync(string fileName)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".ini");
            if (_itemDefinitionCache.TryGetValue(
                    safeFileName,
                    out JxqyItemDefinition cached))
            {
                return cached;
            }
            string text = await LoadDynamicTextAsync(
                "ini/goods",
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            JxqyItemDefinition definition =
                ParseItemDefinition(safeFileName, text);
            _itemDefinitionCache[safeFileName] = definition;
            return definition;
        }

        private async UniTask<JxqyItemDefinition>
            LoadRandomItemDefinitionAsync(string listFileName)
        {
            string safeListFileName =
                SafeLegacyFileName(listFileName, ".ini");
            string text = await LoadDynamicTextAsync(
                "ini/buy",
                safeListFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            if (!sections.TryGetValue(
                    "Header",
                    out Dictionary<string, string> header))
            {
                throw new InvalidOperationException(
                    $"Random goods list '{safeListFileName}' has no Header.");
            }
            int count = ParseIniInteger(header, "Count", 0);
            if (count <= 0)
            {
                throw new InvalidOperationException(
                    $"Random goods list '{safeListFileName}' is empty.");
            }
            int index = _legacyRandom.Next(1, count + 1);
            if (!sections.TryGetValue(
                    index.ToString(CultureInfo.InvariantCulture),
                    out Dictionary<string, string> entry))
            {
                throw new InvalidOperationException(
                    $"Random goods list '{safeListFileName}' is missing " +
                    $"entry {index}.");
            }
            string itemFileName = GetIniValue(entry, "IniFile");
            return await LoadItemDefinitionAsync(itemFileName);
        }

        private async UniTask<JxqyMagicDefinition>
            LoadMagicDefinitionAsync(string fileName)
        {
            return await LoadMagicDefinitionAsync(fileName, 1);
        }

        private async UniTask<JxqyMagicDefinition>
            LoadMagicDefinitionAsync(
                string fileName,
                int level)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".ini");
            string text = await LoadDynamicTextAsync(
                "ini/magic",
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            JxqyMagicDefinition definition =
                ParseMagicDefinitionAtLevel(
                    safeFileName,
                    text,
                    level);
            await LoadMagicVisualAssetsAsync(
                definition,
                this.GetCancellationTokenOnDestroy());
            return definition;
        }

        private async UniTask PrepareCharacterBasicMagicsAsync(
            JxqyCharacter character,
            CancellationToken cancellationToken)
        {
            if (character == null)
                return;
            if (!string.IsNullOrWhiteSpace(character.MagicFileName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                character.BasicMagic =
                    await TryLoadCharacterMagicDefinitionAsync(
                        character,
                        character.MagicFileName,
                        character.AttackLevel,
                        cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(character.MagicFileName2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                character.BasicMagic2 =
                    await TryLoadCharacterMagicDefinitionAsync(
                        character,
                        character.MagicFileName2,
                        character.AttackLevel,
                        cancellationToken);
            }
            foreach (JxqyRangedMagicReference reference in
                     character.AdditionalBasicMagics)
            {
                string fileName = reference.Magic?.Id;
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                cancellationToken.ThrowIfCancellationRequested();
                reference.Magic = await TryLoadCharacterMagicDefinitionAsync(
                    character,
                    fileName,
                    character.AttackLevel,
                    cancellationToken);
                if (reference.Distance <= 0)
                {
                    reference.Distance = character is JxqyNpc npc
                        ? Math.Max(1, npc.AttackRadius)
                        : 1;
                }
            }
            if (!string.IsNullOrWhiteSpace(
                    character.RetaliationMagicFileName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                character.MagicToUseWhenBeAttacked =
                    await TryLoadCharacterMagicDefinitionAsync(
                        character,
                        character.RetaliationMagicFileName,
                        character.AttackLevel,
                        cancellationToken);
            }
            if (character is JxqyNpc skillOwner)
                SynchronizeNpcSkillCombatDefinitions(skillOwner);
        }

        private static void SynchronizeNpcSkillCombatDefinitions(
            JxqyNpc npc)
        {
            if (npc?.Skills == null)
                return;
            npc.BasicMagic = ResolveNpcSkillDefinition(
                npc,
                npc.BasicMagic);
            npc.BasicMagic2 = ResolveNpcSkillDefinition(
                npc,
                npc.BasicMagic2);
            npc.MagicToUseWhenBeAttacked = ResolveNpcSkillDefinition(
                npc,
                npc.MagicToUseWhenBeAttacked);
            foreach (JxqyRangedMagicReference reference in
                     npc.AdditionalBasicMagics)
            {
                reference.Magic = ResolveNpcSkillDefinition(
                    npc,
                    reference.Magic);
            }
        }

        private static JxqyMagicDefinition ResolveNpcSkillDefinition(
            JxqyNpc npc,
            JxqyMagicDefinition combatMagic)
        {
            if (combatMagic == null || npc?.Skills == null)
                return combatMagic;
            JxqySkillEntry entry = npc.Skills.Find(combatMagic.Id);
            return entry?.Magic?.CreateRuntimeSnapshot() ?? combatMagic;
        }

        private async UniTask<JxqyMagicDefinition>
            TryLoadCharacterMagicDefinitionAsync(
                JxqyCharacter character,
                string fileName,
                int level,
                CancellationToken cancellationToken)
        {
            try
            {
                return await LoadMagicDefinitionAsync(
                    fileName,
                    Math.Max(1, level));
            }
            catch (FileNotFoundException exception)
            {
                // Original Utils.GetMagic returns null when a referenced INI
                // is absent. Two shipped event-NPC definitions rely on that
                // behavior; loading the actor must not abort the whole map.
                Debug.LogWarning(
                    $"JXQY-NPC magic '{fileName}' is unavailable for " +
                    $"'{character?.Name}'. Original behavior leaves the " +
                    $"slot empty. {exception.Message}",
                    this);
                return null;
            }
        }

        private static JxqyMagicDefinition ParseMagicDefinition(
            string safeFileName,
            string text)
        {
            return ParseMagicDefinitionAtLevel(
                safeFileName,
                text,
                1);
        }

        private static JxqyMagicDefinition ParseMagicDefinitionAtLevel(
            string safeFileName,
            string text,
            int requestedLevel)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            if (!sections.TryGetValue(
                    "Init",
                    out Dictionary<string, string> init))
            {
                throw new InvalidOperationException(
                    $"Magic '{safeFileName}' has no Init section.");
            }
            var levels = new List<JxqyMagicLevelDefinition>(10);
            JxqyMagicLevelDefinition baseLevel = null;
            // Original Magic.cpp resolves AttackRadius from level[i - 1],
            // rather than jumping back to Init for every omitted level key.
            // Keep the existing five-tile fallback only for files that never
            // author the field at all; once authored, later levels inherit it.
            int inheritedAttackRadius = ParseIniInteger(
                init,
                "AttackRadius",
                5);
            for (int levelNumber = 0; levelNumber <= 10; levelNumber++)
            {
                Dictionary<string, string> level = null;
                if (levelNumber > 0)
                {
                    sections.TryGetValue(
                        $"Level{levelNumber}",
                        out level);
                }
                level ??= new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                int explicitEffectLevel = ParseIniInteger(
                    level,
                    "EffectLevel",
                    ParseIniInteger(init, "EffectLevel", 0));
                var levelDefinition = new JxqyMagicLevelDefinition
                {
                    Level = levelNumber,
                    MoveKind = ParseIniInteger(
                        level,
                        "MoveKind",
                        ParseIniInteger(init, "MoveKind", 2)),
                    PassThroughWall = ParseIniInteger(
                        level,
                        "PassThroughWall",
                        ParseIniInteger(init, "PassThroughWall", 0)),
                    EffectLevel = explicitEffectLevel > 0
                        ? explicitEffectLevel
                        : levelNumber,
                    Region = ParseIniInteger(
                        level,
                        "Region",
                        ParseIniInteger(init, "Region", 0)),
                    SpecialKind = ParseIniInteger(
                        level,
                        "SpecialKind",
                        ParseIniInteger(init, "SpecialKind", 0)),
                    SpecialKindValue = ParseIniInteger(
                        level,
                        "SpecialKindValue",
                        ParseIniInteger(init, "SpecialKindValue", 0)),
                    SpecialKindMilliseconds = ParseIniInteger(
                        level,
                        "SpecialKindMilliSeconds",
                        ParseIniInteger(
                            init,
                            "SpecialKindMilliSeconds",
                            0)),
                    NoSpecialKindEffect = ParseIniInteger(
                        level,
                        "NoSpecialKindEffect",
                        ParseIniInteger(init, "NoSpecialKindEffect", 0)),
                    NoInterruption = ParseIniInteger(
                        level,
                        "NoInterruption",
                        ParseIniInteger(init, "NoInterruption", 0)),
                    WaitFrame = ParseIniInteger(
                        level,
                        "WaitFrame",
                        ParseIniInteger(init, "WaitFrame", 0)),
                    LifeFrame = ParseIniInteger(
                        level,
                        "LifeFrame",
                        ParseIniInteger(init, "LifeFrame", 0)),
                    KeepMilliseconds = ParseIniInteger(
                        level,
                        "KeepMilliseconds",
                        ParseIniInteger(init, "KeepMilliseconds", 0)),
                    ColdMilliseconds = ParseIniInteger(
                        level,
                        "ColdMilliSeconds",
                        ParseIniInteger(init, "ColdMilliSeconds", 0)),
                    Effect = ParseIniInteger(
                        level,
                        "Effect",
                        ParseIniInteger(init, "Effect", 0)),
                    EffectExt = ParseIniInteger(
                        level,
                        "EffectExt",
                        ParseIniInteger(init, "EffectExt", 0)),
                    Effect2 = ParseIniInteger(
                        level,
                        "Effect2",
                        ParseIniInteger(init, "Effect2", 0)),
                    Effect3 = ParseIniInteger(
                        level,
                        "Effect3",
                        ParseIniInteger(init, "Effect3", 0)),
                    EffectMana = ParseIniInteger(
                        level,
                        "EffectMana",
                        ParseIniInteger(init, "EffectMana", 0)),
                    ManaCost = ParseIniInteger(
                        level,
                        "ManaCost",
                        ParseIniInteger(init, "ManaCost", 0)),
                    ThewCost = ParseIniInteger(
                        level,
                        "ThewCost",
                        ParseIniInteger(init, "ThewCost", 0)),
                    LifeCost = ParseIniInteger(
                        level,
                        "LifeCost",
                        ParseIniInteger(init, "LifeCost", 0)),
                    ProjectileSpeed = LegacyMagicBaseSpeed *
                        ParseIniInteger(
                            level,
                            "Speed",
                            ParseIniInteger(init, "Speed", 8)),
                    AttackRadius = ParseIniInteger(
                        level,
                        "AttackRadius",
                        inheritedAttackRadius),
                    LevelUpExperience = ParseIniInteger(
                        level,
                        "LevelupExp",
                        ParseIniInteger(init, "LevelupExp", 0)),
                    RestoreType = ParseIniInteger(
                        level,
                        "RestoreType",
                        ParseIniInteger(init, "RestoreType", 0)),
                    RestorePercent = ParseIniInteger(
                        level,
                        "RestorePercent",
                        ParseIniInteger(init, "RestorePercent", 0)),
                    RestoreProbability = ParseIniInteger(
                        level,
                        "RestoreProbability",
                        ParseIniInteger(init, "RestoreProbability", 0)),
                    DisableMoveMilliseconds = ParseIniInteger(
                        level,
                        "DisableMoveMilliseconds",
                        ParseIniInteger(
                            init,
                            "DisableMoveMilliseconds",
                            0)),
                    DisableSkillMilliseconds = ParseIniInteger(
                        level,
                        "DisableSkillMilliseconds",
                        ParseIniInteger(
                            init,
                            "DisableSkillMilliseconds",
                            0)),
                    SideEffectType = ParseIniInteger(
                        level,
                        "SideEffectType",
                        ParseIniInteger(init, "SideEffectType", 0)),
                    SideEffectPercent = ParseIniInteger(
                        level,
                        "SideEffectPercent",
                        ParseIniInteger(init, "SideEffectPercent", 0)),
                    SideEffectProbability = ParseIniInteger(
                        level,
                        "SideEffectProbability",
                        ParseIniInteger(
                            init,
                            "SideEffectProbability",
                            0)),
                    DieAfterUse = ParseIniInteger(
                        level,
                        "DieAfterUse",
                        ParseIniInteger(init, "DieAfterUse", 0)),
                    LifeMax = ParseIniInteger(
                        level,
                        "LifeMax",
                        ParseIniInteger(init, "LifeMax", 0)),
                    ThewMax = ParseIniInteger(
                        level,
                        "ThewMax",
                        ParseIniInteger(init, "ThewMax", 0)),
                    ManaMax = ParseIniInteger(
                        level,
                        "ManaMax",
                        ParseIniInteger(init, "ManaMax", 0)),
                    Attack = ParseIniInteger(
                        level,
                        "Attack",
                        ParseIniInteger(init, "Attack", 0)),
                    Attack2 = ParseIniInteger(
                        level,
                        "Attack2",
                        ParseIniInteger(init, "Attack2", 0)),
                    Attack3 = ParseIniInteger(
                        level,
                        "Attack3",
                        ParseIniInteger(init, "Attack3", 0)),
                    Defend = ParseIniInteger(
                        level,
                        "Defend",
                        ParseIniInteger(init, "Defend", 0)),
                    Defend2 = ParseIniInteger(
                        level,
                        "Defend2",
                        ParseIniInteger(init, "Defend2", 0)),
                    Defend3 = ParseIniInteger(
                        level,
                        "Defend3",
                        ParseIniInteger(init, "Defend3", 0)),
                    Evade = ParseIniInteger(
                        level,
                        "Evade",
                        ParseIniInteger(init, "Evade", 0)),
                    AddLifeRestorePercent = ParseIniInteger(
                        level,
                        "AddLifeRestorePercent",
                        ParseIniInteger(
                            init,
                            "AddLifeRestorePercent",
                            0)),
                    AddThewRestorePercent = ParseIniInteger(
                        level,
                        "AddThewRestorePercent",
                        ParseIniInteger(
                            init,
                            "AddThewRestorePercent",
                            0)),
                    AddManaRestorePercent = ParseIniInteger(
                        level,
                        "AddManaRestorePercent",
                        ParseIniInteger(
                            init,
                            "AddManaRestorePercent",
                            0)),
                    ReviveBodyRadius = ParseIniInteger(
                        level,
                        "ReviveBodyRadius",
                        ParseIniInteger(init, "ReviveBodyRadius", 0)),
                    ReviveBodyMaxCount = ParseIniInteger(
                        level,
                        "ReviveBodyMaxCount",
                        ParseIniInteger(
                            init,
                            "ReviveBodyMaxCount",
                            0)),
                    ReviveBodyLifeMilliseconds = ParseIniInteger(
                        level,
                        "ReviveBodyLifeMilliSeconds",
                        ParseIniInteger(
                            init,
                            "ReviveBodyLifeMilliSeconds",
                            0)),
                    FlyIni = GetLevelIniValue(
                        level,
                        init,
                        "FlyIni"),
                    FlyIni2 = GetLevelIniValue(
                        level,
                        init,
                        "FlyIni2"),
                    MagicToUseWhenBeAttacked = GetLevelIniValue(
                        level,
                        init,
                        "MagicToUseWhenBeAttacked"),
                    MagicDirectionWhenBeAttacked = ParseIniInteger(
                        level,
                        "MagicDirectionWhenBeAttacked",
                        ParseIniInteger(
                            init,
                            "MagicDirectionWhenBeAttacked",
                            0)),
                };
                inheritedAttackRadius = levelDefinition.AttackRadius;
                if (levelNumber == 0)
                    baseLevel = levelDefinition;
                else
                    levels.Add(levelDefinition);
            }

            var definition = new JxqyMagicDefinition
            {
                Id = safeFileName,
                Name = GetIniValue(init, "Name"),
                Introduction = GetIniValue(init, "Intro"),
                ImageFileName = GetIniValue(init, "Image"),
                IconFileName = GetIniValue(init, "Icon"),
                FlyingImageFileName =
                    GetIniValue(init, "FlyingImage"),
                FlyingSoundFileName =
                    GetIniValue(init, "FlyingSound"),
                VanishImageFileName =
                    GetIniValue(init, "VanishImage"),
                VanishSoundFileName =
                    GetIniValue(init, "VanishSound"),
                SuperModeImageFileName =
                    GetIniValue(init, "SuperModeImage"),
                ActionFileName = GetIniValue(init, "ActionFile"),
                AttackFileName = GetIniValue(init, "AttackFile"),
                Belong = ParseIniInteger(init, "Belong", 0),
                AlphaBlend = ParseIniInteger(init, "AlphaBlend", 0),
                FlyingLum = ParseIniInteger(init, "FlyingLum", 0),
                VanishLum = ParseIniInteger(init, "VanishLum", 0),
            };
            // Original Magic.GetLevel returns the unlevelled Init object when
            // AttackLevel falls outside its cloned 1..10 level dictionary.
            // Some shipped enemies intentionally use AttackLevel=49, so
            // clamping those actors to level 10 changes their projectile
            // topology substantially.
            if (baseLevel != null)
            {
                baseLevel.Level = 1;
                definition.SetLevelDefinitions(new[] { baseLevel });
                definition.ApplyLevel(1);
            }
            definition.SetLevelDefinitions(levels);
            if (requestedLevel >= 1 && requestedLevel <= 10)
                definition.ApplyLevel(requestedLevel);
            return definition;
        }

        private async UniTask LoadMagicVisualAssetsAsync(
            JxqyMagicDefinition magic,
            CancellationToken cancellationToken)
        {
            if (magic == null || string.IsNullOrWhiteSpace(magic.Id))
                return;
            var assets = new JxqyRuntimeMagicAssets();
            if (!string.IsNullOrWhiteSpace(magic.FlyingImageFileName))
            {
                assets.Flying = await LoadOptionalMagicAnimationAsync(
                    magic.FlyingImageFileName,
                    "FlyingImage",
                    cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(magic.VanishImageFileName))
            {
                assets.Vanish = await LoadOptionalMagicAnimationAsync(
                    magic.VanishImageFileName,
                    "VanishImage",
                    cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(magic.SuperModeImageFileName))
            {
                assets.SuperMode = await LoadOptionalMagicAnimationAsync(
                    magic.SuperModeImageFileName,
                    "SuperModeImage",
                    cancellationToken);
            }
            _magicVisualAssets[magic.Id] = assets;
            RefreshMagicLifetime(magic);
        }

        private async UniTask<JxqyAnimationMetadata>
            LoadOptionalMagicAnimationAsync(
                string fileName,
                string propertyName,
                CancellationToken cancellationToken)
        {
            // DaoJian 5.4.3 assigns magic ASF fields through Utils.GetAsf.
            // GetAsf returns null when the file is absent or unreadable and
            // Magic.Load still succeeds.  Shipped Mod data relies on that
            // behavior (for example a configured VanishImage may not exist).
            if (!JxqyResourceAddressCatalog.TryResolveAnimationAddress(
                    fileName,
                    out _,
                    "effect"))
            {
                Debug.LogWarning(
                    $"JXQY-MAGIC optional {propertyName} animation " +
                    $"'{fileName}' was not converted; using no effect.",
                    this);
                return null;
            }
            try
            {
                return await LoadDynamicAnimationAsync(
                    fileName,
                    cancellationToken,
                    "effect");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"JXQY-MAGIC optional {propertyName} animation " +
                    $"'{fileName}' could not be loaded; using no effect. " +
                    $"Error={exception.GetBaseException().Message}",
                    this);
                return null;
            }
        }

        private void RefreshMagicLifetime(JxqyMagicDefinition magic)
        {
            if (magic == null)
                return;
            _magicVisualAssets.TryGetValue(
                magic.Id,
                out JxqyRuntimeMagicAssets assets);
            if (magic.MoveKind == 15 && assets?.SuperMode != null)
            {
                magic.LifeSeconds = GetFullAnimationActiveSeconds(
                    assets.SuperMode);
                return;
            }
            if (magic.LifeFrame > 0)
            {
                magic.LifeSeconds = magic.MoveKind == 13
                    ? magic.LifeFrame * 0.01f
                    : JxqyLegacyMagicTiming
                        .GetPositiveLifeFrameActiveSeconds(
                            magic.LifeFrame,
                            assets?.Flying?.IntervalMilliseconds ?? 0);
                return;
            }
            if (assets?.Flying == null)
                return;
            magic.LifeSeconds = GetFullAnimationActiveSeconds(
                assets.Flying);
        }

        private static float GetFullAnimationActiveSeconds(
            JxqyAnimationMetadata metadata)
        {
            JxqyAnimationDirectionMetadata firstDirection =
                metadata?.Directions
                    .OrderBy(direction => direction.DirectionIndex)
                    .FirstOrDefault();
            if (firstDirection == null)
                return 0.01f;
            // Original MagicSprite treats LifeFrame=0 (and MoveKind=15) as
            // "play one complete direction". Sprite.Update runs at 60 Hz,
            // advances at most one ASF frame per tick even when Interval=0,
            // and MagicSprite gets one final collision tick after playback.
            return JxqyLegacyMagicTiming.GetPositiveLifeFrameActiveSeconds(
                firstDirection.FrameCount,
                metadata.IntervalMilliseconds);
        }

        private async UniTask OpenShopAsync(
            string fileName,
            bool canSellPlayerGoods)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".ini");
            string text = await LoadDynamicTextAsync(
                "ini/buy",
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            if (!sections.TryGetValue(
                    "Header",
                    out Dictionary<string, string> header))
            {
                throw new InvalidOperationException(
                    $"Shop '{safeFileName}' has no Header.");
            }
            int count = ParseIniInteger(header, "Count", 0);
            _shop = new JxqyShop
            {
                CanSellPlayerGoods = canSellPlayerGoods,
            };
            for (int index = 1; index <= count; index++)
            {
                if (!sections.TryGetValue(
                        index.ToString(CultureInfo.InvariantCulture),
                        out Dictionary<string, string> entry))
                {
                    continue;
                }
                string itemFileName = GetIniValue(entry, "IniFile");
                JxqyItemDefinition item =
                    await LoadItemDefinitionAsync(itemFileName);
                _shop.AddStock(
                    item,
                    ParseIniInteger(entry, "Number", -1));
            }
            _uiSession.Shop = _shop;
            _legacyInputDisabled = true;
            try
            {
                _uiSession.Open(JxqyUiScreen.Trade);
                await UniTask.WaitUntil(
                    () => !_uiSession.IsOpen(JxqyUiScreen.Trade),
                    cancellationToken:
                        this.GetCancellationTokenOnDestroy());
            }
            finally
            {
                // Original IsBuyGoodsEnd always restores player input when
                // the trade interface closes, including scripted shops that
                // return immediately after BuyGoods/SellGoods.
                _legacyInputDisabled = false;
            }
        }

        private async UniTask ClearAllSavesAsync()
        {
            await _saveRepository.DeleteAllAsync(
                this.GetCancellationTokenOnDestroy());
            await RefreshSaveSlotsAsync(
                this.GetCancellationTokenOnDestroy());
        }

        private void UsePlayerMagicFromScript(
            string magicFileName,
            int mapX,
            int mapY)
        {
            if (_skills == null || string.IsNullOrWhiteSpace(magicFileName))
                return;
            string requested = SafeLegacyFileName(
                magicFileName,
                ".ini");
            int index = -1;
            for (int skillIndex = 0;
                 skillIndex < _skills.Skills.Count;
                 skillIndex++)
            {
                JxqyMagicDefinition magic =
                    _skills.Skills[skillIndex].Magic;
                if (string.Equals(
                        magic.Id,
                        requested,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        magic.Id,
                        magicFileName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        magic.Name,
                        magicFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    index = skillIndex;
                    break;
                }
            }
            if (index < 0)
                return;
            JxqyIntPoint destination =
                JxqyIsometricMapMath.TileToWorldPixel(mapX, mapY);
            TryUsePlayerSkill(
                index,
                new JxqyFloat2(destination.X, destination.Y),
                null);
        }

        private async UniTask ChangePlayerAsync(int index)
        {
            if (index < 0 ||
                index > JxqyUiSession.LastOriginalPlayerIndex)
                throw new ArgumentOutOfRangeException(nameof(index));
            CaptureCurrentPlayerProfile();
            CancellationToken cancellationToken =
                this.GetCancellationTokenOnDestroy();
            if (_playerProfiles.TryGetValue(
                    index,
                    out JxqySavePlayerProfileState profile))
            {
                JxqyRestoredGameplayState state =
                    JxqyRuntimeSaveCodec.RestorePlayerProfile(
                        profile,
                        _player);
                ApplyPlayerProfileCollections(state, profile);
                await RestorePlayerLevelFileAsync(
                    profile.LevelFile,
                    cancellationToken);
                await PrepareCharacterBasicMagicsAsync(
                    _player,
                    cancellationToken);
                await PrepareRestoredMagicVisualsAsync(
                    cancellationToken);
            }
            else
            {
                await LoadOriginalPlayerProfileAsync(
                    index,
                    cancellationToken);
            }
            if (_originalCharacterSkills.TryGetValue(
                    index,
                    out JxqySkillManager cachedSkills))
            {
                _skills = CloneSkillManager(cachedSkills);
                _uiSession.Skills = _skills;
                _uiSession.ClearSelectedSkill();
                if (_playerProfiles.TryGetValue(
                        index,
                        out JxqySavePlayerProfileState cachedProfile))
                {
                    RestoreSelectedPlayerMagic(cachedProfile);
                }
            }
            else
            {
                _originalCharacterSkills[index] =
                    CloneSkillManager(_skills);
            }
            string address = _contentContext.PlayerAddress(index);
            JxqyAssetLease<TextAsset> lease = await LoadTextAsync(
                address,
                cancellationToken);
            // The original Loader.ChangePlayer creates a new Player instance,
            // so action-file overrides applied to the previous protagonist do
            // not survive the switch. This runtime reuses one player object;
            // clear those per-character overrides before loading the new body.
            _playerScriptActions.Clear();
            await SetCharacterResourceAsync(
                _player,
                GetPlayerResourceFileName(lease.Asset.text));
            _playerIndex = index;
            _uiSession?.SetPlayerIndex(index);
            _pendingPlayerMagicCast = null;
            _queuedPlayerMagicCast = null;
            _pendingBasicAttacks.Remove(_player);
            _transientCombatStates.Remove(_player);
            if (_playerAutoAttack != null)
                _playerAutoAttack.Target = null;
            _playerAutoAttackRunRequested = false;
            UpdatePlayerVisual();
            CenterCameraOnPlayer();
        }

        private void CaptureCurrentPlayerProfile()
        {
            if (_player == null || _inventory == null ||
                _equipment == null || _skills == null)
            {
                return;
            }
            _playerProfiles[_playerIndex] =
                JxqyRuntimeSaveCodec.CapturePlayerProfile(
                    _playerIndex,
                    _player,
                    _inventory,
                    _equipment,
                    _skills);
            CacheCurrentPlayerSkills();
            JxqySavePlayerProfileState profile =
                _playerProfiles[_playerIndex];
            profile.LevelFile = _levelFileName ?? string.Empty;
            profile.SelectedMagicLegacyIndex =
                _uiSession?.SelectedSkill?.LegacyListIndex ?? 0;
        }

        private static JxqySavePlayerProfileState ClonePlayerProfile(
            JxqySavePlayerProfileState source)
        {
            return new JxqySavePlayerProfileState
            {
                PlayerIndex = source.PlayerIndex,
                Direction = source.Direction,
                TileColumn = source.TileColumn,
                TileRow = source.TileRow,
                Life = source.Life,
                Mana = source.Mana,
                Thew = source.Thew,
                SelectedMagicLegacyIndex =
                    source.SelectedMagicLegacyIndex,
                LevelFile = source.LevelFile ?? string.Empty,
                PlayerDataJson = source.PlayerDataJson ?? string.Empty,
                InventoryDataJson =
                    source.InventoryDataJson ?? string.Empty,
                MagicDataJson = source.MagicDataJson ?? string.Empty,
            };
        }

        private async UniTask LoadOriginalPlayerProfileAsync(
            int index,
            CancellationToken cancellationToken)
        {
            string playerAddress = _contentContext.PlayerAddress(index);
            JxqyAssetLease<TextAsset> playerLease = await LoadTextAsync(
                playerAddress,
                cancellationToken);
            ApplyOriginalPlayerIni(playerLease.Asset.text);
            await RestorePlayerLevelFileAsync(
                GetPlayerLevelFileName(playerLease.Asset.text),
                cancellationToken);

            _inventory = new JxqyInventory();
            _equipment = new JxqyEquipmentManager();
            _skills = new JxqySkillManager();
            if (index == 0)
            {
                JxqyAssetLease<TextAsset> goodsLease = await LoadTextAsync(
                    _contentContext.NewGameGoodsAddress,
                    cancellationToken);
                await LoadInitialGoodsAsync(
                    goodsLease.Asset.text,
                    cancellationToken);
            }
            string magicAddress = _contentContext.MagicAddress(index);
            JxqyAssetLease<TextAsset> magicLease = await LoadTextAsync(
                magicAddress,
                cancellationToken);
            await LoadInitialMagicAsync(
                magicLease.Asset.text,
                cancellationToken);
            ApplyPlayerProfileCollections(
                new JxqyRestoredGameplayState
                {
                    Inventory = _inventory,
                    Equipment = _equipment,
                    Skills = _skills,
                },
                null);
            await PrepareCharacterBasicMagicsAsync(
                _player,
                cancellationToken);
        }

        private void ApplyPlayerProfileCollections(
            JxqyRestoredGameplayState state,
            JxqySavePlayerProfileState profile)
        {
            _inventory = state.Inventory ?? new JxqyInventory();
            _equipment = state.Equipment ?? new JxqyEquipmentManager();
            _skills = state.Skills ?? new JxqySkillManager();
            _uiSession.Inventory = _inventory;
            _uiSession.Equipment = _equipment;
            _uiSession.Skills = _skills;
            _uiSession.ClearSelectedSkill();
            RestoreSelectedPlayerMagic(profile);
        }

        private void RestoreSelectedPlayerMagic(
            JxqySavePlayerProfileState profile)
        {
            int legacyIndex = profile?.SelectedMagicLegacyIndex ?? 0;
            if (legacyIndex <= 0 || _skills == null)
                return;
            for (int index = 0; index < _skills.Skills.Count; index++)
            {
                if (_skills.Skills[index].LegacyListIndex != legacyIndex)
                    continue;
                _uiSession.SelectSkill(index);
                return;
            }
        }

        private async UniTask RestorePlayerLevelFileAsync(
            string fileName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _levelFileName = string.Empty;
                _levelEntries.Clear();
                _levelRewardMagics.Clear();
                _levelRewardItems.Clear();
                return;
            }
            // A restored player/profile already owns its exact saved current
            // stats. Only restore the table used by later level-ups.
            await LoadLevelFileAsync(fileName, applyCurrentLevel: false);
        }

        private async UniTask SetCharacterResourceAsync(
            JxqyCharacter character,
            string fileName)
        {
            string safeFileName = SafeLegacyFileName(fileName, ".ini");
            if (character is JxqyNpc npc)
            {
                RemoveNpcVisual(npc);
                npc.NpcIniFileName = safeFileName;
                npc.ResourceFileName = safeFileName;
                await CreateNpcVisualAsync(
                    npc,
                    this.GetCancellationTokenOnDestroy());
                return;
            }
            _playerNpcIniIndex = GetLegacyPlayerNpcIniIndex(safeFileName);
            _playerResourceFileName = safeFileName;
            _playerSpecialAction = null;
            string text = await LoadDynamicTextAsync(
                "ini/npcres",
                safeFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            Dictionary<int, JxqyAnimationMetadata> actions =
                await LoadCharacterStateAnimationsAsync(
                    sections,
                    this.GetCancellationTokenOnDestroy());
            Dictionary<int, string> sounds =
                ParseCharacterStateSounds(sections);
            if (!actions.TryGetValue(
                    (int)JxqyCharacterState.Stand,
                    out JxqyAnimationMetadata stand))
            {
                throw new InvalidOperationException(
                    $"Player resource '{safeFileName}' has no Stand animation.");
            }
            _playerStateActions.Clear();
            _playerStateSounds.Clear();
            foreach (KeyValuePair<int, string> sound in sounds)
                _playerStateSounds[sound.Key] = sound.Value;
            RemoveActiveCharacterStateSound(
                ref _playerActiveStateSoundId);
            foreach (KeyValuePair<int, JxqyAnimationMetadata> action
                     in actions)
            {
                JxqyCharacterState state =
                    (JxqyCharacterState)action.Key;
                _playerStateActions[action.Key] =
                    new JxqyAnimationPlayer(action.Value)
                    {
                        IsLooping = IsLoopingCharacterState(state),
                    };
            }
            JxqyAnimationMetadata walk = ResolveCharacterAnimation(
                actions,
                JxqyCharacterState.Walk,
                stand);
            JxqyAnimationMetadata run = ResolveCharacterAnimation(
                actions,
                JxqyCharacterState.Run,
                walk);
            _playerStand = new JxqyAnimationPlayer(stand);
            _playerWalk = new JxqyAnimationPlayer(walk);
            _playerRun = new JxqyAnimationPlayer(run);
            UpdatePlayerVisual();
        }

        private static int GetLegacyPlayerNpcIniIndex(
            string resourceFileName)
        {
            string stem = Path.GetFileNameWithoutExtension(
                resourceFileName ?? string.Empty);
            if (!string.IsNullOrEmpty(stem) &&
                char.IsDigit(stem[stem.Length - 1]))
            {
                return stem[stem.Length - 1] - '0';
            }
            return 1;
        }

        private async UniTask PlayCharacterSpecialActionAsync(
            JxqyCharacter character,
            string fileName,
            bool waitForCompletion)
        {
            if (!TryResolveLegacyCharacterAnimation(
                    fileName,
                    out string metadataAddress))
            {
                JxqyResourceAddressCatalog.ReportMissing(
                    "NpcSpecialAction",
                    fileName);
                return;
            }
            JxqyAnimationMetadata metadata =
                await LoadLegacyCharacterAnimationAsync(
                    metadataAddress,
                    fileName,
                    this.GetCancellationTokenOnDestroy());
            var action = new JxqyAnimationPlayer(metadata)
            {
                IsLooping = false,
            };
            action.Restart();
            character.BeginSpecialAction();
            if (ReferenceEquals(character, _player))
            {
                _playerSpecialAction = action;
            }
            else if (character is JxqyNpc npc)
            {
                if (!_npcVisuals.TryGetValue(
                        npc,
                        out JxqyRuntimeActorVisual visual))
                {
                    visual = new JxqyRuntimeActorVisual
                    {
                        Visual = new JxqyWorldVisual
                        {
                            Id = $"npc:{npc.Name}:{_npcVisuals.Count}",
                            Kind = npc.Kind == JxqyCharacterKind.Flyer
                                ? JxqyWorldVisualKind.FlyingNpc
                                : JxqyWorldVisualKind.Npc,
                            Animation = action,
                        },
                        Stand = metadata,
                        Walk = metadata,
                        Current = metadata,
                        SpecialActionOnly = true,
                    };
                    _npcVisuals.Add(npc, visual);
                    _frameVisuals.Add(visual.Visual);
                    RefreshActorVisual(npc);
                }
                visual.SpecialAction = action;
                visual.Visual.Animation = action;
                visual.Visual.IsVisible = npc.IsVisible;
            }
            if (waitForCompletion)
            {
                await UniTask.WaitUntil(
                    () => action.IsFinished,
                    cancellationToken:
                        this.GetCancellationTokenOnDestroy());
            }
        }

        private static JxqyNpc CloneNpc(JxqyNpc source)
        {
            var target = new JxqyNpc
            {
                Name = source.Name,
                NpcIniFileName = source.NpcIniFileName,
                Kind = source.Kind,
                Relation = source.Relation,
                Action = source.Action,
                PathFinderMode = source.PathFinderMode,
                FixedPositionData = source.FixedPositionData,
                CurrentFixedPositionIndex =
                    source.CurrentFixedPositionIndex,
                Group = source.Group,
                VisionRadius = source.VisionRadius,
                AttackRadius = source.AttackRadius,
                IdleFrames = source.IdleFrames,
                LightRadius = source.LightRadius,
                LifeMilliseconds = source.LifeMilliseconds,
                NoAutoAttackPlayer = source.NoAutoAttackPlayer,
                StopFindingTarget = source.StopFindingTarget,
                IsVisible = source.IsVisible,
                ResourceFileName = source.ResourceFileName,
                MagicFileName = source.MagicFileName,
                MagicFileName2 = source.MagicFileName2,
                RetaliationMagicFileName =
                    source.RetaliationMagicFileName,
                MagicDirectionWhenBeAttacked =
                    source.MagicDirectionWhenBeAttacked,
                ActionType = source.ActionType,
                BlindMilliseconds = source.BlindMilliseconds,
                DestinationMapPosX = source.DestinationMapPosX,
                DestinationMapPosY = source.DestinationMapPosY,
                KeepAttackX = source.KeepAttackX,
                KeepAttackY = source.KeepAttackY,
                CanEquip = source.CanEquip,
                CanLevelUp = source.CanLevelUp,
                BodyFileName = source.BodyFileName,
                IsBodyCreated = source.IsBodyCreated,
                IsMagicSummon = source.IsMagicSummon,
                ReviveDelaySeconds = source.ReviveDelaySeconds,
                EquipmentBackgroundFileName =
                    source.EquipmentBackgroundFileName,
                Skills = JxqyRuntimeSaveCodec.RestoreSkills(
                    JxqyRuntimeSaveCodec.CaptureSkills(source.Skills)),
                ScriptAddress = source.ScriptAddress,
                ClickScriptAddress = source.ClickScriptAddress,
                DeathScriptAddress = source.DeathScriptAddress,
                TilePosition = source.TilePosition,
                CurrentDirection = source.CurrentDirection,
                WalkSpeed = source.WalkSpeed,
                LifeMax = source.LifeMax,
                ThewMax = source.ThewMax,
                ManaMax = source.ManaMax,
                Attack = source.Attack,
                Attack2 = source.Attack2,
                Attack3 = source.Attack3,
                Defend = source.Defend,
                Defend2 = source.Defend2,
                Defend3 = source.Defend3,
                Evade = source.Evade,
                CanEvade = source.CanEvade,
                Level = source.Level,
                AttackLevel = source.AttackLevel,
                DialogRadius = source.DialogRadius,
                Experience = source.Experience,
                LevelUpExperience = source.LevelUpExperience,
                ExpBonus = source.ExpBonus,
                DropIni = source.DropIni,
                NoDropWhenDead = source.NoDropWhenDead,
                Invincible = source.Invincible,
                IsPetrified = source.IsPetrified,
                IsInTransport = source.IsInTransport,
                IsMovementDisabled = source.IsMovementDisabled,
                IsRunDisabled = source.IsRunDisabled,
                IsJumpDisabled = source.IsJumpDisabled,
                IsFightDisabled = source.IsFightDisabled,
                AddMoveSpeedPercent = source.AddMoveSpeedPercent,
                ChangeMoveSpeedPercent =
                    source.ChangeMoveSpeedPercent,
                RunSpeedFold = source.RunSpeedFold,
            };
            foreach (JxqyCharacterState disabled in
                     source.DisabledActionStates)
            {
                target.SetActionEnabled(disabled, false);
            }
            target.Life = source.Life;
            target.Thew = source.Thew;
            target.Mana = source.Mana;
            foreach (KeyValuePair<JxqyEquipmentSlot, string> entry in
                     source.EquipmentFileNames)
            {
                target.EquipmentFileNames[entry.Key] = entry.Value;
            }
            foreach (KeyValuePair<JxqyEquipmentSlot, JxqyItemDefinition>
                     entry in source.Equipment.Equipped)
            {
                target.EquipmentFileNames[entry.Key] = entry.Value.Id;
            }
            foreach (JxqyRangedMagicReference reference in
                     source.AdditionalBasicMagics)
            {
                if (reference.Magic == null)
                    continue;
                target.AdditionalBasicMagics.Add(
                    new JxqyRangedMagicReference
                    {
                        Magic = reference.Magic.CreateRuntimeSnapshot(),
                        Distance = reference.Distance,
                    });
            }
            target.SetFighting(source.IsInFighting);
            target.SetState(source.State);
            target.ApplyStatus(
                JxqyStatusKind.Frozen,
                source.GetStatusSeconds(JxqyStatusKind.Frozen));
            target.ApplyStatus(
                JxqyStatusKind.Poisoned,
                source.GetStatusSeconds(JxqyStatusKind.Poisoned));
            target.SetPoisonExperienceOwner(
                source.PoisonExperienceOwnerName);
            target.ApplyStatus(
                JxqyStatusKind.Petrified,
                source.GetStatusSeconds(JxqyStatusKind.Petrified));
            target.ApplyStatus(
                JxqyStatusKind.MovementDisabled,
                source.GetStatusSeconds(
                    JxqyStatusKind.MovementDisabled));
            target.ApplyStatus(
                JxqyStatusKind.SkillDisabled,
                source.GetStatusSeconds(JxqyStatusKind.SkillDisabled));
            target.RestoreStatusVisualEffects(
                source.IsFrozenVisualEffect,
                source.IsPoisonVisualEffect,
                source.IsPetrifiedVisualEffect);
            if (source.IsDead)
            {
                target.Die();
                target.RestoreReviveSecondsRemaining(
                    source.ReviveSecondsRemaining);
            }
            return target;
        }

        private static JxqyWorldObject CloneWorldObject(
            JxqyWorldObject source)
        {
            return new JxqyWorldObject
            {
                Name = source.Name,
                ResourceFileName = source.ResourceFileName,
                WavFileName = source.WavFileName,
                Kind = source.Kind,
                OffsetX = source.OffsetX,
                OffsetY = source.OffsetY,
                Height = source.Height,
                Frame = source.Frame,
                Damage = source.Damage,
                LightRadius = source.LightRadius,
                ScriptAddress = source.ScriptAddress,
                RightScriptAddress = source.RightScriptAddress,
                TimerScriptAddress = source.TimerScriptAddress,
                TimerScriptIntervalMilliseconds =
                    source.TimerScriptIntervalMilliseconds,
                ReviveNpcFileName = source.ReviveNpcFileName,
                MillisecondsToRemove = source.MillisecondsToRemove,
                IsVisible = source.IsVisible,
                IsOpen = source.IsOpen,
                IsRemoved = source.IsRemoved,
                TilePosition = source.TilePosition,
                CurrentDirection = source.CurrentDirection,
            };
        }

        private void ClearWorldActors()
        {
            RemoveActiveCharacterStateSound(
                ref _playerActiveStateSoundId);
            ResetCombatTransientState();
            ClearMagicVisuals();
            ClearNpcActors();
            ClearObjectActors();
        }

        private void ResetCombatTransientState()
        {
            _pendingPlayerMagicCast = null;
            _queuedPlayerMagicCast = null;
            _pendingBasicAttacks.Clear();
            _transientCombatStates.Clear();
            _combatCollisionTargets.Clear();
            _npcAutoAttacks.Clear();
            _npcKeepAttackCooldowns.Clear();
            if (_playerAutoAttack != null)
                _playerAutoAttack.Target = null;
            if (_uiSession != null)
                _uiSession.HoveredNpc = null;
            if (_player != null)
                _player.SetFighting(false);
            if (_npcs == null)
                return;
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                npc.Follow(null);
                npc.SetFighting(false);
                npc.Stop();
            }
        }

        private void ClearNpcActors()
        {
            ClearNpcActors(keepFollowers: false);
        }

        private void ClearNpcActors(bool keepFollowers)
        {
            JxqyNpc[] actors = _npcVisuals.Keys.ToArray();
            foreach (JxqyNpc npc in actors)
            {
                if (keepFollowers &&
                    npc.Kind == JxqyCharacterKind.Follower)
                {
                    npc.Stop();
                    continue;
                }
                JxqyRuntimeActorVisual state = _npcVisuals[npc];
                RemoveActiveCharacterStateSound(
                    ref state.ActiveStateSoundId);
                _frameVisuals.Remove(state.Visual);
                _npcVisuals.Remove(npc);
            }
            _npcs?.Clear(keepFollowers);
            _npcAutoAttacks.Clear();
            _npcKeepAttackCooldowns.Clear();
            _processedNpcDeaths.Clear();
            _finalizedNpcDeaths.Clear();
            _npcDeathsReadyToFinalize.Clear();
            if (_playerAutoAttack != null)
                _playerAutoAttack.Target = null;
        }

        private void ClearObjectActors()
        {
            if (_audio is IJxqyWorldAudioPort worldAudio)
            {
                foreach (JxqyWorldObject worldObject in
                         _objects?.Objects ??
                         Array.Empty<JxqyWorldObject>())
                {
                    worldAudio.RemoveWorldSound(
                        WorldSoundId(worldObject));
                }
            }
            foreach (JxqyRuntimeActorVisual state in _objectVisuals.Values)
                _frameVisuals.Remove(state.Visual);
            _objectVisuals.Clear();
            _objectTimerElapsedMilliseconds.Clear();
            _expiredWorldObjects.Clear();
            _objects?.Clear();
        }

        private void RemoveNpcVisual(JxqyNpc npc)
        {
            if (!_npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual state))
            {
                return;
            }
            RemoveActiveCharacterStateSound(
                ref state.ActiveStateSoundId);
            _frameVisuals.Remove(state.Visual);
            _npcVisuals.Remove(npc);
        }

        private void RemoveObjectVisual(JxqyWorldObject worldObject)
        {
            if (_audio is IJxqyWorldAudioPort worldAudio)
                worldAudio.RemoveWorldSound(WorldSoundId(worldObject));
            if (!_objectVisuals.TryGetValue(
                    worldObject,
                    out JxqyRuntimeActorVisual state))
            {
                return;
            }
            _frameVisuals.Remove(state.Visual);
            _objectVisuals.Remove(worldObject);
            _objectTimerElapsedMilliseconds.Remove(worldObject);
        }

        private void RefreshActorVisual(JxqySprite actor)
        {
            if (ReferenceEquals(actor, _player))
            {
                UpdatePlayerVisual();
                return;
            }
            if (actor is JxqyNpc npc &&
                _npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual npcState))
            {
                npcState.Visual.IsVisible = npc.IsVisible;
                ApplyActorPosition(npc, npcState);
                npcState.Visual.Animation.SetDirection(
                    npc.CurrentDirection);
                return;
            }
            if (actor is JxqyWorldObject worldObject &&
                _objectVisuals.TryGetValue(
                    worldObject,
                    out JxqyRuntimeActorVisual objectState))
            {
                objectState.Visual.IsVisible =
                    worldObject.IsVisible && !worldObject.IsRemoved;
                ApplyActorPosition(worldObject, objectState);
                objectState.Visual.Animation.SetDirection(
                    worldObject.CurrentDirection);
                if (objectState.ObjectOpenState != worldObject.IsOpen)
                {
                    objectState.Visual.Animation.IsLooping = false;
                    if (worldObject.IsOpen)
                        objectState.Visual.Animation.PlayForward();
                    else
                        objectState.Visual.Animation.PlayReverse();
                    objectState.ObjectOpenState = worldObject.IsOpen;
                    objectState.ObjectTransition = true;
                }
            }
        }

        private void UpdateActorVisuals(
            float elapsedSeconds,
            float movementElapsedSeconds)
        {
            foreach (KeyValuePair<JxqyNpc, JxqyRuntimeActorVisual> pair
                     in _npcVisuals)
            {
                JxqyNpc npc = pair.Key;
                JxqyRuntimeActorVisual state = pair.Value;
                ApplyCharacterStatusPresentation(npc, state.Visual);
                if (state.SpecialAction != null &&
                    !state.SpecialAction.IsFinished)
                {
                    state.Visual.Animation = state.SpecialAction;
                    state.SpecialAction.SetDirection(
                        npc.CurrentDirection);
                    state.SpecialAction.Advance(elapsedSeconds);
                    ApplyActorPosition(npc, state);
                    continue;
                }
                bool specialActionCompleted = state.SpecialAction != null;
                if (specialActionCompleted)
                    npc.EndSpecialAction();
                state.SpecialAction = null;
                if (state.SpecialActionOnly)
                {
                    state.Visual.IsVisible = false;
                    continue;
                }
                bool statusDeath = TryGetStatusDeathAnimation(
                    npc,
                    out JxqyAnimationMetadata statusDeathAnimation);
                bool actionPending = !statusDeath &&
                                     EnsureNpcActionLoading(
                                         npc,
                                         state,
                                         npc.State);
                if (actionPending)
                {
                    // The original resolves a state image synchronously when
                    // the state first uses it. Until the localized async load
                    // completes, hold the current pose and do not let a Stand
                    // fallback finish Attack/Hurt/Death early.
                    state.Visual.Animation.SetDirection(
                        npc.CurrentDirection);
                    ApplyActorPosition(npc, state);
                    continue;
                }
                JxqyAnimationMetadata desired = statusDeath
                    ? statusDeathAnimation
                    : ResolveCharacterAnimation(
                        state.Actions,
                        npc.State,
                        state.Stand);
                // Original Character.EndSpecialAction force-refreshes the
                // current state texture. The cached normal-state metadata is
                // still the same here, but Visual.Animation still references
                // the completed one-shot action, so completion itself must
                // force the normal animation to be rebound.
                if (specialActionCompleted ||
                    !ReferenceEquals(state.Current, desired) ||
                    state.CurrentState != npc.State ||
                    state.CurrentStateVersion != npc.StateVersion)
                {
                    ApplyCharacterStateSound(
                        npc,
                        npc.State,
                        state.StateSounds,
                        state.Visual.Id,
                        ref state.ActiveStateSoundId);
                    state.Visual.Animation =
                        new JxqyAnimationPlayer(desired)
                        {
                            IsLooping =
                                IsLoopingCharacterState(npc.State),
                        };
                    state.Current = desired;
                    state.CurrentState = npc.State;
                    state.CurrentStateVersion = npc.StateVersion;
                }
                state.Visual.Animation.SetDirection(
                    statusDeath ? 0 : npc.CurrentDirection);
                state.Visual.Animation.Advance(
                    statusDeath
                        ? elapsedSeconds
                        : (IsMovementCharacterState(npc.State)
                            ? movementElapsedSeconds
                            : elapsedSeconds) * npc.CharacterTimeScale);
                ApplyActorPosition(npc, state);
                if (!string.IsNullOrWhiteSpace(
                        state.ActiveStateSoundId) &&
                    _audio is IJxqyWorldAudioPort actorWorldAudio)
                {
                    actorWorldAudio.SetWorldSoundPosition(
                        state.ActiveStateSoundId,
                        npc.PositionInWorld);
                }
                if (state.Visual.Animation.IsFinished &&
                    !state.Visual.Animation.IsLooping &&
                    !ShouldHoldFinishedCharacterPose(npc.State) &&
                    !npc.IsDead)
                {
                    PlayCharacterCompletionSound(
                        npc,
                        npc.State,
                        state.StateSounds);
                    CompleteFinishedCharacterAction(npc);
                }
            }
            foreach (KeyValuePair<
                         JxqyWorldObject,
                         JxqyRuntimeActorVisual> pair in _objectVisuals)
            {
                if (IsAutoPlayObject(pair.Key) ||
                    pair.Value.ObjectTransition)
                {
                    pair.Value.Visual.Animation.Advance(elapsedSeconds);
                }
                if (pair.Value.ObjectTransition &&
                    pair.Value.Visual.Animation.IsFinished)
                {
                    pair.Value.ObjectTransition = false;
                }
                ApplyActorPosition(pair.Key, pair.Value);
            }
        }

        private static bool IsAutoPlayObject(JxqyWorldObject worldObject)
        {
            return worldObject != null &&
                   (worldObject.Kind == JxqyObjectKind.Dynamic ||
                    worldObject.Kind == JxqyObjectKind.Trap ||
                    worldObject.Kind == JxqyObjectKind.Drop);
        }

        private static bool IsInvisibleObjectPlaceholder(string imageFile)
        {
            // The legacy maps use obj-sound.mpc as a transparent placeholder
            // for sound emitters (and two invisible door-kind records in
            // map004). It has no visual frames to convert.
            return string.Equals(
                Path.GetFileName(
                    (imageFile ?? string.Empty)
                    .Trim()
                    .Replace('\\', '/')),
                "obj-sound.mpc",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string WorldSoundId(JxqyWorldObject worldObject)
        {
            return $"obj-sound:{worldObject.Name}:" +
                   $"{worldObject.TilePosition.X}:" +
                   $"{worldObject.TilePosition.Y}:" +
                   $"{worldObject.WavFileName}";
        }

        private void SelectPlayerAttackTarget()
        {
            if (_player.IsDead || _player.IsFightDisabled)
                return;
            JxqyNpc target = FindClosestEnemy(
                _player,
                _playerAutoAttack.Range);
            if (target == null)
            {
                _player.StopMovementPreservingAction();
                _playerAutoAttack.Target = null;
                _player.SetFighting(false);
                _player.SetState(JxqyCharacterState.Attack);
                return;
            }
            SelectPlayerAttackTarget(target, false);
        }

        private void SelectPlayerAttackTarget(
            JxqyNpc target,
            bool runRequested)
        {
            if (_player.IsDead ||
                _player.IsFightDisabled ||
                target == null ||
                target.IsDead ||
                !target.IsVisible ||
                !JxqyRelations.AreOpposed(_player, target))
            {
                return;
            }

            _player.StopMovementPreservingAction();
            ClearPendingInteraction();
            _playerAutoAttack.Target = target;
            _playerAutoAttackRunRequested = runRequested;
            _player.SetFighting(true);
            if (IsWithinAutoAttackRange(
                    _playerAutoAttack,
                    _player,
                    target))
            {
                return;
            }

            BeginPlayerAutoAttackPursuit();
        }

        private bool BeginPlayerAutoAttackPursuit()
        {
            JxqyCharacter target = _playerAutoAttack?.Target;
            if (target == null || target.IsDead ||
                !JxqyRelations.AreOpposed(_player, target))
            {
                return false;
            }
            IReadOnlyList<JxqyFloat2> path =
                JxqyPathfinder.FindPathToNearestReachable(
                    CreateLiveCollisionMap(),
                    _player.TilePosition,
                    target.TilePosition,
                    out _);
            return path.Count >= 2 &&
                   _player.BeginPath(
                       path,
                       _playerAutoAttackRunRequested &&
                       !_player.IsRunDisabled);
        }

        private void TryUsePlayerSkill(int slot)
        {
            TryUsePlayerSkill(slot, null, null, true);
        }

        private void TryUsePlayerSkill(
            int slot,
            JxqyFloat2? requestedDestination,
            JxqyNpc requestedTarget)
        {
            TryUsePlayerSkill(
                slot,
                requestedDestination,
                requestedTarget,
                false);
        }

        private void TryUsePlayerSkill(
            int slot,
            JxqyFloat2? requestedDestination,
            JxqyNpc requestedTarget,
            bool clampToConfiguredRange)
        {
            if (_player == null || _skills == null || _combat == null)
                return;
            if (slot < 0 || slot >= _skills.Skills.Count)
                return;
            JxqySkillEntry skill = _skills.Skills[slot];
            if (skill.CooldownMilliseconds > 0)
            {
                _uiSession?.SetNotice("武功尚未冷却");
                return;
            }
            JxqyMagicDefinition magic = skill.Magic;
            if (magic == null)
                return;
            magic.ApplyLevel(skill.Level);
            JxqyNpc target = requestedTarget;
            JxqyFloat2 destination;
            if (magic.MoveKind == 13)
            {
                JxqyCharacter bodyTarget =
                    JxqyMagicTargeting.ResolveBodyEffectTarget(
                        _player,
                        target);
                target = bodyTarget as JxqyNpc;
                destination = bodyTarget.PositionInWorld;
            }
            else
            {
                if (!requestedDestination.HasValue && target == null)
                {
                    target = _npcs == null
                        ? null
                        : FindClosestEnemy(
                            _player,
                            OriginalMobileAutoTargetRadius);
                }
                destination = requestedDestination ??
                              target?.PositionInWorld ??
                              GetFacingWorldDestination(_player, 1);
                if (clampToConfiguredRange)
                {
                    destination = JxqyMagicTargeting
                        .ClampWorldDestinationToRange(
                            _player,
                            destination,
                            magic.AttackRadius);
                }
            }
            var pending = new JxqyPendingMagicCast
            {
                SkillIndex = slot,
                Skill = skill,
                Destination = destination,
                Target = target,
            };

            // The original player keeps one NextAction. A martial-art command
            // issued during a basic swing replaces the auto-attack continuation
            // and runs as soon as that already-started swing completes.
            if (_playerAutoAttack != null)
                _playerAutoAttack.Target = null;
            _playerAutoAttackRunRequested = false;
            ClearPendingInteraction();
            if (_pendingPlayerMagicCast != null)
                return;
            if (!_player.CanPerformAction)
            {
                if (IsBasicAttackState(_player.State))
                {
                    _player.StopMovementPreservingAction();
                    _queuedPlayerMagicCast = pending;
                }
                return;
            }

            _queuedPlayerMagicCast = null;
            BeginPlayerMagicCast(pending);
        }

        private void BeginPlayerMagicCast(JxqyPendingMagicCast pending)
        {
            if (pending?.Skill?.Magic == null || !_player.CanPerformAction)
                return;

            // A combat action owns the character until its non-looping
            // animation completes. Keeping an old click-to-move path here
            // leaves the action on its final frame forever because movement
            // cannot advance while the state is Magic.
            _player.StopMovementPreservingAction();
            _player.SetDirection(
                pending.Destination - _player.PositionInWorld);
            _pendingPlayerMagicCast = pending;
            _pendingBasicAttacks.Remove(_player);
            _transientCombatStates.Remove(_player);
            _player.SetMagicState(
                pending.Skill.Magic.NoInterruption > 0);
        }

        private bool TryStartQueuedPlayerMagicCast()
        {
            if (_queuedPlayerMagicCast == null ||
                _pendingPlayerMagicCast != null ||
                !_player.CanPerformAction)
            {
                return false;
            }

            JxqyPendingMagicCast pending = _queuedPlayerMagicCast;
            _queuedPlayerMagicCast = null;
            if (pending.Skill == null || pending.Skill.Magic == null)
                return false;
            if (pending.Skill.CooldownMilliseconds > 0)
            {
                _uiSession?.SetNotice("武功尚未冷却");
                return false;
            }
            BeginPlayerMagicCast(pending);
            return ReferenceEquals(_pendingPlayerMagicCast, pending);
        }

        private static bool IsBasicAttackState(JxqyCharacterState state)
        {
            return state == JxqyCharacterState.Attack ||
                   state == JxqyCharacterState.Attack1 ||
                   state == JxqyCharacterState.Attack2;
        }

        private void CompletePendingPlayerMagicCast()
        {
            JxqyPendingMagicCast pending = _pendingPlayerMagicCast;
            if (pending == null)
                return;
            _pendingPlayerMagicCast = null;
            JxqyMagicDefinition magic = pending.Skill.Magic;
            magic.ApplyLevel(pending.Skill.Level);
            RefreshMagicLifetime(magic);
            JxqyMagicDefinition castMagic =
                magic.CreateRuntimeSnapshot();
            bool used = !_player.ManaLimit &&
                        _combat.UseMagic(
                            _player,
                            castMagic,
                            pending.Destination,
                            pending.Target);
            if (!used)
            {
                if (_player.ManaLimit ||
                    _player.Mana < magic.ManaCost)
                {
                    _uiSession?.SetNotice(
                        "没有足够的内力使用这种武功");
                }
                else if (_player.Thew < magic.ThewCost)
                {
                    _uiSession?.SetNotice(
                        "没有足够的体力使用这种武功");
                }
                _player.Stop();
                return;
            }
            _skills.BeginCooldown(
                magic.Id,
                Math.Max(0, magic.ColdMilliseconds));
            PlayMagicSound(magic.FlyingSoundFileName);
            _player.Stop();
        }

        private void OnMagicProjectileSpawned(
            JxqyMagicProjectile projectile)
        {
            if (projectile == null || projectile.Magic == null)
                return;
#if UNITY_EDITOR
            ObserveAcceptanceMartialArtProjectile(projectile);
#endif
            SpawnMagicVisual(
                projectile.Magic,
                projectile,
                projectile.Destination,
                projectile.Target);
        }

        private void OnMagicUsed(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 destination)
        {
            if (magic != null &&
                magic.MoveKind == 13)
            {
                if (ReferenceEquals(source, _player))
                {
                    AddAutomaticMagicExperience(magic.Id);
                }
                else if (source is JxqyNpc partner &&
                         partner.Kind == JxqyCharacterKind.Follower)
                {
                    AddNpcAutomaticMagicExperience(partner, magic.Id);
                }
            }
            if (magic == null || magic.ReviveBodyRadius <= 0 ||
                _objects == null)
            {
                return;
            }
            JxqyBodyRevivalResult result =
                JxqyBodyRevivalSystem.Resolve(
                    _objects,
                    source,
                    magic,
                    destination);
            foreach (JxqyWorldObject body in result.RemovedBodies)
                RemoveObjectVisual(body);
            SpawnRevivedNpcsAsync(
                result.RevivedNpcs,
                this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTask SpawnRevivedNpcsAsync(
            IReadOnlyList<JxqyRevivedNpcRequest> requests,
            CancellationToken cancellationToken)
        {
            if (requests == null)
                return;
            foreach (JxqyRevivedNpcRequest request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string safeFileName = SafeLegacyFileName(
                    request.NpcFileName,
                    ".ini");
                string text = await LoadDynamicTextAsync(
                    "ini/npc",
                    safeFileName,
                    cancellationToken);
                Dictionary<string, Dictionary<string, string>> sections =
                    JxqyLegacySaveImporter.ParseIni(text);
                if (!sections.TryGetValue(
                        "INIT",
                        out Dictionary<string, string> init))
                {
                    throw new InvalidOperationException(
                        $"Revived NPC definition '{safeFileName}' has no " +
                        "INIT section.");
                }
                JxqyNpc npc = CreateNpc(init);
                await PrepareNpcEquipmentAsync(npc);
                npc.TilePosition = request.TilePosition;
                npc.CurrentDirection = request.Direction;
                npc.Relation = request.Relation;
                npc.LifeMilliseconds = request.LifeMilliseconds;
                npc.IsMagicSummon = true;
                npc.SetMagicSummoner(request.Summoner);
                _npcs.Add(npc);
                await CreateNpcVisualAsync(npc, cancellationToken);
            }
        }

        private void SpawnMagicVisual(
            JxqyMagicDefinition magic,
            JxqyMagicProjectile projectile,
            JxqyFloat2 destination,
            JxqyCharacter target)
        {
            if (magic == null ||
                !_magicVisualAssets.TryGetValue(
                    magic.Id,
                    out JxqyRuntimeMagicAssets assets))
            {
                return;
            }
            JxqyAnimationMetadata metadata =
                magic.MoveKind == 15 && assets.SuperMode != null
                    ? assets.SuperMode
                    : assets.Flying;
            if (metadata == null)
                return;
            var animation = new JxqyAnimationPlayer(metadata)
            {
                IsLooping = projectile != null,
            };
            JxqyFloat2 origin = projectile?.Position ??
                                target?.PositionInWorld ??
                                _player.PositionInWorld;
            JxqyFloat2 direction =
                projectile?.PresentationDirection ?? JxqyFloat2.Zero;
            if (direction == JxqyFloat2.Zero)
            {
                direction =
                    (target?.PositionInWorld ?? destination) - origin;
            }
            if (direction != JxqyFloat2.Zero)
            {
                animation.SetDirection(
                    JxqyDirection.GetIndex(
                        direction,
                        metadata.DirectionCount));
            }
            animation.Restart();
            var state = new JxqyRuntimeMagicVisual
            {
                Magic = magic,
                Projectile = projectile,
                FollowTarget = magic.MoveKind == 13
                    ? target ?? _player
                    : null,
                Visual = new JxqyWorldVisual
                {
                    Id = $"magic:{magic.Id}:{Time.frameCount}:" +
                         $"{_magicVisuals.Count}",
                    Kind = projectile == null
                        ? JxqyWorldVisualKind.Magic
                        : JxqyWorldVisualKind.Projectile,
                    Animation = animation,
                    MaterialKey = "default",
                    UsesMagicOcclusionOutline =
                        JxqyMagicOcclusionOutlinePolicy.IsEnabled(
                            magic.Id),
                    IsVisible = projectile == null ||
                                projectile.DelaySeconds <= 0f,
                },
            };
            SetMagicVisualPosition(state.Visual, origin);
            _magicVisuals.Add(state);
            _frameVisuals.Add(state.Visual);
            if (projectile != null)
                _projectileVisuals[projectile] = state;
        }

        private void OnMagicProjectileResolved(
            JxqyMagicProjectile projectile)
        {
#if UNITY_EDITOR
            if (projectile?.Source == _player)
                _acceptanceMagicResolveCount++;
#endif
            BeginMagicProjectileVanish(projectile);
        }

        private void OnMagicProjectileExpired(
            JxqyMagicProjectile projectile)
        {
            bool playVanish =
                ShouldPlayVanishOnNaturalExpiry(projectile?.Magic);
#if UNITY_EDITOR
            ObserveAcceptanceMartialArtNaturalExpiry(
                projectile,
                playVanish);
#endif
            if (!playVanish)
            {
                RemoveMagicProjectileVisual(projectile);
                return;
            }
            BeginMagicProjectileVanish(projectile);
        }

        private void RemoveMagicProjectileVisual(
            JxqyMagicProjectile projectile)
        {
            if (projectile == null ||
                !_projectileVisuals.TryGetValue(
                    projectile,
                    out JxqyRuntimeMagicVisual state))
            {
                return;
            }
            RemoveMagicVisual(state);
        }

        private static bool ShouldPlayVanishOnNaturalExpiry(
            JxqyMagicDefinition magic)
        {
            if (magic == null)
                return false;
            // Original MagicManager creates moving projectiles with
            // destroyOnEnd=false. Their FlyingImage ends silently; the
            // VanishImage is reserved for an actual hit or obstacle impact.
            // Treating natural expiry as impact produces an extra ring for
            // MoveKind 6 embedded attack definitions.
            return magic.MoveKind != 2 &&
                   magic.MoveKind != 3 &&
                   magic.MoveKind != 4 &&
                   magic.MoveKind != 5 &&
                   magic.MoveKind != 6 &&
                   magic.MoveKind != 7 &&
                   magic.MoveKind != 8 &&
                   magic.MoveKind != 10 &&
                   magic.MoveKind != 16 &&
                   magic.MoveKind != 24;
        }

        private void OnMagicContacted(
            JxqyMagicProjectile projectile,
            JxqyCharacter target,
            JxqyDamageResult result)
        {
            if (projectile?.Magic?.MoveKind == 15 && target != null)
            {
                SpawnSuperMagicVanish(
                    projectile.Magic,
                    target.PositionInWorld);
            }
            if (target != null && !result.Hit)
                _combatFloatTextPool?.ShowMiss(target);
            if (projectile?.Magic == null ||
                target == null ||
                !result.Hit)
            {
                return;
            }
            if (JxqyExperienceRules.IsPlayerMagicExperienceSource(
                    projectile.Source,
                    _player))
            {
                string magicId =
                    JxqyExperienceRules.ResolvePlayerMagicExperienceId(
                        projectile.Magic.Id,
                        _skills?.Find(projectile.Magic.Id) != null);
                if (!string.IsNullOrWhiteSpace(magicId))
                    AddAutomaticMagicExperience(magicId);
                return;
            }
            if (projectile.Source is JxqyNpc partner &&
                partner.Kind == JxqyCharacterKind.Follower)
            {
                AddNpcAutomaticMagicExperience(
                    partner,
                    projectile.Magic.Id);
            }
        }

        private void SpawnSuperMagicVanish(
            JxqyMagicDefinition magic,
            JxqyFloat2 position)
        {
            if (magic == null ||
                !_magicVisualAssets.TryGetValue(
                    magic.Id,
                    out JxqyRuntimeMagicAssets assets) ||
                assets.Vanish == null)
            {
                return;
            }
            var animation = new JxqyAnimationPlayer(assets.Vanish)
            {
                IsLooping = false,
            };
            animation.Restart();
            var state = new JxqyRuntimeMagicVisual
            {
                Magic = magic,
                IsVanish = true,
                Visual = new JxqyWorldVisual
                {
                    Id = $"magic-super-vanish:{magic.Id}:" +
                         $"{Time.frameCount}:{_magicVisuals.Count}",
                    Kind = JxqyWorldVisualKind.Magic,
                    Animation = animation,
                    MaterialKey = "default",
                    UsesMagicOcclusionOutline =
                        JxqyMagicOcclusionOutlinePolicy.IsEnabled(
                            magic.Id),
                },
            };
            SetMagicVisualPosition(state.Visual, position);
            _magicVisuals.Add(state);
            _frameVisuals.Add(state.Visual);
            PlayMagicSound(magic.VanishSoundFileName);
        }

        private void AddMagicExperience(string magicId, int amount)
        {
            if (_skills == null ||
                !_skills.AddExperience(
                    magicId,
                    amount,
                    out bool leveledUp))
            {
                return;
            }
            if (leveledUp)
            {
                JxqySkillEntry entry = _skills.Find(magicId);
                _player.ApplyMagicLevelBonuses(entry?.Magic);
                RefreshMagicLifetime(entry?.Magic);
                string name = string.IsNullOrWhiteSpace(entry?.Magic?.Name)
                    ? magicId
                    : entry.Magic.Name;
                _uiSession?.SetNotice(
                    $"\u6b66\u529f {name} \u5347\u7ea7\u4e86");
                return;
            }
            _uiSession?.Refresh();
        }

        private void AddAutomaticMagicExperience(string magicId)
        {
            JxqySkillEntry entry = _skills?.Find(magicId);
            if (entry == null ||
                !JxqyExperienceRules.CanGainAutomaticMagicExperience(
                    entry.Level,
                    _player?.Level ?? 0))
            {
                return;
            }
            AddMagicExperience(
                magicId,
                JxqyExperienceRules.GetAutomaticMagicExperience(
                    entry.Level));
        }

        private void AddNpcAutomaticMagicExperience(
            JxqyNpc npc,
            string magicId)
        {
            JxqySkillEntry entry = npc?.Skills?.Find(magicId);
            if (entry == null ||
                !JxqyExperienceRules.CanGainAutomaticMagicExperience(
                    entry.Level,
                    npc.Level))
            {
                return;
            }
            if (!npc.Skills.AddExperience(
                    magicId,
                    JxqyExperienceRules.GetAutomaticMagicExperience(
                        entry.Level),
                    out bool leveledUp))
            {
                return;
            }
            CacheOriginalCharacterSkills(npc);
            if (leveledUp)
            {
                entry = npc.Skills.Find(magicId);
                RefreshMagicLifetime(entry?.Magic);
                SynchronizeNpcSkillCombatDefinitions(npc);
                string name = string.IsNullOrWhiteSpace(entry?.Magic?.Name)
                    ? magicId
                    : entry.Magic.Name;
                _uiSession?.SetNotice(
                    $"{name}的等级提升了。");
                return;
            }
            _uiSession?.Refresh();
        }

        private void AddKillMagicExperience(int characterExperience)
        {
            if (characterExperience <= 0 || _skills == null)
                return;

            JxqySkillEntry selected = _uiSession?.SelectedSkill;
            if (selected == null ||
                selected.LegacyListIndex < 40 ||
                selected.LegacyListIndex > 44)
            {
                return;
            }
            AddMagicExperience(
                selected.Magic.Id,
                JxqyExperienceRules.GetSelectedMagicKillExperience(
                    characterExperience));
        }

        private void BeginMagicProjectileVanish(
            JxqyMagicProjectile projectile)
        {
            if (projectile == null ||
                !_projectileVisuals.TryGetValue(
                    projectile,
                    out JxqyRuntimeMagicVisual state))
                return;
            _projectileVisuals.Remove(projectile);
            state.Projectile = null;
            SetMagicVisualPosition(state.Visual, projectile.Position);
            if (state.Magic.MoveKind == 15)
            {
                // Super-mode settlement owns one VanishImage at every
                // affected character. The fixed full-screen source image
                // disappears instead of producing a duplicate at its origin.
                RemoveMagicVisual(state);
                return;
            }
            if (!_magicVisualAssets.TryGetValue(
                    state.Magic.Id,
                    out JxqyRuntimeMagicAssets assets) ||
                assets.Vanish == null)
            {
                // Fixed-wall magic is born at its requested cells. A cell
                // blocked for damage must still present the original flying
                // frame once; otherwise every blocked sprite is removed in
                // the simulation tick before the renderer can show its
                // foreground-clipped outline.
                if (state.Magic.MoveKind == 9 &&
                    projectile.WasBlockedByWall &&
                    state.Visual?.Animation != null)
                {
                    state.Visual.Kind = JxqyWorldVisualKind.Magic;
                    state.Visual.Animation.IsLooping = false;
                    state.Visual.Animation.Restart();
                    return;
                }
                // MoveKind 13 resolves on the caster/target in the same
                // simulation tick. Its FlyingImage is the actual body effect
                // (for example 清心术), not a travelling projectile. Let that
                // animation finish once instead of deleting it before the
                // first rendered frame when no VanishImage exists.
                if (state.Magic.MoveKind == 13 &&
                    state.Visual?.Animation != null)
                {
                    state.Visual.Kind = JxqyWorldVisualKind.Magic;
                    bool persistentBuff =
                        state.Magic.SpecialKind == 3 ||
                        state.Magic.SpecialKind == 6;
                    state.Visual.Animation.IsLooping = persistentBuff;
                    state.RemainingSeconds = persistentBuff
                        ? state.Magic.KeepMilliseconds > 0
                            ? state.Magic.KeepMilliseconds / 1000f
                            : state.Magic.LifeSeconds
                        : 0f;
                    state.Visual.Animation.Restart();
                    return;
                }
                RemoveMagicVisual(state);
                return;
            }
            state.IsVanish = true;
            state.Visual.Kind = JxqyWorldVisualKind.Magic;
            state.Visual.Animation =
                new JxqyAnimationPlayer(assets.Vanish)
                {
                    IsLooping = false,
                };
            state.Visual.Animation.Restart();
            PlayMagicSound(state.Magic.VanishSoundFileName);
        }

        private bool IsMagicProjectileBlocked(JxqyFloat2 worldPosition)
        {
            if (_map == null)
                return false;
            JxqyIntPoint tile =
                JxqyIsometricMapMath.WorldPixelToTile(
                    Mathf.RoundToInt(worldPosition.X),
                    Mathf.RoundToInt(worldPosition.Y));
            // Flying magic has its own legacy barrier rules. In particular,
            // tiles marked as character-only/transitional obstacles must not
            // swallow arrows and other projectiles before they can resolve a
            // hit or MISS.
            return _map.IsObstacleForMagic(tile.X, tile.Y);
        }

        private void UpdateMagicVisuals(float elapsedSeconds)
        {
            for (int index = _magicVisuals.Count - 1;
                 index >= 0;
                 index--)
            {
                JxqyRuntimeMagicVisual state = _magicVisuals[index];
                if (state.Projectile != null)
                {
                    bool delayElapsed =
                        state.Projectile.DelaySeconds <= 0f;
                    state.Visual.IsVisible = delayElapsed;
                    if (!delayElapsed)
                        continue;
                    SetMagicVisualPosition(
                        state.Visual,
                        state.Projectile.Position);
                    if (state.Projectile.Direction != JxqyFloat2.Zero)
                    {
                        state.Visual.Animation.SetDirection(
                            JxqyDirection.GetIndex(
                                state.Projectile.Direction,
                                state.Visual.Animation.Metadata
                                    .DirectionCount));
                    }
                }
                else if (state.FollowTarget != null &&
                         !state.FollowTarget.IsDead &&
                         !state.IsVanish)
                {
                    SetMagicVisualPosition(
                        state.Visual,
                        state.FollowTarget.PositionInWorld);
                }
                if (state.RemainingSeconds > 0f)
                {
                    state.RemainingSeconds -= elapsedSeconds;
                    if (state.RemainingSeconds <= 0f)
                    {
                        RemoveMagicVisualAt(index);
                        continue;
                    }
                }
                state.Visual.Animation.Advance(elapsedSeconds);
                if (!state.Visual.Animation.IsLooping &&
                    state.Visual.Animation.IsFinished)
                {
                    RemoveMagicVisualAt(index);
                }
            }
        }

        private static void SetMagicVisualPosition(
            JxqyWorldVisual visual,
            JxqyFloat2 position)
        {
            visual.WorldPosition = new Vector2(position.X, position.Y);
            JxqyIntPoint tile =
                JxqyIsometricMapMath.WorldPixelToTile(
                    Mathf.RoundToInt(position.X),
                    Mathf.RoundToInt(position.Y));
            visual.TileColumn = tile.X;
            visual.TileRow = tile.Y;
        }

        private void RemoveMagicVisual(
            JxqyRuntimeMagicVisual state)
        {
            int index = _magicVisuals.IndexOf(state);
            if (index >= 0)
                RemoveMagicVisualAt(index);
        }

        private void RemoveMagicVisualAt(int index)
        {
            JxqyRuntimeMagicVisual state = _magicVisuals[index];
            if (state.Projectile != null)
                _projectileVisuals.Remove(state.Projectile);
            _frameVisuals.Remove(state.Visual);
            _magicVisuals.RemoveAt(index);
        }

        private void ClearMagicVisuals()
        {
            for (int index = _magicVisuals.Count - 1;
                 index >= 0;
                 index--)
            {
                _frameVisuals.Remove(_magicVisuals[index].Visual);
            }
            _magicVisuals.Clear();
            _projectileVisuals.Clear();
        }

        private void PlayMagicSound(string legacyFileName)
        {
            if (_audio == null ||
                !TryResolveSoundAddress(
                    legacyFileName,
                    "MagicSound",
                    out string address))
            {
                return;
            }
            _audio.PlaySoundAsync(
                    address,
                    1f,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private void ApplyCharacterStateSound(
            JxqyCharacter character,
            JxqyCharacterState state,
            IReadOnlyDictionary<int, string> stateSounds,
            string visualId,
            ref string activeSoundId)
        {
            RemoveActiveCharacterStateSound(ref activeSoundId);
            if (character == null || stateSounds == null ||
                !stateSounds.TryGetValue((int)state, out string fileName) ||
                string.IsNullOrWhiteSpace(fileName) ||
                IsDelayedCharacterStateSound(state))
            {
                return;
            }

            if (IsMovementCharacterState(state) &&
                _audio is IJxqyWorldAudioPort worldAudio)
            {
                if (!TryResolveSoundAddress(
                        fileName,
                        "CharacterMovementSound",
                        out string address))
                {
                    return;
                }
                activeSoundId = $"actor-state:{visualId}";
                worldAudio.RegisterWorldSoundAsync(
                        activeSoundId,
                        address,
                        true,
                        character.PositionInWorld,
                        1f,
                        this.GetCancellationTokenOnDestroy())
                    .Forget();
                return;
            }

            PlayCharacterStateSoundOnce(character, fileName);
        }

        private void PlayCharacterCompletionSound(
            JxqyCharacter character,
            JxqyCharacterState state,
            IReadOnlyDictionary<int, string> stateSounds)
        {
            if (!IsDelayedCharacterStateSound(state) ||
                stateSounds == null ||
                !stateSounds.TryGetValue((int)state, out string fileName) ||
                string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }
            PlayCharacterStateSoundOnce(character, fileName);
        }

        private void PlayCharacterStateSoundOnce(
            JxqyCharacter character,
            string legacyFileName)
        {
            if (_audio == null || character == null ||
                !TryResolveSoundAddress(
                    legacyFileName,
                    "CharacterStateSound",
                    out string address))
            {
                return;
            }
            // The original Player plays state one-shots directly. NPCs use
            // positional playback. Keeping player jump/hurt/death sounds out
            // of the ambient world-sound pool also preserves that behavior.
            if (ReferenceEquals(character, _player))
            {
                _audio.PlaySoundAsync(
                        address,
                        1f,
                        this.GetCancellationTokenOnDestroy())
                    .Forget();
                return;
            }
            if (_audio is IJxqyWorldAudioPort worldAudio)
            {
                worldAudio.PlayWorldSoundOnceAsync(
                        address,
                        character.PositionInWorld,
                        1f,
                        this.GetCancellationTokenOnDestroy())
                    .Forget();
                return;
            }
            _audio.PlaySoundAsync(
                    address,
                    1f,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private bool TryResolveSoundAddress(
            string legacyFileName,
            string usage,
            out string address)
        {
            address = string.Empty;
            if (string.IsNullOrWhiteSpace(legacyFileName))
                return false;
            string generated = _contentContext.MediaAddressResolver
                .ResolveSound(legacyFileName);
            if (JxqyResourceAddressCatalog.TryResolveGeneratedAddress(
                    JxqyLegacyResourceKind.Sound,
                    legacyFileName,
                    generated,
                    out address))
            {
                return true;
            }
            JxqyResourceAddressCatalog.ReportOptionalAudioMissing(
                usage,
                legacyFileName,
                generated);
            return false;
        }

        private void RemoveActiveCharacterStateSound(
            ref string activeSoundId)
        {
            if (!string.IsNullOrWhiteSpace(activeSoundId) &&
                _audio is IJxqyWorldAudioPort worldAudio)
            {
                worldAudio.RemoveWorldSound(activeSoundId);
            }
            activeSoundId = string.Empty;
        }

        private static bool IsDelayedCharacterStateSound(
            JxqyCharacterState state)
        {
            return state == JxqyCharacterState.Attack ||
                   state == JxqyCharacterState.Attack1 ||
                   state == JxqyCharacterState.Attack2 ||
                   state == JxqyCharacterState.Magic;
        }

        private static bool IsMovementCharacterState(
            JxqyCharacterState state)
        {
            return state == JxqyCharacterState.Walk ||
                   state == JxqyCharacterState.Run ||
                   state == JxqyCharacterState.FightWalk ||
                   state == JxqyCharacterState.FightRun;
        }

        private void OnUiSoundRequested(JxqyUiSound sound)
        {
            if (_uiSoundFileNames.TryGetValue(
                    sound,
                    out string legacyFileName))
            {
                PlayMagicSound(legacyFileName);
            }
        }

        private void OnMusicVolumeChanged(float volume) =>
            ApplyMusicVolume(volume, true);

        private void OnSoundVolumeChanged(float volume) =>
            ApplySoundVolume(volume, true);

        private void OnGameSpeedChanged(int speed) =>
            ApplyGameSpeed(speed, true);

        private void ApplyMusicVolume(float volume, bool persist)
        {
            volume = Mathf.Clamp01(volume);
            _audio?.SetMusicVolume(volume);
            if (!persist)
                return;
            PlayerPrefs.SetFloat(MusicVolumePreference, volume);
            PlayerPrefs.Save();
        }

        private void ApplySoundVolume(float volume, bool persist)
        {
            volume = Mathf.Clamp01(volume);
            _audio?.SetSoundVolume(volume);
            if (!persist)
                return;
            PlayerPrefs.SetFloat(SoundVolumePreference, volume);
            PlayerPrefs.Save();
        }

        private void ApplyGameSpeed(int speed, bool persist)
        {
            speed = Mathf.Clamp(speed, 0, 2);
            // Product override: preserve authored combat, script, UI, weather
            // and audio timing. This multiplier is consumed only by actor
            // locomotion and player recovery/conversion clocks.
            _gameSpeedScale = speed switch
            {
                0 => 1f / 3f,
                1 => 1f / 2f,
                _ => 1f,
            };
            if (!persist)
                return;
            PlayerPrefs.SetInt(GameSpeedPreference, speed);
            PlayerPrefs.Save();
        }

        private void TickCombat(float elapsedSeconds)
        {
            if (_combat == null || _player == null)
                return;

            float elapsedMilliseconds = elapsedSeconds * 1000f;
            _inventory?.Tick(elapsedMilliseconds);
            _skills?.Tick(elapsedMilliseconds);
            _player.TickCombat(elapsedSeconds);
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                bool wasDead = npc.IsDead;
                npc.TickCombat(elapsedSeconds);
                if (wasDead && !npc.IsDead)
                    ProcessNpcRevival(npc);
            }
            PopulateCombatCollisionTargets(
                _combat.IsSuperModeActive);
            _combat.Tick(
                elapsedSeconds,
                _combatCollisionTargets,
                IsMagicProjectileBlocked);
            UpdateMagicVisuals(elapsedSeconds);
            CancelInterruptedBasicAttacks();

            if (_playerAutoAttack.Target != null)
            {
                if (_playerAutoAttack.TryRequestPursuit(
                        _player,
                        elapsedSeconds))
                {
                    BeginPlayerAutoAttackPursuit();
                }
                _playerAutoAttack.Tick(
                    _player,
                    elapsedSeconds,
                    BeginBasicAttack);
                if (_playerAutoAttack.Target.IsDead)
                    _playerAutoAttack.Target = null;
            }

            _npcDeathsReadyToFinalize.Clear();
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (npc.IsDead)
                {
                    ProcessNpcDeath(npc);
                    if (!_finalizedNpcDeaths.Contains(npc) &&
                        IsNpcDeathPresentationComplete(npc))
                    {
                        _npcDeathsReadyToFinalize.Add(npc);
                    }
                    continue;
                }
                if (_npcs.IsAiDisabled)
                    continue;
                if (npc.KeepAttackX > 0 || npc.KeepAttackY > 0)
                {
                    _npcKeepAttackCooldowns.TryGetValue(
                        npc,
                        out float keepCooldown);
                    keepCooldown -= elapsedSeconds;
                    if (keepCooldown <= 0f &&
                        npc.CanPerformAction)
                    {
                        JxqyIntPoint attackWorld =
                            JxqyIsometricMapMath.TileToWorldPixel(
                                npc.KeepAttackX,
                                npc.KeepAttackY);
                        BeginBasicAttackAt(
                            npc,
                            new JxqyFloat2(
                                attackWorld.X,
                                attackWorld.Y));
                        keepCooldown = 0f;
                    }
                    _npcKeepAttackCooldowns[npc] = keepCooldown;
                    continue;
                }
                if (npc.Intent != JxqyNpcIntent.Attack ||
                    npc.FollowTarget == null)
                    continue;
                if (!_npcAutoAttacks.TryGetValue(
                        npc,
                        out JxqyAutoAttackController controller))
                {
                    controller = new JxqyAutoAttackController
                    {
                        IntervalSeconds = npc.AttackIntervalSeconds,
                        Range = Math.Max(
                            48f,
                            GetMaximumBasicAttackDistance(npc) *
                            48f),
                        MaximumTileDistance =
                            GetMaximumBasicAttackDistance(npc),
                    };
                    _npcAutoAttacks.Add(npc, controller);
                }
                controller.Target = npc.FollowTarget;
                controller.Tick(
                    npc,
                    elapsedSeconds,
                    BeginBasicAttack);
            }
            UpdateCombatEngagementState();
            TickTransientCombatStates(elapsedSeconds);
            foreach (JxqyNpc npc in _npcDeathsReadyToFinalize)
                FinalizeNpcDeathAsync(npc).Forget();
            _npcDeathsReadyToFinalize.Clear();
        }

        private void SetNpcAiDisabled(bool disabled)
        {
            // Preserve the original process-wide AI switch independently of
            // the current map's manager. Some scripts disable AI before
            // LoadMap and only re-enable it after their blocking dialogue.
            _npcAiDisabled = disabled;
            if (_npcs == null)
                return;
            if (!disabled)
            {
                _npcs.EnableAi();
                return;
            }

            _npcs.DisableAi();
            _npcAutoAttacks.Clear();
            foreach (JxqyCharacter attacker in
                     _pendingBasicAttacks.Keys.ToArray())
            {
                if (attacker is JxqyNpc)
                    _pendingBasicAttacks.Remove(attacker);
            }
        }

        private void PopulateCombatCollisionTargets(
            bool visibleViewportOnly)
        {
            _combatCollisionTargets.Clear();
            _combatCollisionTargets.Add(_player);
            if (_npcs == null)
                return;
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (visibleViewportOnly && !IsNpcInCurrentViewport(npc))
                    continue;
                _combatCollisionTargets.Add(npc);
            }
        }

        private bool IsNpcInCurrentViewport(JxqyNpc npc)
        {
            if (npc == null || !npc.IsVisible)
                return false;
            JxqyFloat2 position = npc.PositionInWorld;
            return position.X >= _camera.X &&
                   position.Y >= _camera.Y &&
                   position.X <= _camera.Right &&
                   position.Y <= _camera.Bottom;
        }

        private bool HasActiveSuperMagicPresentation()
        {
            if (_combat?.IsSuperModeActive == true)
                return true;
            for (int index = 0; index < _magicVisuals.Count; index++)
            {
                if (_magicVisuals[index].Magic?.MoveKind == 15)
                    return true;
            }
            return false;
        }

        private void TickSuperMagicPresentation(float elapsedSeconds)
        {
            if (_combat?.IsSuperModeActive == true)
            {
                PopulateCombatCollisionTargets(
                    visibleViewportOnly: true);
                _combat.Tick(
                    elapsedSeconds,
                    _combatCollisionTargets,
                    IsMagicProjectileBlocked);
            }
            UpdateMagicVisuals(elapsedSeconds);
            _combatFloatTextPool?.UpdateVisuals(
                _worldCamera,
                elapsedSeconds);
            SubmitFrame();
        }

        private static int GetMaximumBasicAttackDistance(
            JxqyCharacter character)
        {
            int maximum = character is JxqyNpc npc
                ? Math.Max(1, npc.AttackRadius)
                : 1;
            foreach (JxqyRangedMagicReference reference in
                     character.AdditionalBasicMagics)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Max(1, reference.Distance));
            }
            return maximum;
        }

        private void UpdateCombatEngagementState()
        {
            if (_player == null)
                return;
            bool hasLiveTarget =
                _playerAutoAttack?.Target != null &&
                !_playerAutoAttack.Target.IsDead &&
                JxqyRelations.AreOpposed(
                    _player,
                    _playerAutoAttack.Target);
            bool isBeingAttacked = _npcs?.Npcs.Any(npc =>
                !npc.IsDead &&
                npc.IsVisible &&
                npc.Intent == JxqyNpcIntent.Attack &&
                ReferenceEquals(npc.FollowTarget, _player)) == true;
            bool fighting = JxqyPlayerFightStatePolicy.Resolve(
                hasLiveTarget,
                isBeingAttacked,
                _scriptSession?.ScriptedPlayerFightState);
            _player.SetFighting(fighting);
        }

        private bool ResolveBasicAttack(
            JxqyCharacter attacker,
            JxqyCharacter target)
        {
            JxqyDamageResult result = JxqyDamageCalculator.Resolve(
                attacker,
                target,
                Math.Max(
                    JxqyDamageCalculator.MinimalDamage,
                    attacker.Attack),
                attacker.Attack2,
                attacker.Attack3,
                0,
                _legacyRandom,
                guaranteedHit: false,
                enterHurtState: false);
            if (result.Hit &&
                JxqyDamageCalculator.ShouldEnterHurtState(
                    target,
                    _legacyRandom.Next(0, 4)))
            {
                _pendingBasicAttacks.Remove(target);
                target.SetState(JxqyCharacterState.Hurt);
            }
            if (!result.Hit)
                _combatFloatTextPool?.ShowMiss(target);
            return result.Hit;
        }

        private bool BeginBasicAttack(
            JxqyCharacter attacker,
            JxqyCharacter target)
        {
            if (attacker == null || target == null ||
                attacker.IsDead || target.IsDead)
            {
                return false;
            }
            attacker.StopMovementPreservingAction();
            attacker.SetDirection(
                target.PositionInWorld - attacker.PositionInWorld);
            JxqyCharacterState attackState =
                SelectLegacyBasicAttackState(attacker);
            attacker.SetState(attackState);
            JxqyMagicDefinition magic = SelectBasicAttackMagic(
                attacker,
                target);
            _pendingBasicAttacks[attacker] =
                new JxqyPendingBasicAttack
                {
                    Target = target,
                    Magic = magic,
                    Destination = target.PositionInWorld,
                    MaximumTileDistance = GetBasicAttackDistance(
                        attacker,
                        magic),
                };
            return true;
        }

        private bool BeginBasicAttackAt(
            JxqyCharacter attacker,
            JxqyFloat2 destination)
        {
            if (attacker == null || attacker.IsDead ||
                !attacker.CanPerformAction)
                return false;
            JxqyPendingBasicAttack pending =
                CreateBasicAttackAt(attacker, destination);
            StartBasicAttackAt(attacker, pending);
            return true;
        }

        private bool BeginForcedScriptedAttackAt(
            JxqyCharacter attacker,
            JxqyFloat2 destination)
        {
            if (attacker == null || attacker.IsDead)
                return false;
            // NpcAttack is a directing command. In the original opening each
            // command releases its selected martial art on that command's own
            // sword swing. Deferring it until the next NpcAttack made the
            // second swing release both 南岳支天 and 牧野流星 together.
            _pendingBasicAttacks.Remove(attacker);
            if (ReferenceEquals(attacker, _player))
            {
                _pendingPlayerMagicCast = null;
                _queuedPlayerMagicCast = null;
            }
            StartBasicAttackAt(
                attacker,
                CreateBasicAttackAt(attacker, destination));
            CompletePendingBasicAttack(attacker);
            return true;
        }

        private JxqyPendingBasicAttack CreateBasicAttackAt(
            JxqyCharacter attacker,
            JxqyFloat2 destination)
        {
            JxqyIntPoint destinationTile =
                JxqyIsometricMapMath.WorldPixelToTile(
                    (int)Math.Round(destination.X),
                    (int)Math.Round(destination.Y));
            int distance = JxqyPathfinder.GetViewTileDistance(
                attacker.TilePosition,
                destinationTile);
            return new JxqyPendingBasicAttack
            {
                Magic = SelectBasicAttackMagic(attacker, distance),
                Destination = destination,
                MaximumTileDistance = Math.Max(1, distance),
            };
        }

        private void StartBasicAttackAt(
            JxqyCharacter attacker,
            JxqyPendingBasicAttack pending)
        {
            attacker.StopMovementPreservingAction();
            attacker.SetDirection(
                pending.Destination - attacker.PositionInWorld);
            _pendingBasicAttacks[attacker] = pending;
            JxqyCharacterState attackState =
                SelectLegacyBasicAttackState(attacker);
            attacker.SetState(attackState);
        }

        private bool BeginScriptedMagicAt(
            JxqyCharacter attacker,
            JxqyFloat2 destination)
        {
            if (attacker == null || attacker.IsDead ||
                !attacker.CanPerformAction || attacker.BasicMagic == null)
            {
                return false;
            }
            attacker.StopMovementPreservingAction();
            attacker.SetDirection(
                destination - attacker.PositionInWorld);
            JxqyIntPoint destinationTile =
                JxqyIsometricMapMath.WorldPixelToTile(
                    (int)Math.Round(destination.X),
                    (int)Math.Round(destination.Y));
            int distance = JxqyPathfinder.GetViewTileDistance(
                attacker.TilePosition,
                destinationTile);
            _pendingBasicAttacks[attacker] =
                new JxqyPendingBasicAttack
                {
                    Magic = attacker.BasicMagic,
                    Destination = destination,
                    MaximumTileDistance = Math.Max(1, distance),
                };
            attacker.SetMagicState(
                attacker.BasicMagic.NoInterruption > 0);
            return true;
        }

        private JxqyCharacterState SelectLegacyBasicAttackState(
            JxqyCharacter attacker)
        {
            int value = _legacyRandom.Next(0, 3);
            if (value == 1 && HasExactAttackAction(
                    attacker,
                    JxqyCharacterState.Attack1))
            {
                return JxqyCharacterState.Attack1;
            }
            if (value == 2 && HasExactAttackAction(
                    attacker,
                    JxqyCharacterState.Attack2))
            {
                return JxqyCharacterState.Attack2;
            }
            return JxqyCharacterState.Attack;
        }

        private bool HasExactAttackAction(
            JxqyCharacter attacker,
            JxqyCharacterState state)
        {
            if (!attacker.IsActionEnabled(state))
                return false;
            if (ReferenceEquals(attacker, _player))
            {
                return _playerScriptActions.ContainsKey((int)state) ||
                       _playerStateActions.ContainsKey((int)state);
            }
            return attacker is JxqyNpc npc &&
                   _npcVisuals.TryGetValue(
                       npc,
                       out JxqyRuntimeActorVisual visual) &&
                   (visual.Actions.ContainsKey((int)state) ||
                    visual.ActionFileNames.ContainsKey((int)state));
        }

        private void CompletePendingBasicAttack(JxqyCharacter attacker)
        {
            if (attacker == null ||
                !_pendingBasicAttacks.Remove(
                    attacker,
                    out JxqyPendingBasicAttack pending))
            {
                return;
            }
            JxqyCharacter target = pending.Target;
            if (attacker.IsDead ||
                target != null &&
                (target.IsDead ||
                 !JxqyRelations.AreOpposed(attacker, target)))
            {
                return;
            }
            if (pending.Magic != null)
            {
                JxqyMagicDefinition magic =
                    pending.Magic.CreateRuntimeSnapshot();
                magic.ApplyLevel(GetBasicAttackMagicLevel(
                    attacker,
                    pending.Magic));
                magic.AdditionalEffect =
                    GetBasicAttackAdditionalEffect(
                        attacker,
                        pending.Magic);
                RefreshMagicLifetime(magic);
                if (_combat.UseMagic(
                        attacker,
                        magic,
                        pending.Destination,
                        magic.MoveKind == 13 &&
                        IsWithinBasicAttackRange(
                            attacker,
                            target,
                            pending.MaximumTileDistance)
                            ? target
                            : null,
                        consumePlayerResources: false))
                {
                    PlayMagicSound(magic.FlyingSoundFileName);
                }
            }
            else if (target != null &&
                     IsWithinBasicAttackRange(
                         attacker,
                         target,
                         pending.MaximumTileDistance))
            {
                ResolveBasicAttack(attacker, target);
            }
        }

        private JxqyMagicAdditionalEffect GetBasicAttackAdditionalEffect(
            JxqyCharacter attacker,
            JxqyMagicDefinition selectedMagic)
        {
            if (attacker == null || selectedMagic == null ||
                !ReferenceEquals(selectedMagic, attacker.BasicMagic) &&
                !ReferenceEquals(selectedMagic, attacker.BasicMagic2))
            {
                return JxqyMagicAdditionalEffect.None;
            }
            JxqyEquipmentManager equipment =
                ReferenceEquals(attacker, _player)
                    ? _equipment
                    : (attacker as JxqyNpc)?.Equipment;
            return equipment?.GetAdditionalAttackEffect(
                       attacker,
                       selectedMagic) ??
                   JxqyMagicAdditionalEffect.None;
        }

        private static bool IsWithinAutoAttackRange(
            JxqyAutoAttackController controller,
            JxqyCharacter attacker,
            JxqyCharacter target)
        {
            if (controller == null || attacker == null || target == null)
                return false;
            return controller.MaximumTileDistance > 0
                ? IsWithinBasicAttackRange(
                    attacker,
                    target,
                    controller.MaximumTileDistance)
                : JxqyFloat2.Distance(
                    attacker.PositionInWorld,
                    target.PositionInWorld) <= controller.Range;
        }

        private static bool IsWithinBasicAttackRange(
            JxqyCharacter attacker,
            JxqyCharacter target,
            int maximumTileDistance)
        {
            return attacker != null &&
                   target != null &&
                   JxqyPathfinder.GetViewTileDistance(
                       attacker.TilePosition,
                       target.TilePosition) <=
                   Math.Max(1, maximumTileDistance);
        }

        private static int GetBasicAttackDistance(
            JxqyCharacter attacker,
            JxqyMagicDefinition magic)
        {
            if (attacker is not JxqyNpc npc)
                return 1;
            int defaultDistance = Math.Max(1, npc.AttackRadius);
            if (magic == null ||
                ReferenceEquals(magic, npc.BasicMagic) ||
                ReferenceEquals(magic, npc.BasicMagic2))
            {
                return defaultDistance;
            }
            foreach (JxqyRangedMagicReference reference in
                     npc.AdditionalBasicMagics)
            {
                if (ReferenceEquals(reference.Magic, magic))
                    return Math.Max(1, reference.Distance);
            }
            return defaultDistance;
        }

        private JxqyMagicDefinition SelectBasicAttackMagic(
            JxqyCharacter attacker,
            JxqyCharacter target)
        {
            int distance = JxqyPathfinder.GetViewTileDistance(
                attacker.TilePosition,
                target.TilePosition);
            return SelectBasicAttackMagic(attacker, distance);
        }

        private JxqyMagicDefinition SelectBasicAttackMagic(
            JxqyCharacter attacker,
            int distance)
        {
            JxqyMagicDefinition followerMagic =
                SelectOriginalFollowerMartialArt(attacker);
            if (followerMagic != null)
                return followerMagic;

            var choices = new List<JxqyRangedMagicReference>();
            int defaultDistance = attacker is JxqyNpc npc
                ? Math.Max(1, npc.AttackRadius)
                : 1;
            if (attacker.BasicMagic != null)
            {
                choices.Add(new JxqyRangedMagicReference
                {
                    Magic = attacker.BasicMagic,
                    Distance = defaultDistance,
                });
            }
            if (attacker.BasicMagic2 != null)
            {
                choices.Add(new JxqyRangedMagicReference
                {
                    Magic = attacker.BasicMagic2,
                    Distance = defaultDistance,
                });
            }
            choices.AddRange(attacker.AdditionalBasicMagics);
            if (choices.Count == 0)
                return null;
            int selectedDistance =
                SelectOriginalBasicAttackDistance(choices, distance);
            JxqyRangedMagicReference[] nearest = choices
                .Where(choice =>
                    Math.Max(1, choice.Distance) == selectedDistance)
                .ToArray();
            return nearest[_legacyRandom.Next(0, nearest.Length)].Magic;
        }

        private JxqyMagicDefinition SelectOriginalFollowerMartialArt(
            JxqyCharacter attacker)
        {
            if (attacker is not JxqyNpc npc ||
                npc.Kind != JxqyCharacterKind.Follower ||
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name) < 0)
            {
                return null;
            }

            JxqyMagicDefinition magic =
                SelectOriginalFollowerMartialArtForRoll(
                    npc,
                    _legacyRandom.Next(0, 100));
            if (magic != null &&
                _npcAutoAttacks.TryGetValue(
                    npc,
                    out JxqyAutoAttackController controller))
            {
                controller.MaximumTileDistance =
                    Math.Max(1, npc.AttackRadius);
                controller.Range = Math.Max(
                    48f,
                    controller.MaximumTileDistance * 48f);
            }
            return magic;
        }

        private static JxqyMagicDefinition
            SelectOriginalFollowerMartialArtForRoll(
                JxqyNpc npc,
                int roll)
        {
            if (npc == null ||
                npc.Kind != JxqyCharacterKind.Follower ||
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name) < 0)
            {
                return null;
            }

            int legacyListIndex = roll < 80 ? 1 : 2;
            JxqySkillEntry entry =
                npc.Skills?.FindAtLegacyIndex(legacyListIndex);
            JxqyMagicDefinition magic = entry?.Magic;
            if (magic == null ||
                npc.HasStatus(JxqyStatusKind.SkillDisabled))
            {
                return null;
            }
            // Character.SelectFightMagic copies the selected learned
            // magic's current AttackRadius onto the follower before the
            // attack begins. Keep the AI controller on that same range so a
            // ranged companion does not stand behind the player waiting for
            // an unnecessary adjacent path.
            npc.AttackRadius = Math.Max(1, magic.AttackRadius);
            return magic;
        }

        private static int GetBasicAttackMagicLevel(
            JxqyCharacter attacker,
            JxqyMagicDefinition selectedMagic)
        {
            if (attacker is JxqyNpc npc &&
                npc.Kind == JxqyCharacterKind.Follower &&
                JxqyOriginalCharacterCatalog.GetProfileIndex(npc.Name) >= 0)
            {
                foreach (JxqySkillEntry entry in npc.Skills.Skills)
                {
                    if (ReferenceEquals(entry.Magic, selectedMagic))
                        return Math.Max(1, entry.Level);
                }
            }
            return Math.Max(1, attacker.AttackLevel);
        }

        private static int SelectOriginalBasicAttackDistance(
            IReadOnlyList<JxqyRangedMagicReference> choices,
            int targetDistance)
        {
            int selectedDistance = 0;
            int smallestOffset = int.MaxValue;
            foreach (JxqyRangedMagicReference choice in choices
                         .OrderBy(choice =>
                             Math.Max(1, choice.Distance)))
            {
                int useDistance = Math.Max(1, choice.Distance);
                int offset = Math.Abs(targetDistance - useDistance);
                // Character.GetClosedAttackRadius updates only on a strict
                // improvement while iterating the sorted FlyIni list, so a
                // midpoint tie always keeps the shorter use distance.
                if (offset >= smallestOffset)
                    continue;
                smallestOffset = offset;
                selectedDistance = useDistance;
            }
            return selectedDistance;
        }

        private void CompleteFinishedCharacterAction(
            JxqyCharacter character)
        {
            CompletePendingBasicAttack(character);
            if (character.IsJumping)
            {
                // In the original runtime the jump action itself owns the
                // lifetime: when its last frame ends, any remaining jump path
                // is cancelled and the character returns to Stand.
                character.Stop();
                return;
            }
            // Hurt pauses an existing route while its original non-looping
            // animation plays. Resume that route instead of requiring
            // HasPath to be false, which left Hurt and the path blocking each
            // other forever.
            if (!character.ResumePathMovement())
                character.Stop();
            if (ReferenceEquals(character, _player))
                TryStartQueuedPlayerMagicCast();
        }

        private void CancelInterruptedBasicAttacks()
        {
            if (_pendingBasicAttacks.Count == 0)
                return;
            JxqyCharacter[] attackers =
                _pendingBasicAttacks.Keys.ToArray();
            foreach (JxqyCharacter attacker in attackers)
            {
                if (attacker.IsDead ||
                    attacker.State != JxqyCharacterState.Attack &&
                    attacker.State != JxqyCharacterState.Attack1 &&
                    attacker.State != JxqyCharacterState.Attack2)
                {
                    _pendingBasicAttacks.Remove(attacker);
                }
            }
        }

        private JxqyNpc FindClosestEnemy(
            JxqyCharacter source,
            float maximumWorldDistance)
        {
            JxqyNpc closest = null;
            float bestDistance = maximumWorldDistance;
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (npc.IsDead ||
                    !npc.IsVisible ||
                    !JxqyRelations.AreOpposed(source, npc))
                    continue;
                float distance = JxqyFloat2.Distance(
                    source.PositionInWorld,
                    npc.PositionInWorld);
                if (distance > bestDistance)
                    continue;
                bestDistance = distance;
                closest = npc;
            }
            return closest;
        }

        private JxqyNpc FindClosestEnemy(
            JxqyCharacter source,
            int maximumTileDistance)
        {
            JxqyNpc closest = null;
            int bestDistance = Math.Max(0, maximumTileDistance);
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (npc.IsDead ||
                    !npc.IsVisible ||
                    !JxqyRelations.AreOpposed(source, npc))
                {
                    continue;
                }
                int distance = JxqyPathfinder.GetViewTileDistance(
                    source.TilePosition,
                    npc.TilePosition);
                if (distance > bestDistance)
                    continue;
                bestDistance = distance;
                closest = npc;
            }
            return closest;
        }

        private void BeginTransientCombatState(
            JxqyCharacter character,
            JxqyCharacterState state,
            float seconds)
        {
            if (character == null || character.IsDead)
                return;
            character.SetState(state);
            _transientCombatStates[character] =
                Math.Max(0.01f, seconds);
        }

        private void TickTransientCombatStates(float elapsedSeconds)
        {
            if (_transientCombatStates.Count == 0)
                return;
            JxqyCharacter[] characters =
                _transientCombatStates.Keys.ToArray();
            foreach (JxqyCharacter character in characters)
            {
                if (character.IsDead)
                {
                    _transientCombatStates.Remove(character);
                    continue;
                }
                float remaining =
                    _transientCombatStates[character] -
                    elapsedSeconds * character.CharacterTimeScale;
                if (remaining > 0)
                {
                    _transientCombatStates[character] = remaining;
                    continue;
                }
                _transientCombatStates.Remove(character);
                character.SetState(
                    character.IsInFighting
                        ? JxqyCharacterState.FightStand
                        : JxqyCharacterState.Stand);
            }
        }

        private void ProcessNpcDeath(JxqyNpc npc)
        {
            if (!_processedNpcDeaths.Add(npc))
                return;
            _npcAutoAttacks.Remove(npc);
            _npcKeepAttackCooldowns.Remove(npc);
            if (ReferenceEquals(_playerAutoAttack.Target, npc))
                _playerAutoAttack.Target = null;
            JxqyCharacter killer = npc.LastAttacker;
            string poisonOwnerName =
                npc.PoisonDeathExperienceOwnerName;
            if (!string.IsNullOrWhiteSpace(poisonOwnerName))
            {
                if (string.Equals(
                        poisonOwnerName,
                        _player.Name,
                        StringComparison.Ordinal))
                {
                    int characterExperience =
                        JxqyExperienceRules.CalculateDeathExperience(
                            _player,
                            npc,
                            _contentContext.ContentProfile.DeathExperience);
                    AddKillMagicExperience(characterExperience);
                    AddSharedPartyExperience(characterExperience);
                }
                else
                {
                    JxqyNpc poisonOwner = _npcs.Npcs.FirstOrDefault(item =>
                        string.Equals(
                            item.Name,
                            poisonOwnerName,
                            StringComparison.Ordinal));
                    if (poisonOwner != null &&
                        poisonOwner.CanLevelUp > 0)
                    {
                        int characterExperience =
                            JxqyExperienceRules.CalculateDeathExperience(
                                poisonOwner,
                                npc,
                                _contentContext.ContentProfile
                                    .DeathExperience);
                        AddSharedPartyExperience(characterExperience);
                    }
                }
            }
            else if (JxqyExperienceRules.IsPlayerExperienceKiller(
                    killer,
                    _player))
            {
                JxqyCharacter experienceReference = killer ?? _player;
                int characterExperience =
                    JxqyExperienceRules.CalculateDeathExperience(
                        experienceReference,
                        npc,
                        _contentContext.ContentProfile.DeathExperience);
                AddKillMagicExperience(characterExperience);
                AddSharedPartyExperience(characterExperience);
            }
            if (!string.IsNullOrWhiteSpace(npc.DeathScriptAddress))
            {
                // Original Character.Death always appends this script to
                // ScriptManager's serial list and preserves the dead actor as
                // BelongObject, even while another script is active.
                _scriptSession?.QueueSerialScript(
                    npc.DeathScriptAddress,
                    this.GetCancellationTokenOnDestroy(),
                    npc);
            }
        }

        private void AddSharedPartyExperience(int amount)
        {
            if (amount <= 0)
                return;
            AddPlayerExperience(amount);
            IReadOnlyList<JxqyNpc> partners =
                JxqyExperienceRules.GetSharedPartnerExperienceBeneficiaries(
                    _npcs?.Npcs,
                    _player);
            for (int index = 0; index < partners.Count; index++)
                AddNpcExperience(partners[index], amount);
        }

        private bool IsNpcDeathPresentationComplete(JxqyNpc npc)
        {
            if (npc == null || !npc.IsDead)
                return false;
            if (!npc.IsActionEnabled(JxqyCharacterState.Death))
                return true;
            if (!_npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual state))
            {
                return true;
            }
            if (TryGetStatusDeathAnimation(
                    npc,
                    out JxqyAnimationMetadata statusDeathAnimation))
            {
                return state.CurrentState == JxqyCharacterState.Death &&
                       ReferenceEquals(state.Current, statusDeathAnimation) &&
                       state.Visual.Animation.IsFinished;
            }
            if (!state.Actions.ContainsKey(
                    (int)JxqyCharacterState.Death))
            {
                if (state.ActionFileNames.ContainsKey(
                        (int)JxqyCharacterState.Death))
                {
                    EnsureNpcActionLoading(
                        npc,
                        state,
                        JxqyCharacterState.Death);
                    return false;
                }
                return true;
            }
            return state.CurrentState == JxqyCharacterState.Death &&
                   state.Visual.Animation.IsFinished;
        }

        private void ProcessNpcRevival(JxqyNpc npc)
        {
            if (npc == null)
                return;
            _processedNpcDeaths.Remove(npc);
            _finalizedNpcDeaths.Remove(npc);
            npc.IsBodyCreated = false;
            if (_npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual state))
            {
                state.CurrentState = (JxqyCharacterState)(-1);
                state.CurrentStateVersion = -1;
            }
        }

        private async UniTask FinalizeNpcDeathAsync(JxqyNpc npc)
        {
            if (npc == null || !npc.IsDead ||
                !_finalizedNpcDeaths.Add(npc))
            {
                return;
            }

            if (!npc.IsBodyCreated)
            {
                npc.IsBodyCreated = true;
                if (!HasStatusDeathVisual(npc) &&
                    !string.IsNullOrWhiteSpace(npc.BodyFileName))
                {
                    try
                    {
                        await CreateNpcBodyAsync(
                            npc,
                            this.GetCancellationTokenOnDestroy());
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        // The shipped source corpus itself contains a small
                        // number of dangling BodyIni/ObjFile references. A
                        // missing presentation asset must not prevent the
                        // original death/drop/NPC-removal sequence.
                        Debug.LogWarning(
                            $"JXQY-BODY failed for '{npc.Name}' " +
                            $"({npc.BodyFileName}): {exception.Message}",
                            this);
                    }
                }
            }

            if (!_dropGoodWhenDefeatEnemyDisabled)
            {
                JxqyDrop drop = CreateDropForDefeatedNpc(npc);
                if (drop != null)
                    await SpawnDropAsync(drop);
            }

            if (npc.ReviveDelaySeconds > 0 || !npc.IsDead)
                return;
            RemoveNpcVisual(npc);
            _npcs.Remove(npc);
            _processedNpcDeaths.Remove(npc);
            _finalizedNpcDeaths.Remove(npc);
        }

        private async UniTask CreateNpcBodyAsync(
            JxqyNpc npc,
            CancellationToken cancellationToken)
        {
            string safeFileName = SafeLegacyFileName(
                npc.BodyFileName,
                ".ini");
            if (safeFileName.Length == 0)
                return;
            string text = await LoadDynamicTextAsync(
                "ini/obj",
                safeFileName,
                cancellationToken);
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(text);
            if (!sections.TryGetValue(
                    "INIT",
                    out Dictionary<string, string> init))
            {
                throw new InvalidDataException(
                    $"Body definition '{safeFileName}' has no INIT section.");
            }
            JxqyWorldObject body = CreateWorldObject(init);
            if (body.Kind != JxqyObjectKind.Body)
            {
                throw new InvalidDataException(
                    $"Body definition '{safeFileName}' has kind " +
                    $"{body.Kind}, expected {JxqyObjectKind.Body}.");
            }
            body.PositionInWorld = npc.PositionInWorld;
            body.CurrentDirection = npc.CurrentDirection;
            if (npc.ReviveDelaySeconds > 0)
            {
                body.IsRemoved = false;
                body.MillisecondsToRemove = Math.Max(
                    0,
                    npc.ReviveSecondsRemaining * 1000f);
            }
            _objects.Add(body);
            await CreateObjectVisualAsync(body, cancellationToken);
        }

        private JxqyDrop CreateDropForDefeatedNpc(JxqyNpc npc)
        {
            return JxqyDropGenerator.Generate(
                npc,
                _legacyRandom,
                _contentContext.ContentProfile.DropContent);
        }

        private async UniTask SpawnDropAsync(JxqyDrop drop)
        {
            try
            {
                string iniFileName = Path.GetFileName(
                    drop.ResourcePath.Replace('\\', '/'));
                string text = await LoadDynamicTextAsync(
                    "ini/obj",
                    iniFileName,
                    this.GetCancellationTokenOnDestroy());
                Dictionary<string, Dictionary<string, string>> sections =
                    JxqyLegacySaveImporter.ParseIni(text);
                if (!sections.TryGetValue(
                        "INIT",
                        out Dictionary<string, string> init))
                    return;
                JxqyWorldObject worldObject = CreateWorldObject(init);
                worldObject.Kind = JxqyObjectKind.Drop;
                worldObject.TilePosition = drop.TilePosition;
                if (!string.IsNullOrWhiteSpace(drop.ScriptFile))
                    worldObject.ScriptAddress = drop.ScriptFile;
                _objects.Add(worldObject);
                await CreateObjectVisualAsync(
                    worldObject,
                    this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"JXQY-DROP failed: {exception.Message}",
                    this);
            }
        }

        private static void ApplyActorPosition(
            JxqySprite actor,
            JxqyRuntimeActorVisual state)
        {
            if (actor is JxqyNpc npc)
            {
                state.Visual.Kind =
                    npc.Kind == JxqyCharacterKind.Flyer
                        ? JxqyWorldVisualKind.FlyingNpc
                        : JxqyWorldVisualKind.Npc;
            }
            else if (actor is JxqyWorldObject worldObject)
            {
                state.Visual.Kind =
                    worldObject.Kind == JxqyObjectKind.Body
                        ? JxqyWorldVisualKind.BodyObject
                        : JxqyWorldVisualKind.Object;
            }
            JxqyIntPoint tile = actor.TilePosition;
            state.Visual.TileColumn = tile.X;
            state.Visual.TileRow = tile.Y;
            state.Visual.WorldPosition = new Vector2(
                actor.PositionInWorld.X + state.OffsetX,
                actor.PositionInWorld.Y + state.OffsetY);
        }

        private void SetMapTrap(int index, string scriptFileName)
        {
            _trapRegistry.SetTrap(
                GetMapDisplayName(ActiveMapStableId),
                index,
                scriptFileName,
                activate: true);
        }

        private void SetNamedMapTrap(
            string mapName,
            int index,
            string scriptFileName)
        {
            string activeMapName = GetMapDisplayName(ActiveMapStableId);
            _trapRegistry.SetTrap(
                mapName,
                index,
                scriptFileName,
                activate: string.Equals(
                    mapName,
                    activeMapName,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void SetMapPosition(int tileX, int tileY)
        {
            JxqyIntPoint world =
                JxqyIsometricMapMath.TileToWorldPixel(tileX, tileY);
            _camera = JxqyIsometricMapMath.ClampCamera(
                world.X,
                world.Y,
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            _presentationEffects?.SetCameraPositionPreservingMove(
                new JxqyFloat2(_camera.X, _camera.Y));
        }

        private void FreeMapFromScript()
        {
            foreach (Tilemap tilemap in _mapTilemaps)
            {
                if (tilemap != null)
                    tilemap.gameObject.SetActive(false);
            }
            ClearWorldActors();
        }

        private void OpenTimeLimit(int seconds)
        {
            _timeLimitRemainingSeconds = Math.Max(0, seconds);
            _timerWindowVisible = true;
            _timeScriptFired = false;
            _lastTimerNoticeSecond = -1;
            _uiSession?.SetTimer(
                true,
                Mathf.CeilToInt(_timeLimitRemainingSeconds));
        }

        private void CloseTimeLimit()
        {
            _timeLimitRemainingSeconds = 0;
            _timerWindowVisible = false;
            _timeScriptFileName = string.Empty;
            _timeScriptFired = false;
            _lastTimerNoticeSecond = -1;
            _uiSession?.SetTimer(false, 0);
        }

        private void HideTimerWindow()
        {
            _timerWindowVisible = false;
            _uiSession?.SetTimer(
                false,
                Mathf.CeilToInt(_timeLimitRemainingSeconds));
        }

        private void SetTimeScript(int seconds, string scriptFileName)
        {
            if (_timeLimitRemainingSeconds <= 0)
                return;
            _timeScriptTriggerSeconds = Math.Max(0, seconds);
            _timeScriptFileName = scriptFileName ?? string.Empty;
            _timeScriptFired = false;
        }

        private void TickTimeLimit(float elapsedSeconds)
        {
            if (_timeLimitRemainingSeconds <= 0)
                return;

            _timeLimitRemainingSeconds = Math.Max(
                0,
                _timeLimitRemainingSeconds - elapsedSeconds);
            int visibleSeconds =
                Mathf.CeilToInt(_timeLimitRemainingSeconds);
            if (_timerWindowVisible &&
                visibleSeconds != _lastTimerNoticeSecond)
            {
                _lastTimerNoticeSecond = visibleSeconds;
                _uiSession.SetTimer(true, visibleSeconds);
            }

            if (_timeScriptFired ||
                string.IsNullOrWhiteSpace(_timeScriptFileName) ||
                _timeLimitRemainingSeconds > _timeScriptTriggerSeconds ||
                (_scriptSession?.IsRunning ?? false))
                return;

            _timeScriptFired = true;
            _scriptSession.StartAsync(
                _timeScriptFileName,
                cancellationToken:
                    this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void TickWorldObjects(float elapsedSeconds)
        {
            if (_objects == null)
                return;
            _expiredWorldObjects.Clear();
            foreach (JxqyWorldObject worldObject in _objects.Objects)
            {
                if (worldObject == null || worldObject.IsRemoved)
                    continue;
                if (worldObject.TickLifetime(
                        elapsedSeconds * 1000f))
                {
                    _expiredWorldObjects.Add(worldObject);
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(
                        worldObject.TimerScriptAddress))
                {
                    _objectTimerElapsedMilliseconds.TryGetValue(
                        worldObject,
                        out float timer);
                    timer += elapsedSeconds * 1000f;
                    int interval = Math.Max(
                        1,
                        worldObject.TimerScriptIntervalMilliseconds);
                    while (timer >= interval)
                    {
                        timer -= interval;
                        _scriptSession?.StartParallel(
                            worldObject.TimerScriptAddress,
                            belongObject: worldObject);
                    }
                    _objectTimerElapsedMilliseconds[worldObject] =
                        timer;
                }
                if (worldObject.Kind != JxqyObjectKind.Trap ||
                    worldObject.Damage <= 0 ||
                    !_objectVisuals.TryGetValue(
                        worldObject,
                        out JxqyRuntimeActorVisual visual) ||
                    visual.Visual.Animation.FrameWithinDirection != 0)
                {
                    continue;
                }
                if (_player != null &&
                    !_player.IsDead &&
                    _player.TilePosition.Equals(
                        worldObject.TilePosition))
                {
                    ApplyTrapDamage(_player, worldObject.Damage);
                }
                foreach (JxqyNpc npc in _npcs.Npcs)
                {
                    if (npc.Kind is not (
                            JxqyCharacterKind.Fighter or
                            JxqyCharacterKind.Follower) ||
                        npc.IsDead ||
                        !npc.TilePosition.Equals(
                            worldObject.TilePosition))
                    {
                        continue;
                    }
                    ApplyTrapDamage(npc, worldObject.Damage);
                }
            }
            foreach (JxqyWorldObject worldObject in _expiredWorldObjects)
                RemoveObjectVisual(worldObject);
            _expiredWorldObjects.Clear();
        }

        private void ApplyTrapDamage(
            JxqyCharacter target,
            int damage)
        {
            target.TakeDamage(
                Math.Max(0, damage),
                attacker: null,
                enterHurtState: false);
            if (JxqyDamageCalculator.ShouldEnterHurtState(
                    target,
                    _legacyRandom.Next(0, 4)))
            {
                target.SetState(JxqyCharacterState.Hurt);
            }
        }

        private string GetTalkText(int textId)
        {
            if (!_talkTexts.TryGetValue(textId, out string text))
            {
                throw new InvalidOperationException(
                    $"Talk text {textId} was not loaded.");
            }
            return text;
        }

        private static string GetIniValue(
            Dictionary<string, string> section,
            string key)
        {
            return section.TryGetValue(key, out string value)
                ? value
                : string.Empty;
        }

        private static string GetFirstIniValue(
            Dictionary<string, string> section,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = GetIniValue(section, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return string.Empty;
        }

        private static string GetLevelIniValue(
            Dictionary<string, string> level,
            Dictionary<string, string> init,
            string key)
        {
            string value = GetIniValue(level, key);
            return string.IsNullOrWhiteSpace(value)
                ? GetIniValue(init, key)
                : value;
        }

        private static int ParseIniInteger(
            Dictionary<string, string> section,
            string key,
            int fallback)
        {
            return int.TryParse(
                GetIniValue(section, key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : fallback;
        }

        private static int ParseFirstIniInteger(
            Dictionary<string, string> section,
            int fallback,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (int.TryParse(
                        GetIniValue(section, key),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int value))
                {
                    return value;
                }
            }
            return fallback;
        }

        private sealed class JxqyLevelEntry
        {
            public int LevelUpExperience;
            public int LifeMax;
            public int ThewMax;
            public int ManaMax;
            public int Attack;
            public int Attack2;
            public int Attack3;
            public int Defend;
            public int Defend2;
            public int Defend3;
            public int Evade;
            public string NewMagic = string.Empty;
            public string NewGood = string.Empty;
        }

        private void ReturnToTitle()
        {
            _newGameOpeningVideoPending = false;
            _legacyInputDisabled = false;
            _uiSession?.HideFade();
            _gameStarted = false;
            _scriptSession?.Cancel();
            ResetCombatTransientState();
            ClearMagicVisuals();
            _player?.Stop();
            _uiSession.ShowTitle();
            _input.ResetTransientState();
        }

        private static void ExitApplication()
        {
            JxqyApplicationLifecycle.RequestExit();
        }

        private void OnPlayerDied(
            JxqyCharacter player,
            JxqyCharacter attacker)
        {
            if (!ReferenceEquals(player, _player))
                return;

            _legacyInputDisabled = true;
            ClearPendingInteraction();
            _pendingPlayerMagicCast = null;
            _playerSpecialAction = null;
            ResetCombatTransientState();
            _uiSession?.Open(JxqyUiScreen.Hud);

            _scriptSession?.Cancel();
            _scriptFaultReported = false;
            if (string.IsNullOrWhiteSpace(player.DeathScriptAddress))
            {
                Debug.LogError(
                    "JXQY player death script is not configured.",
                    this);
                ReturnToTitle();
                return;
            }

            StartPlayerDeathScriptAsync(
                    player,
                    player.DeathScriptAddress,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private void OnPlayerRevived(JxqyCharacter player)
        {
            if (ReferenceEquals(player, _player))
                _legacyInputDisabled = false;
        }

        private async UniTaskVoid StartPlayerDeathScriptAsync(
            JxqyCharacter player,
            string scriptFileName,
            CancellationToken cancellationToken)
        {
            try
            {
                await _scriptSession.StartAsync(
                    scriptFileName,
                    cancellationToken,
                    belongObject: player);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ReturnToTitle();
            }
        }

        private void OnVideoPlaybackStarted()
        {
            if (!_newGameOpeningVideoPending)
                return;
            _newGameOpeningVideoPending = false;
            // The video overlay is topmost. Preserve the opaque fade beneath
            // it until the opening map script explicitly executes FadeIn().
        }

        private void OnSaveRequested(int slot)
        {
            SaveGameAsync(
                    slot,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private void OnLoadRequested(int slot)
        {
            LoadGameAsync(
                    slot,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private bool CanSaveGame()
        {
            return _gameStarted &&
                   !_saveDisabled &&
                   !_saveOperationInProgress &&
                   !_mapSwitchInProgress &&
                   !(_scriptSession?.IsRunning ?? false);
        }

        private async UniTask SaveGameAsync(
            int slot,
            CancellationToken cancellationToken)
        {
            if (_saveOperationInProgress || _saveRepository == null)
                return;
            if (!CanSaveGame())
            {
                _uiSession?.SetNotice("当前状态不能存档");
                return;
            }
            _saveOperationInProgress = true;
            try
            {
                _uiSession.SetNotice("正在保存...");
                JxqySaveGameData save = JxqyRuntimeSaveCodec.Capture(
                    ActiveMapStableId,
                    _player,
                    _inventory,
                    _equipment,
                    _skills);
                CaptureDynamicWorldState(save);
                CapturePersistentRuntimeState(save);
                await _saveRepository.SaveAsync(
                    slot,
                    save,
                    cancellationToken);
                try
                {
                    byte[] snapshot = CaptureWorldSnapshotPng();
                    await _saveRepository.SaveSnapshotAsync(
                        slot,
                        snapshot,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"存档已写入，但截图保存失败：{exception.Message}",
                        this);
                }
                await RefreshSaveSlotsAsync(cancellationToken);
                _uiSession.SetNotice("保存完成");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _uiSession.SetNotice("保存失败");
            }
            finally
            {
                _saveOperationInProgress = false;
            }
        }

        private async UniTask LoadGameAsync(
            int slot,
            CancellationToken cancellationToken)
        {
            if (_saveOperationInProgress || _saveRepository == null)
                return;
            _saveOperationInProgress = true;
            try
            {
                _uiSession.SetNotice("正在读取...");
                JxqySaveGameData save =
                    await _saveRepository.LoadWithBackupFallbackAsync(
                        slot,
                        cancellationToken);
                if (!string.Equals(
                        save.World.Map,
                        ActiveMapStableId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await SwitchMapFromScriptAsync(
                        GetLegacyMapFileName(save.World.Map));
                }
                ResetCombatTransientState();
                ClearMagicVisuals();
                _originalCharacterSkills.Clear();
                JxqyRestoredGameplayState state =
                    JxqyRuntimeSaveCodec.Restore(save, _player);
                await RestorePlayerActionResourceAsync(
                    save.Player.PlayerIndex,
                    cancellationToken);
                await PrepareCharacterBasicMagicsAsync(
                    _player,
                    cancellationToken);
                _inventory = state.Inventory;
                _equipment = state.Equipment;
                _skills = state.Skills;
                CacheCurrentPlayerSkills();
                await PrepareRestoredMagicVisualsAsync(
                    cancellationToken);
                _uiSession.Inventory = _inventory;
                _uiSession.Equipment = _equipment;
                _uiSession.Skills = _skills;
                await RestoreDynamicWorldStateAsync(
                    save,
                    cancellationToken);
                await RestorePersistentRuntimeStateAsync(
                    save,
                    cancellationToken);
                UpdatePlayerVisual();
                CenterCameraOnPlayer();
                _gameStarted = true;
                _uiSession.Open(JxqyUiScreen.Hud);
                _input.ResetTransientState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
                when (exception is InvalidDataException ||
                      exception is NotSupportedException)
            {
                Debug.LogWarning(
                    $"JXQY save slot {slot} could not be loaded and was " +
                    $"preserved: " +
                    exception.Message,
                    this);
                await RefreshSaveSlotsAsync(cancellationToken);
                _uiSession.SetNotice(
                    "存档无法读取，原文件已保留");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _uiSession.SetNotice("读取失败");
            }
            finally
            {
                _saveOperationInProgress = false;
            }
        }

        private async UniTask RestorePlayerActionResourceAsync(
            int playerIndex,
            CancellationToken cancellationToken)
        {
            int safeIndex = Math.Max(
                0,
                Math.Min(
                    JxqyUiSession.LastOriginalPlayerIndex,
                    playerIndex));
            string address = _contentContext.PlayerAddress(safeIndex);
            JxqyAssetLease<TextAsset> lease = await LoadTextAsync(
                address,
                cancellationToken);
            await SetCharacterResourceAsync(
                _player,
                GetPlayerResourceFileName(lease.Asset.text));
            _playerIndex = safeIndex;
            _uiSession?.SetPlayerIndex(safeIndex, notify: false);
        }

        private async UniTask PrepareRestoredMagicVisualsAsync(
            CancellationToken cancellationToken)
        {
            await PrepareRestoredMagicVisualsAsync(
                _skills,
                cancellationToken);
        }

        private async UniTask PrepareRestoredMagicVisualsAsync(
            JxqySkillManager skills,
            CancellationToken cancellationToken)
        {
            if (skills == null)
                return;
            foreach (JxqySkillEntry entry in skills.Skills)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JxqyMagicDefinition magic = entry.Magic;
                if (magic == null || string.IsNullOrWhiteSpace(magic.Id))
                    continue;
                JxqyMagicDefinition source =
                    await LoadMagicDefinitionAsync(
                        magic.Id,
                        entry.Level);
                skills.ReplaceDefinition(magic.Id, source);
            }
        }

        private void CaptureDynamicWorldState(JxqySaveGameData save)
        {
            save.World.NpcFile = _activeNpcFileName;
            save.World.ObjectFile = _activeObjectFileName;
            save.World.NpcAiDisabled = _npcs.IsAiDisabled;
            save.World.Npcs.Clear();
            foreach (JxqyNpc npc in
                     _npcs.Npcs.Where(item => !item.IsMagicSummon))
                save.World.Npcs.Add(CaptureNpcState(npc));

            save.World.Objects.Clear();
            foreach (JxqyWorldObject worldObject in _objects.Objects)
                save.World.Objects.Add(CaptureObjectState(worldObject));

            save.World.Traps.Clear();
            _trapRegistry.ForEach(
                (mapName, index, script, triggered) =>
                    save.World.Traps.Add(new JxqySaveTrapState
                    {
                        MapName = mapName,
                        Index = index,
                        Script = script,
                        Triggered = triggered,
                    }));

            save.World.NpcSnapshots.Clear();
            foreach (KeyValuePair<string, List<JxqyNpc>> snapshot in
                     _savedNpcSnapshots.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                var saved = new JxqySaveNpcSnapshot
                {
                    FileName = snapshot.Key,
                };
                foreach (JxqyNpc npc in snapshot.Value)
                    saved.Npcs.Add(CaptureNpcState(npc));
                save.World.NpcSnapshots.Add(saved);
            }
            save.World.ObjectSnapshots.Clear();
            foreach (KeyValuePair<string, List<JxqyWorldObject>> snapshot in
                     _savedObjectSnapshots.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                var saved = new JxqySaveObjectSnapshot
                {
                    FileName = snapshot.Key,
                };
                foreach (JxqyWorldObject worldObject in snapshot.Value)
                    saved.Objects.Add(CaptureObjectState(worldObject));
                save.World.ObjectSnapshots.Add(saved);
            }
        }

        private static JxqySaveNpcState CaptureNpcState(JxqyNpc npc)
        {
            var state = new JxqySaveNpcState
            {
                Name = npc.Name,
                NpcIniFile = npc.NpcIniFileName,
                Kind = (int)npc.Kind,
                Relation = (int)npc.Relation,
                TileColumn = npc.TilePosition.X,
                TileRow = npc.TilePosition.Y,
                Direction = npc.CurrentDirection,
                Life = npc.Life,
                LifeMax = npc.LifeMax,
                Thew = npc.Thew,
                ThewMax = npc.ThewMax,
                Mana = npc.Mana,
                ManaMax = npc.ManaMax,
                Attack = npc.Attack,
                Attack2 = npc.Attack2,
                Attack3 = npc.Attack3,
                Defend = npc.Defend,
                Defend2 = npc.Defend2,
                Defend3 = npc.Defend3,
                Evade = npc.Evade,
                CanEvade = npc.CanEvade,
                Level = npc.Level,
                AttackLevel = npc.AttackLevel,
                DialogRadius = npc.DialogRadius,
                Experience = npc.Experience,
                LevelUpExperience = npc.LevelUpExperience,
                ExpBonus = npc.ExpBonus,
                Action = npc.Action,
                PathFinderMode = npc.PathFinderMode,
                FixedPositionData = npc.FixedPositionData,
                CurrentFixedPositionIndex =
                    npc.CurrentFixedPositionIndex,
                Group = npc.Group,
                VisionRadius = npc.VisionRadius,
                AttackRadius = npc.AttackRadius,
                IdleFrames = npc.IdleFrames,
                LightRadius = npc.LightRadius,
                CharacterState = (int)npc.State,
                LifeMilliseconds = npc.LifeMilliseconds,
                Script = npc.ScriptAddress,
                ClickScript = npc.ClickScriptAddress,
                DeathScript = npc.DeathScriptAddress,
                MagicFile = npc.MagicFileName,
                MagicFile2 = npc.MagicFileName2,
                RetaliationMagicFile =
                    npc.RetaliationMagicFileName,
                MagicDirectionWhenBeAttacked =
                    npc.MagicDirectionWhenBeAttacked,
                MagicDataJson = JxqyRuntimeSaveCodec.CaptureSkills(
                    npc.Skills),
                DestinationMapPosX = npc.DestinationMapPosX,
                DestinationMapPosY = npc.DestinationMapPosY,
                KeepAttackX = npc.KeepAttackX,
                KeepAttackY = npc.KeepAttackY,
                CanEquip = npc.CanEquip,
                CanLevelUp = npc.CanLevelUp,
                BodyFile = npc.BodyFileName,
                IsBodyCreated = npc.IsBodyCreated,
                ReviveDelaySeconds = npc.ReviveDelaySeconds,
                ReviveSecondsRemaining =
                    npc.ReviveSecondsRemaining,
                EquipmentBackgroundFile =
                    npc.EquipmentBackgroundFileName,
                HeadEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Head),
                NeckEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Neck),
                BodyEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Body),
                BackEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Back),
                HandEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Hand),
                WristEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Wrist),
                FootEquip = GetNpcEquipmentFileName(
                    npc, JxqyEquipmentSlot.Foot),
                ResourceFile = npc.ResourceFileName,
                DropIni = npc.DropIni,
                NoDropWhenDead = npc.NoDropWhenDead,
                IsVisible = npc.IsVisible,
                NoAutoAttackPlayer = npc.NoAutoAttackPlayer,
                StopFindingTarget = npc.StopFindingTarget,
                ActionType = npc.ActionType,
                BlindMilliseconds = npc.BlindMilliseconds,
                Invincible = npc.Invincible,
                IsPetrified = npc.IsPetrified,
                FrozenSeconds = npc.GetStatusSeconds(
                    JxqyStatusKind.Frozen),
                PetrifiedSeconds = npc.GetStatusSeconds(
                    JxqyStatusKind.Petrified),
                PoisonSeconds = npc.GetStatusSeconds(
                    JxqyStatusKind.Poisoned),
                PoisonExperienceOwnerName =
                    npc.PoisonExperienceOwnerName,
                IsFrozenVisualEffect = npc.IsFrozenVisualEffect,
                IsPoisonVisualEffect = npc.IsPoisonVisualEffect,
                IsPetrifiedVisualEffect =
                    npc.IsPetrifiedVisualEffect,
                IsInTransport = npc.IsInTransport,
                IsMovementDisabled = npc.IsMovementDisabled,
                IsRunDisabled = npc.IsRunDisabled,
                IsJumpDisabled = npc.IsJumpDisabled,
                IsFightDisabled = npc.IsFightDisabled,
                AddMoveSpeedPercent = npc.AddMoveSpeedPercent,
                ChangeMoveSpeedPercent = npc.ChangeMoveSpeedPercent,
                RunSpeedFold = npc.RunSpeedFold,
                WalkSpeed = npc.WalkSpeed,
            };
            foreach (JxqyRangedMagicReference reference in
                     npc.AdditionalBasicMagics)
            {
                if (reference.Magic == null)
                    continue;
                state.AdditionalBasicMagics.Add(
                    new JxqySaveRangedMagicState
                    {
                        FileName = reference.Magic.Id,
                        Distance = reference.Distance,
                    });
            }
            return state;
        }

        private static string GetNpcEquipmentFileName(
            JxqyNpc npc,
            JxqyEquipmentSlot slot)
        {
            if (npc.Equipment.Equipped.TryGetValue(
                    slot,
                    out JxqyItemDefinition equipped))
            {
                return equipped.Id ?? string.Empty;
            }
            return npc.EquipmentFileNames.TryGetValue(slot, out string fileName)
                ? fileName ?? string.Empty
                : string.Empty;
        }

        private static JxqySaveObjectState CaptureObjectState(
            JxqyWorldObject worldObject)
        {
            return new JxqySaveObjectState
            {
                Name = worldObject.Name,
                ResourceFile = worldObject.ResourceFileName,
                WavFile = worldObject.WavFileName,
                Kind = (int)worldObject.Kind,
                TileColumn = worldObject.TilePosition.X,
                TileRow = worldObject.TilePosition.Y,
                Direction = worldObject.CurrentDirection,
                Frame = worldObject.Frame,
                OffsetX = worldObject.OffsetX,
                OffsetY = worldObject.OffsetY,
                Height = worldObject.Height,
                Damage = worldObject.Damage,
                LightRadius = worldObject.LightRadius,
                Script = worldObject.ScriptAddress,
                RightScript = worldObject.RightScriptAddress,
                TimerScript = worldObject.TimerScriptAddress,
                TimerScriptIntervalMilliseconds =
                    worldObject.TimerScriptIntervalMilliseconds,
                ReviveNpcFile = worldObject.ReviveNpcFileName,
                MillisecondsToRemove =
                    worldObject.MillisecondsToRemove,
                IsVisible = worldObject.IsVisible,
                IsOpen = worldObject.IsOpen,
                IsRemoved = worldObject.IsRemoved,
            };
        }

        private void CapturePersistentRuntimeState(
            JxqySaveGameData save)
        {
            CaptureCurrentPlayerProfile();
            save.Player.PlayerIndex = _playerIndex;
            save.Player.LevelFile = _levelFileName;
            save.Player.Profiles.Clear();
            foreach (KeyValuePair<int, JxqySavePlayerProfileState> entry in
                     _playerProfiles.OrderBy(item => item.Key))
            {
                save.Player.Profiles.Add(
                    ClonePlayerProfile(entry.Value));
            }
            save.World.BackgroundMusic = _backgroundMusicAddress;
            save.World.MapTime = _presentationEffects.MapTime;
            save.World.IsSnowing = _presentationEffects.IsSnowing;
            save.World.RainFile = _presentationEffects.IsRaining
                ? _presentationEffects.RainFileName
                : string.Empty;
            save.World.WaterEffectEnabled =
                _presentationEffects.WaterEffectEnabled;
            save.World.SaveDisabled = _saveDisabled;
            save.World.DropGoodWhenDefeatEnemyDisabled =
                _dropGoodWhenDefeatEnemyDisabled;

            save.Presentation.MapColorBgra = FormatColorBgra(
                _presentationEffects.MapBaseColor);
            save.Presentation.SpriteColorBgra = FormatColorBgra(
                _presentationEffects.SpriteBaseColor);
            save.Presentation.ScriptShowMapPosition =
                _showMapPosition;
            save.Presentation.TimerEnabled =
                _timeLimitRemainingSeconds > 0;
            save.Presentation.TimerTotalSeconds =
                Mathf.CeilToInt(_timeLimitRemainingSeconds);
            save.Presentation.TimerWindowVisible =
                _timerWindowVisible;
            save.Presentation.TimerScriptEnabled =
                save.Presentation.TimerEnabled &&
                !_timeScriptFired &&
                !string.IsNullOrWhiteSpace(_timeScriptFileName);
            save.Presentation.TimerScript =
                _timeScriptFileName ?? string.Empty;
            save.Presentation.TimerTriggerSeconds =
                Mathf.CeilToInt(_timeScriptTriggerSeconds);

            save.Variables.Clear();
            foreach (KeyValuePair<string, int> variable in
                     _scriptSession.Variables.Values.OrderBy(
                         entry => entry.Key,
                         StringComparer.Ordinal))
            {
                save.Variables.Add(new JxqySaveVariable
                {
                    Name = variable.Key,
                    Value = variable.Value.ToString(
                        CultureInfo.InvariantCulture),
                });
            }
            save.ParallelScripts.Clear();
            save.ParallelScripts.AddRange(
                _scriptSession.CaptureParallelScripts());
            save.Memos.Clear();
            save.Memos.AddRange(_memoEntries);
        }

        private async UniTask RestorePersistentRuntimeStateAsync(
            JxqySaveGameData save,
            CancellationToken cancellationToken)
        {
            _playerIndex = save.Player.PlayerIndex;
            _uiSession?.SetPlayerIndex(_playerIndex, notify: false);
            _playerProfiles.Clear();
            foreach (JxqySavePlayerProfileState profile in
                     save.Player.Profiles ??
                     new List<JxqySavePlayerProfileState>())
            {
                if (profile == null ||
                    profile.PlayerIndex < 0 ||
                    profile.PlayerIndex >
                    JxqyUiSession.LastOriginalPlayerIndex)
                {
                    continue;
                }
                _playerProfiles[profile.PlayerIndex] =
                    ClonePlayerProfile(profile);
                if (!_originalCharacterSkills.ContainsKey(
                        profile.PlayerIndex) &&
                    !string.IsNullOrWhiteSpace(profile.MagicDataJson))
                {
                    _originalCharacterSkills[profile.PlayerIndex] =
                        JxqyRuntimeSaveCodec.RestoreSkills(
                            profile.MagicDataJson);
                }
            }
            if (!string.IsNullOrWhiteSpace(save.Player.LevelFile))
            {
                await LoadLevelFileAsync(
                    save.Player.LevelFile,
                    applyCurrentLevel: false);
            }
            else
            {
                _levelFileName = string.Empty;
                _levelEntries.Clear();
                _levelRewardMagics.Clear();
                _levelRewardItems.Clear();
            }
            if (_playerProfiles.TryGetValue(
                    _playerIndex,
                    out JxqySavePlayerProfileState activeProfile))
            {
                _uiSession.ClearSelectedSkill();
                RestoreSelectedPlayerMagic(activeProfile);
            }
            // The top-level player payload is authoritative for the active
            // profile and also upgrades schema-v8 saves that had no profile
            // collection.
            CaptureCurrentPlayerProfile();

            _scriptSession.Variables.Clear();
            foreach (JxqySaveVariable variable in save.Variables)
            {
                if (variable == null ||
                    string.IsNullOrWhiteSpace(variable.Name) ||
                    !int.TryParse(
                        variable.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int value))
                {
                    throw new InvalidDataException(
                        "存档包含无效的剧情变量。");
                }
                _scriptSession.Variables.Set(variable.Name, value);
            }
            _scriptSession.RestoreParallelScripts(
                save.ParallelScripts);
            _memoEntries.Clear();
            _memoEntries.AddRange(
                save.Memos.Where(value => value != null));
            _showMapPosition =
                save.Presentation.ScriptShowMapPosition;

            JxqyColor32 mapColor = ParseColorBgra(
                save.Presentation.MapColorBgra);
            JxqyColor32 spriteColor = ParseColorBgra(
                save.Presentation.SpriteColorBgra);
            _presentationEffects.SetMapColor(
                mapColor.Red,
                mapColor.Green,
                mapColor.Blue);
            _presentationEffects.SetSpriteColor(
                spriteColor.Red,
                spriteColor.Green,
                spriteColor.Blue);
            if (string.IsNullOrWhiteSpace(save.World.RainFile))
                _presentationEffects.EndRain();
            else
                _presentationEffects.BeginRain(save.World.RainFile);
            _presentationEffects.ShowSnow(save.World.IsSnowing);
            _presentationEffects.MapTime = save.World.MapTime;
            _presentationEffects.WaterEffectEnabled =
                save.World.WaterEffectEnabled;

            _timeLimitRemainingSeconds =
                save.Presentation.TimerEnabled
                    ? Math.Max(
                        0,
                        save.Presentation.TimerTotalSeconds)
                    : 0;
            _timerWindowVisible =
                save.Presentation.TimerEnabled &&
                save.Presentation.TimerWindowVisible;
            _timeScriptFileName =
                save.Presentation.TimerScript ?? string.Empty;
            _timeScriptTriggerSeconds = Math.Max(
                0,
                save.Presentation.TimerTriggerSeconds);
            _timeScriptFired =
                !save.Presentation.TimerEnabled ||
                !save.Presentation.TimerScriptEnabled ||
                string.IsNullOrWhiteSpace(_timeScriptFileName);
            _lastTimerNoticeSecond = -1;
            _uiSession?.SetTimer(
                _timerWindowVisible,
                Mathf.CeilToInt(_timeLimitRemainingSeconds));

            _audio?.StopMusic();
            _backgroundMusicAddress =
                save.World.BackgroundMusic ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_backgroundMusicAddress) &&
                _audio != null)
            {
                await _audio.PlayMusicAsync(
                    _backgroundMusicAddress,
                    loop: true,
                    cancellationToken);
            }

            _saveDisabled = save.World.SaveDisabled;
            _dropGoodWhenDefeatEnemyDisabled =
                save.World.DropGoodWhenDefeatEnemyDisabled;
            ApplyPresentationColors();
            _uiSession.Refresh();
        }

        private static string FormatColorBgra(JxqyColor32 color)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:X2}{1:X2}{2:X2}00",
                color.Blue,
                color.Green,
                color.Red);
        }

        private static JxqyColor32 ParseColorBgra(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return JxqyColor32.White;
            string text = value.Trim();
            if (text.Length < 6 ||
                !byte.TryParse(
                    text.Substring(0, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out byte blue) ||
                !byte.TryParse(
                    text.Substring(2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out byte green) ||
                !byte.TryParse(
                    text.Substring(4, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out byte red))
            {
                throw new InvalidDataException(
                    $"存档包含无效颜色值 '{value}'。");
            }
            return new JxqyColor32(red, green, blue);
        }

        private async UniTask RestoreDynamicWorldStateAsync(
            JxqySaveGameData save,
            CancellationToken cancellationToken)
        {
            if (save.SchemaVersion < 2)
            {
                if (!string.IsNullOrWhiteSpace(save.World.NpcFile))
                    await LoadNpcsFromScriptAsync(save.World.NpcFile);
                if (!string.IsNullOrWhiteSpace(save.World.ObjectFile))
                    await LoadObjectsFromScriptAsync(
                        save.World.ObjectFile);
                return;
            }

            ClearWorldActors();
            _objects = new JxqyObjectManager();
            _npcs = new JxqyNpcManager(
                _player,
                _objects,
                new JxqyRuntimeCollisionMap(_map),
                _legacyRandom);
            _npcAiDisabled = save.World.NpcAiDisabled;
            _npcs.IsAiDisabled = _npcAiDisabled;
            _activeNpcFileName = save.World.NpcFile ?? string.Empty;
            _activeObjectFileName =
                save.World.ObjectFile ?? string.Empty;

            var restoredNpcs = new List<JxqyNpc>(save.World.Npcs.Count);
            foreach (JxqySaveNpcState entry in save.World.Npcs)
            {
                JxqyNpc npc = RestoreNpc(entry);
                RepairOriginalPartnerProfileAfterRestore(npc);
                await PrepareNpcEquipmentAsync(npc);
                _npcs.Add(npc);
                if (npc.IsDead)
                {
                    _processedNpcDeaths.Add(npc);
                    if (npc.IsBodyCreated)
                        _finalizedNpcDeaths.Add(npc);
                }
                restoredNpcs.Add(npc);
            }
            await CreateNpcVisualsAsync(
                restoredNpcs,
                cancellationToken);
            var restoredObjects =
                new List<JxqyWorldObject>(save.World.Objects.Count);
            foreach (JxqySaveObjectState entry in save.World.Objects)
            {
                JxqyWorldObject worldObject = RestoreObject(entry);
                _objects.Add(worldObject);
                if (!worldObject.IsRemoved)
                    restoredObjects.Add(worldObject);
            }
            await PreloadObjectResourcesAsync(
                restoredObjects,
                cancellationToken);
            for (int index = 0;
                 index < restoredObjects.Count;
                 index++)
            {
                await CreateObjectVisualAsync(
                    restoredObjects[index],
                    cancellationToken);
            }

            _trapRegistry = new JxqyTrapRegistry();
            foreach (JxqySaveTrapState trap in save.World.Traps)
            {
                _trapRegistry.SetTrap(
                    trap.MapName,
                    trap.Index,
                    trap.Script,
                    activate: !trap.Triggered);
                _trapRegistry.SetTriggered(
                    trap.Index,
                    trap.Triggered);
            }
            _savedTrapRegistry = _trapRegistry.Clone();

            _savedNpcSnapshots.Clear();
            foreach (JxqySaveNpcSnapshot snapshot in
                     save.World.NpcSnapshots)
            {
                if (snapshot == null ||
                    string.IsNullOrWhiteSpace(snapshot.FileName))
                    continue;
                _savedNpcSnapshots[snapshot.FileName] =
                    snapshot.Npcs
                        .Where(entry => entry != null)
                        .Select(RestoreNpc)
                        .Where(npc =>
                            npc.Kind != JxqyCharacterKind.Follower)
                        .ToList();
            }
            _savedObjectSnapshots.Clear();
            foreach (JxqySaveObjectSnapshot snapshot in
                     save.World.ObjectSnapshots)
            {
                if (snapshot == null ||
                    string.IsNullOrWhiteSpace(snapshot.FileName))
                    continue;
                _savedObjectSnapshots[snapshot.FileName] =
                    snapshot.Objects
                        .Where(entry => entry != null)
                        .Select(RestoreObject)
                        .ToList();
            }
        }

        private static JxqyNpc RestoreNpc(JxqySaveNpcState entry)
        {
            var npc = new JxqyNpc
            {
                Name = entry.Name ?? string.Empty,
                NpcIniFileName = entry.NpcIniFile ?? string.Empty,
                Kind = (JxqyCharacterKind)entry.Kind,
                Relation = (JxqyRelationType)entry.Relation,
                TilePosition = new JxqyIntPoint(
                    entry.TileColumn,
                    entry.TileRow),
                CurrentDirection = entry.Direction,
                LifeMax = entry.LifeMax,
                ThewMax = entry.ThewMax,
                ManaMax = entry.ManaMax,
                Attack = entry.Attack,
                Attack2 = entry.Attack2,
                Attack3 = entry.Attack3,
                Defend = entry.Defend,
                Defend2 = entry.Defend2,
                Defend3 = entry.Defend3,
                Evade = entry.Evade,
                CanEvade = entry.CanEvade,
                Level = entry.Level,
                AttackLevel = Math.Max(1, entry.AttackLevel),
                DialogRadius = Math.Max(0, entry.DialogRadius),
                Experience = entry.Experience,
                LevelUpExperience = entry.LevelUpExperience,
                ExpBonus = entry.ExpBonus,
                Action = entry.Action,
                PathFinderMode = entry.PathFinderMode,
                FixedPositionData =
                    entry.FixedPositionData ?? string.Empty,
                CurrentFixedPositionIndex =
                    entry.CurrentFixedPositionIndex,
                Group = entry.Group,
                VisionRadius = entry.VisionRadius,
                AttackRadius = entry.AttackRadius,
                IdleFrames = entry.IdleFrames,
                LightRadius = entry.LightRadius,
                LifeMilliseconds = entry.LifeMilliseconds,
                ScriptAddress = entry.Script ?? string.Empty,
                ClickScriptAddress = entry.ClickScript ?? string.Empty,
                DeathScriptAddress =
                    entry.DeathScript ?? string.Empty,
                MagicFileName = entry.MagicFile ?? string.Empty,
                MagicFileName2 = entry.MagicFile2 ?? string.Empty,
                RetaliationMagicFileName =
                    entry.RetaliationMagicFile ?? string.Empty,
                MagicDirectionWhenBeAttacked =
                    entry.MagicDirectionWhenBeAttacked,
                Skills = JxqyRuntimeSaveCodec.RestoreSkills(
                    entry.MagicDataJson),
                DestinationMapPosX = entry.DestinationMapPosX,
                DestinationMapPosY = entry.DestinationMapPosY,
                KeepAttackX = entry.KeepAttackX,
                KeepAttackY = entry.KeepAttackY,
                CanEquip = entry.CanEquip,
                CanLevelUp = entry.CanLevelUp,
                BodyFileName = entry.BodyFile ?? string.Empty,
                IsBodyCreated = entry.IsBodyCreated,
                ReviveDelaySeconds = Math.Max(
                    0,
                    entry.ReviveDelaySeconds),
                EquipmentBackgroundFileName =
                    entry.EquipmentBackgroundFile ?? string.Empty,
                ResourceFileName =
                    entry.ResourceFile ?? string.Empty,
                DropIni = entry.DropIni ?? string.Empty,
                NoDropWhenDead = entry.NoDropWhenDead,
                IsVisible = entry.IsVisible,
                NoAutoAttackPlayer = entry.NoAutoAttackPlayer,
                StopFindingTarget = entry.StopFindingTarget,
                ActionType = entry.ActionType,
                BlindMilliseconds = entry.BlindMilliseconds,
                Invincible = entry.Invincible,
                IsPetrified = entry.IsPetrified,
                IsInTransport = entry.IsInTransport,
                IsMovementDisabled = entry.IsMovementDisabled,
                IsRunDisabled = entry.IsRunDisabled,
                IsJumpDisabled = entry.IsJumpDisabled,
                IsFightDisabled = entry.IsFightDisabled,
                AddMoveSpeedPercent = entry.AddMoveSpeedPercent,
                ChangeMoveSpeedPercent =
                    entry.ChangeMoveSpeedPercent,
            };
            npc.SetState((JxqyCharacterState)entry.CharacterState);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Head, entry.HeadEquip);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Neck, entry.NeckEquip);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Body, entry.BodyEquip);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Back, entry.BackEquip);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Hand, entry.HandEquip);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Wrist, entry.WristEquip);
            AddNpcEquipmentFileName(
                npc, JxqyEquipmentSlot.Foot, entry.FootEquip);
            if (entry.RunSpeedFold > 0)
                npc.RunSpeedFold = entry.RunSpeedFold;
            if (entry.WalkSpeed > 0)
                npc.WalkSpeed = entry.WalkSpeed;
            npc.ApplyStatus(
                JxqyStatusKind.Frozen,
                entry.FrozenSeconds);
            npc.ApplyStatus(
                JxqyStatusKind.Petrified,
                entry.PetrifiedSeconds);
            npc.ApplyStatus(
                JxqyStatusKind.Poisoned,
                entry.PoisonSeconds);
            npc.SetPoisonExperienceOwner(
                entry.PoisonExperienceOwnerName);
            npc.RestoreStatusVisualEffects(
                entry.IsFrozenVisualEffect,
                entry.IsPoisonVisualEffect,
                entry.IsPetrifiedVisualEffect);
            npc.Life = entry.Life;
            npc.Thew = entry.Thew;
            npc.Mana = entry.Mana;
            foreach (JxqySaveRangedMagicState reference in
                     entry.AdditionalBasicMagics ??
                     new List<JxqySaveRangedMagicState>())
            {
                if (string.IsNullOrWhiteSpace(reference.FileName))
                    continue;
                npc.AdditionalBasicMagics.Add(
                    new JxqyRangedMagicReference
                    {
                        Magic = new JxqyMagicDefinition
                        {
                            Id = reference.FileName,
                        },
                        Distance = reference.Distance,
                    });
            }
            if (entry.Life <= 0)
            {
                npc.Die();
                npc.RestoreReviveSecondsRemaining(
                    entry.ReviveSecondsRemaining);
            }
            SynchronizeOriginalFollowerInitialAttackRadius(npc);
            return npc;
        }

        private static JxqyWorldObject RestoreObject(
            JxqySaveObjectState entry)
        {
            return new JxqyWorldObject
            {
                Name = entry.Name ?? string.Empty,
                ResourceFileName = entry.ResourceFile ?? string.Empty,
                WavFileName = entry.WavFile ?? string.Empty,
                Kind = (JxqyObjectKind)entry.Kind,
                TilePosition = new JxqyIntPoint(
                    entry.TileColumn,
                    entry.TileRow),
                CurrentDirection = entry.Direction,
                Frame = entry.Frame,
                OffsetX = entry.OffsetX,
                OffsetY = entry.OffsetY,
                Height = entry.Height,
                Damage = entry.Damage,
                LightRadius = entry.LightRadius,
                ScriptAddress = entry.Script ?? string.Empty,
                RightScriptAddress =
                    entry.RightScript ?? string.Empty,
                TimerScriptAddress =
                    entry.TimerScript ?? string.Empty,
                TimerScriptIntervalMilliseconds =
                    entry.TimerScriptIntervalMilliseconds > 0
                        ? entry.TimerScriptIntervalMilliseconds
                        : 1000,
                ReviveNpcFileName =
                    entry.ReviveNpcFile ?? string.Empty,
                MillisecondsToRemove = Math.Max(
                    0,
                    entry.MillisecondsToRemove),
                IsVisible = entry.IsVisible,
                IsOpen = entry.IsOpen,
                IsRemoved = entry.IsRemoved,
            };
        }

        private static string GetLegacyMapFileName(
            string mapStableId)
        {
            if (string.IsNullOrWhiteSpace(mapStableId))
                throw new InvalidOperationException(
                    "Save map ID is empty.");
            string normalized = mapStableId.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            string fileName = slash >= 0
                ? normalized.Substring(slash + 1)
                : normalized;
            if (!fileName.EndsWith(
                    ".map",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Save map ID is invalid: {mapStableId}");
            }
            return fileName;
        }

        private async UniTask RefreshSaveSlotsAsync(
            CancellationToken cancellationToken)
        {
            if (_saveRepository == null || _uiSession == null)
                return;
            foreach (JxqySaveSlotView view in _uiSession.SaveSlots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                view.Exists = _saveRepository.Exists(view.Slot);
                view.Description = "空存档";
                view.SavedAt = string.Empty;
                view.SnapshotPng = null;
                if (!view.Exists)
                    continue;
                try
                {
                    JxqySaveGameData save =
                        await _saveRepository.LoadWithBackupFallbackAsync(
                            view.Slot,
                            cancellationToken);
                    view.Description =
                        $"{GetMapDisplayName(save.World.Map)}  " +
                        $"位置 {save.Player.TileColumn}," +
                        $"{save.Player.TileRow}";
                    if (DateTime.TryParse(
                            save.SavedUtc,
                            out DateTime savedUtc))
                    {
                        view.SavedAt = savedUtc
                            .ToLocalTime()
                            .ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    if (_saveRepository.SnapshotExists(view.Slot))
                    {
                        view.SnapshotPng =
                            await _saveRepository.LoadSnapshotAsync(
                                view.Slot,
                                cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    view.Description = "存档不可读取（原文件已保留）";
                    Debug.LogException(exception, this);
                }
            }
            _uiSession.Refresh();
        }

        private byte[] CaptureWorldSnapshotPng()
        {
            if (_worldCamera == null)
                throw new InvalidOperationException(
                    "场景摄像机不可用，无法生成存档截图。");
            const int width = 267;
            const int height = 200;
            RenderTexture previousTarget = _worldCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture renderTexture =
                RenderTexture.GetTemporary(
                    width,
                    height,
                    24,
                    RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                _worldCamera.targetTexture = renderTexture;
                _worldCamera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGB24,
                    false);
                texture.ReadPixels(
                    new Rect(0, 0, width, height),
                    0,
                    0,
                    false);
                texture.Apply(false, false);
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                _worldCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (texture != null)
                    Destroy(texture);
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private async UniTask ValidateWorldRenderingAsync(
            CancellationToken cancellationToken)
        {
            await UniTask.Yield(
                PlayerLoopTiming.LastPostLateUpdate,
                cancellationToken);
            const int width = 160;
            const int height = 120;
            RenderTexture previousTarget = _worldCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture renderTexture =
                RenderTexture.GetTemporary(
                    width,
                    height,
                    24,
                    RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                _worldCamera.targetTexture = renderTexture;
                _worldCamera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGB24,
                    false);
                texture.ReadPixels(
                    new Rect(0, 0, width, height),
                    0,
                    0,
                    false);
                texture.Apply(false, false);
                string diagnosticsDirectory = Path.GetFullPath(
                    Path.Combine(
                        Application.dataPath,
                        "..",
                        "Temp",
                        "JxqyValidation"));
                Directory.CreateDirectory(diagnosticsDirectory);
                File.WriteAllBytes(
                    Path.Combine(
                        diagnosticsDirectory,
                        "runtime-world-camera.png"),
                    ImageConversion.EncodeToPNG(texture));
                Color32[] pixels = texture.GetPixels32();
                int visiblePixels = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 pixel = pixels[index];
                    if (pixel.r > 8 || pixel.g > 8 || pixel.b > 8)
                        visiblePixels++;
                }
                float visibleRatio =
                    visiblePixels / (float)pixels.Length;
                if (visibleRatio < 0.05f)
                {
                    Debug.LogError(
                        $"JXQY-RUNTIME-CHECK map-render FAILED: " +
                        $"non-black={visibleRatio:P1}",
                        this);
                }
                else
                {
                }
            }
            finally
            {
                _worldCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (texture != null)
                    Destroy(texture);
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private async UniTask SwitchMapFromScriptAsync(
            string legacyFileName)
        {
            if (_mapSwitchInProgress)
            {
                throw new InvalidOperationException(
                    "A Jxqy map switch is already in progress.");
            }
            if (_mapCoordinator == null || _preloadManifest == null)
            {
                throw new InvalidOperationException(
                    "The playable map runtime is not initialized.");
            }
            string fileName = Path.GetFileName(
                (legacyFileName ?? string.Empty).Trim()
                .Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException(
                    "Legacy map file name is empty.",
                    nameof(legacyFileName));
            }
            JxqyPreloadGroup group =
                _preloadManifest.Groups.SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.Kind,
                        "Map",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetFileName(
                            candidate.OwnerRelativePath.Replace(
                                '\\',
                                '/')),
                        fileName,
                        StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                throw new KeyNotFoundException(
                    $"Legacy map '{legacyFileName}' has no generated " +
                    "preload group.");
            }

#if UNITY_EDITOR
            double switchStartedAt = Time.realtimeSinceStartupAsDouble;
#endif
            _mapSwitchInProgress = true;
            _ready = false;
            try
            {
                // JxqyMap.LoadMapFromBuffer clears the ignored trap indexes
                // before opening every map. Trap numbers are map-local and
                // commonly repeat (for example map001/Trap02 followed by
                // map003/Trap02), so carrying them across maps blocks exits.
                _trapRegistry.ClearTriggered();
                // ScriptExecuter.LoadMap stops rain before opening the map.
                // Keeping that ordering also stops the active ambient loop.
                _presentationEffects.EndRain();
#if UNITY_EDITOR
                double preloadStartedAt =
                    Time.realtimeSinceStartupAsDouble;
#endif
                await _mapCoordinator.SwitchMapAsync(
                    group.OwnerStableId,
                    cancellationToken:
                        this.GetCancellationTokenOnDestroy());
#if UNITY_EDITOR
                double preloadMilliseconds =
                    (Time.realtimeSinceStartupAsDouble - preloadStartedAt) *
                    1000d;
                double hydrationStartedAt =
                    Time.realtimeSinceStartupAsDouble;
#endif
                string sceneKey =
                    string.IsNullOrWhiteSpace(group.SceneKey)
                        ? group.OwnerStableId
                        : group.SceneKey;
                JxqyResourceAddressCatalog.Configure(
                    _preloadManifest,
                    sceneKey,
                    _contentContext.PackageName);
                await LoadMapGroupAsync(
                    group,
                    null,
                    this.GetCancellationTokenOnDestroy());
#if UNITY_EDITOR
                double hydrationMilliseconds =
                    (Time.realtimeSinceStartupAsDouble -
                     hydrationStartedAt) * 1000d;
                Debug.Log(
                    $"JXQY-PERF map switch phases {fileName}: " +
                    $"preload+scene={preloadMilliseconds:F1} ms; " +
                    $"runtime-hydration={hydrationMilliseconds:F1} ms",
                    this);
#endif
                ActiveMapStableId = group.OwnerStableId;
                if (_player == null)
                {
                    await EnsureWorldAssetsLoadedAsync(
                        this.GetCancellationTokenOnDestroy());
                    CreatePlayer();
                    CreateRenderer();
                    _uiSession.Player = _player;
                    _uiSession.Npcs = _npcs.Npcs;
                }
                else
                {
                    JxqyNpc[] followers = _npcs.Npcs
                        .Where(npc =>
                            npc.Kind == JxqyCharacterKind.Follower)
                        .ToArray();
                    ResetCombatTransientState();
                    ClearMagicVisuals();
                    ClearNpcActors(keepFollowers: true);
                    ClearObjectActors();
                    _objects = new JxqyObjectManager();
                    _npcs = new JxqyNpcManager(
                        _player,
                        _objects,
                        new JxqyRuntimeCollisionMap(_map),
                        _legacyRandom);
                    _npcs.IsAiDisabled = _npcAiDisabled;
                    foreach (JxqyNpc follower in followers)
                        _npcs.Add(follower);
                    RefreshActiveMapBindings(true);
                }
                _lastTrapObservedTile = new JxqyIntPoint(-1, -1);
                foreach (Tilemap tilemap in _mapTilemaps)
                {
                    if (tilemap != null)
                        tilemap.gameObject.SetActive(true);
                }
                CenterCameraOnPlayer();
                UpdatePlayerVisual();
                SubmitFrame();
            }
            finally
            {
#if UNITY_EDITOR
                _acceptanceLastMapSwitchMilliseconds = Math.Max(
                    0d,
                    (Time.realtimeSinceStartupAsDouble - switchStartedAt) *
                    1000d);
                Debug.Log(
                    $"JXQY-PERF map switch {fileName}: " +
                    $"{_acceptanceLastMapSwitchMilliseconds:F1} ms",
                    this);
#endif
                _ready = true;
                _mapSwitchInProgress = false;
            }
        }

        private static string GetMapDisplayName(string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                return "未知地图";
            string normalized = stableId.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            string name = slash >= 0
                ? normalized.Substring(slash + 1)
                : normalized;
            if (name.EndsWith(
                    ".map",
                    StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        private string GetLittleMapDisplayName(string stableId)
        {
            string mapKey = GetMapDisplayName(stableId);
            return _mapDisplayNames.TryGetValue(
                    mapKey,
                    out string displayName)
                ? displayName
                : "无名地图";
        }

        private void CenterCameraOnPlayer()
        {
            _presentationEffects?.ReleaseCamera();
            CenterCameraOnPlayerCore();
            JxqyCharacter player = ResolvePlayerKindCharacter();
            _lastCameraPlayerPosition = player.PositionInWorld;
            _lastCameraPlayerCharacter = player;
            _cameraPlayerTracked = true;
            _presentationEffects?.SetCameraAnchor(
                new JxqyFloat2(_camera.X, _camera.Y));
        }

        private void CenterCameraOnCharacter(JxqyCharacter character)
        {
            if (character == null || _mapMetadata == null)
                return;
            _presentationEffects?.ReleaseCamera();
            JxqyFloat2 position = character.PositionInWorld;
            _camera = JxqyIsometricMapMath.ClampCamera(
                Mathf.RoundToInt(position.X) - LogicalWidth / 2,
                Mathf.RoundToInt(position.Y) - LogicalHeight / 2,
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            _presentationEffects?.SetCameraAnchor(
                new JxqyFloat2(_camera.X, _camera.Y));
        }

        private void HandleScriptedPlayerPositionSet()
        {
            CenterCameraOnPlayer();
            ResetPartnerPositions();
            QueuePlayerTrapAtCurrentTile();
        }

        private void ResetPartnerPositions()
        {
            if (_npcs == null || _player == null)
                return;
            JxqyCharacter leader = ResolvePlayerKindCharacter();
            if (leader == null)
                return;
            JxqyNpc[] partners = _npcs.Npcs
                .Where(npc =>
                    npc.Kind == JxqyCharacterKind.Follower)
                .ToArray();
            if (partners.Length == 0)
                return;
            IReadOnlyList<JxqyIntPoint> neighbors =
                JxqyPathfinder.GetAllNeighbors(leader.TilePosition);
            int index = leader.CurrentDirection + 4;
            foreach (JxqyNpc partner in partners)
            {
                if (index == leader.CurrentDirection)
                    index++;
                partner.Stop();
                partner.TilePosition = neighbors[index % 8];
                RefreshActorVisual(partner);
                index++;
            }
        }

        private void QueuePlayerTrapAtCurrentTile()
        {
            if (!_gameStarted || _mapSwitchInProgress ||
                _map == null || _scriptSession == null)
            {
                return;
            }
            JxqyIntPoint tile = _player.TilePosition;
            _lastTrapObservedTile = tile;
            int trapIndex = _map.GetTrapIndex(tile.X, tile.Y);
            if (!_trapRegistry.TryTrigger(
                    GetMapDisplayName(ActiveMapStableId),
                    trapIndex,
                    out string scriptFileName))
            {
                return;
            }
            _player.Stop();
            _scriptSession.QueueSerialScript(
                scriptFileName,
                this.GetCancellationTokenOnDestroy());
        }

        private void CenterCameraOnPlayerCore()
        {
            JxqyFloat2 position =
                ResolvePlayerKindCharacter().PositionInWorld;
            _camera = JxqyIsometricMapMath.ClampCamera(
                Mathf.RoundToInt(position.X) - LogicalWidth / 2,
                Mathf.RoundToInt(position.Y) - LogicalHeight / 2,
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
        }

        private void UpdateCameraFromOriginalPlayerFollow()
        {
            JxqyCharacter player = ResolvePlayerKindCharacter();
            JxqyFloat2 position = player.PositionInWorld;
            if (!_cameraPlayerTracked)
            {
                _lastCameraPlayerPosition = position;
                _lastCameraPlayerCharacter = player;
                _cameraPlayerTracked = true;
                _presentationEffects.SetCameraPositionPreservingMove(
                    new JxqyFloat2(_camera.X, _camera.Y));
                return;
            }
            if (player.Kind != JxqyCharacterKind.Player)
            {
                _lastCameraPlayerPosition = position;
                _lastCameraPlayerCharacter = player;
                return;
            }
            if (!ReferenceEquals(_lastCameraPlayerCharacter, player))
                _lastCameraPlayerPosition = position;
            JxqyFloat2 followed =
                JxqyPresentationEffects.ApplyLegacyPlayerFollow(
                    new JxqyFloat2(_camera.X, _camera.Y),
                    _lastCameraPlayerPosition,
                    position,
                    LogicalWidth,
                    LogicalHeight);
            _camera = JxqyIsometricMapMath.ClampCamera(
                Mathf.RoundToInt(followed.X),
                Mathf.RoundToInt(followed.Y),
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            _presentationEffects.SetCameraPositionPreservingMove(
                new JxqyFloat2(_camera.X, _camera.Y));
            // Match the original early return: a temporary player at world
            // zero must not replace the last real follow position.
            if (position != JxqyFloat2.Zero)
                _lastCameraPlayerPosition = position;
            _lastCameraPlayerCharacter = player;
        }

        private JxqyCharacter ResolvePlayerKindCharacter()
        {
            return _npcs?.ResolvePlayerKindCharacter() ?? _player;
        }

        private JxqyFloat2 ResolveTileCameraPosition(
            int column,
            int row)
        {
            JxqyIntPoint world =
                JxqyIsometricMapMath.TileToWorldPixel(
                    column,
                    row,
                    boundCheck: false);
            JxqyIntRect camera = JxqyIsometricMapMath.ClampCamera(
                world.X - LogicalWidth / 2,
                world.Y - LogicalHeight / 2,
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            return new JxqyFloat2(camera.X, camera.Y);
        }

        private void ApplyPresentationCamera()
        {
            JxqyFloat2 position =
                _presentationEffects.CameraPosition;
            _camera = JxqyIsometricMapMath.ClampCamera(
                Mathf.RoundToInt(position.X),
                Mathf.RoundToInt(position.Y),
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            _presentationEffects.SetCameraPositionPreservingMove(
                new JxqyFloat2(_camera.X, _camera.Y));
        }

        private void Update()
        {
            if (!_ready)
                return;
            _combatFloatTextPool?.Synchronize(
                _gameStarted ? _player : null,
                _gameStarted ? _npcs?.Npcs : null);
#if UNITY_EDITOR
            long updateAllocatedBytes =
                GC.GetAllocatedBytesForCurrentThread();
#endif
            if (_player == null || _renderer == null || _map == null)
            {
                JxqyInputFrame titleInput = _input.CaptureFrame();
                IReadOnlyList<JxqyInputIntent> titleIntents =
                    _input.CaptureIntents();
                float titleElapsed =
                    Mathf.Min(0.1f, Time.unscaledDeltaTime);
                ProcessUiInput(titleInput, titleIntents);
                using (ScriptTickMarker.Auto())
                    _scriptSession?.Tick(titleElapsed * 1000.0);
                _presentationEffects.Tick(titleElapsed);
                UpdateScreenFadeUi();
#if UNITY_EDITOR
                _acceptanceManagedBytesLastUpdate =
                    GC.GetAllocatedBytesForCurrentThread() -
                    updateAllocatedBytes;
#endif
                return;
            }
            RefreshActiveMapBindings();
            RefreshLogicalViewport(false);
            JxqyInputFrame input = _input.CaptureFrame();
            IReadOnlyList<JxqyInputIntent> intents = _input.CaptureIntents();
            float elapsed = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            // The option only controls locomotion and natural resource
            // recovery. Scripts, combat, presentations, music and all other
            // clocks keep their authored timing.
            float movementAndRecoveryElapsed = elapsed * _gameSpeedScale;
            if (_gameStarted && HasActiveSuperMagicPresentation())
            {
                TickSuperMagicPresentation(elapsed);
                return;
            }
            ProcessUiInput(input, intents);
            bool gameplayPaused =
                _gameStarted &&
                _uiSession?.RequestsGameplayPause == true;
            if (!gameplayPaused)
                TickTimeLimit(elapsed);
            using (ScriptTickMarker.Auto())
                _scriptSession?.Tick(elapsed * 1000.0);
            if (_video is JxqyUnityVideoPort activeVideo &&
                activeVideo.IsPresentationActive)
            {
                _renderer.Submit(Array.Empty<JxqyDrawCommand>());
                return;
            }
            if (_scriptSession?.IsFaulted == true &&
                !_scriptFaultReported)
            {
                _scriptFaultReported = true;
                string diagnostics = string.Join(
                    Environment.NewLine,
                    _scriptSession.Diagnostics.Select(
                        item => item.ToString()));
                Debug.LogError(
                    $"Jxqy legacy script faulted.{Environment.NewLine}" +
                    diagnostics,
                    this);
                ReturnToTitle();
            }
            RecoverOrphanedOpaqueFade();
            if (gameplayPaused)
            {
                UpdateScreenFadeUi();
                ApplyPresentationColors();
                SubmitFrame();
                return;
            }
            bool specialActionActive =
                _player.IsSpecialActionActive &&
                _playerSpecialAction != null &&
                !_playerSpecialAction.IsFinished;
            if (_player.IsSpecialActionActive && !specialActionActive)
                _player.EndSpecialAction();
            // Original JumpTo never enters a jump state unless the active
            // character resource actually supplies a jump action. Temporary
            // playable characters such as Nalan Zhen do not; recovering here
            // also releases a bad jump state created before this correction.
            if (_player.IsJumping && !HasPlayerJumpAction())
                _player.Stop();
            bool scriptedMovement =
                _gameStarted &&
                _player.HasPath &&
                !specialActionActive;
            if (scriptedMovement && !IsPlayerJumpTakeoffFrame())
            {
                _player.TickMovement(
                    movementAndRecoveryElapsed,
                    IsPlayerPathTileBlocked);
            }
            bool manualMovementRequested =
                _gameStarted &&
                !_legacyInputDisabled &&
                !_legacyKeyboardMovementThisFrame &&
                (input.Buttons &
                 JxqyInputButtons.LegacyKeyboardMovement) == 0 &&
                !_uiSession.IsModal &&
                !(_scriptSession?.IsRunning ?? false) &&
                !specialActionActive &&
                !_player.HasPath &&
                (!Mathf.Approximately(input.MoveX, 0f) ||
                 !Mathf.Approximately(input.MoveY, 0f));
            bool runRequested =
                _player.WantsToRun(
                    IsRunRequested(input),
                    useThewWhenNormalRun: true);
            bool manualRunning = manualMovementRequested && runRequested;
            bool manualMovement =
                manualMovementRequested &&
                _player.TryBeginManualMovement(manualRunning);
            if (!manualMovement && _manualPlayerMovementActive)
                _player.EndManualMovement();
            _manualPlayerMovementActive = manualMovement;
            bool moving = scriptedMovement || manualMovement;
            if (manualMovement)
            {
                ClearPendingInteraction();
                JxqyFloat2 previous = _player.PositionInWorld;
                if (!_player.IsMovementDisabled)
                {
                    _player.Move(
                        new JxqyFloat2(input.MoveX, -input.MoveY),
                        movementAndRecoveryElapsed,
                        manualRunning
                            ? Math.Max(1, _player.RunSpeedFold) *
                              _player.MoveSpeedScale
                            : _player.WalkSpeed *
                              _player.MoveSpeedScale);
                }
                JxqyIntPoint tile = _player.TilePosition;
                if (CreateLiveCollisionMap()
                    .IsObstacleForCharacter(tile))
                    _player.PositionInWorld = previous;
            }
            _player.TickThew(
                movementAndRecoveryElapsed,
                moving,
                _player.IsRunning || manualRunning,
                useThewWhenNormalRun: true);
            if (_player.Thew <= 0 && _player.IsRunning)
            {
                _player.SetState(
                    _player.IsInFighting
                        ? JxqyCharacterState.FightWalk
                        : JxqyCharacterState.Walk);
            }
            if (_pendingPlayerMagicCast != null &&
                _player.State != JxqyCharacterState.Magic)
            {
                _pendingPlayerMagicCast = null;
            }
            JxqyAnimationPlayer animation;
            bool playerStatusDeath = TryGetPlayerStatusDeathAnimation(
                _player,
                out JxqyAnimationPlayer playerDeathAnimation);
            if (playerStatusDeath)
            {
                animation = playerDeathAnimation;
            }
            else if (_playerSpecialAction != null &&
                !_playerSpecialAction.IsFinished)
            {
                animation = _playerSpecialAction;
            }
            else if (_playerScriptActions.TryGetValue(
                         (int)_player.State,
                         out JxqyAnimationPlayer scriptedAction))
            {
                animation = scriptedAction;
            }
            else if ((!moving || !_player.IsStanding) &&
                     TryGetPlayerStateAction(
                         _player.State,
                         out JxqyAnimationPlayer stateAction))
            {
                animation = stateAction;
            }
            else if (_player.IsJumping)
            {
                // A moving Jump must never be presented as Walk/Run. This can
                // occur while restoring an older save whose character action
                // resource has not been loaded yet.
                animation = _playerStand;
            }
            else
            {
                animation = _player.IsRunning || manualRunning
                    ? _playerRun
                    : moving
                        ? _playerWalk
                        : _playerStand;
            }
            bool playerStateChanged =
                _playerVisualState != _player.State ||
                _playerVisualStateVersion != _player.StateVersion;
            if (!ReferenceEquals(_playerVisual.Animation, animation) ||
                playerStateChanged)
            {
                if (playerStateChanged)
                {
                    ApplyCharacterStateSound(
                        _player,
                        _player.State,
                        _playerStateSounds,
                        _playerVisual.Id,
                        ref _playerActiveStateSoundId);
                }
                _playerVisual.Animation = animation;
                animation.Restart();
            }
            _playerVisualState = _player.State;
            _playerVisualStateVersion = _player.StateVersion;
            animation.SetDirection(
                playerStatusDeath ? 0 : _player.CurrentDirection);
            animation.Advance(
                playerStatusDeath || specialActionActive
                    ? elapsed
                    : (IsMovementCharacterState(_player.State) ||
                       _player.IsJumping
                        ? movementAndRecoveryElapsed
                        : elapsed) * _player.CharacterTimeScale);
            if (!string.IsNullOrWhiteSpace(
                    _playerActiveStateSoundId) &&
                _audio is IJxqyWorldAudioPort playerWorldAudio)
            {
                playerWorldAudio.SetWorldSoundPosition(
                    _playerActiveStateSoundId,
                    _player.PositionInWorld);
            }
            if (_player.State != JxqyCharacterState.Sit ||
                animation.IsFinished)
            {
                // Sitdown first plays the transition once and holds its last
                // frame. The original only starts converting thew to mana
                // after that transition has completed.
                    _player.TickMeditation(
                        _player.State == JxqyCharacterState.Sit
                        ? movementAndRecoveryElapsed
                        : 0f);
            }
            if (animation.IsFinished &&
                !animation.IsLooping &&
                !IsLoopingCharacterState(_player.State) &&
                !ShouldHoldFinishedCharacterPose(_player.State) &&
                !_player.IsDead)
            {
                PlayCharacterCompletionSound(
                    _player,
                    _player.State,
                    _playerStateSounds);
                if (_pendingPlayerMagicCast != null &&
                    _player.State == JxqyCharacterState.Magic)
                {
                    CompletePendingPlayerMagicCast();
                }
                else
                {
                    CompleteFinishedCharacterAction(_player);
                }
            }
            if (_gameStarted)
            {
                _uiSession.Npcs = _npcs?.Npcs ?? Array.Empty<JxqyNpc>();
                TickWorldObjects(elapsed);
                using (NpcTickMarker.Auto())
                    _npcs?.Tick(elapsed, movementAndRecoveryElapsed);
                if (_npcs?.FollowerResetRequested == true)
                    ResetPartnerPositions();
                using (CombatTickMarker.Auto())
                    TickCombat(elapsed);
                TryStartPendingInteraction();
            }
#if UNITY_EDITOR
            long actorVisualAllocatedBytes =
                GC.GetAllocatedBytesForCurrentThread();
#endif
            using (ActorVisualTickMarker.Auto())
                UpdateActorVisuals(elapsed, movementAndRecoveryElapsed);
#if UNITY_EDITOR
            _acceptanceManagedBytesLastActorVisualTick =
                GC.GetAllocatedBytesForCurrentThread() -
                actorVisualAllocatedBytes;
#endif
            _presentationEffects.Tick(elapsed);
            UpdateScreenFadeUi();
            UpdatePlayerVisual();
            ApplyPresentationColors();
            if (_presentationEffects.HasCameraOverride)
                ApplyPresentationCamera();
            UpdateCameraFromOriginalPlayerFollow();
            UpdatePointerHighlight(
                new JxqyFloat2(input.PointerX, input.PointerY));
            TryTriggerPlayerTrap();
            _combatFloatTextPool?.UpdateVisuals(
                _worldCamera,
                elapsed);
            SubmitFrame();
#if UNITY_EDITOR
            _acceptanceManagedBytesLastUpdate =
                GC.GetAllocatedBytesForCurrentThread() -
                updateAllocatedBytes;
#endif
        }

        private void TryTriggerPlayerTrap()
        {
            if (!_gameStarted ||
                _mapSwitchInProgress ||
                _uiSession.IsModal ||
#if UNITY_EDITOR
                _acceptanceSuppressTraps ||
#endif
                (_scriptSession?.IsRunning ?? true))
            {
                return;
            }
            JxqyIntPoint tile = _player.TilePosition;
            if (tile.Equals(_lastTrapObservedTile))
                return;
            _lastTrapObservedTile = tile;
            int trapIndex = _map.GetTrapIndex(tile.X, tile.Y);
            if (!_trapRegistry.TryTrigger(
                    GetMapDisplayName(ActiveMapStableId),
                    trapIndex,
                    out string scriptFileName))
            {
                return;
            }
            _player.Stop();
            StartTrapScriptAsync(
                    scriptFileName,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private async UniTaskVoid StartTrapScriptAsync(
            string scriptFileName,
            CancellationToken cancellationToken)
        {
            try
            {
                await _scriptSession.StartAsync(
                    scriptFileName,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ReturnToTitle();
            }
        }

        private void ProcessUiInput(
            JxqyInputFrame frame,
            IReadOnlyList<JxqyInputIntent> intents)
        {
            _legacyKeyboardMovementThisFrame = false;
            foreach (JxqyInputIntent intent in intents)
            {
                if (intent.Phase == JxqyInputPhase.Started &&
                    intent.Kind ==
                    JxqyInputIntentKind.ToggleFullscreen)
                {
                    ToggleDesktopFullscreen();
                    break;
                }
            }
            if (_video is JxqyUnityVideoPort video &&
                video.IsPresentationActive)
            {
                foreach (JxqyInputIntent intent in intents)
                {
                    if (intent.Phase != JxqyInputPhase.Started)
                        continue;
                    if (intent.Kind is (
                        JxqyInputIntentKind.Cancel or
                        JxqyInputIntentKind.PointerPrimary))
                    {
                        video.RequestSkip();
                        break;
                    }
                }
                return;
            }
            if (_gameStarted && _player?.IsDead == true)
                return;
            if (_legacyInputDisabled && !_uiSession.IsModal)
                return;

            int menuMove = 0;
            if (_uiSession.IsModal)
            {
                if (frame.MoveY > 0.5f)
                    menuMove = -1;
                else if (frame.MoveY < -0.5f)
                    menuMove = 1;
                if (menuMove != 0 && menuMove != _lastMenuMove)
                    _uiSession.MoveSelection(menuMove);
            }
            _lastMenuMove = menuMove;

            foreach (JxqyInputIntent intent in intents)
            {
                _inputIntentCoverageMask |= 1 << (int)intent.Kind;
                if (intent.Phase != JxqyInputPhase.Started)
                    continue;
                switch (intent.Kind)
                {
                    case JxqyInputIntentKind.PointerPrimary:
                        if (_player != null)
                        {
                            HandlePointerPrimary(
                                intent.Value,
                                _player.WantsToRun(
                                    IsRunRequested(frame),
                                    useThewWhenNormalRun: true),
                                false,
                                false);
                        }
                        break;
                    case JxqyInputIntentKind.Jump:
                        if (_player != null)
                        {
                            HandlePointerPrimary(
                                intent.Value,
                                false,
                                true,
                                false);
                        }
                        break;
                    case JxqyInputIntentKind.ForceAttack:
                        if (_player != null)
                        {
                            HandlePointerPrimary(
                                intent.Value,
                                false,
                                false,
                                true);
                        }
                        break;
                    case JxqyInputIntentKind.PointerSecondary:
                        if (_player != null)
                            HandlePointerSecondary(intent.Value);
                        break;
                    case JxqyInputIntentKind.Interact:
                        if (_player != null)
                            HandleInteract(intent.Value);
                        break;
                    case JxqyInputIntentKind.PrimaryAttack:
                        if (_gameStarted && !_uiSession.IsModal)
                            SelectPlayerAttackTarget();
                        break;
                    case JxqyInputIntentKind.MobileMove:
                        if (_player != null)
                            HandleMobileMoveStarted();
                        break;
                    case JxqyInputIntentKind.MobileDirectionalAttack:
                        if (_player != null)
                            HandleMobileDirectionalAttack();
                        break;
                    case JxqyInputIntentKind.MobileDirectionalSkill:
                        if (_gameStarted &&
                            !_uiSession.IsModal &&
                            intent.Slot >= 0)
                        {
                            UseMobileDirectionalMagicShortcut(
                                intent.Slot);
                        }
                        break;
                    case JxqyInputIntentKind.UseSkill:
                        if (_gameStarted &&
                            !_uiSession.IsModal &&
                            intent.Slot >= 0)
                        {
                            UseMagicShortcut(
                                intent.Slot,
                                new JxqyFloat2(
                                    frame.PointerX,
                                    frame.PointerY));
                        }
                        break;
                    case JxqyInputIntentKind.UseItem:
                        if (_gameStarted &&
                            !_uiSession.IsModal &&
                            intent.Slot >= 0)
                        {
                            UseGoodsShortcut(intent.Slot);
                        }
                        break;
                    case JxqyInputIntentKind.Menu:
                        if (!_gameStarted)
                        {
                            if (_uiSession.CurrentScreen ==
                                JxqyUiScreen.Title)
                            {
                                _uiSession.Cancel();
                            }
                            break;
                        }
                        if (_uiSession.IsModal)
                        {
                            _uiSession.Cancel();
                        }
                        else if (_uiSession.LeftPanelScreen.HasValue ||
                                 _uiSession.RightPanelScreen.HasValue)
                        {
                            _uiSession.Open(JxqyUiScreen.Hud);
                        }
                        else
                        {
                            _uiSession.Open(JxqyUiScreen.Menu);
                        }
                        break;
                    case JxqyInputIntentKind.Confirm:
                        if (_uiSession.IsModal)
                            _uiSession.Confirm();
                        break;
                    case JxqyInputIntentKind.Cancel:
                        if (_uiSession.IsModal)
                            _uiSession.Cancel();
                        break;
                    case JxqyInputIntentKind.Meditate:
                        if (_gameStarted &&
                            !_uiSession.IsModal &&
                            !(_scriptSession?.IsRunning ?? false))
                        {
                            if (_player.ManaLimit)
                            {
                                _uiSession.SetNotice(
                                    "内力尽失中无法打坐");
                                break;
                            }
                            ClearPendingInteraction();
                            _pendingPlayerMagicCast = null;
                            _queuedPlayerMagicCast = null;
                            _player.ToggleMeditation();
                        }
                        break;
                    case JxqyInputIntentKind.ToggleStatus:
                        ToggleGameplayWindow(JxqyUiScreen.Status);
                        break;
                    case JxqyInputIntentKind.ToggleEquipment:
                        if (_gameStarted &&
                            !(_scriptSession?.IsRunning ?? false))
                        {
                            _uiSession.OpenPlayerEquipment();
                        }
                        break;
                    case JxqyInputIntentKind.ToggleTraining:
                        break;
                    case JxqyInputIntentKind.ToggleInventory:
                        ToggleGameplayWindow(JxqyUiScreen.Inventory);
                        break;
                    case JxqyInputIntentKind.ToggleSkills:
                        ToggleGameplayWindow(JxqyUiScreen.Skills);
                        break;
                    case JxqyInputIntentKind.ToggleMemo:
                        ToggleGameplayWindow(JxqyUiScreen.Memo);
                        break;
                    case JxqyInputIntentKind.ToggleLittleMap:
                        ToggleLittleMap();
                        break;
                    case JxqyInputIntentKind.LegacyMoveForward:
                        _legacyKeyboardMovementThisFrame = true;
                        HandleLegacyKeyboardMove(
                            _player.CurrentDirection,
                            frame);
                        break;
                    case JxqyInputIntentKind.LegacyMoveDirection:
                        _legacyKeyboardMovementThisFrame = true;
                        HandleLegacyKeyboardMove(intent.Slot, frame);
                        break;
                    case JxqyInputIntentKind.LegacyTurnLeft:
                        _legacyKeyboardMovementThisFrame = true;
                        HandleLegacyKeyboardTurn(-1);
                        break;
                    case JxqyInputIntentKind.LegacyTurnRight:
                        _legacyKeyboardMovementThisFrame = true;
                        HandleLegacyKeyboardTurn(1);
                        break;
                    case JxqyInputIntentKind.LegacyTurnBack:
                        _legacyKeyboardMovementThisFrame = true;
                        HandleLegacyKeyboardTurn(4);
                        break;
                    case JxqyInputIntentKind.ToggleFullscreen:
                        // Handled before gameplay/modal input gates because
                        // the original game treats this as a global chord.
                        break;
                }
            }
        }

        private bool CanAcceptLegacyKeyboardMovement()
        {
            return _gameStarted &&
                   !_legacyInputDisabled &&
                   !_uiSession.IsModal &&
                   !(_scriptSession?.IsRunning ?? false) &&
                   !_player.IsDead &&
                   !_player.IsMovementDisabled;
        }

        private void HandleLegacyKeyboardMove(
            int direction,
            JxqyInputFrame frame)
        {
            if (!CanAcceptLegacyKeyboardMovement() ||
                direction < 0 || direction > 7)
            {
                return;
            }

            bool runRequested = _player.WantsToRun(
                IsRunRequested(frame),
                useThewWhenNormalRun: true);
            if (_player.HasPath)
            {
                if (!_player.IsWalking && !_player.IsRunning)
                    return;

                if (_player.CurrentDirection != direction)
                {
                    // A new keyboard direction has the same immediate
                    // retargeting contract as a fresh mouse movement click:
                    // replace the route from the actor's exact current
                    // position instead of waiting for the next tile center.
                    BeginLegacyKeyboardPath(direction, runRequested);
                    return;
                }

                SynchronizeLegacyKeyboardRunState(runRequested);

                // Keep the waypoint currently being approached and replace
                // only its tail. This maintains one tile of lookahead while
                // a keyboard direction is held, so movement does not stop
                // and restart at every tile boundary.
                JxqyIntPoint anchor = _player.NextPathTilePosition;
                JxqyIntPoint continuedDestination =
                    JxqyPathfinder.GetAllNeighbors(anchor)[direction];
                JxqyIntPoint anchorWorld =
                    JxqyIsometricMapMath.TileToWorldPixel(
                        anchor.X,
                        anchor.Y);
                if (CreateLiveCollisionMap()
                    .IsObstacleForCharacter(continuedDestination))
                {
                    _player.RetargetPath(new[]
                    {
                        new JxqyFloat2(anchorWorld.X, anchorWorld.Y),
                    });
                    MoveStandingPartnersTo(continuedDestination);
                    return;
                }

                JxqyIntPoint continuedWorld =
                    JxqyIsometricMapMath.TileToWorldPixel(
                        continuedDestination.X,
                        continuedDestination.Y);
                _player.RetargetPath(new[]
                {
                    new JxqyFloat2(anchorWorld.X, anchorWorld.Y),
                    new JxqyFloat2(
                        continuedWorld.X,
                        continuedWorld.Y),
                });
                return;
            }

            BeginLegacyKeyboardPath(direction, runRequested);
        }

        private void BeginLegacyKeyboardPath(
            int direction,
            bool runRequested)
        {
            JxqyIntPoint current = _player.TilePosition;
            JxqyIntPoint destination =
                JxqyPathfinder.GetAllNeighbors(current)[direction];
            if (CreateLiveCollisionMap()
                .IsObstacleForCharacter(destination))
            {
                MoveStandingPartnersTo(destination);
                return;
            }
            JxqyIntPoint currentWorld =
                JxqyIsometricMapMath.TileToWorldPixel(
                    current.X,
                    current.Y);
            JxqyIntPoint destinationWorld =
                JxqyIsometricMapMath.TileToWorldPixel(
                    destination.X,
                    destination.Y);
            var path = new[]
            {
                new JxqyFloat2(currentWorld.X, currentWorld.Y),
                new JxqyFloat2(
                    destinationWorld.X,
                    destinationWorld.Y),
            };
            ClearPendingInteraction();
            _playerAutoAttack.Target = null;
            _player.BeginPath(
                path,
                runRequested);
        }

        private void SynchronizeLegacyKeyboardRunState(bool runRequested)
        {
            if (_player.IsRunning == runRequested)
                return;
            _player.SetState(
                runRequested
                    ? _player.IsInFighting
                        ? JxqyCharacterState.FightRun
                        : JxqyCharacterState.Run
                    : _player.IsInFighting
                        ? JxqyCharacterState.FightWalk
                        : JxqyCharacterState.Walk);
        }

        private void HandleLegacyKeyboardTurn(int delta)
        {
            if (!CanAcceptLegacyKeyboardMovement() || _player.HasPath)
                return;
            ClearPendingInteraction();
            _playerAutoAttack.Target = null;
            _player.CurrentDirection += delta;
        }

        private static void ToggleDesktopFullscreen()
        {
#if !UNITY_EDITOR
            Screen.fullScreen = !Screen.fullScreen;
#endif
        }

        private void ToggleGameplayWindow(JxqyUiScreen screen)
        {
            if (!_gameStarted ||
                (_scriptSession?.IsRunning ?? false))
            {
                return;
            }
            _uiSession.Toggle(screen);
        }

        private void ToggleLittleMap()
        {
            if (!_gameStarted || _mapSwitchInProgress ||
                (_scriptSession?.IsRunning ?? false) ||
                _uiSession.ActiveModalScreen.HasValue &&
                _uiSession.ActiveModalScreen != JxqyUiScreen.LittleMap)
            {
                return;
            }
            string fileName = GetLegacyMapFileName(ActiveMapStableId);
            string baseName = fileName.Substring(0, fileName.Length - 4);
            if (!_contentContext.TryResolveLittleMapTextureAddress(
                    baseName,
                    out string littleMapAddress))
            {
                JxqyResourceAddressCatalog.ReportMissing(
                    "LittleMap",
                    fileName,
                    _contentContext.LittleMapTextureAddress(baseName));
                _uiSession.ShowSystemMessage("当前地图没有小地图。");
                return;
            }
            _uiSession.LittleMapTextureAddress = littleMapAddress;
            _uiSession.LittleMapName =
                GetLittleMapDisplayName(ActiveMapStableId);
            _uiSession.LittleMapViewX = Math.Max(0, _camera.X / 4);
            _uiSession.LittleMapViewY = Math.Max(0, _camera.Y / 4);
            _uiSession.Npcs = _npcs?.Npcs ?? Array.Empty<JxqyNpc>();
            _uiSession.Toggle(JxqyUiScreen.LittleMap);
        }

        private bool TryMovePlayerFromLittleMap(
            JxqyFloat2 worldPosition,
            bool runRequested)
        {
            if (!_gameStarted || _mapSwitchInProgress ||
                (_scriptSession?.IsRunning ?? false))
            {
                return false;
            }
            JxqyIntPoint destination =
                JxqyIsometricMapMath.WorldPixelToTile(
                    Mathf.RoundToInt(worldPosition.X),
                    Mathf.RoundToInt(worldPosition.Y));
            IReadOnlyList<JxqyFloat2> path =
                JxqyPathfinder.FindPathToNearestReachable(
                    CreateLiveCollisionMap(),
                    _player.TilePosition,
                    destination,
                    out _);
            if (path.Count < 2 ||
                !_player.BeginPath(
                    path,
                    runRequested && !_player.IsRunDisabled))
            {
                MoveStandingPartnersTo(destination);
                return false;
            }
            if (_playerAutoAttack != null)
                _playerAutoAttack.Target = null;
            ClearPendingInteraction();
            _pendingPlayerMagicCast = null;
            _queuedPlayerMagicCast = null;
            return true;
        }

        private static bool IsRunRequested(JxqyInputFrame frame)
        {
            return (frame.Buttons & JxqyInputButtons.RunModifier) != 0;
        }

        private void UseGoodsShortcut(int slot)
        {
            if (slot < 0 || slot >= 3)
                return;
            JxqyInventoryEntry entry =
                _inventory.FindAtLegacyIndex(221 + slot);
            if (entry?.Definition?.Kind != JxqyItemKind.Drug)
                return;
            for (int index = 0; index < _inventory.Entries.Count; index++)
            {
                if (!ReferenceEquals(_inventory.Entries[index], entry))
                    continue;
                _uiSession.UseInventoryItem(index);
                return;
            }
        }

        private void UseMagicShortcut(int slot)
        {
            if (slot < 0 || slot >= 5)
                return;
            JxqySkillEntry entry =
                _skills.FindAtLegacyIndex(40 + slot);
            if (entry == null)
                return;
            for (int index = 0; index < _skills.Skills.Count; index++)
            {
                if (!ReferenceEquals(_skills.Skills[index], entry))
                    continue;
                _uiSession.SelectSkill(index);
                TryUsePlayerSkill(index);
                return;
            }
        }

        private void UseMagicShortcut(
            int slot,
            JxqyFloat2 logicalPointer)
        {
            if (slot < 0 || slot >= 5)
                return;
            JxqySkillEntry entry =
                _skills.FindAtLegacyIndex(40 + slot);
            if (entry == null)
                return;
            for (int index = 0; index < _skills.Skills.Count; index++)
            {
                if (!ReferenceEquals(_skills.Skills[index], entry))
                    continue;
                _uiSession.SelectSkill(index);
                TryFindPointerMagicTarget(
                    logicalPointer,
                    out JxqyNpc target);
                JxqyFloat2 destination =
                    ResolvePlayerMagicDestination(
                        logicalPointer,
                        target);
                TryUsePlayerSkill(
                    index,
                    destination,
                    target,
                    true);
                return;
            }
        }

        private void HandleMobileMoveStarted()
        {
            if (!_gameStarted ||
                _legacyInputDisabled ||
                _uiSession.IsModal ||
                (_scriptSession?.IsRunning ?? false) ||
                _player == null ||
                _player.IsDead ||
                _player.IsSpecialActionActive ||
                !_player.HasPath)
            {
                return;
            }
            ClearPendingInteraction();
            _player.StopMovementPreservingAction();
        }

        private void HandleMobileDirectionalAttack()
        {
            if (!_gameStarted ||
                _uiSession.IsModal ||
                (_scriptSession?.IsRunning ?? false) ||
                _player == null)
            {
                return;
            }
            ClearPendingInteraction();
            BeginBasicAttackAt(
                _player,
                GetFacingWorldDestination(
                    _player,
                    GetMaximumBasicAttackDistance(_player)));
        }

        private void UseMobileDirectionalMagicShortcut(int slot)
        {
            if (slot < 0 || slot >= 5)
                return;
            JxqySkillEntry entry =
                _skills.FindAtLegacyIndex(40 + slot);
            if (entry == null)
                return;
            for (int index = 0; index < _skills.Skills.Count; index++)
            {
                if (!ReferenceEquals(_skills.Skills[index], entry))
                    continue;
                _uiSession.SelectSkill(index);
                JxqyMagicDefinition magic = entry.Magic;
                bool bodyEffect = IsBodyEffectMagic(magic);
                JxqyNpc target = bodyEffect
                    ? FindMobileMagicTarget(
                        npc =>
                            npc.Kind == JxqyCharacterKind.Follower &&
                            npc.Relation == JxqyRelationType.Friend)
                    : FindMobileMagicTarget(
                        npc => JxqyRelations.AreOpposed(_player, npc));
                JxqyFloat2 destination;
                if (target != null)
                {
                    destination = target.PositionInWorld;
                }
                else if (bodyEffect)
                {
                    destination = _player.PositionInWorld;
                }
                else
                {
                    destination = GetFacingWorldDestination(
                        _player,
                        1);
                }
                TryUsePlayerSkill(
                    index,
                    destination,
                    target,
                    true);
                return;
            }
        }

        private JxqyNpc FindMobileMagicTarget(
            Func<JxqyNpc, bool> predicate)
        {
            if (_npcs == null || _player == null || predicate == null)
                return null;
            JxqyNpc nearest = null;
            int nearestDistance = int.MaxValue;
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (npc == null ||
                    !npc.IsVisible ||
                    npc.IsDead ||
                    !predicate(npc))
                {
                    continue;
                }
                int distance = JxqyPathfinder.GetViewTileDistance(
                    _player.TilePosition,
                    npc.TilePosition);
                if (distance > OriginalMobileAutoTargetRadius ||
                    distance >= nearestDistance)
                {
                    continue;
                }
                nearest = npc;
                nearestDistance = distance;
            }
            return nearest;
        }

        private static bool IsBodyEffectMagic(
            JxqyMagicDefinition magic)
        {
            return magic != null &&
                   magic.MoveKind == 13;
        }

        private static JxqyFloat2 GetFacingWorldDestination(
            JxqyCharacter character,
            int tileDistance)
        {
            JxqyIntPoint tile = character.TilePosition;
            int direction = character.CurrentDirection;
            int steps = Math.Max(0, tileDistance);
            for (int index = 0; index < steps; index++)
                tile = JxqyPathfinder.GetAllNeighbors(tile)[direction];
            JxqyIntPoint world =
                JxqyIsometricMapMath.TileToWorldPixel(tile.X, tile.Y);
            return new JxqyFloat2(world.X, world.Y);
        }

        private void HandlePointerPrimary(
            JxqyFloat2 pointer,
            bool runRequested,
            bool jumpRequested,
            bool forceAttackRequested)
        {
            if (_video is JxqyUnityVideoPort unityVideo &&
                unityVideo.IsPlaying)
            {
                unityVideo.RequestSkip();
                return;
            }
            if (!_gameStarted ||
                _uiSession.IsModal ||
                (_scriptSession?.IsRunning ?? false) ||
                JxqyDesktopInputBridge.OverrideSource == null &&
                EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            int worldX = _camera.X + Mathf.RoundToInt(pointer.X);
            int worldY = _camera.Y + Mathf.RoundToInt(pointer.Y);
            JxqyIntPoint requestedDestination =
                JxqyIsometricMapMath.WorldPixelToTile(worldX, worldY);

            // Alt+click is a movement command in the PC input contract. It
            // must win before NPC/object hit testing; otherwise any actor
            // under the pointer silently turns the command into interaction.
            if (jumpRequested)
            {
                if (!HasPlayerJumpAction())
                    return;
                JxqyIntPoint jumpWorld =
                    JxqyIsometricMapMath.TileToWorldPixel(
                        requestedDestination.X,
                        requestedDestination.Y);
                if (_player.Thew >= 10 &&
                    _player.BeginJump(
                        new JxqyFloat2(jumpWorld.X, jumpWorld.Y),
                        tile => !CreateLiveCollisionMap()
                            .IsObstacleForCharacterJump(tile)))
                {
                    _player.Thew -= 10;
                    if (_playerAutoAttack != null)
                        _playerAutoAttack.Target = null;
                    ClearPendingInteraction();
                }
                return;
            }

            object interactionOwner = null;
            string interactionScript = string.Empty;
            bool pointerHit = TryFindPointerInteractable(
                pointer,
                out interactionOwner,
                out interactionScript);
            if (!pointerHit)
            {
                TryFindInteractableAt(
                    requestedDestination,
                    false,
                    out interactionOwner,
                    out interactionScript);
            }
            if (interactionOwner is JxqyNpc attackTarget &&
                IsLegacyPointerAttackTarget(attackTarget))
            {
                SelectPlayerAttackTarget(
                    attackTarget,
                    runRequested);
                return;
            }
            // The original cancels its auto-attack target as soon as the
            // player clicks anywhere other than an enemy. Without this, an
            // old target survives a movement click and attacks resume later.
            if (_playerAutoAttack != null)
                _playerAutoAttack.Target = null;
            if (interactionOwner is JxqyNpc clickScriptNpc &&
                IsNpcClickScript(clickScriptNpc, interactionScript))
            {
                StartInteraction(interactionOwner, interactionScript);
                return;
            }
            if (interactionOwner != null &&
                !string.IsNullOrWhiteSpace(interactionScript) &&
                IsInInteractionRange(interactionOwner))
            {
                StartInteraction(interactionOwner, interactionScript);
                return;
            }
            if (forceAttackRequested && interactionOwner == null)
            {
                JxqyIntPoint attackWorld =
                    JxqyIsometricMapMath.TileToWorldPixel(
                        requestedDestination.X,
                        requestedDestination.Y);
                ClearPendingInteraction();
                BeginBasicAttackAt(
                    _player,
                    new JxqyFloat2(attackWorld.X, attackWorld.Y));
                return;
            }
            requestedDestination = ResolvePointerPrimaryDestination(
                requestedDestination,
                interactionOwner);

            int directionBefore = _player.CurrentDirection;
            JxqyIntPoint pathStart = _player.TilePosition;
            IReadOnlyList<JxqyFloat2> path =
                JxqyPathfinder.FindPathToNearestReachable(
                    CreateLiveCollisionMap(),
                    pathStart,
                    requestedDestination,
                    out JxqyIntPoint resolvedDestination);
            if (_npcs?.IsPlayerRouteBlockedByStandingFollower(
                    pathStart,
                    requestedDestination,
                    resolvedDestination) == true)
            {
                // Original Player.WalkTo/RunTo leaves the player standing
                // when no route exists, then asks every standing partner to
                // move toward the requested destination. Only enter that
                // recovery when the route is proven to be follower-blocked;
                // inaccessible terrain and other NPCs must not make partners
                // copy a normal long-distance pointer command.
                ClearPendingInteraction();
                MoveStandingPartnersTo(requestedDestination);
                return;
            }
            bool accepted =
                path.Count >= 2 &&
                _player.BeginPath(
                    path,
                    runRequested && !_player.IsRunDisabled);
            if (accepted)
            {
                _pendingInteractionOwner = interactionOwner;
                _pendingInteractionScript = interactionOwner == null
                    ? string.Empty
                    : interactionScript;
#if UNITY_EDITOR
                _acceptanceLastPointerAcceptedFrame = Time.frameCount;
                _acceptanceLastPointerTurnedImmediately =
                    _player.CurrentDirection != directionBefore;
#endif
            }
            else
            {
                ClearPendingInteraction();
            }
        }

        private bool HasPlayerJumpAction()
        {
            return HasPlayerAction(JxqyCharacterState.Jump) ||
                   HasPlayerAction(JxqyCharacterState.FightJump);
        }

        private bool HasPlayerAction(JxqyCharacterState state)
        {
            return _player.IsActionEnabled(state) &&
                   (_playerScriptActions.ContainsKey((int)state) ||
                    _playerStateActions.ContainsKey((int)state));
        }

        private void HandleInteract(JxqyFloat2 pointer)
        {
            if (!_gameStarted ||
                _uiSession.IsModal ||
                (_scriptSession?.IsRunning ?? false))
                return;

            if (TryFindNearestInteractable(
                    1,
                    out object nearestOwner,
                    out string nearestScript))
            {
                StartInteraction(nearestOwner, nearestScript);
                return;
            }

            int worldX = _camera.X + Mathf.RoundToInt(pointer.X);
            int worldY = _camera.Y + Mathf.RoundToInt(pointer.Y);
            JxqyIntPoint tile =
                JxqyIsometricMapMath.WorldPixelToTile(worldX, worldY);
            if (!TryFindInteractableAt(
                    tile,
                    true,
                    out object pointerOwner,
                    out string pointerScript))
                return;

            IReadOnlyList<JxqyFloat2> path =
                JxqyPathfinder.FindPathToNearestReachable(
                    CreateLiveCollisionMap(),
                    _player.TilePosition,
                    GetInteractionTile(pointerOwner),
                    out _);
            if (path.Count < 2 || !_player.BeginPath(path))
                return;
            _pendingInteractionOwner = pointerOwner;
            _pendingInteractionScript = pointerScript;
        }

        private void HandlePointerSecondary(JxqyFloat2 pointer)
        {
            if (!_gameStarted ||
                _uiSession.IsModal ||
                (_scriptSession?.IsRunning ?? false) ||
                JxqyDesktopInputBridge.OverrideSource == null &&
                EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            JxqySkillEntry selected = _uiSession.SelectedSkill;
            if (selected == null)
            {
                HandleInteract(pointer);
                return;
            }
            int slot = -1;
            for (int index = 0; index < _skills.Skills.Count; index++)
            {
                if (ReferenceEquals(_skills.Skills[index], selected))
                {
                    slot = index;
                    break;
                }
            }
            if (slot < 0)
                return;
            TryFindPointerMagicTarget(
                pointer,
                out JxqyNpc target);
            JxqyFloat2 destination =
                ResolvePlayerMagicDestination(pointer, target);
            TryUsePlayerSkill(
                slot,
                destination,
                target,
                true);
        }

        private JxqyFloat2 ResolvePlayerMagicDestination(
            JxqyFloat2 logicalPointer,
            JxqyNpc target)
        {
            if (target != null)
            {
                JxqyIntPoint targetPosition =
                    JxqyIsometricMapMath.TileToWorldPixel(
                        target.TilePosition.X,
                        target.TilePosition.Y);
                return new JxqyFloat2(
                    targetPosition.X,
                    targetPosition.Y);
            }
            int worldX =
                _camera.X + Mathf.RoundToInt(logicalPointer.X);
            int worldY =
                _camera.Y + Mathf.RoundToInt(logicalPointer.Y);
            return SnapMagicDestinationToTileCenter(worldX, worldY);
        }

        private static JxqyFloat2 SnapMagicDestinationToTileCenter(
            int worldX,
            int worldY)
        {
            JxqyIntPoint tile =
                JxqyIsometricMapMath.WorldPixelToTile(worldX, worldY);
            JxqyIntPoint center =
                JxqyIsometricMapMath.TileToWorldPixel(tile.X, tile.Y);
            return new JxqyFloat2(center.X, center.Y);
        }

        private bool TryFindNearestInteractable(
            int maximumDistance,
            out object owner,
            out string scriptFileName)
        {
            owner = null;
            scriptFileName = string.Empty;
            int bestDistance = int.MaxValue;
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (!npc.IsVisible ||
                    npc.Life <= 0 ||
                    string.IsNullOrWhiteSpace(npc.ScriptAddress))
                    continue;
                int distance = JxqyPathfinder.GetViewTileDistance(
                    _player.TilePosition,
                    npc.TilePosition);
                if (distance > Math.Max(0, npc.DialogRadius) ||
                    distance >= bestDistance)
                    continue;
                bestDistance = distance;
                owner = npc;
                scriptFileName = npc.ScriptAddress;
            }
            foreach (JxqyWorldObject worldObject in _objects.Objects)
            {
                if (!worldObject.IsVisible ||
                    worldObject.IsRemoved ||
                    !worldObject.IsInteractive)
                    continue;
                int distance = JxqyPathfinder.GetViewTileDistance(
                    _player.TilePosition,
                    worldObject.TilePosition);
                if (distance > maximumDistance ||
                    distance >= bestDistance)
                    continue;
                bestDistance = distance;
                owner = worldObject;
                scriptFileName =
                    !string.IsNullOrWhiteSpace(worldObject.ScriptAddress)
                        ? worldObject.ScriptAddress
                        : worldObject.RightScriptAddress;
            }
            return owner != null &&
                   !string.IsNullOrWhiteSpace(scriptFileName);
        }

        private bool TryFindInteractableAt(
            JxqyIntPoint tile,
            bool preferRightScript,
            out object owner,
            out string scriptFileName)
        {
            owner = null;
            scriptFileName = string.Empty;
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (!npc.IsVisible ||
                    npc.Life <= 0 ||
                    !npc.TilePosition.Equals(tile))
                    continue;
                string npcScript = !preferRightScript &&
                                   !string.IsNullOrWhiteSpace(
                                       npc.ClickScriptAddress)
                    ? npc.ClickScriptAddress
                    : npc.ScriptAddress;
                if (string.IsNullOrWhiteSpace(npcScript))
                    continue;
                owner = npc;
                scriptFileName = npcScript;
                return true;
            }
            foreach (JxqyWorldObject worldObject in _objects.At(tile))
            {
                if (!worldObject.IsVisible ||
                    worldObject.IsRemoved ||
                    !worldObject.IsInteractive)
                    continue;
                string script = preferRightScript &&
                                !string.IsNullOrWhiteSpace(
                                    worldObject.RightScriptAddress)
                    ? worldObject.RightScriptAddress
                    : worldObject.ScriptAddress;
                if (string.IsNullOrWhiteSpace(script))
                    script = worldObject.RightScriptAddress;
                if (string.IsNullOrWhiteSpace(script))
                    continue;
                owner = worldObject;
                scriptFileName = script;
                return true;
            }
            return false;
        }

        private void UpdatePointerHighlight(JxqyFloat2 pointer)
        {
            if (_uiSession != null)
                _uiSession.HoveredNpc = null;
            foreach (JxqyRuntimeActorVisual state in _npcVisuals.Values)
                state.Visual.OutlineColor = Color.clear;
            foreach (JxqyRuntimeActorVisual state in _objectVisuals.Values)
                state.Visual.OutlineColor = Color.clear;
            if (!_gameStarted ||
                _uiSession.IsModal ||
                (_scriptSession?.IsRunning ?? false) ||
                JxqyDesktopInputBridge.OverrideSource == null &&
                EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            if (!TryFindPointerInteractable(
                    pointer,
                    out object owner,
                    out _))
            {
                return;
            }
            if (owner is JxqyNpc npc &&
                _npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual npcState))
            {
                npcState.Visual.OutlineColor = npc.Relation switch
                {
                    JxqyRelationType.Enemy =>
                        new Color(1f, 0f, 0f, 0.6f),
                    JxqyRelationType.Friend =>
                        new Color(0f, 1f, 0f, 0.6f),
                    JxqyRelationType.None =>
                        new Color(0f, 0f, 1f, 0.6f),
                    _ => new Color(1f, 1f, 0f, 0.6f),
                };
                if (_uiSession != null && IsLegacyTargetLifeNpc(npc))
                    _uiSession.HoveredNpc = npc;
            }
            else if (owner is JxqyWorldObject worldObject &&
                     _objectVisuals.TryGetValue(
                         worldObject,
                         out JxqyRuntimeActorVisual objectState))
            {
                objectState.Visual.OutlineColor =
                    new Color(1f, 1f, 0f, 0.6f);
            }
        }

        private bool TryFindPointerInteractable(
            JxqyFloat2 pointer,
            out object owner,
            out string scriptFileName)
        {
            owner = null;
            scriptFileName = string.Empty;
            float worldX = _camera.X + pointer.X;
            float worldY = _camera.Y + pointer.Y;

            foreach (KeyValuePair<JxqyNpc, JxqyRuntimeActorVisual> pair
                     in _npcVisuals)
            {
                JxqyNpc npc = pair.Key;
                if (!npc.IsVisible ||
                    npc.Life <= 0 ||
                    !IsLegacyPointerInteractiveNpc(npc) ||
                    !HitTestVisual(pair.Value.Visual, worldX, worldY))
                {
                    continue;
                }
                owner = npc;
                scriptFileName =
                    !string.IsNullOrWhiteSpace(npc.ClickScriptAddress)
                        ? npc.ClickScriptAddress
                        : npc.ScriptAddress;
                return true;
            }
            foreach (KeyValuePair<
                         JxqyWorldObject,
                         JxqyRuntimeActorVisual> pair in _objectVisuals)
            {
                JxqyWorldObject worldObject = pair.Key;
                if (!worldObject.IsVisible ||
                    worldObject.IsRemoved ||
                    !worldObject.IsInteractive ||
                    !HitTestVisual(pair.Value.Visual, worldX, worldY))
                {
                    continue;
                }
                string script = worldObject.ScriptAddress;
                if (string.IsNullOrWhiteSpace(script))
                    script = worldObject.RightScriptAddress;
                if (string.IsNullOrWhiteSpace(script))
                    continue;
                owner = worldObject;
                scriptFileName = script;
                return true;
            }
            return false;
        }

        private static bool IsLegacyPointerInteractiveNpc(JxqyNpc npc)
        {
            return npc != null &&
                   (!string.IsNullOrWhiteSpace(npc.ScriptAddress) ||
                    !string.IsNullOrWhiteSpace(npc.ClickScriptAddress) ||
                   IsLegacyTargetLifeNpc(npc));
        }

        private static bool IsNpcClickScript(
            JxqyNpc npc,
            string scriptFileName)
        {
            return npc != null &&
                   !string.IsNullOrWhiteSpace(npc.ClickScriptAddress) &&
                   string.Equals(
                       npc.ClickScriptAddress,
                       scriptFileName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyTargetLifeNpc(JxqyNpc npc)
        {
            if (npc == null)
                return false;
            return (npc.Kind == JxqyCharacterKind.Fighter &&
                    (npc.Relation == JxqyRelationType.Enemy ||
                     npc.Relation == JxqyRelationType.None)) ||
                   ((npc.Kind == JxqyCharacterKind.Fighter ||
                     npc.Kind == JxqyCharacterKind.Follower) &&
                    npc.Relation == JxqyRelationType.Friend);
        }

        private static bool IsLegacyPointerAttackTarget(JxqyNpc npc)
        {
            return npc != null &&
                   npc.Kind == JxqyCharacterKind.Fighter &&
                   (npc.Relation == JxqyRelationType.Enemy ||
                    npc.Relation == JxqyRelationType.None);
        }

        private bool TryFindPointerMagicTarget(
            JxqyFloat2 pointer,
            out JxqyNpc target)
        {
            target = null;
            float worldX = _camera.X + pointer.X;
            float worldY = _camera.Y + pointer.Y;
            foreach (KeyValuePair<JxqyNpc, JxqyRuntimeActorVisual> pair
                     in _npcVisuals)
            {
                JxqyNpc npc = pair.Key;
                if (!npc.IsVisible ||
                    npc.IsDead ||
                    !HitTestVisual(pair.Value.Visual, worldX, worldY))
                {
                    continue;
                }
                target = npc;
                return true;
            }
            return false;
        }

        private bool HitTestVisual(
            JxqyWorldVisual visual,
            float worldX,
            float worldY)
        {
            if (visual == null ||
                !visual.IsVisible ||
                visual.Animation == null)
            {
                return false;
            }
            JxqyAnimationPose pose = visual.Animation.GetPose();
            float left = visual.WorldPosition.x - pose.AnchorX;
            float top = visual.WorldPosition.y - pose.AnchorY;
            float localX = worldX - left;
            float localY = worldY - top;
            if (localX < 0f ||
                localY < 0f ||
                localX >= pose.Width ||
                localY >= pose.Height)
            {
                return false;
            }
            if (!_textures.TryGet(
                    pose.AtlasAddress,
                    out Texture2D texture) ||
                !texture.isReadable)
            {
                return true;
            }
            int pixelX = pose.AtlasX + Mathf.FloorToInt(localX);
            int pixelY = pose.AtlasY + pose.Height - 1 -
                         Mathf.FloorToInt(localY);
            if (pixelX < 0 ||
                pixelY < 0 ||
                pixelX >= texture.width ||
                pixelY >= texture.height)
            {
                return false;
            }
            return texture.GetPixel(pixelX, pixelY).a >= 200f / 255f;
        }

        private bool IsInInteractionRange(object owner)
        {
            if (owner == null)
                return false;
            int maximumDistance = owner is JxqyNpc npc
                ? Math.Max(0, npc.DialogRadius)
                : 1;
            return JxqyPathfinder.GetViewTileDistance(
                       _player.TilePosition,
                       GetInteractionTile(owner)) <= maximumDistance;
        }

        private static JxqyIntPoint GetInteractionTile(object owner)
        {
            return owner switch
            {
                JxqyNpc npc => npc.TilePosition,
                JxqyWorldObject worldObject => worldObject.TilePosition,
                _ => throw new InvalidOperationException(
                    "Unsupported interaction owner."),
            };
        }

        private static JxqyIntPoint ResolvePointerPrimaryDestination(
            JxqyIntPoint pointerTile,
            object interactionOwner)
        {
            return interactionOwner == null
                ? pointerTile
                : GetInteractionTile(interactionOwner);
        }

        private void TryStartPendingInteraction()
        {
            if (_pendingInteractionOwner == null ||
                (_scriptSession?.IsRunning ?? true) ||
                !IsInInteractionRange(_pendingInteractionOwner))
                return;
            _player.Stop();
            StartInteraction(
                _pendingInteractionOwner,
                _pendingInteractionScript);
        }

        private void StartInteraction(
            object owner,
            string scriptFileName)
        {
            if (owner == null ||
                string.IsNullOrWhiteSpace(scriptFileName) ||
                (_scriptSession?.IsRunning ?? true))
                return;
            _player.Stop();
            if (owner is JxqyNpc npc &&
                !IsNpcClickScript(npc, scriptFileName))
            {
                npc.BeginInteraction(
                    _player,
                    ResolveInteractionDirectionCount(npc));
            }
            else if (owner is JxqyWorldObject worldObject)
            {
                _player.SetDirection(
                    worldObject.PositionInWorld - _player.PositionInWorld);
            }
            ClearPendingInteraction();
            StartInteractionAsync(
                    owner,
                    scriptFileName,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private int ResolveInteractionDirectionCount(JxqyNpc npc)
        {
            if (npc == null ||
                !_npcVisuals.TryGetValue(
                    npc,
                    out JxqyRuntimeActorVisual visualState))
            {
                // A missing current visual cannot prove that the pose can
                // rotate. Keep it fixed instead of inventing directions.
                return 1;
            }

            bool specialActionActive =
                visualState.SpecialAction != null &&
                !visualState.SpecialAction.IsFinished;
            JxqyAnimationMetadata animation = specialActionActive
                ? visualState.SpecialAction.Metadata
                : visualState.Visual?.Animation?.Metadata;
            if (!specialActionActive &&
                npc.HasPath &&
                (npc.IsWalking || npc.IsRunning))
            {
                // BeginInteraction presents a moving NPC in its standing
                // pose, so use that pose's actual direction count.
                animation = ResolveCharacterAnimation(
                    visualState.Actions,
                    npc.IsInFighting
                        ? JxqyCharacterState.FightStand
                        : JxqyCharacterState.Stand,
                    visualState.Stand);
            }
            return Math.Max(1, animation?.DirectionCount ?? 1);
        }


        private async UniTaskVoid StartInteractionAsync(
            object owner,
            string scriptFileName,
            CancellationToken cancellationToken)
        {
            JxqyNpc interactionNpc = owner as JxqyNpc;
            if (interactionNpc != null &&
                IsNpcClickScript(interactionNpc, scriptFileName))
            {
                interactionNpc = null;
            }
            try
            {
                await _scriptSession.StartAsync(
                    scriptFileName,
                    cancellationToken,
                    belongObject: owner);
#if UNITY_EDITOR
                if (ReferenceEquals(owner, _acceptanceInteractionTarget))
                    _acceptanceInteractionStarted = true;
#endif
                // StartAsync only loads and installs the root runner. The
                // original interaction remains active until that runner has
                // actually completed, not merely until its script resource
                // has finished loading.
                await UniTask.WaitUntil(
                    () => !_scriptSession.IsRunning,
                    cancellationToken: cancellationToken);
                if (owner is JxqyNpc npc &&
                    IsNpcClickScript(npc, scriptFileName) &&
                    _npcs.Npcs.Contains(npc) &&
                    npc.IsVisible &&
                    npc.Life > 0 &&
                    !string.IsNullOrWhiteSpace(npc.ScriptAddress) &&
                    !string.Equals(
                        npc.ScriptAddress,
                        scriptFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // ClickScript is the legacy movement pre-hook used by
                    // counter NPCs. Keep the regular interaction pending so
                    // it starts only after that script has finished and the
                    // player has actually reached the NPC's dialogue range.
                    _pendingInteractionOwner = npc;
                    _pendingInteractionScript = npc.ScriptAddress;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ReturnToTitle();
            }
            finally
            {
                interactionNpc?.EndInteraction();
            }
        }

        private void OnItemScriptRequested(JxqyInventoryEntry entry)
        {
            if (entry?.Definition == null ||
                string.IsNullOrWhiteSpace(entry.Definition.UseScript) ||
                _scriptSession == null ||
                _scriptSession.IsRunning)
            {
                return;
            }
            _player?.Stop();
            StartItemScriptAsync(
                    entry,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private async UniTaskVoid StartItemScriptAsync(
            JxqyInventoryEntry entry,
            CancellationToken cancellationToken)
        {
            try
            {
                await _scriptSession.StartAsync(
                    entry.Definition.UseScript,
                    cancellationToken,
                    belongObject: entry,
                    category: JxqyScriptCategory.Good);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _uiSession?.SetNotice("物品脚本执行失败");
            }
        }

        private void ClearPendingInteraction()
        {
            _pendingInteractionOwner = null;
            _pendingInteractionScript = string.Empty;
        }

        private IJxqyTileCollisionMap CreateLiveCollisionMap()
        {
            return new JxqyRuntimeCollisionMap(
                _map,
                _objects,
                _npcs);
        }

        private void MoveStandingPartnersTo(JxqyIntPoint destination)
        {
            if (_npcs?.MoveStandingFollowersTo(destination) == true)
                ResetPartnerPositions();
        }

        private bool IsPlayerPathTileBlocked(
            JxqyCharacter character,
            JxqyIntPoint tile)
        {
            return CreateLiveCollisionMap().IsObstacleForCharacter(tile);
        }

#if UNITY_EDITOR
        public void BeginAcceptanceTrapTransition(
            string scriptFileName)
        {
            bool wudangTransition =
                (scriptFileName ?? string.Empty)
                .Replace('\\', '/')
                .IndexOf(
                    "/map_005_",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                string.Equals(
                    Path.GetFileName(scriptFileName),
                    "Trap02.txt",
                    StringComparison.OrdinalIgnoreCase);
            BeginAcceptanceTrapTransition(
                scriptFileName,
                wudangTransition
                    ? "map:map/map_004_"
                    : "map:map/map_001_",
                wudangTransition
                    ? "map004-luren.npc"
                    : "map001-luren.npc");
        }

        public void BeginAcceptanceTrapTransition(
            string scriptFileName,
            string expectedMapFileName,
            string expectedNpcFileName)
        {
            _acceptanceTrapTransitionFinished = false;
            _acceptanceTrapTransitionError = string.Empty;
            _acceptanceSuppressTraps = true;
            _scriptSession?.Cancel();
            _uiSession?.Open(JxqyUiScreen.Hud);
            RunAcceptanceTrapTransitionAsync(
                scriptFileName,
                expectedMapFileName,
                expectedNpcFileName).Forget();
        }

        private async UniTaskVoid RunAcceptanceTrapTransitionAsync(
            string scriptFileName,
            string expectedMapFileName,
            string expectedNpcFileName)
        {
            try
            {
                await _scriptSession.StartAsync(
                    scriptFileName,
                    this.GetCancellationTokenOnDestroy());
                await UniTask.WaitUntil(
                    () => !_scriptSession.IsRunning,
                    cancellationToken:
                        this.GetCancellationTokenOnDestroy());
                bool mapMatches = expectedMapFileName.StartsWith(
                        "map:",
                        StringComparison.OrdinalIgnoreCase)
                    ? ActiveMapStableId.StartsWith(
                        expectedMapFileName,
                        StringComparison.OrdinalIgnoreCase)
                    : string.Equals(
                        GetLegacyMapFileName(ActiveMapStableId),
                        expectedMapFileName,
                        StringComparison.OrdinalIgnoreCase);
                if (!mapMatches ||
                    !string.Equals(
                        _activeNpcFileName,
                        expectedNpcFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Trap transition completed without loading its target " +
                        $"map/NPC source. ActiveMap='{ActiveMapStableId}', " +
                        $"Npc='{_activeNpcFileName}', expected map=" +
                        $"'{expectedMapFileName}', NPC=" +
                        $"'{expectedNpcFileName}'.");
                }
                // Keep the real trap result isolated from the remaining
                // acceptance phases, which were authored for the initial map.
                await LoadOriginalNewGameAsync();
                SubmitFrame();
            }
            catch (Exception exception)
            {
                _acceptanceTrapTransitionError = exception.ToString();
                Debug.LogException(exception, this);
            }
            finally
            {
                _acceptanceTrapTransitionFinished = true;
            }
        }

        public void BeginAcceptanceMapSwitch(string legacyFileName)
        {
            if (_mapSwitchInProgress)
                return;
            _acceptanceMapSwitchError = string.Empty;
            RunAcceptanceMapSwitchAsync(legacyFileName).Forget();
        }

        public void BeginAcceptanceActorLoad(
            string npcFileName,
            string objectFileName)
        {
            _acceptanceActorLoadError = string.Empty;
            _acceptanceActorLoadFinished = false;
            RunAcceptanceActorLoadAsync(
                    npcFileName,
                    objectFileName)
                .Forget();
        }

        private async UniTaskVoid RunAcceptanceActorLoadAsync(
            string npcFileName,
            string objectFileName)
        {
            try
            {
                await LoadNpcsFromScriptAsync(npcFileName);
                await LoadObjectsFromScriptAsync(objectFileName);
                SubmitFrame();
            }
            catch (Exception exception)
            {
                _acceptanceActorLoadError = exception.ToString();
                Debug.LogException(exception, this);
            }
            finally
            {
                _acceptanceActorLoadFinished = true;
            }
        }

        private async UniTaskVoid RunAcceptanceMapSwitchAsync(
            string legacyFileName)
        {
            try
            {
                await SwitchMapFromScriptAsync(legacyFileName);
            }
            catch (Exception exception)
            {
                _acceptanceMapSwitchError = exception.ToString();
                Debug.LogException(exception, this);
            }
        }

        public void PrepareMovementAcceptance()
        {
            _acceptanceSuppressTraps = true;
            _scriptSession?.Cancel();
            if (_video is JxqyUnityVideoPort unityVideo)
                unityVideo.RequestSkip();
            _player?.Stop();
            _uiSession?.Open(JxqyUiScreen.Hud);
            _input.ResetTransientState();
        }

        public bool TryGetReachableAcceptancePointer(
            out JxqyFloat2 pointer)
        {
            return TryGetReachableAcceptancePointer(
                new JxqyIntPoint(-1, -1),
                new JxqyFloat2(-1, -1),
                -1,
                out pointer);
        }

        public bool TryGetReachableAcceptancePointer(
            JxqyIntPoint excludedDestination,
            JxqyFloat2 excludedPointer,
            int excludedInitialDirection,
            out JxqyFloat2 pointer)
        {
            pointer = default;
            if (_player == null || _map == null)
                return false;
            JxqyIntPoint pathStart = _player.TilePosition;
            int[] offsets = { 96, -96, 160, -160, 224, -224 };
            for (int yIndex = 0; yIndex < offsets.Length; yIndex++)
            {
                for (int xIndex = 0; xIndex < offsets.Length; xIndex++)
                {
                    int logicalX = LogicalWidth / 2 + offsets[xIndex];
                    int logicalY = LogicalHeight / 2 + offsets[yIndex];
                    if (logicalX < 0 || logicalX > LogicalWidth ||
                        logicalY < 0 || logicalY > LogicalHeight ||
                        Mathf.Approximately(
                            logicalX,
                            excludedPointer.X) &&
                        Mathf.Approximately(
                            logicalY,
                            excludedPointer.Y))
                    {
                        continue;
                    }
                    JxqyIntPoint destination =
                        JxqyIsometricMapMath.WorldPixelToTile(
                            _camera.X + logicalX,
                            _camera.Y + logicalY);
                    if (destination.Equals(excludedDestination) ||
                        destination.X < 0 ||
                        destination.X >= _map.Columns ||
                        destination.Y < 0 ||
                        destination.Y >= _map.Rows ||
                        _map.IsObstacleForCharacter(
                            destination.X,
                            destination.Y))
                    {
                        continue;
                    }
                    IReadOnlyList<JxqyFloat2> path =
                        JxqyPathfinder.FindPath(
                            CreateLiveCollisionMap(),
                            pathStart,
                            destination);
                    if (path.Count < 2)
                        continue;
                    int firstWaypointIndex = 1;
                    while (firstWaypointIndex < path.Count &&
                           path[firstWaypointIndex] ==
                           _player.PositionInWorld)
                    {
                        firstWaypointIndex++;
                    }
                    if (firstWaypointIndex >= path.Count)
                        continue;
                    int initialDirection = JxqyDirection.GetIndex(
                        path[firstWaypointIndex] -
                        _player.PositionInWorld,
                        _player.DirectionCount);
                    if (excludedInitialDirection >= 0 &&
                        initialDirection == excludedInitialDirection)
                    {
                        continue;
                    }
                    pointer = new JxqyFloat2(logicalX, logicalY);
                    return true;
                }
            }
            return false;
        }

        public bool TryGetBlockedAcceptancePointer(
            out JxqyFloat2 pointer,
            out JxqyIntPoint requestedDestination,
            out JxqyIntPoint resolvedDestination)
        {
            pointer = default;
            requestedDestination = default;
            resolvedDestination = default;
            if (_player == null || _map == null)
                return false;
            IJxqyTileCollisionMap collision = CreateLiveCollisionMap();
            for (int logicalY = 64;
                 logicalY < LogicalHeight - 64;
                 logicalY += 32)
            {
                for (int logicalX = 64;
                     logicalX < LogicalWidth - 64;
                     logicalX += 32)
                {
                    JxqyIntPoint requested =
                        JxqyIsometricMapMath.WorldPixelToTile(
                            _camera.X + logicalX,
                            _camera.Y + logicalY);
                    if (requested.X < 0 ||
                        requested.X >= _map.Columns ||
                        requested.Y < 0 ||
                        requested.Y >= _map.Rows ||
                        !_map.IsObstacleForCharacter(
                            requested.X,
                            requested.Y))
                    {
                        continue;
                    }
                    IReadOnlyList<JxqyFloat2> path =
                        JxqyPathfinder.FindPathToNearestReachable(
                            collision,
                            _player.TilePosition,
                            requested,
                            out JxqyIntPoint resolved);
                    if (path.Count < 2 ||
                        resolved.Equals(requested))
                    {
                        continue;
                    }
                    pointer = new JxqyFloat2(logicalX, logicalY);
                    requestedDestination = requested;
                    resolvedDestination = resolved;
                    return true;
                }
            }
            return false;
        }

        public byte[] CaptureAcceptanceWorldPng()
        {
            return CaptureWorldSnapshotPng();
        }

        public bool TryPrepareAcceptanceInteraction(
            out JxqyFloat2 pointer)
        {
            pointer = default;
            if (_player == null || _map == null)
                return false;
            JxqyWorldObject target = _objects.Objects.FirstOrDefault(
                item =>
                    item.IsVisible &&
                    !item.IsRemoved &&
                    string.Equals(
                        item.ScriptAddress,
                        "捡钱.txt",
                        StringComparison.OrdinalIgnoreCase));
            if (target == null)
                return false;

            IJxqyTileCollisionMap collision = CreateLiveCollisionMap();
            foreach (JxqyIntPoint neighbor in
                     JxqyPathfinder.GetAllNeighbors(
                         target.TilePosition))
            {
                if (collision.IsObstacleForCharacter(neighbor))
                    continue;
                _scriptSession?.Cancel();
                _uiSession.Open(JxqyUiScreen.Hud);
                _player.Stop();
                _player.TilePosition = neighbor;
                _acceptanceInteractionTarget = target;
                _acceptanceInteractionStarted = false;
                _acceptanceInteractionMoneyBefore = _player.Money;
                _acceptanceInteractionScript = target.ScriptAddress;
                CenterCameraOnPlayer();
                UpdatePlayerVisual();
                SubmitFrame();

                JxqyIntPoint targetWorld =
                    JxqyIsometricMapMath.TileToWorldPixel(
                        target.TilePosition.X,
                        target.TilePosition.Y);
                pointer = new JxqyFloat2(
                    targetWorld.X - _camera.X,
                    targetWorld.Y - _camera.Y);
                return pointer.X >= 0 &&
                       pointer.X <= LogicalWidth &&
                       pointer.Y >= 0 &&
                       pointer.Y <= LogicalHeight;
            }
            return false;
        }

        public bool TryPrepareAcceptanceCombat()
        {
            if (_player == null || _npcs == null)
                return false;
            JxqyNpc target = _npcs.Npcs.FirstOrDefault(
                item => item.IsVisible && !item.IsDead);
            if (target == null)
                return false;
            _scriptSession?.Cancel();
            _uiSession.Open(JxqyUiScreen.Hud);
            _player.Stop();
            _player.Evade = Math.Max(100, _player.Evade);
            _player.SetFighting(false);

            IJxqyTileCollisionMap collision = CreateLiveCollisionMap();
            foreach (JxqyIntPoint neighbor in
                     JxqyPathfinder.GetAllNeighbors(
                         target.TilePosition))
            {
                if (collision.IsObstacleForCharacter(neighbor))
                    continue;
                _player.TilePosition = neighbor;
                target.Relation = JxqyRelationType.Enemy;
                target.Kind = JxqyCharacterKind.Fighter;
                target.Invincible = false;
                target.NoDropWhenDead = false;
                target.DropIni = "可捡药品.ini[100]";
                target.Level = 1;
                if (_player.LevelUpExperience <= 0)
                    return false;
                // The original ignores ExpBonus values at or above its
                // exclusive cap.  Put the fixture at the current threshold
                // instead, then let ordinary death experience cross it.
                _player.Experience = _player.LevelUpExperience;
                target.ExpBonus = 0;
                target.Defend = 0;
                target.Evade = 0;
                target.LifeMax = Math.Max(
                    1,
                    Math.Min(10, target.LifeMax));
                target.Life = target.LifeMax;
                _acceptanceCombatTarget = target;
                _acceptanceCombatTargetLifeBefore = target.Life;
                _acceptanceCombatExperienceBefore =
                    _player.Experience;
                _acceptanceCombatLevelBefore = _player.Level;
                _acceptanceCombatLevelUpExperience =
                    checked(
                        _player.Experience +
                        JxqyExperienceRules.CalculateDeathExperience(
                            _player,
                            target,
                            _contentContext.ContentProfile
                                .DeathExperience));
                _acceptanceCombatObjectCountBefore =
                    _objects.Objects.Count;
                CenterCameraOnPlayer();
                UpdatePlayerVisual();
                SubmitFrame();
                return true;
            }
            return false;
        }

        public void BeginAcceptanceSaveLoad()
        {
            if (_acceptanceSaveLoadFinished)
                return;
            RunAcceptanceSaveLoadAsync().Forget();
        }

        private async UniTaskVoid RunAcceptanceSaveLoadAsync()
        {
            JxqySaveRepository originalRepository = _saveRepository;
            _acceptanceSaveLoadFinished = false;
            _acceptanceSaveLoadPassed = false;
            _acceptanceSaveLoadError = string.Empty;
            try
            {
                _saveRepository = new JxqySaveRepository(
                    new AcceptanceMemoryPersistencePort());
                int expectedMoney = _player.Money;
                JxqyIntPoint expectedTile = _player.TilePosition;
                int expectedNpcCount = _npcs.Npcs.Count;
                int expectedObjectCount = _objects.Objects.Count;
                int expectedDeadNpcCount =
                    _npcs.Npcs.Count(item => item.IsDead);
                int expectedOpenObjectCount =
                    _objects.Objects.Count(item => item.IsOpen);
                const string expectedVariableName =
                    "$AcceptanceSaveVariable";
                const int expectedVariableValue = 7319;
                const string expectedMemo = "存档验收备忘";
                _scriptSession.Variables.Set(
                    expectedVariableName,
                    expectedVariableValue);
                _memoEntries.Add(expectedMemo);
                _presentationEffects.SetMapColor(12, 34, 56);
                _presentationEffects.SetSpriteColor(78, 90, 123);
                _presentationEffects.MapTime = 7;
                _presentationEffects.ShowSnow(true);
                _presentationEffects.WaterEffectEnabled = true;
                OpenTimeLimit(73);
                _timerWindowVisible = false;
                SetTimeScript(12, "acceptance-timer.txt");
                int expectedDirection = _player.CurrentDirection;

                await SaveGameAsync(
                    0,
                    this.GetCancellationTokenOnDestroy());
                _player.Money = checked(_player.Money + 123);
                _player.TilePosition = new JxqyIntPoint(
                    expectedTile.X + 1,
                    expectedTile.Y);
                foreach (JxqyWorldObject worldObject in _objects.Objects)
                    worldObject.IsOpen = false;
                _player.CurrentDirection = expectedDirection + 1;
                _scriptSession.Variables.Set(
                    expectedVariableName,
                    -1);
                _memoEntries.Remove(expectedMemo);
                _presentationEffects.SetMapColor(1, 2, 3);
                _presentationEffects.SetSpriteColor(4, 5, 6);
                _presentationEffects.MapTime = 0;
                _presentationEffects.ShowSnow(false);
                _presentationEffects.WaterEffectEnabled = false;
                CloseTimeLimit();
                await LoadGameAsync(
                    0,
                    this.GetCancellationTokenOnDestroy());

                _acceptanceSaveLoadPassed =
                    _player.Money == expectedMoney &&
                    _player.TilePosition.Equals(expectedTile) &&
                    _npcs.Npcs.Count == expectedNpcCount &&
                    _objects.Objects.Count == expectedObjectCount &&
                    _npcs.Npcs.Count(item => item.IsDead) ==
                        expectedDeadNpcCount &&
                    _objects.Objects.Count(item => item.IsOpen) ==
                        expectedOpenObjectCount &&
                    _player.CurrentDirection == expectedDirection &&
                    _scriptSession.Variables.Get(
                        expectedVariableName) ==
                        expectedVariableValue &&
                    _memoEntries.Contains(expectedMemo) &&
                    _presentationEffects.MapBaseColor ==
                        new JxqyColor32(12, 34, 56) &&
                    _presentationEffects.SpriteBaseColor ==
                        new JxqyColor32(78, 90, 123) &&
                    _presentationEffects.MapTime == 7 &&
                    _presentationEffects.IsSnowing &&
                    _presentationEffects.WaterEffectEnabled &&
                    _timeLimitRemainingSeconds > 0 &&
                    !_timerWindowVisible &&
                    string.Equals(
                        _timeScriptFileName,
                        "acceptance-timer.txt",
                        StringComparison.Ordinal) &&
                    !_timeScriptFired;
                if (!_acceptanceSaveLoadPassed)
                {
                    _acceptanceSaveLoadError =
                        "Restored state differed: " +
                        $"money={_player.Money}/{expectedMoney}, " +
                        $"tile={_player.TilePosition.X}," +
                        $"{_player.TilePosition.Y}/" +
                        $"{expectedTile.X},{expectedTile.Y}, " +
                        $"npcs={_npcs.Npcs.Count}/" +
                        $"{expectedNpcCount}, objects=" +
                        $"{_objects.Objects.Count}/" +
                        $"{expectedObjectCount}, dead=" +
                        $"{_npcs.Npcs.Count(item => item.IsDead)}/" +
                        $"{expectedDeadNpcCount}, open=" +
                        $"{_objects.Objects.Count(item => item.IsOpen)}/" +
                        $"{expectedOpenObjectCount}, variable=" +
                        $"{_scriptSession.Variables.Get(expectedVariableName)}/" +
                        $"{expectedVariableValue}, mapTime=" +
                        $"{_presentationEffects.MapTime}/7, timer=" +
                        $"{_timeLimitRemainingSeconds}.";
                }
            }
            catch (Exception exception)
            {
                _acceptanceSaveLoadError = exception.ToString();
                Debug.LogException(exception, this);
            }
            finally
            {
                // The synthetic save payload deliberately contains a timer
                // script name so the round trip can verify that field.  It
                // must not remain armed after the probe: later acceptance
                // map switches would resolve that synthetic relative name
                // against an unrelated real map script directory.
                CloseTimeLimit();
                _saveRepository = originalRepository;
                _acceptanceSaveLoadFinished = true;
            }
        }

        public void BeginAcceptanceDaoJianCommandProbe()
        {
            if (_acceptanceDaoJianCommandProbeFinished)
                return;
            RunAcceptanceDaoJianCommandProbeAsync().Forget();
        }

        private async UniTaskVoid RunAcceptanceDaoJianCommandProbeAsync()
        {
            _acceptanceDaoJianCommandProbeFinished = false;
            _acceptanceDaoJianCommandProbePassed = false;
            _acceptanceDaoJianCommandProbeError = string.Empty;
            try
            {
                if (!JxqyLegacyScriptCommands.IsDaoJian543Dialect(
                        _contentContext.ScriptDialectId))
                {
                    throw new InvalidOperationException(
                        "DaoJian command probe cannot run in the formal " +
                        "XinJianXia dialect.");
                }
                if (_scriptSession == null || _scriptSession.IsRunning)
                {
                    throw new InvalidOperationException(
                        "DaoJian command probe requires an idle script " +
                        "session after the opening route.");
                }

                ValidateEnableMapScrollCommand();
                ValidateSetNpcScnCommand();
                await ValidateNpcRunToCommandAsync();
                await ValidateChangeNpcResCommandAsync();
                await ValidateExtendedActorCommandsAsync();
                await ValidateShowMiniGameCommandAsync();
                _acceptanceDaoJianCommandProbePassed = true;
            }
            catch (Exception exception)
            {
                _acceptanceDaoJianCommandProbeError = exception.ToString();
                Debug.LogException(exception, this);
            }
            finally
            {
                _acceptanceDaoJianCommandProbeFinished = true;
            }
        }

        private void ValidateEnableMapScrollCommand()
        {
            JxqyFloat2 position =
                ResolvePlayerKindCharacter().PositionInWorld;
            JxqyIntRect expected = JxqyIsometricMapMath.ClampCamera(
                Mathf.RoundToInt(position.X) - LogicalWidth / 2,
                Mathf.RoundToInt(position.Y) - LogicalHeight / 2,
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            _camera = new JxqyIntRect(
                expected.X + 17,
                expected.Y + 19,
                expected.Width,
                expected.Height);
            JxqyScriptStep step = ExecuteAcceptanceCommand(
                "EnableMapScroll",
                Array.Empty<string>());
            if (step.Kind != JxqyScriptStepKind.Continue ||
                _camera.X != expected.X ||
                _camera.Y != expected.Y)
            {
                throw new InvalidOperationException(
                    "EnableMapScroll did not synchronously restore the " +
                    "player-centered camera position.");
            }
        }

        private void ValidateSetNpcScnCommand()
        {
            JxqyNpc target = _npcs.Npcs.FirstOrDefault(npc =>
                npc != null && !string.IsNullOrWhiteSpace(npc.Name));
            if (target == null)
                return;

            JxqyFloat2 position = target.PositionInWorld;
            JxqyIntRect expected = JxqyIsometricMapMath.ClampCamera(
                Mathf.RoundToInt(position.X) - LogicalWidth / 2,
                Mathf.RoundToInt(position.Y) - LogicalHeight / 2,
                LogicalWidth,
                LogicalHeight,
                _mapMetadata.MapPixelWidth,
                _mapMetadata.MapPixelHeight);
            _camera = new JxqyIntRect(
                expected.X + 23,
                expected.Y + 29,
                expected.Width,
                expected.Height);
            JxqyScriptStep step = ExecuteAcceptanceCommand(
                "SetNpcScn",
                new[] { Quote(target.Name) });
            if (step.Kind != JxqyScriptStepKind.Continue ||
                _camera.X != expected.X ||
                _camera.Y != expected.Y)
            {
                throw new InvalidOperationException(
                    "SetNpcScn did not synchronously center the camera on " +
                    $"NPC '{target.Name}'.");
            }

            JxqyIntRect beforeMissingNpc = _camera;
            step = ExecuteAcceptanceCommand(
                "SetNpcScn",
                new[] { Quote("__jxqy_acceptance_missing_npc__") });
            if (step.Kind != JxqyScriptStepKind.Continue ||
                _camera.X != beforeMissingNpc.X ||
                _camera.Y != beforeMissingNpc.Y)
            {
                throw new InvalidOperationException(
                    "SetNpcScn did not preserve the camera for a missing NPC.");
            }

            CenterCameraOnPlayer();
        }

        private async UniTask ValidateNpcRunToCommandAsync()
        {
            IJxqyTileCollisionMap collision = CreateLiveCollisionMap();
            JxqyNpc target = null;
            JxqyIntPoint destination = default;
            foreach (JxqyNpc candidate in _npcs.Npcs)
            {
                if (candidate == null || candidate.IsDead ||
                    !candidate.IsVisible)
                    continue;
                foreach (JxqyIntPoint tile in JxqyPathfinder
                             .GetAllNeighbors(candidate.TilePosition))
                {
                    if (tile.X < 0 || tile.X >= collision.Columns ||
                        tile.Y < 0 || tile.Y >= collision.Rows ||
                        collision.IsObstacleForCharacter(tile) ||
                        _player.TilePosition.Equals(tile) ||
                        _npcs.Npcs.Any(npc =>
                            !ReferenceEquals(npc, candidate) &&
                            npc.TilePosition.Equals(tile)))
                    {
                        continue;
                    }
                    target = candidate;
                    destination = tile;
                    break;
                }
                if (target != null)
                    break;
            }
            if (target == null)
                return;

            JxqyIntPoint start = target.TilePosition;

            bool previousAiDisabled = _npcs.IsAiDisabled;
            _npcs.IsAiDisabled = true;
            try
            {
                JxqyScriptStep step = ExecuteAcceptanceCommand(
                    "NpcRunTo",
                    new[]
                    {
                        Quote(target.Name),
                        destination.X.ToString(CultureInfo.InvariantCulture),
                        destination.Y.ToString(CultureInfo.InvariantCulture),
                    });
                if (step.Kind != JxqyScriptStepKind.Wait ||
                    step.Wait == null)
                {
                    throw new InvalidOperationException(
                        "NpcRunTo did not return a blocking movement wait.");
                }
                bool complete = step.Wait.Tick(0);
                bool observedRun = target.IsRunning;
                double deadline = Time.realtimeSinceStartupAsDouble + 10d;
                while (!complete &&
                       Time.realtimeSinceStartupAsDouble < deadline)
                {
                    await UniTask.Yield(
                        PlayerLoopTiming.LastUpdate,
                        this.GetCancellationTokenOnDestroy());
                    observedRun |= target.IsRunning;
                    complete = step.Wait.Tick(
                        Math.Max(0, Time.unscaledDeltaTime * 1000d));
                }
                if (!complete || !observedRun ||
                    !target.TilePosition.Equals(destination) ||
                    !target.IsStanding)
                {
                    throw new InvalidOperationException(
                        $"NpcRunTo run/wait semantics differed: " +
                        $"complete={complete}, observedRun={observedRun}, " +
                        $"tile={target.TilePosition}, expected={destination}, " +
                        $"standing={target.IsStanding}.");
                }
            }
            finally
            {
                _npcs.IsAiDisabled = previousAiDisabled;
            }
        }

        private async UniTask ValidateChangeNpcResCommandAsync()
        {
            string resourceFileName = _playerResourceFileName;
            if (!HasDynamicText("ini/npcres", resourceFileName))
                return;
            string resourceText = await LoadDynamicTextAsync(
                "ini/npcres",
                resourceFileName,
                this.GetCancellationTokenOnDestroy());
            Dictionary<string, Dictionary<string, string>> sections =
                JxqyLegacySaveImporter.ParseIni(resourceText);
            string expectedStandFileName = Path.GetFileName(
                GetStateImage(sections, "Stand"));
            if (string.IsNullOrWhiteSpace(expectedStandFileName))
            {
                throw new InvalidOperationException(
                    $"ChangeNpcRes probe resource '{resourceFileName}' has " +
                    "no Stand animation.");
            }

            await CompleteAcceptanceCommandAsync(
                ExecuteAcceptanceCommand(
                    "ChangeNpcRes",
                    new[]
                    {
                        Quote(_player.Name),
                        Quote(resourceFileName),
                    }),
                "ChangeNpcRes current player resource");
            string actualStand =
                _playerStand?.Metadata?.SourceStableId ?? string.Empty;
            if (actualStand.IndexOf(
                    expectedStandFileName,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "ChangeNpcRes did not reload the current player resource: " +
                    $"actual={actualStand}, expected={expectedStandFileName}.");
            }
        }

        private async UniTask ValidateShowMiniGameCommandAsync()
        {
            const string resultVariable = "$AcceptanceGambleState";
            JxqyScriptStep step = ExecuteAcceptanceCommand(
                "ShowMiniGame",
                new[]
                {
                    Quote("gamble"),
                    "100",
                    "2",
                    Quote("赌场老板"),
                    resultVariable,
                });
            if (step.Kind != JxqyScriptStepKind.Wait ||
                step.Wait == null ||
                _uiSession.DaoJianGamble == null)
            {
                throw new InvalidOperationException(
                    "ShowMiniGame did not open the DaoJian gamble session " +
                    "and return a blocking wait.");
            }
            if (!_uiSession.RequestDaoJianGambleQuit())
            {
                throw new InvalidOperationException(
                    "ShowMiniGame gamble could not quit from its initial " +
                    "bet-selection phase.");
            }
            await CompleteAcceptanceCommandAsync(
                step,
                "ShowMiniGame quit");
            if (_uiSession.DaoJianGamble != null ||
                _scriptSession.Variables.Get(resultVariable) != 0)
            {
                throw new InvalidOperationException(
                    "ShowMiniGame did not close cleanly with GambleState=0.");
            }
        }

        private async UniTask ValidateExtendedActorCommandsAsync()
        {
            JxqyNpc target = _npcs.Npcs.FirstOrDefault(npc =>
                npc != null && !npc.IsDead &&
                !string.IsNullOrWhiteSpace(npc.Name));
            if (target == null)
                return;

            int originalDirection = target.CurrentDirection;
            int probeDirection = (originalDirection + 3) %
                                 Math.Max(1, target.DirectionCount);
            JxqyScriptStep step = ExecuteAcceptanceCommand(
                "SetPlayerDir",
                new[] { Quote(target.Name), probeDirection.ToString() });
            if (step.Kind != JxqyScriptStepKind.Continue ||
                target.CurrentDirection != probeDirection)
            {
                throw new InvalidOperationException(
                    "Named SetPlayerDir overload did not update its actor.");
            }

            step = ExecuteAcceptanceCommand(
                "SetNpcDir",
                new[] { Quote(target.Name), originalDirection.ToString(), "0" });
            if (step.Kind != JxqyScriptStepKind.Continue ||
                target.CurrentDirection != originalDirection)
            {
                throw new InvalidOperationException(
                    "SetNpcDir did not accept its reserved trailing value.");
            }

            JxqyIntPoint originalTile = target.TilePosition;
            step = ExecuteAcceptanceCommand(
                "SetNpcPos",
                new[]
                {
                    Quote(target.Name),
                    originalTile.X.ToString(CultureInfo.InvariantCulture),
                    originalTile.Y.ToString(CultureInfo.InvariantCulture),
                    probeDirection.ToString(CultureInfo.InvariantCulture),
                });
            if (step.Kind != JxqyScriptStepKind.Continue ||
                !target.TilePosition.Equals(originalTile) ||
                target.CurrentDirection != probeDirection)
            {
                throw new InvalidOperationException(
                    "Four-parameter SetNpcPos did not preserve position " +
                    "and apply direction.");
            }
            target.CurrentDirection = originalDirection;
            RefreshActorVisual(target);

            bool originallyFighting = target.IsInFighting;
            step = ExecuteAcceptanceCommand(
                "SetPlayerState",
                new[] { Quote(target.Name), originallyFighting ? "0" : "1" });
            if (step.Kind != JxqyScriptStepKind.Continue ||
                target.IsInFighting == originallyFighting)
            {
                throw new InvalidOperationException(
                    "Named SetPlayerState overload did not update its actor.");
            }
            target.SetFighting(originallyFighting);
            RefreshActorVisual(target);

            JxqySkillEntry skill = _skills.Skills.FirstOrDefault(item =>
                item?.Magic != null &&
                !string.IsNullOrWhiteSpace(item.Magic.Id));
            if (skill == null)
                return;

            const int expectedLevel = 2;
            const int expectedDistance = 5;
            await CompleteAcceptanceCommandAsync(
                ExecuteAcceptanceCommand(
                    "SetNpcMagicLevel",
                    new[]
                    {
                        Quote(target.Name),
                        Quote(skill.Magic.Id),
                        expectedLevel.ToString(CultureInfo.InvariantCulture),
                        expectedDistance.ToString(
                            CultureInfo.InvariantCulture),
                    }),
                "SetNpcMagicLevel four-parameter overload");
            JxqySkillEntry npcSkill = target.Skills?.Find(skill.Magic.Id);
            JxqyRangedMagicReference ranged =
                target.AdditionalBasicMagics.FirstOrDefault(reference =>
                    string.Equals(
                        reference.Magic?.Id,
                        skill.Magic.Id,
                        StringComparison.OrdinalIgnoreCase));
            if (npcSkill == null || npcSkill.Level != expectedLevel ||
                ranged == null || ranged.Distance != expectedDistance)
            {
                throw new InvalidOperationException(
                    "SetNpcMagicLevel did not apply the DaoJian level and " +
                    "use-distance semantics.");
            }
        }

        private async UniTask CompleteAcceptanceCommandAsync(
            JxqyScriptStep step,
            string description)
        {
            if (step.Kind == JxqyScriptStepKind.Continue)
                return;
            if (step.Kind != JxqyScriptStepKind.Wait || step.Wait == null)
            {
                throw new InvalidOperationException(
                    description + " returned " + step.Kind + ".");
            }
            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
            while (!step.Wait.Tick(0))
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException(description + " timed out.");
                await UniTask.Yield(
                    PlayerLoopTiming.LastUpdate,
                    this.GetCancellationTokenOnDestroy());
            }
        }

        private JxqyScriptStep ExecuteAcceptanceCommand(
            string name,
            IReadOnlyList<string> parameters)
        {
            return _scriptSession.Execute(
                new JxqyScriptContext(),
                new JxqyScriptInstruction(
                    JxqyScriptInstructionKind.Command,
                    name,
                    parameters,
                    string.Empty,
                    1,
                    name));
        }

        private static string Quote(string value) =>
            "\"" + (value ?? string.Empty).Replace("\"", string.Empty) +
            "\"";

        private sealed class AcceptanceMemoryPersistencePort :
            IJxqyPersistencePort
        {
            private readonly Dictionary<string, byte[]> _files =
                new(StringComparer.OrdinalIgnoreCase);

            public UniTask<byte[]> ReadAsync(
                string relativePath,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_files.TryGetValue(
                        relativePath,
                        out byte[] bytes))
                    throw new FileNotFoundException(relativePath);
                return UniTask.FromResult((byte[])bytes.Clone());
            }

            public UniTask WriteAtomicAsync(
                string relativePath,
                byte[] bytes,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _files[relativePath] = (byte[])bytes.Clone();
                return UniTask.CompletedTask;
            }

            public UniTask DeleteAsync(
                string relativePath,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _files.Remove(relativePath);
                return UniTask.CompletedTask;
            }

            public bool Exists(string relativePath)
            {
                return _files.ContainsKey(relativePath);
            }
        }

        public bool PrepareAcceptanceItemSystems()
        {
            if (_player == null || _uiSession == null)
                return false;

            _inventory = new JxqyInventory();
            _equipment = new JxqyEquipmentManager();
            _shop = new JxqyShop
            {
                BuyPercentage = 100,
                RecyclePercentage = 100,
                CanSellPlayerGoods = true,
            };
            _uiSession.Inventory = _inventory;
            _uiSession.Equipment = _equipment;
            _uiSession.Shop = _shop;

            _player.LifeMax = Math.Max(100, _player.LifeMax);
            _player.Life = Math.Max(1, _player.LifeMax - 40);
            _player.Money = Math.Max(100, _player.Money);

            var drug = new JxqyItemDefinition
            {
                Id = AcceptanceDrugId,
                Name = "验收金创药",
                Introduction = "恢复生命，用于验证背包按钮与物品效果。",
                Kind = JxqyItemKind.Drug,
                Life = 25,
                ExplicitCost = 12,
            };
            var equipment = new JxqyItemDefinition
            {
                Id = AcceptanceEquipmentId,
                Name = "验收短剑",
                Introduction = "用于验证装备按钮与属性生效。",
                Kind = JxqyItemKind.Equipment,
                Slot = JxqyEquipmentSlot.Hand,
                ExplicitCost = 30,
            };
            equipment.Modifiers.Attack = 6;
            var shopItem = new JxqyItemDefinition
            {
                Id = AcceptanceShopItemId,
                Name = "验收行军丸",
                Introduction = "用于验证商店买入与卖出。",
                Kind = JxqyItemKind.Drug,
                Life = 5,
                ExplicitCost = 20,
                ExplicitSellPrice = 10,
            };

            if (!_inventory.Add(drug) ||
                !_inventory.Add(equipment))
            {
                return false;
            }
            _shop.AddStock(shopItem, 2);
            _acceptanceItemLifeBefore = _player.Life;
            _acceptanceEquipmentAttackBefore = _player.Attack;
            _acceptanceShopMoneyBefore = _player.Money;
            _acceptanceShopInventoryBefore =
                _inventory.Count(AcceptanceShopItemId);
            _uiSession.Refresh();
            return true;
        }

        public bool BeginAcceptancePresentation()
        {
            if (_presentationEffects == null ||
                PresentationCommands == null ||
                _uiSession == null)
            {
                return false;
            }

            _uiSession.Open(JxqyUiScreen.Hud);
            CenterCameraOnPlayer();
            _presentationEffects.SetCameraAnchor(
                new JxqyFloat2(_camera.X, _camera.Y));
            _acceptancePresentationCameraBefore =
                _presentationEffects.CameraPosition;
            var context = new JxqyScriptContext();
            PresentationCommands.Execute(
                context,
                AcceptanceCommand(
                    "ChangeMapColor",
                    "120",
                    "130",
                    "140"));
            PresentationCommands.Execute(
                context,
                AcceptanceCommand(
                    "ChangeAsfColor",
                    "150",
                    "160",
                    "170"));
            bool colorsApplied =
                _presentationEffects.MapColor ==
                new JxqyColor32(120, 130, 140) &&
                _presentationEffects.SpriteColor ==
                new JxqyColor32(150, 160, 170);
            PresentationCommands.Execute(
                context,
                AcceptanceCommand("OpenWaterEffect"));
            PresentationCommands.Execute(
                context,
                AcceptanceCommand("SetMapTime", "7"));
            PresentationCommands.Execute(
                context,
                AcceptanceCommand(
                    "BeginRain",
                    "Rain.ini"));
            PresentationCommands.Execute(
                context,
                AcceptanceCommand("ShowSnow", "1"));
            PresentationCommands.Execute(
                context,
                AcceptanceCommand("FadeOut"));
            JxqyScriptStep cameraStep =
                PresentationCommands.Execute(
                    context,
                    AcceptanceCommand(
                        "MoveScreen",
                        "6",
                        "90",
                        "4"));
            _acceptancePresentationCommandsAccepted =
                colorsApplied &&
                cameraStep.Kind == JxqyScriptStepKind.Wait;
            return _acceptancePresentationCommandsAccepted;
        }

        public bool PrepareAcceptanceKeyboardInput()
        {
            if (_player == null ||
                _map == null ||
                _inventory == null ||
                _uiSession == null)
            {
                return false;
            }

            _acceptanceSuppressTraps = true;
            _scriptSession?.Cancel();
            _legacyInputDisabled = false;
            _player.Stop();
            _uiSession.Open(JxqyUiScreen.Hud);
            _presentationEffects.EndRain();
            _presentationEffects.ShowSnow(false);
            _presentationEffects.ReleaseCamera();
            var collision = CreateLiveCollisionMap();
            bool placed = false;
            for (int row = 2; row < _map.Rows - 2 && !placed; row++)
            {
                for (int column = 2;
                    column < _map.Columns - 2;
                    column++)
                {
                    var tile = new JxqyIntPoint(column, row);
                    var right = new JxqyIntPoint(column + 1, row);
                    if (collision.IsObstacleForCharacter(tile) ||
                        collision.IsObstacleForCharacter(right))
                    {
                        continue;
                    }
                    _player.TilePosition = tile;
                    _player.CurrentDirection = 6;
                    placed = true;
                    break;
                }
            }
            if (!placed)
                return false;

            var item = new JxqyItemDefinition
            {
                Id = AcceptanceHotkeyItemId,
                Name = "验收快捷药",
                Introduction = "用于验证桌面快捷键物品输入。",
                Kind = JxqyItemKind.Drug,
                Life = 20,
                ExplicitCost = 10,
            };
            _inventory.Remove(
                AcceptanceHotkeyItemId,
                Math.Max(
                    1,
                    _inventory.Count(AcceptanceHotkeyItemId)));
            if (!_inventory.Add(item, legacyListIndex: 221))
                return false;
            _player.LifeMax = Math.Max(100, _player.LifeMax);
            _player.Life = Math.Max(1, _player.LifeMax - 30);
            _acceptanceHotkeyItemLifeBefore = _player.Life;
            CenterCameraOnPlayer();
            UpdatePlayerVisual();
            _uiSession.Refresh();
            _input.ResetTransientState();
            return true;
        }

        public bool PrepareAcceptanceCrowdCombat(int enemyCount)
        {
            if (!_ready || enemyCount <= 0 || enemyCount > 64 ||
                _player == null || _npcs == null ||
                _playerStand?.Metadata == null)
            {
                return false;
            }

            _acceptanceSuppressTraps = true;
            _scriptSession?.Cancel();
            if (_video is JxqyUnityVideoPort unityVideo)
                unityVideo.RequestSkip();
            _uiSession?.Open(JxqyUiScreen.Hud);
            _gameStarted = true;
            _legacyInputDisabled = false;
            _player.Stop();
            _player.IsVisible = true;
            _player.Invincible = true;
            _player.LifeMax = Math.Max(_player.LifeMax, 100000);
            _player.Life = _player.LifeMax;
            _player.Evade = Math.Max(_player.Evade, 100000);
            ClearNpcActors();
            _npcs.IsAiDisabled = false;

            var open = new Queue<JxqyIntPoint>();
            var visited = new HashSet<JxqyIntPoint>();
            JxqyIntPoint origin = _player.TilePosition;
            open.Enqueue(origin);
            visited.Add(origin);
            int created = 0;
            while (open.Count > 0 && created < enemyCount)
            {
                JxqyIntPoint tile = open.Dequeue();
                foreach (JxqyIntPoint neighbor in
                         JxqyPathfinder.GetAllNeighbors(tile))
                {
                    if (!visited.Add(neighbor) ||
                        neighbor.X < 0 ||
                        neighbor.X >= _map.Columns ||
                        neighbor.Y < 0 ||
                        neighbor.Y >= _map.Rows ||
                        _map.IsObstacleForCharacter(
                            neighbor.X,
                            neighbor.Y) ||
                        _objects.IsObstacle(neighbor))
                    {
                        continue;
                    }
                    open.Enqueue(neighbor);
                    if (JxqyPathfinder.GetViewTileDistance(
                            origin,
                            neighbor) < 3)
                    {
                        continue;
                    }

                    var npc = new JxqyNpc
                    {
                        Name = $"acceptance-enemy-{created:D2}",
                        Kind = JxqyCharacterKind.Fighter,
                        Relation = JxqyRelationType.Enemy,
                        Group = 1000 + created,
                        LifeMax = 100000,
                        Attack = 1,
                        Defend = 1,
                        Evade = 0,
                        VisionRadius = 100,
                        AttackRadius = 1,
                        NoDropWhenDead = true,
                        TilePosition = neighbor,
                        CurrentDirection = created % 8,
                    };
                    npc.Life = npc.LifeMax;
                    _npcs.Add(npc);
                    JxqyAnimationMetadata metadata =
                        _playerStand.Metadata;
                    var visualState = new JxqyRuntimeActorVisual
                    {
                        Visual = new JxqyWorldVisual
                        {
                            Id = $"acceptance-crowd:{created:D2}",
                            Kind = JxqyWorldVisualKind.Npc,
                            Animation =
                                new JxqyAnimationPlayer(metadata),
                        },
                        Stand = metadata,
                        Walk = metadata,
                        Current = metadata,
                    };
                    visualState.Visual.Animation.SetDirection(
                        npc.CurrentDirection);
                    _npcVisuals.Add(npc, visualState);
                    _frameVisuals.Add(visualState.Visual);
                    RefreshActorVisual(npc);
                    created++;
                    if (created >= enemyCount)
                        break;
                }
            }
            UpdatePlayerVisual();
            CenterCameraOnPlayer();
            SubmitFrame();
            return created == enemyCount;
        }

        public bool TryPrepareAcceptanceOcclusionProbe(
            out JxqyIntPoint playerTile,
            out int occluderScore)
        {
            playerTile = new JxqyIntPoint(-1, -1);
            occluderScore = 0;
            if (!_ready || _map == null || _player == null)
                return false;

            int bestScore = 0;
            JxqyIntPoint best = playerTile;
            for (int row = 4; row < _map.Rows - 20; row++)
            {
                for (int column = 4;
                     column < _map.Columns - 4;
                     column++)
                {
                    var candidate = new JxqyIntPoint(column, row);
                    if (_map.IsObstacleForCharacter(column, row) ||
                        _objects.IsObstacle(candidate) ||
                        _npcs.IsObstacle(candidate))
                    {
                        continue;
                    }

                    int score = 0;
                    for (int y = row + 1;
                         y <= Math.Min(_map.Rows - 1, row + 20);
                         y++)
                    {
                        for (int x = column - 4;
                             x <= column + 4;
                             x++)
                        {
                            JxqyRuntimeMapTile tile = _map.GetTile(x, y);
                            if (tile.GetMpc(1) != 0)
                            {
                                score += y <= row + 4 &&
                                         Math.Abs(x - column) <= 1
                                    ? 20
                                    : 3;
                            }
                            if (tile.GetMpc(2) != 0)
                                score += 2;
                        }
                    }
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = candidate;
                }
            }
            if (bestScore <= 0)
                return false;

            _acceptanceSuppressTraps = true;
            _scriptSession?.Cancel();
            _uiSession?.Open(JxqyUiScreen.Hud);
            _gameStarted = true;
            _player.Stop();
            _player.IsVisible = true;
            _player.TilePosition = best;
            _npcs.IsAiDisabled = true;
            UpdatePlayerVisual();
            CenterCameraOnPlayer();
            SubmitFrame();
            playerTile = best;
            occluderScore = bestScore;
            return true;
        }

        private static JxqyScriptInstruction AcceptanceCommand(
            string name,
            params string[] parameters)
        {
            return new JxqyScriptInstruction(
                JxqyScriptInstructionKind.Command,
                name,
                parameters,
                string.Empty,
                1,
                name);
        }

        public bool TryPrepareAcceptanceMagic()
        {
            if (_player == null || _skills == null)
                return false;
            _scriptSession?.Cancel();
            _uiSession?.Open(JxqyUiScreen.Hud);
            _gameStarted = true;
            _player.Stop();
            _npcs.IsAiDisabled = true;
            JxqyNpc target = _npcs.Npcs.FirstOrDefault(
                item => item.IsVisible && !item.IsDead);
            if (target == null)
                return false;
            JxqyMagicDefinition magic = _skills.Skills
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Magic?.Id,
                        "player-magic-长剑.ini",
                        StringComparison.OrdinalIgnoreCase))
                ?.Magic;
            if (magic == null)
                return false;
            foreach (string id in _skills.Skills
                         .Select(item => item.Magic.Id)
                         .ToArray())
                _skills.Forget(id);
            if (!_skills.Learn(magic, legacyListIndex: 40))
                return false;
            _player.ManaMax = Math.Max(20, _player.ManaMax);
            _player.Mana = _player.ManaMax;
            _player.Evade = Math.Max(100, _player.Evade);
            target.Relation = JxqyRelationType.Enemy;
            target.Invincible = false;
            target.Evade = 0;
            target.Defend = 0;
            target.LifeMax = Math.Max(2000, target.LifeMax);
            target.Life = target.LifeMax;
            target.PositionInWorld = new JxqyFloat2(
                _camera.X + LogicalWidth * 0.5f,
                _camera.Y + LogicalHeight * 0.5f);
            if (!_player.BeginPath(
                    new[]
                    {
                        _player.PositionInWorld,
                        _player.PositionInWorld +
                        new JxqyFloat2(4096, 0),
                    }))
            {
                return false;
            }
            RefreshActorVisual(target);
            SubmitFrame();
            _acceptanceMagicTarget = target;
            _acceptanceMagicTargetLifeBefore = target.Life;
            _acceptanceMagicManaBefore = _player.Mana;
            _acceptanceMagicResolveCount = 0;
            return true;
        }

        public bool TryGetAcceptanceMagicPointer(
            out JxqyFloat2 pointer)
        {
            pointer = default;
            if (_acceptanceMagicTarget == null ||
                !_npcVisuals.TryGetValue(
                    _acceptanceMagicTarget,
                    out JxqyRuntimeActorVisual visual) ||
                visual.Visual?.Animation == null)
            {
                return false;
            }
            JxqyAnimationPose pose = visual.Visual.Animation.GetPose();
            pointer = new JxqyFloat2(
                visual.Visual.WorldPosition.x - pose.AnchorX +
                pose.Width * 0.5f - _camera.X,
                visual.Visual.WorldPosition.y - pose.AnchorY +
                pose.Height * 0.5f - _camera.Y);
            return pointer.X >= 0f &&
                   pointer.X <= LogicalWidth &&
                   pointer.Y >= 0f &&
                   pointer.Y <= LogicalHeight;
        }

#endif

        private bool IsPlayerJumpTakeoffFrame()
        {
            if (!_player.IsJumping)
                return false;
            if (_playerVisualState != _player.State ||
                _playerVisualStateVersion != _player.StateVersion)
                return true;
            return _playerVisual?.Animation == null ||
                   _playerVisual.Animation.FrameWithinDirection == 0;
        }

        private void UpdatePlayerVisual()
        {
            _playerVisual.IsVisible = _player.IsVisible;
            JxqyIntPoint tile = _player.TilePosition;
            _playerVisual.TileColumn = tile.X;
            _playerVisual.TileRow = tile.Y;
            _playerVisual.WorldPosition = new Vector2(
                _player.PositionInWorld.X,
                _player.PositionInWorld.Y);
            if (_presentationEffects != null)
            {
                ApplyCharacterStatusPresentation(
                    _player,
                    _playerVisual);
            }
        }

        private void ApplyCharacterStatusPresentation(
            JxqyCharacter character,
            JxqyWorldVisual visual)
        {
            if (character == null || visual == null)
                return;
            Color color = _presentationEffects == null
                ? Color.white
                : JxqyPresentationDrawCommandBuilder.ToUnityColor(
                    _presentationEffects.SpriteColor);
            visual.Color = JxqyCharacterStatusPresentation.ResolveColor(
                character,
                color);
            visual.MaterialKey =
                JxqyCharacterStatusPresentation.ResolveMaterialKey(
                    character);
        }

        private bool TryGetStatusDeathAnimation(
            JxqyCharacter character,
            out JxqyAnimationMetadata animation)
        {
            animation = null;
            if (character == null || !character.IsDead)
                return false;
            if (character.HasStatus(JxqyStatusKind.Frozen) &&
                character.IsFrozenVisualEffect)
            {
                return _statusDeathAnimations.TryGetValue(
                    JxqyStatusKind.Frozen,
                    out animation);
            }
            if (character.HasStatus(JxqyStatusKind.Poisoned) &&
                character.IsPoisonVisualEffect)
            {
                return _statusDeathAnimations.TryGetValue(
                    JxqyStatusKind.Poisoned,
                    out animation);
            }
            if (character.HasStatus(JxqyStatusKind.Petrified) &&
                character.IsPetrifiedVisualEffect)
            {
                return _statusDeathAnimations.TryGetValue(
                    JxqyStatusKind.Petrified,
                    out animation);
            }
            return false;
        }

        private bool TryGetPlayerStatusDeathAnimation(
            JxqyCharacter character,
            out JxqyAnimationPlayer animation)
        {
            animation = null;
            if (!TryGetStatusDeathAnimation(
                    character,
                    out JxqyAnimationMetadata metadata))
            {
                return false;
            }
            foreach (KeyValuePair<
                         JxqyStatusKind,
                         JxqyAnimationMetadata> entry in
                     _statusDeathAnimations)
            {
                if (!ReferenceEquals(entry.Value, metadata))
                    continue;
                return _playerStatusDeathPlayers.TryGetValue(
                    entry.Key,
                    out animation);
            }
            return false;
        }

        private bool HasStatusDeathVisual(JxqyCharacter character)
        {
            return JxqyCharacterStatusPresentation.HasSpecialDeathVisual(
                       character) &&
                   TryGetStatusDeathAnimation(character, out _);
        }

        private bool TryGetPlayerStateAction(
            JxqyCharacterState state,
            out JxqyAnimationPlayer animation)
        {
            if (_playerStateActions.TryGetValue(
                    (int)state,
                    out animation))
            {
                return true;
            }
            return state == JxqyCharacterState.FightJump &&
                   _playerStateActions.TryGetValue(
                       (int)JxqyCharacterState.Jump,
                       out animation);
        }

        private void SubmitFrame()
        {
            if (_uiSession != null &&
                _uiSession.CurrentScreen == JxqyUiScreen.Title)
            {
                _renderer?.Submit(Array.Empty<JxqyDrawCommand>());
#if UNITY_EDITOR
                _acceptanceManagedBytesLastFrameBuild = 0;
                _acceptanceManagedBytesLastFrameSubmit = 0;
#endif
                return;
            }

#if UNITY_EDITOR
            long frameBuildAllocatedBytes =
                GC.GetAllocatedBytesForCurrentThread();
#endif
            using (FrameBuildMarker.Auto())
            {
                PrepareCharacterOcclusionTargets();
                _mapCommands.BuildWorld(
                    _camera,
                    Time.unscaledTimeAsDouble,
                    JxqyPresentationDrawCommandBuilder.ToUnityColor(
                        _presentationEffects.MapColor),
                    _frameCommands,
                    _player.TilePosition.Y,
                    HasActiveMagicPresentation(),
                    _characterOcclusionRows);
                _worldCommands.Build(
                    _frameVisuals,
                    _camera,
                    _actorCommands,
                    _player.TilePosition.Y,
                    _characterOcclusionRows);
                _frameCommands.AddRange(_actorCommands);
                _presentationBuilder.Build(
                    _presentationEffects,
                    _camera,
                    _presentationCommands,
                    _weatherParticles);
                _frameCommands.AddRange(_presentationCommands);
            }
#if UNITY_EDITOR
            _acceptanceManagedBytesLastFrameBuild =
                GC.GetAllocatedBytesForCurrentThread() -
                frameBuildAllocatedBytes;
#endif
            _renderer.SetCameraPosition(
                _camera.X + LogicalWidth * 0.5f,
                _camera.Y + LogicalHeight * 0.5f);
            if (_audio is IJxqyWorldAudioPort worldAudio)
            {
                worldAudio.SetWorldSoundListener(
                    new JxqyFloat2(
                        _camera.X + LogicalWidth * 0.5f,
                        _camera.Y + LogicalHeight * 0.5f),
                    new JxqyFloat2(LogicalWidth, LogicalHeight));
            }
#if UNITY_EDITOR
            long frameSubmitAllocatedBytes =
                GC.GetAllocatedBytesForCurrentThread();
#endif
            using (FrameSubmitMarker.Auto())
                _renderer.Submit(_frameCommands);
#if UNITY_EDITOR
            _acceptanceManagedBytesLastFrameSubmit =
                GC.GetAllocatedBytesForCurrentThread() -
                frameSubmitAllocatedBytes;
#endif
        }

        private void PrepareCharacterOcclusionTargets()
        {
            _characterOcclusionRows.Clear();
            _characterOcclusionRows.Add(_player.TilePosition.Y);
            _playerVisual.CharacterOcclusionStencilBit =
                JxqyCharacterOcclusionPolicy.GetTargetStencilBit(0);
            foreach (KeyValuePair<JxqyNpc, JxqyRuntimeActorVisual> pair in
                     _npcVisuals)
            {
                pair.Value.Visual.CharacterOcclusionStencilBit = 0;
            }
            foreach (JxqyNpc npc in _npcs.Npcs)
            {
                if (_characterOcclusionRows.Count >=
                    JxqyCharacterOcclusionPolicy.MaximumTargetCount)
                {
                    break;
                }
                if (npc == null ||
                    npc.Kind != JxqyCharacterKind.Follower ||
                    !npc.IsVisible || npc.IsDead ||
                    !_npcVisuals.TryGetValue(
                        npc,
                        out JxqyRuntimeActorVisual visual))
                {
                    continue;
                }
                int targetIndex = _characterOcclusionRows.Count;
                _characterOcclusionRows.Add(npc.TilePosition.Y);
                visual.Visual.CharacterOcclusionStencilBit =
                    JxqyCharacterOcclusionPolicy.GetTargetStencilBit(
                        targetIndex);
            }
        }

        private bool HasActiveMagicPresentation()
        {
            for (int index = 0; index < _frameVisuals.Count; index++)
            {
                JxqyWorldVisualKind kind = _frameVisuals[index].Kind;
                if (kind == JxqyWorldVisualKind.Magic ||
                    kind == JxqyWorldVisualKind.Projectile ||
                    kind == JxqyWorldVisualKind.CharacterEffect)
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyPresentationColors()
        {
            if (_presentationEffects == null)
                return;
            Color mapColor =
                JxqyPresentationDrawCommandBuilder.ToUnityColor(
                    _presentationEffects.MapColor);
            for (int index = 0;
                 index < _mapTilemaps.Count;
                 index++)
            {
                if (_mapTilemaps[index] != null)
                    _mapTilemaps[index].color = mapColor;
            }
            if (_waterRefractionEffect != null)
            {
                _waterRefractionEffect.EffectEnabled =
                    _presentationEffects.WaterEffectEnabled;
            }
        }

        private void PlayThunder()
        {
            if (_audio == null ||
                _rainThunderSoundFileNames.Count == 0)
            {
                return;
            }
            string fileName = _rainThunderSoundFileNames[
                _legacyRandom.Next(0, _rainThunderSoundFileNames.Count)];
            if (!TryResolveSoundAddress(
                    fileName,
                    "RainThunderSound",
                    out string address))
                return;
            _audio.PlaySoundAsync(
                address,
                1f,
                this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void PlayRainAmbient(string rainFileName)
        {
            StopRainAmbient();
            LoadRainConfigurationAndAudioAsync(
                    rainFileName,
                    this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private async UniTaskVoid LoadRainConfigurationAndAudioAsync(
            string rainFileName,
            CancellationToken cancellationToken)
        {
            try
            {
                string safeFileName = SafeLegacyFileName(
                    rainFileName,
                    ".ini");
                string text = await LoadDynamicTextAsync(
                    "ini/map",
                    safeFileName,
                    cancellationToken);
                if (!_presentationEffects.IsRaining ||
                    !string.Equals(
                        _presentationEffects.RainFileName,
                        rainFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Dictionary<string, Dictionary<string, string>> sections =
                    JxqyLegacySaveImporter.ParseIni(text);
                if (!sections.TryGetValue(
                        "Init",
                        out Dictionary<string, string> init))
                {
                    throw new InvalidOperationException(
                        $"Legacy rain file '{safeFileName}' has no Init section.");
                }
                _presentationEffects.ConfigureRain(
                    ParseIniInteger(init, "Number", 300),
                    ParseIniInteger(init, "Speed", 20),
                    ParseIniInteger(init, "BoltProb", 1000));

                _rainThunderSoundFileNames.Clear();
                if (sections.TryGetValue(
                        "BoltSound",
                        out Dictionary<string, string> boltSounds))
                {
                    _rainThunderSoundFileNames.AddRange(
                        boltSounds
                            .OrderBy(pair => pair.Key)
                            .Select(pair => pair.Value)
                            .Where(value =>
                                !string.IsNullOrWhiteSpace(value)));
                }
                if (_audio == null ||
                    !sections.TryGetValue(
                        "RainSound",
                        out Dictionary<string, string> rainSounds))
                {
                    return;
                }
                string ambientFileName = rainSounds
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Value)
                    .FirstOrDefault(value =>
                        !string.IsNullOrWhiteSpace(value));
                if (!TryResolveSoundAddress(
                        ambientFileName,
                        "RainAmbientSound",
                        out string address))
                {
                    return;
                }
                await _audio.PlayAmbientLoopAsync(
                    address,
                    1f,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void StopRainAmbient()
        {
            _rainThunderSoundFileNames.Clear();
            _audio?.StopAmbientLoop();
        }

        private void RegisterPresentationTexture(
            string address,
            Texture2D texture)
        {
            _ownedTextures.Add(texture);
            _textures.Register(address, texture);
        }

        private static Texture2D CreateSolidTexture(
            int width,
            int height,
            Color32 color,
            string name)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[width * height];
            Array.Fill(pixels, color);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateRainTexture()
        {
            var texture = new Texture2D(
                JxqyPresentationDrawCommandBuilder.RainTextureWidth,
                JxqyPresentationDrawCommandBuilder.RainTextureHeight,
                TextureFormat.RGBA32,
                false)
            {
                name = "JxqyRain",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(
                JxqyPresentationDrawCommandBuilder
                    .CreateRainTexturePixels());
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateSnowTexture(int variant)
        {
            var texture = new Texture2D(
                16,
                16,
                TextureFormat.RGBA32,
                false)
            {
                name = $"JxqySnow{variant}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[16 * 16];
            int center = 7 + (variant & 1);
            int radius = 2 + variant % 3;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int dx = Math.Abs(x - center);
                    int dy = Math.Abs(y - center);
                    if (dx <= radius && dy <= 1 ||
                        dy <= radius && dx <= 1 ||
                        dx == dy && dx <= radius)
                    {
                        pixels[y * 16 + x] =
                            new Color32(255, 255, 255, 230);
                    }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

#if false // Retired UI draw-command path. All screens are TEngine UIWindow/UGUI.
        private List<JxqyDrawCommand> BuildTitleCommands()
        {
            var commands = new List<JxqyDrawCommand>(5)
            {
                new JxqyDrawCommand(
                    TitleBackgroundAddress,
                    new Rect(0, 0, LogicalWidth, LogicalHeight),
                    Vector2.zero,
                    Vector2.zero,
                    Color.white,
                    1000000,
                    "default")
            };
            int[] left = { 327, 327, 327, 327 };
            int[] top = { 112, 177, 240, 303 };
            for (int index = 0; index < _titleButtons.Length; index++)
            {
                JxqyAnimationMetadata animation = _titleButtons[index];
                int frameIndex =
                    index == _uiSession.Selection &&
                    animation.Frames.Count > 1
                        ? 1
                        : 0;
                JxqyAnimationFrameMetadata frame =
                    animation.Frames[frameIndex];
                commands.Add(new JxqyDrawCommand(
                    animation.AtlasAddresses[frame.AtlasPage],
                    new Rect(
                        frame.AtlasX,
                        frame.AtlasY,
                        frame.AtlasWidth,
                        frame.AtlasHeight),
                    new Vector2(left[index], top[index]),
                    Vector2.zero,
                    Color.white,
                    1000010 + index,
                    "default"));
            }
            return commands;
        }

        private List<JxqyDrawCommand> BuildHudCommands()
        {
            var commands = new List<JxqyDrawCommand>();
            AddUiAnimation(
                commands,
                "asf:asf/ui/top/window.asf",
                177,
                0,
                10_000_000);
            string[] topButtons =
            {
                "asf:asf/ui/top/btnstate.asf",
                "asf:asf/ui/top/btnequip.asf",
                "asf:asf/ui/top/btnxiulian.asf",
                "asf:asf/ui/top/btngoods.asf",
                "asf:asf/ui/top/btnmagic.asf",
                "asf:asf/ui/top/btnnotes.asf",
                "asf:asf/ui/top/btnoption.asf",
            };
            int[] buttonLeft = { 52, 80, 107, 135, 162, 189, 216 };
            for (int index = 0; index < topButtons.Length; index++)
            {
                AddUiAnimation(
                    commands,
                    topButtons[index],
                    177 + buttonLeft[index],
                    0,
                    10_000_010 + index);
            }
            AddUiAnimation(
                commands,
                "asf:asf/ui/bottom/window.asf",
                218,
                410,
                10_000_100);
            AddUiAnimation(
                commands,
                "asf:asf/ui/column/panel9.asf",
                0,
                404,
                10_000_110);
            AddUiAnimation(
                commands,
                "asf:asf/ui/column/collife.asf",
                11,
                426,
                10_000_120,
                animated: true);
            AddUiAnimation(
                commands,
                "asf:asf/ui/column/colthew.asf",
                59,
                426,
                10_000_121,
                animated: true);
            AddUiAnimation(
                commands,
                "asf:asf/ui/column/colmana.asf",
                113,
                426,
                10_000_122,
                animated: true);
            if (_showMapPosition && _player != null && _uiFont != null)
            {
                commands.AddRange(_uiFont.Build(
                    $"{_player.TilePosition.X}," +
                    $"{_player.TilePosition.Y}",
                    new Vector2(4f, 4f),
                    Color.white,
                    10_000_200));
            }
            return commands;
        }

        private List<JxqyDrawCommand> BuildModalCommands()
        {
            var commands = new List<JxqyDrawCommand>();
            switch (_uiSession.CurrentScreen)
            {
                case JxqyUiScreen.Hud:
                    break;
                case JxqyUiScreen.Status:
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/common/panel5b.asf",
                        0,
                        0,
                        20_000_000);
                    AddStateText(commands);
                    break;
                case JxqyUiScreen.Inventory:
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/common/panel3.asf",
                        320,
                        0,
                        20_000_000);
                    break;
                case JxqyUiScreen.Equipment:
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/common/panel7b.asf",
                        0,
                        0,
                        20_000_000);
                    break;
                case JxqyUiScreen.Training:
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/common/panel6.asf",
                        0,
                        0,
                        20_000_000);
                    break;
                case JxqyUiScreen.Skills:
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/common/panel2.asf",
                        320,
                        0,
                        20_000_000);
                    break;
                case JxqyUiScreen.Trade:
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/common/panel8.asf",
                        0,
                        0,
                        20_000_000);
                    AddUiAnimation(
                        commands,
                        "asf:asf/ui/buysell/closebtn.asf",
                        117,
                        354,
                        20_000_010);
                    break;
                case JxqyUiScreen.Menu:
                    AddSystemMenu(commands);
                    break;
                case JxqyUiScreen.SaveLoad:
                    AddSaveLoad(commands);
                    break;
                case JxqyUiScreen.Dialogue:
                    AddDialogue(commands);
                    break;
                case JxqyUiScreen.Selection:
                    break;
            }
            return commands;
        }

        private void AddSystemMenu(List<JxqyDrawCommand> commands)
        {
            AddUiAnimation(
                commands,
                "asf:asf/ui/common/panel.asf",
                226,
                26,
                20_000_000);
            string[] buttons =
            {
                "asf:asf/ui/system/saveload.asf",
                "asf:asf/ui/system/option.asf",
                "asf:asf/ui/system/quit.asf",
                "asf:asf/ui/system/return.asf",
            };
            int[] top = { 86, 150, 213, 276 };
            for (int index = 0; index < buttons.Length; index++)
            {
                AddUiAnimation(
                    commands,
                    buttons[index],
                    226 + 58,
                    26 + top[index],
                    20_000_010 + index,
                    frameIndex: index == _uiSession.Selection ? 1 : 0);
            }
        }

        private void AddSaveLoad(List<JxqyDrawCommand> commands)
        {
            AddUiAnimation(
                commands,
                "asf:asf/ui/saveload/panel.asf",
                0,
                0,
                20_000_000);
            AddUiAnimation(
                commands,
                "asf:asf/ui/saveload/btnload.asf",
                248,
                355,
                20_000_010);
            AddUiAnimation(
                commands,
                "asf:asf/ui/saveload/btnsave.asf",
                366,
                355,
                20_000_011);
            AddUiAnimation(
                commands,
                "asf:asf/ui/saveload/btnexit.asf",
                464,
                355,
                20_000_012);
            if (_uiFont == null)
                return;
            for (int index = 0; index < 7; index++)
            {
                Color color = index == _uiSession.Selection
                    ? new Color32(102, 73, 212, 255)
                    : new Color32(91, 31, 27, 255);
                commands.AddRange(_uiFont.Build(
                    $"进度{ToChineseNumber(index + 1)}",
                    new Vector2(138, 118 + index * 25),
                    color,
                    20_000_100 + index));
            }
        }

        private void AddStateText(List<JxqyDrawCommand> commands)
        {
            if (_uiFont == null)
                return;
            string[] values =
            {
                _player.Level.ToString(),
                $"{_player.Experience}/{_player.LevelUpExperience}",
                Math.Max(
                    0,
                    _player.LevelUpExperience - _player.Experience).ToString(),
                $"{_player.Life}/{_player.LifeMax}",
                $"{_player.Thew}/{_player.ThewMax}",
                $"{_player.Mana}/{_player.ManaMax}",
                _player.Attack.ToString(),
                _player.Defend.ToString(),
                _player.Evade.ToString(),
            };
            int[] top = { 220, 235, 250, 265, 280, 295, 310, 325, 340 };
            for (int index = 0; index < values.Length; index++)
            {
                commands.AddRange(_uiFont.Build(
                    values[index],
                    new Vector2(144, top[index]),
                    Color.black,
                    20_000_100 + index));
            }
        }

        private void AddDialogue(List<JxqyDrawCommand> commands)
        {
            JxqyDialoguePage page = _uiSession.Dialogue?.Current;
            bool hasChoices = page != null && page.Choices.Count > 0;
            AddUiAnimation(
                commands,
                "asf:asf/ui/dialog/panel.asf",
                100,
                hasChoices ? 285 : 295,
                20_000_000);
            if (_uiFont == null || page == null)
                return;
            commands.AddRange(_uiFont.Build(
                page.Text,
                new Vector2(118, hasChoices ? 296 : 306),
                Color.black,
                20_000_100));
            for (int index = 0; index < page.Choices.Count; index++)
            {
                Color choiceColor =
                    index == _uiSession.Selection
                        ? new Color32(172, 45, 30, 255)
                        : Color.black;
                commands.AddRange(_uiFont.Build(
                    page.Choices[index].Text,
                    new Vector2(130, 326 + index * 23),
                    choiceColor,
                    20_000_110 + index));
            }
        }

        private void AddUiAnimation(
            List<JxqyDrawCommand> commands,
            string stableId,
            int left,
            int top,
            int depth,
            int frameIndex = 0,
            bool animated = false)
        {
            if (!_animations.TryGetValue(
                    stableId,
                    out JxqyAnimationMetadata animation) ||
                animation.Frames.Count == 0)
                return;
            int index = frameIndex;
            if (animated)
            {
                int interval = Math.Max(1, animation.IntervalMilliseconds);
                index = (int)(Time.unscaledTimeAsDouble * 1000 / interval) %
                        animation.Frames.Count;
            }
            index = Mathf.Clamp(index, 0, animation.Frames.Count - 1);
            JxqyAnimationFrameMetadata frame = animation.Frames[index];
            commands.Add(new JxqyDrawCommand(
                animation.AtlasAddresses[frame.AtlasPage],
                new Rect(
                    frame.AtlasX,
                    frame.AtlasY,
                    frame.AtlasWidth,
                    frame.AtlasHeight),
                new Vector2(left, top),
                Vector2.zero,
                Color.white,
                depth,
                "default"));
        }

        private static string ToChineseNumber(int value)
        {
            string[] values =
            {
                "零", "一", "二", "三", "四", "五", "六", "七",
            };
            return value >= 0 && value < values.Length
                ? values[value]
                : value.ToString();
        }

#endif

        private void OnDestroy()
        {
            _ready = false;
            if (_player != null)
            {
                _player.Died -= OnPlayerDied;
                _player.Revived -= OnPlayerRevived;
            }
            if (_combat != null)
                _combat.MagicUsed -= OnMagicUsed;
            if (_video is JxqyUnityVideoPort unityVideo)
                unityVideo.PlaybackStarted -= OnVideoPlaybackStarted;
            _scriptSession?.Dispose();
            _scriptSession = null;
            if (_presentationEffects != null)
            {
                _presentationEffects.Thunder -= PlayThunder;
                _presentationEffects.RainStarted -= PlayRainAmbient;
                _presentationEffects.RainEnded -= StopRainAmbient;
            }
            if (_uiSession != null)
            {
                _uiSession.NewGameRequested -= StartNewGame;
                _uiSession.CreditsRequested -= StartCredits;
                _uiSession.QuitRequested -= ReturnToTitle;
                _uiSession.ExitApplicationRequested -= ExitApplication;
                _uiSession.SaveRequested -= OnSaveRequested;
                _uiSession.LoadRequested -= OnLoadRequested;
                _uiSession.ItemScriptRequested -= OnItemScriptRequested;
                _uiSession.SoundRequested -= OnUiSoundRequested;
                _uiSession.MusicVolumeChanged -= OnMusicVolumeChanged;
                _uiSession.SoundVolumeChanged -= OnSoundVolumeChanged;
                _uiSession.GameSpeedChanged -= OnGameSpeedChanged;
            }
            _combatFloatTextPool?.Dispose();
            _combatFloatTextPool = null;
            _textures?.Dispose();
            _textures = null;
            foreach (Texture2D texture in _ownedTextures)
            {
                if (texture != null)
                    Destroy(texture);
            }
            _ownedTextures.Clear();
            foreach (IDisposable lease in _leases)
                lease.Dispose();
            _leases.Clear();
            _renderMaterials.Clear();
            foreach (IDisposable lease in _activeMapLeases)
                lease.Dispose();
            _activeMapLeases.Clear();
            if (_resources != null &&
                _activeMapAssetScope != null)
            {
                _resources.ReleaseScopeAsync(
                    _activeMapAssetScope,
                    CancellationToken.None).Forget();
                _activeMapAssetScope = null;
            }
            if (_resources != null && _mapScope != null)
                _resources.ReleaseScopeAsync(
                    _mapScope,
                    CancellationToken.None).Forget();
            _resources?.Dispose();
            _resources = null;
            _contentContext = null;
        }
    }
}
