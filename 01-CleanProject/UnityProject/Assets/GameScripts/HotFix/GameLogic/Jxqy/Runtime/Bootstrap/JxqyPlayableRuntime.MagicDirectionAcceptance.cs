#if UNITY_EDITOR
using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Jxqy.Domain.Simulation;
using Jxqy.Domain.World;

namespace Jxqy.Bootstrap
{
    internal sealed partial class JxqyPlayableRuntime
    {
        private bool _acceptanceMagicDirectionPreparing;
        private bool _acceptanceMagicDirectionReady;
        private bool _acceptanceMagicDirectionTriggered;
        private string _acceptanceMagicDirectionError = string.Empty;
        private int _acceptanceMagicDirectionSlot = -1;
        private int _acceptanceMagicDirectionSkillIndex = -1;
        private string _acceptanceMagicDirectionId = string.Empty;
        private JxqyFloat2 _acceptanceMagicDirectionDestination;

        public JxqyMagicDirectionAcceptanceSnapshot
            GetAcceptanceMagicDirectionSnapshot()
        {
            JxqySkillEntry skill = _acceptanceMagicDirectionSkillIndex >= 0 &&
                                   _skills != null &&
                                   _acceptanceMagicDirectionSkillIndex <
                                   _skills.Skills.Count
                ? _skills.Skills[_acceptanceMagicDirectionSkillIndex]
                : null;
            return new JxqyMagicDirectionAcceptanceSnapshot
            {
                IsPreparing = _acceptanceMagicDirectionPreparing,
                IsReady = _acceptanceMagicDirectionReady,
                Triggered = _acceptanceMagicDirectionTriggered,
                CastPending = _pendingPlayerMagicCast != null,
                Error = _acceptanceMagicDirectionError,
                Slot = _acceptanceMagicDirectionSlot,
                MapStableId = ActiveMapStableId,
                PlayerTile = _player?.TilePosition ??
                             new JxqyIntPoint(-1, -1),
                MagicId = skill?.Magic?.Id ?? string.Empty,
                MagicLevel = skill?.Level ?? 0,
                Destination = _acceptanceMagicDirectionDestination,
                ActiveMagicVisualCount = _magicVisuals.Count(item =>
                    string.Equals(
                        item.Magic?.Id,
                        _acceptanceMagicDirectionId,
                        StringComparison.OrdinalIgnoreCase)),
            };
        }

        public void BeginAcceptanceSavedMagicDirectionCase(
            int slot,
            string magicId,
            JxqyFloat2 destinationOffset)
        {
            if (_acceptanceMagicDirectionPreparing)
                return;
            _acceptanceMagicDirectionPreparing = true;
            _acceptanceMagicDirectionReady = false;
            _acceptanceMagicDirectionTriggered = false;
            _acceptanceMagicDirectionError = string.Empty;
            _acceptanceMagicDirectionSlot = slot;
            _acceptanceMagicDirectionSkillIndex = -1;
            _acceptanceMagicDirectionId = magicId ?? string.Empty;
            RunAcceptanceSavedMagicDirectionCaseAsync(
                    slot,
                    magicId,
                    destinationOffset)
                .Forget();
        }

        private async UniTaskVoid
            RunAcceptanceSavedMagicDirectionCaseAsync(
                int slot,
                string magicId,
                JxqyFloat2 destinationOffset)
        {
            try
            {
                if (!_ready || _saveRepository == null || slot < 0 ||
                    string.IsNullOrWhiteSpace(magicId))
                {
                    throw new InvalidOperationException(
                        "Saved-magic direction acceptance has invalid " +
                        "runtime state or arguments.");
                }

                await LoadGameAsync(
                    slot,
                    this.GetCancellationTokenOnDestroy());
                if (_player == null || _skills == null || _combat == null)
                {
                    throw new InvalidOperationException(
                        "Save loaded without a playable combat state.");
                }

                for (int index = 0; index < _skills.Skills.Count; index++)
                {
                    if (!string.Equals(
                            _skills.Skills[index].Magic?.Id,
                            magicId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    _acceptanceMagicDirectionSkillIndex = index;
                    break;
                }
                if (_acceptanceMagicDirectionSkillIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Save slot {slot} does not contain {magicId}.");
                }

                _acceptanceSuppressTraps = true;
                _scriptSession?.Cancel();
                if (_video is Jxqy.UnityAdapters.JxqyUnityVideoPort video)
                    video.RequestSkip();
                _uiSession?.Open(Jxqy.Domain.Presentation.JxqyUiScreen.Hud);
                _gameStarted = true;
                _legacyInputDisabled = false;
                _npcs.IsAiDisabled = true;
                ResetCombatTransientState();
                ClearMagicVisuals();
                _player.Stop();
                _player.SetFighting(false);
                _player.SetState(JxqyCharacterState.Stand);
                _player.EndSpecialAction();
                _playerSpecialAction = null;
                _player.IsVisible = true;
                _player.ManaLimit = false;
                _player.Mana = _player.ManaMax;
                _uiSession?.ClearSelectedSkill();
                _uiSession?.SelectSkill(
                    _acceptanceMagicDirectionSkillIndex);
                _acceptanceMagicDirectionDestination =
                    _player.PositionInWorld + destinationOffset;
                UpdatePlayerVisual();
                CenterCameraOnPlayer();
                _uiSession?.Refresh();
                SubmitFrame();
                UnityEngine.Time.timeScale = 4f;
                _acceptanceMagicDirectionReady = true;
            }
            catch (Exception exception)
            {
                _acceptanceMagicDirectionError = exception.ToString();
            }
            finally
            {
                _acceptanceMagicDirectionPreparing = false;
            }
        }

        public bool TriggerAcceptanceSavedMagicDirectionCase()
        {
            if (!_acceptanceMagicDirectionReady ||
                _acceptanceMagicDirectionTriggered ||
                _acceptanceMagicDirectionSkillIndex < 0 ||
                _player == null || !_player.CanPerformAction)
            {
                return false;
            }

            TryUsePlayerSkill(
                _acceptanceMagicDirectionSkillIndex,
                _acceptanceMagicDirectionDestination,
                null);
            _acceptanceMagicDirectionTriggered =
                _pendingPlayerMagicCast != null;
            return _acceptanceMagicDirectionTriggered;
        }
    }
}
#endif
