#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Jxqy.Domain.Content;
using Jxqy.Domain.Presentation;
using Jxqy.Domain.Simulation;
using Jxqy.Domain.World;
using Jxqy.UnityAdapters;

namespace Jxqy.Bootstrap
{
    internal sealed partial class JxqyPlayableRuntime
    {
        private bool _acceptanceMartialArtPreparing;
        private bool _acceptanceMartialArtReady;
        private bool _acceptanceMartialArtCultivation;
        private bool _acceptanceMartialArtTriggered;
        private string _acceptanceMartialArtError = string.Empty;
        private string _acceptanceMartialArtFile = string.Empty;
        private int _acceptanceMartialArtLevel;
        private JxqySkillEntry _acceptanceMartialArtSkill;
        private JxqyNpc _acceptanceMartialArtTarget;
        private JxqyFloat2 _acceptanceMartialArtDestination;
        private string _acceptanceMartialArtObservedMagicId = string.Empty;
        private int _acceptanceMartialArtProjectileCount;
        private bool _acceptanceMartialArtValidationMapLoaded;
        private readonly List<int> _acceptanceMartialArtSpawnDirections =
            new();
        private readonly List<float> _acceptanceMartialArtSpawnDelays =
            new();
        private int _acceptanceMartialArtNaturalExpiryVanishCount;
        private int _acceptanceMartialArtNaturalExpirySilentCount;

        public JxqyMartialArtAcceptanceSnapshot
            GetAcceptanceMartialArtSnapshot()
        {
            JxqyMagicDefinition magic = _acceptanceMartialArtSkill?.Magic;
            _magicVisualAssets.TryGetValue(
                magic?.Id ?? string.Empty,
                out JxqyRuntimeMagicAssets assets);
            JxqyAnimationMetadata action = null;
            JxqyMagicDefinition attackMagic = null;

            string actionFile = string.IsNullOrWhiteSpace(
                magic?.ActionFileName)
                ? string.Empty
                : magic.ActionFileName + _playerNpcIniIndex + ".asf";
            string activeAnimation =
                _playerVisual?.Animation?.Metadata?.SourceStableId ??
                string.Empty;
            bool pending = _acceptanceMartialArtCultivation
                ? _player != null && _pendingBasicAttacks.ContainsKey(_player)
                : _pendingPlayerMagicCast != null;
            JxqyRuntimeMagicVisual activeMagicVisual =
                _magicVisuals.FirstOrDefault(item =>
                    string.Equals(
                        item.Magic?.Id,
                        _acceptanceMartialArtObservedMagicId,
                        StringComparison.OrdinalIgnoreCase));

            return new JxqyMartialArtAcceptanceSnapshot
            {
                IsPreparing = _acceptanceMartialArtPreparing,
                IsReady = _acceptanceMartialArtReady,
                IsCultivationAttack = _acceptanceMartialArtCultivation,
                Triggered = _acceptanceMartialArtTriggered,
                Finished = _acceptanceMartialArtTriggered && !pending &&
                           _player?.CanPerformAction == true,
                Error = _acceptanceMartialArtError,
                MagicFile = _acceptanceMartialArtFile,
                MagicName = magic?.Name ?? string.Empty,
                Level = _acceptanceMartialArtLevel,
                MoveKind = magic?.MoveKind ?? 0,
                EffectLevel = magic?.EffectLevel ?? 0,
                Region = magic?.Region ?? 0,
                FlyingImage = magic?.FlyingImageFileName ?? string.Empty,
                VanishImage = magic?.VanishImageFileName ?? string.Empty,
                SuperModeImage =
                    magic?.SuperModeImageFileName ?? string.Empty,
                FlyingAnimationStableId =
                    assets?.Flying?.SourceStableId ?? string.Empty,
                VanishAnimationStableId =
                    assets?.Vanish?.SourceStableId ?? string.Empty,
                SuperModeAnimationStableId =
                    assets?.SuperMode?.SourceStableId ?? string.Empty,
                ExpectedActionFile = actionFile,
                CultivationActionCached = action != null,
                CultivationActionStableId =
                    action?.SourceStableId ?? string.Empty,
                ActivePlayerAnimationStableId = activeAnimation,
                ActivePlayerAnimationFrame =
                    _playerVisual?.Animation?.FrameWithinDirection ?? -1,
                PlayerState = _player != null ? (int)_player.State : -1,
                CultivationActionPresented =
                    _playerSpecialAction != null &&
                    ReferenceEquals(
                        _playerVisual?.Animation,
                        _playerSpecialAction),
                AttackFile = magic?.AttackFileName ?? string.Empty,
                AttackMoveKind = attackMagic?.MoveKind ?? 0,
                SpawnedProjectileCount =
                    _acceptanceMartialArtProjectileCount,
                SpawnDirectionIndices =
                    _acceptanceMartialArtSpawnDirections.ToArray(),
                SpawnDelaySeconds =
                    _acceptanceMartialArtSpawnDelays.ToArray(),
                NaturalExpiryVanishCount =
                    _acceptanceMartialArtNaturalExpiryVanishCount,
                NaturalExpirySilentCount =
                    _acceptanceMartialArtNaturalExpirySilentCount,
                ExpectedAttackDirectionIndex = _player == null
                    ? -1
                    : JxqyDirection.GetIndex(
                        _acceptanceMartialArtDestination -
                        _player.PositionInWorld,
                        32),
                ActiveMagicVisualCount = _magicVisuals.Count(item =>
                    string.Equals(
                        item.Magic?.Id,
                        _acceptanceMartialArtObservedMagicId,
                        StringComparison.OrdinalIgnoreCase)),
                ActiveMagicAnimationStableId =
                    activeMagicVisual?.Visual?.Animation?.Metadata?
                        .SourceStableId ?? string.Empty,
                ActiveMagicAnimationFrame =
                    activeMagicVisual?.Visual?.Animation?
                        .FrameWithinDirection ?? -1,
            };
        }

