#if UNITY_EDITOR
namespace Jxqy.Bootstrap
{
    public sealed class JxqyMartialArtAcceptanceSnapshot
    {
        public bool IsPreparing;
        public bool IsReady;
        public bool IsCultivationAttack;
        public bool Triggered;
        public bool Finished;
        public string Error = string.Empty;
        public string MagicFile = string.Empty;
        public string MagicName = string.Empty;
        public int Level;
        public int MoveKind;
        public int EffectLevel;
        public int Region;
        public string FlyingImage = string.Empty;
        public string VanishImage = string.Empty;
        public string SuperModeImage = string.Empty;
        public string FlyingAnimationStableId = string.Empty;
        public string VanishAnimationStableId = string.Empty;
        public string SuperModeAnimationStableId = string.Empty;
        public string ExpectedActionFile = string.Empty;
        public bool CultivationActionCached;
        public string CultivationActionStableId = string.Empty;
        public string ActivePlayerAnimationStableId = string.Empty;
        public int ActivePlayerAnimationFrame;
        public int PlayerState = -1;
        public bool CultivationActionPresented;
        public string AttackFile = string.Empty;
        public int AttackMoveKind;
        public int SpawnedProjectileCount;
        public int[] SpawnDirectionIndices = System.Array.Empty<int>();
        public float[] SpawnDelaySeconds = System.Array.Empty<float>();
        public int NaturalExpiryVanishCount;
        public int NaturalExpirySilentCount;
        public int ExpectedAttackDirectionIndex = -1;
        public int ActiveMagicVisualCount;
        public string ActiveMagicAnimationStableId = string.Empty;
        public int ActiveMagicAnimationFrame = -1;
    }
}
#endif
