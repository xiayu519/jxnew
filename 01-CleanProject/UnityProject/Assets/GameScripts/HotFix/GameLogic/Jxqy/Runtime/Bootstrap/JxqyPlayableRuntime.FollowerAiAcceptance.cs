#if UNITY_EDITOR
using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Jxqy.Domain.Simulation;
using Jxqy.Domain.World;
using UnityEngine;

namespace Jxqy.Bootstrap
{
    internal sealed partial class JxqyPlayableRuntime
    {
        private bool _acceptanceFollowerAiPreparing;
        private bool _acceptanceFollowerAiReady;
        private string _acceptanceFollowerAiError = string.Empty;
        private JxqyNpc _acceptanceFollowerAiActor;
        private JxqyNpc _acceptanceFollowerAiTarget;
        private string _acceptanceFollowerAiFirstMagicId = string.Empty;
        private string _acceptanceFollowerAiSecondMagicId = string.Empty;
        private int _acceptanceFollowerAiMagicUseCount;
        private int _acceptanceFollowerAiFirstMagicUseCount;
        private int _acceptanceFollowerAiSecondMagicUseCount;
        private int _acceptanceFollowerAiProjectileSpawnCount;
        private int _acceptanceFollowerAiContactCount;
        private string _acceptanceFollowerAiLastMagicId = string.Empty;
        private int _acceptanceSharedKillExpectedExperience;
        private int _acceptanceSharedKillPlayerExperienceDelta;
        private int _acceptanceSharedKillFollowerExperienceDelta;

        public JxqyFollowerAiAcceptanceSnapshot
            GetAcceptanceFollowerAiSnapshot()
        {
            return new JxqyFollowerAiAcceptanceSnapshot
            {
                IsPreparing = _acceptanceFollowerAiPreparing,
                IsReady = _acceptanceFollowerAiReady,
                Error = _acceptanceFollowerAiError,
                MapStableId = ActiveMapStableId,
                FollowerName = _acceptanceFollowerAiActor?.Name ??
                               string.Empty,
                FollowerAttackRadius =
                    _acceptanceFollowerAiActor?.AttackRadius ?? 0,
                FollowerLifeMax =
                    _acceptanceFollowerAiActor?.LifeMax ?? 0,
                FollowerAttack =
                    _acceptanceFollowerAiActor?.Attack ?? 0,
                FollowerDefend =
                    _acceptanceFollowerAiActor?.Defend ?? 0,
                FollowerLevel =
                    _acceptanceFollowerAiActor?.Level ?? 0,
                FollowerCanLevelUp =
                    _acceptanceFollowerAiActor?.CanLevelUp ?? 0,
                SharedKillExpectedExperience =
                    _acceptanceSharedKillExpectedExperience,
                SharedKillPlayerExperienceDelta =
                    _acceptanceSharedKillPlayerExperienceDelta,
                SharedKillFollowerExperienceDelta =
                    _acceptanceSharedKillFollowerExperienceDelta,
                FollowerMana = _acceptanceFollowerAiActor?.Mana ?? 0,
                FollowerThew = _acceptanceFollowerAiActor?.Thew ?? 0,
                FirstMagicId = _acceptanceFollowerAiFirstMagicId,
                SecondMagicId = _acceptanceFollowerAiSecondMagicId,
                SecondMagicMoveKind =
                    _acceptanceFollowerAiActor?.Skills?
                        .FindAtLegacyIndex(2)?.Magic?.MoveKind ?? 0,
                SecondMagicSpecialKind =
                    _acceptanceFollowerAiActor?.Skills?
                        .FindAtLegacyIndex(2)?.Magic?.SpecialKind ?? 0,
                FirstMagicVisualAssetsLoaded =
                    _magicVisualAssets.ContainsKey(
                        _acceptanceFollowerAiFirstMagicId),
                SecondMagicVisualAssetsLoaded =
                    _magicVisualAssets.ContainsKey(
                        _acceptanceFollowerAiSecondMagicId),
                MagicUseCount = _acceptanceFollowerAiMagicUseCount,
                FirstMagicUseCount =
                    _acceptanceFollowerAiFirstMagicUseCount,
                SecondMagicUseCount =
                    _acceptanceFollowerAiSecondMagicUseCount,
                ProjectileSpawnCount =
                    _acceptanceFollowerAiProjectileSpawnCount,
                ContactCount = _acceptanceFollowerAiContactCount,
                LastMagicId = _acceptanceFollowerAiLastMagicId,
                ActiveFollowerMagicVisualCount = _magicVisuals.Count(item =>
                    ReferenceEquals(
                        item.Projectile?.Source,
                        _acceptanceFollowerAiActor)),
            };
        }