        public void BeginAcceptanceMartialArtCase(
            string magicFile,
            int level,
            bool cultivationAttack)
        {
            if (_acceptanceMartialArtPreparing)
                return;
            _acceptanceMartialArtPreparing = true;
            _acceptanceMartialArtReady = false;
            _acceptanceMartialArtCultivation = cultivationAttack;
            _acceptanceMartialArtTriggered = false;
            _acceptanceMartialArtError = string.Empty;
            _acceptanceMartialArtFile = magicFile ?? string.Empty;
            _acceptanceMartialArtLevel = level;
            _acceptanceMartialArtSkill = null;
            _acceptanceMartialArtObservedMagicId = string.Empty;
            _acceptanceMartialArtProjectileCount = 0;
            _acceptanceMartialArtSpawnDirections.Clear();
            _acceptanceMartialArtSpawnDelays.Clear();
            _acceptanceMartialArtNaturalExpiryVanishCount = 0;
            _acceptanceMartialArtNaturalExpirySilentCount = 0;
            RunAcceptanceMartialArtCaseAsync(
                    magicFile,
                    level,
                    cultivationAttack)
                .Forget();
        }

        private async UniTaskVoid RunAcceptanceMartialArtCaseAsync(
            string magicFile,
            int level,
            bool cultivationAttack)
        {
            try
            {
                if (!_ready || _player == null || _skills == null ||
                    _combat == null || string.IsNullOrWhiteSpace(magicFile) ||
                    level < 1 || level > 10)
                {
                    throw new InvalidOperationException(
                        "Martial-art acceptance case has invalid runtime " +
                        "state or arguments.");
                }
                if (cultivationAttack)
                {
                    throw new InvalidOperationException(
                        "XinJian has no shipped cultivation slot or " +
                        "cultivation Attack2 route.");
                }

                JxqyMagicDefinition magic =
                    await LoadMagicDefinitionAsync(magicFile, level);
                if (magic == null)
                    throw new InvalidOperationException(
                        $"Martial art could not be loaded: {magicFile}");

                if (!_acceptanceMartialArtValidationMapLoaded)
                {
                    // The original video demonstrates martial arts in open
                    // ground. Use a fixed original-game map and coordinate so
                    // scenery and projectile collisions cannot turn a
                    // sequential spiral into a misleading apparent ring.
                    await SwitchMapFromScriptAsync("map001_衡山.map");
                    _player.TilePosition = new JxqyIntPoint(24, 39);
                    _acceptanceMartialArtValidationMapLoaded = true;
                }

                _acceptanceSuppressTraps = true;
                _scriptSession?.Cancel();
                if (_video is JxqyUnityVideoPort unityVideo)
                    unityVideo.RequestSkip();
                _uiSession?.Open(JxqyUiScreen.Hud);
                _gameStarted = true;
                _legacyInputDisabled = false;
                _player.Stop();
                _player.SetFighting(false);
                _player.SetState(JxqyCharacterState.Stand);
                _player.EndSpecialAction();
                _playerSpecialAction = null;
                _playerScriptActions.Clear();
                if (_playerStand != null)
                {
                    _playerStand.Restart();
                    _playerVisual.Animation = _playerStand;
                }
                _player.IsVisible = true;
                _player.ManaLimit = false;
                _player.ManaMax = Math.Max(_player.ManaMax, 100000);
                _player.Mana = _player.ManaMax;

                foreach (string id in _skills.Skills
                             .Select(item => item.Magic.Id)
                             .ToArray())
                {
                    _skills.Forget(id);
                }
                int legacyIndex = cultivationAttack ? 49 : 40;
                if (!_skills.Learn(magic, legacyIndex) ||
                    !_skills.SetLevel(magic.Id, level))
                {
                    throw new InvalidOperationException(
                        $"Martial art could not enter slot {legacyIndex}: " +
                        magicFile);
                }
                _acceptanceMartialArtSkill = _skills.Find(magic.Id);
                _uiSession?.ClearSelectedSkill();
                if (!cultivationAttack)
                    _uiSession?.SelectSkill(0);

                foreach (JxqyRuntimeMagicVisual visual in
                         _magicVisuals.ToArray())
                {
                    RemoveMagicVisual(visual);
                }

                _acceptanceMartialArtTarget = _npcs?.Npcs.FirstOrDefault(
                    item => item.IsVisible && !item.IsDead &&
                            item.Name.StartsWith(
                                "acceptance-enemy-",
                                StringComparison.Ordinal));
                if (_acceptanceMartialArtTarget == null)
                {
                    if (!PrepareAcceptanceCrowdCombat(1))
                        throw new InvalidOperationException(
                            "A fixed martial-art target could not be created.");
                    _acceptanceMartialArtTarget = _npcs.Npcs.FirstOrDefault(
                        item => item.Name.StartsWith(
                            "acceptance-enemy-",
                            StringComparison.Ordinal));
                }
                if (_acceptanceMartialArtTarget == null)
                    throw new InvalidOperationException(
                        "The fixed martial-art target is missing.");

                _npcs.IsAiDisabled = true;
                _acceptanceMartialArtTarget.Relation =
                    JxqyRelationType.Enemy;
                _acceptanceMartialArtTarget.Invincible = false;
                _acceptanceMartialArtTarget.Evade = 0;
                _acceptanceMartialArtTarget.Defend = 0;
                _acceptanceMartialArtTarget.LifeMax = 1000000;
                _acceptanceMartialArtTarget.Life =
                    _acceptanceMartialArtTarget.LifeMax;
                _player.TilePosition = new JxqyIntPoint(24, 39);
                // Keep the visual comparison on the same east-facing attack
                // direction used by the reference video's close-up examples.
                _acceptanceMartialArtTarget.PositionInWorld =
                    _player.PositionInWorld + new JxqyFloat2(192, 0);
                _acceptanceMartialArtDestination =
                    _acceptanceMartialArtTarget.PositionInWorld;
                CenterCameraOnPlayer();
                RefreshActorVisual(_acceptanceMartialArtTarget);
                UpdatePlayerVisual();
                _uiSession?.Refresh();
                SubmitFrame();

                // Special normal attacks are a visual-review surface. Slow
                // them down without changing animation frame order, magic
                // delays, or projectile trajectories so every phase remains
                // readable in the captured sequence.
                UnityEngine.Time.timeScale =
                    cultivationAttack ? 0.25f : 1f;

                _acceptanceMartialArtObservedMagicId = magic.Id;
                _acceptanceMartialArtReady = true;
            }
            catch (Exception exception)
            {
                _acceptanceMartialArtError = exception.ToString();
            }
            finally
            {
                _acceptanceMartialArtPreparing = false;
            }
        }

