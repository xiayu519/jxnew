#if UNITY_EDITOR
namespace Jxqy.Bootstrap
{
    public sealed class JxqyFollowerAiAcceptanceSnapshot
    {
        public bool IsPreparing;
        public bool IsReady;
        public string Error = string.Empty;
        public string MapStableId = string.Empty;
        public string FollowerName = string.Empty;
        public int FollowerAttackRadius;
        public int FollowerLifeMax;
        public int FollowerAttack;
        public int FollowerDefend;
        public int FollowerLevel;
        public int FollowerCanLevelUp;
        public int SharedKillExpectedExperience;
        public int SharedKillPlayerExperienceDelta;
        public int SharedKillFollowerExperienceDelta;
        public int FollowerMana;
        public int FollowerThew;
        public string FirstMagicId = string.Empty;
        public string SecondMagicId = string.Empty;
        public int SecondMagicMoveKind;
        public int SecondMagicSpecialKind;
        public bool FirstMagicVisualAssetsLoaded;
        public bool SecondMagicVisualAssetsLoaded;
        public int MagicUseCount;
        public int FirstMagicUseCount;
        public int SecondMagicUseCount;
        public int ProjectileSpawnCount;
        public int ContactCount;
        public string LastMagicId = string.Empty;
        public int ActiveFollowerMagicVisualCount;
    }
}
#endif