        public void BeginAcceptanceFollowerAiCase(
            int slot,
            string followerName)
        {
            if (_acceptanceFollowerAiPreparing)
                return;
            _acceptanceFollowerAiPreparing = true;
            _acceptanceFollowerAiReady = false;
            _acceptanceFollowerAiError = string.Empty;
            RunAcceptanceFollowerAiCaseAsync(slot, followerName).Forget();
        }

        private async UniTaskVoid RunAcceptanceFollowerAiCaseAsync(
            int slot,
            string followerName)
        {
            try
            {
                if (!_ready || _saveRepository == null || slot < 0 ||
                    string.IsNullOrWhiteSpace(followerName))
                {
                    throw new InvalidOperationException(
                        "Follower-AI acceptance has invalid runtime state " +
                        "or arguments.");
                }

                await LoadGameAsync(
                    slot,
                    this.GetCancellationTokenOnDestroy());
                _acceptanceFollowerAiActor = _npcs.Npcs.FirstOrDefault(npc =>
                    npc.Kind == JxqyCharacterKind.Follower &&
                    string.Equals(
                        npc.Name,
                        followerName,
                        StringComparison.OrdinalIgnoreCase));
                if (_acceptanceFollowerAiActor == null)
                {
                    throw new InvalidOperationException(
                        $"Save slot {slot} does not contain follower " +
                        $"'{followerName}'.");
                }

                VerifyAcceptanceSharedKillExperience();

                JxqySkillEntry first =
                    _acceptanceFollowerAiActor.Skills?
                        .FindAtLegacyIndex(1);
                JxqySkillEntry second =
                    _acceptanceFollowerAiActor.Skills?
                        .FindAtLegacyIndex(2);
                if (first?.Magic == null || second?.Magic == null)
                {
                    throw new InvalidOperationException(
                        $"Follower '{followerName}' is missing original " +
                        "martial-art slots 1 or 2.");
                }
                _acceptanceFollowerAiFirstMagicId = first.Magic.Id;
                _acceptanceFollowerAiSecondMagicId = second.Magic.Id;

                _acceptanceSuppressTraps = true;
                _scriptSession?.Cancel();
                if (_video is Jxqy.UnityAdapters.JxqyUnityVideoPort video)
                    video.RequestSkip();
                _uiSession?.Open(
                    Jxqy.Domain.Presentation.JxqyUiScreen.Hud);
                _gameStarted = true;
                _legacyInputDisabled = false;
                ResetCombatTransientState();
                ClearMagicVisuals();

                JxqyIntPoint targetTile =
                    FindAcceptanceFollowerTargetTile(
                        _acceptanceFollowerAiActor);
                _acceptanceFollowerAiTarget = new JxqyNpc
                {
                    Name = "Follower AI acceptance target",
                    Kind = JxqyCharacterKind.Normal,
                    Relation = JxqyRelationType.Enemy,
                    TilePosition = targetTile,
                    LifeMax = 100000,
                    Life = 100000,
                    Defend = 0,
                    Evade = 0,
                    CanEvade = false,
                    VisionRadius = 0,
                    StopFindingTarget = true,
                    IsVisible = true,
                };
                _npcs.Add(_acceptanceFollowerAiTarget);

                _acceptanceFollowerAiActor.Stop();
                _acceptanceFollowerAiActor.SetFighting(false);
                _acceptanceFollowerAiActor.SetState(
                    JxqyCharacterState.Stand);
                _acceptanceFollowerAiActor.Follow(null);
                _acceptanceFollowerAiActor.StopFindingTarget = false;
                _acceptanceFollowerAiActor.VisionRadius = Math.Max(
                    9,
                    _acceptanceFollowerAiActor.VisionRadius);
                _npcs.IsAiDisabled = false;
                _acceptanceFollowerAiMagicUseCount = 0;
                _acceptanceFollowerAiFirstMagicUseCount = 0;
                _acceptanceFollowerAiSecondMagicUseCount = 0;
                _acceptanceFollowerAiProjectileSpawnCount = 0;
                _acceptanceFollowerAiContactCount = 0;
                _acceptanceFollowerAiLastMagicId = string.Empty;
                _combat.MagicUsed -= ObserveAcceptanceFollowerMagicUsed;
                _combat.ProjectileSpawned -=
                    ObserveAcceptanceFollowerProjectileSpawned;
                _combat.MagicContacted -=
                    ObserveAcceptanceFollowerMagicContacted;
                _combat.MagicUsed += ObserveAcceptanceFollowerMagicUsed;
                _combat.ProjectileSpawned +=
                    ObserveAcceptanceFollowerProjectileSpawned;
                _combat.MagicContacted +=
                    ObserveAcceptanceFollowerMagicContacted;

                CenterCameraOnPlayer();
                UpdatePlayerVisual();
                SubmitFrame();
                Time.timeScale = 8f;
                _acceptanceFollowerAiReady = true;
            }
            catch (Exception exception)
            {
                _acceptanceFollowerAiError = exception.ToString();
            }
            finally
            {
                _acceptanceFollowerAiPreparing = false;
            }
        }