        public bool TriggerAcceptanceMartialArtCase()
        {
            if (!_acceptanceMartialArtReady ||
                _acceptanceMartialArtTriggered ||
                _acceptanceMartialArtSkill?.Magic == null ||
                _player == null || !_player.CanPerformAction)
            {
                return false;
            }

            _acceptanceMartialArtProjectileCount = 0;
            _acceptanceMartialArtSpawnDirections.Clear();
            _acceptanceMartialArtSpawnDelays.Clear();
            _acceptanceMartialArtNaturalExpiryVanishCount = 0;
            _acceptanceMartialArtNaturalExpirySilentCount = 0;
            _acceptanceMartialArtTriggered = true;
            if (!_acceptanceMartialArtCultivation)
            {
                JxqyMagicDefinition magic =
                    _acceptanceMartialArtSkill.Magic;
                JxqyNpc target = magic.MoveKind == 13
                    ? null
                    : _acceptanceMartialArtTarget;
                JxqyFloat2 destination = magic.MoveKind == 13
                    ? _player.PositionInWorld
                    : _acceptanceMartialArtDestination;
                TryUsePlayerSkill(0, destination, target);
                if (_pendingPlayerMagicCast != null)
                    return true;
                _acceptanceMartialArtTriggered = false;
                return false;
            }

            _acceptanceMartialArtTriggered = false;
            return false;
        }

        private void ObserveAcceptanceMartialArtProjectile(
            JxqyMagicProjectile projectile)
        {
            if (!_acceptanceMartialArtTriggered ||
                string.IsNullOrWhiteSpace(
                    _acceptanceMartialArtObservedMagicId) ||
                !string.Equals(
                    projectile?.Magic?.Id,
                    _acceptanceMartialArtObservedMagicId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _acceptanceMartialArtProjectileCount++;
            _acceptanceMartialArtSpawnDirections.Add(
                JxqyDirection.GetIndex(projectile.Direction, 32));
            _acceptanceMartialArtSpawnDelays.Add(
                projectile.DelaySeconds);
        }

        private void ObserveAcceptanceMartialArtNaturalExpiry(
            JxqyMagicProjectile projectile,
            bool playedVanish)
        {
            if (!_acceptanceMartialArtTriggered ||
                string.IsNullOrWhiteSpace(
                    _acceptanceMartialArtObservedMagicId) ||
                !string.Equals(
                    projectile?.Magic?.Id,
                    _acceptanceMartialArtObservedMagicId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (playedVanish)
                _acceptanceMartialArtNaturalExpiryVanishCount++;
            else
                _acceptanceMartialArtNaturalExpirySilentCount++;
        }

    }
}
#endif