        private void VerifyAcceptanceSharedKillExperience()
        {
            int playerExperienceBefore = _player.Experience;
            int followerExperienceBefore =
                _acceptanceFollowerAiActor.Experience;
            var defeated = new JxqyNpc
            {
                Name = "Shared experience acceptance target",
                Kind = JxqyCharacterKind.Normal,
                Relation = JxqyRelationType.Enemy,
                Level = Math.Max(1, _player.Level - 10),
                LifeMax = 1,
                Life = 1,
                ExpBonus = 0,
                IsVisible = false,
            };
            _acceptanceSharedKillExpectedExperience =
                JxqyExperienceRules.CalculateDeathExperience(
                    _player,
                    defeated,
                    _contentContext.ContentProfile.DeathExperience);
            defeated.Die(_player);
            ProcessNpcDeath(defeated);
            _acceptanceSharedKillPlayerExperienceDelta =
                _player.Experience - playerExperienceBefore;
            _acceptanceSharedKillFollowerExperienceDelta =
                _acceptanceFollowerAiActor.Experience -
                followerExperienceBefore;
            if (_acceptanceSharedKillExpectedExperience <= 0 ||
                _acceptanceSharedKillPlayerExperienceDelta !=
                _acceptanceSharedKillExpectedExperience ||
                _acceptanceSharedKillFollowerExperienceDelta !=
                _acceptanceSharedKillExpectedExperience)
            {
                throw new InvalidOperationException(
                    "A player kill did not grant the same original " +
                    "character-experience award to the player and loaded " +
                    "follower.");
            }
        }

        private JxqyIntPoint FindAcceptanceFollowerTargetTile(
            JxqyNpc follower)
        {
            const int attackDistance = 5;
            for (int y = 0; y < _map.Rows; y++)
            {
                for (int x = 0; x < _map.Columns; x++)
                {
                    var candidate = new JxqyIntPoint(x, y);
                    if (JxqyPathfinder.GetViewTileDistance(
                            follower.TilePosition,
                            candidate) != attackDistance ||
                        _map.IsObstacleForCharacter(x, y) ||
                        _map.IsObstacleForMagic(x, y) ||
                        !JxqyPathfinder.CanViewTarget(
                            new JxqyRuntimeCollisionMap(_map),
                            follower.TilePosition,
                            candidate,
                            attackDistance))
                    {
                        continue;
                    }
                    return candidate;
                }
            }
            throw new InvalidOperationException(
                "Loaded map has no clear five-tile follower target lane.");
        }

        private void ObserveAcceptanceFollowerMagicUsed(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 destination)
        {
            if (!ReferenceEquals(source, _acceptanceFollowerAiActor) ||
                magic == null)
            {
                return;
            }
            _acceptanceFollowerAiMagicUseCount++;
            _acceptanceFollowerAiLastMagicId = magic.Id;
            if (string.Equals(
                    magic.Id,
                    _acceptanceFollowerAiFirstMagicId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _acceptanceFollowerAiFirstMagicUseCount++;
            }
            else if (string.Equals(
                         magic.Id,
                         _acceptanceFollowerAiSecondMagicId,
                         StringComparison.OrdinalIgnoreCase))
            {
                _acceptanceFollowerAiSecondMagicUseCount++;
            }
        }

        private void ObserveAcceptanceFollowerProjectileSpawned(
            JxqyMagicProjectile projectile)
        {
            if (ReferenceEquals(
                    projectile?.Source,
                    _acceptanceFollowerAiActor))
            {
                _acceptanceFollowerAiProjectileSpawnCount++;
            }
        }

        private void ObserveAcceptanceFollowerMagicContacted(
            JxqyMagicProjectile projectile,
            JxqyCharacter target,
            JxqyDamageResult result)
        {
            if (ReferenceEquals(
                    projectile?.Source,
                    _acceptanceFollowerAiActor))
            {
                _acceptanceFollowerAiContactCount++;
            }
        }
    }
}
#endif
