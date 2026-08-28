#if UNITY_EDITOR
using Jxqy.Domain.World;

namespace Jxqy.Bootstrap
{
    public sealed class JxqyMagicDirectionAcceptanceSnapshot
    {
        public bool IsPreparing { get; set; }
        public bool IsReady { get; set; }
        public bool Triggered { get; set; }
        public bool CastPending { get; set; }
        public string Error { get; set; } = string.Empty;
        public int Slot { get; set; } = -1;
        public string MapStableId { get; set; } = string.Empty;
        public JxqyIntPoint PlayerTile { get; set; }
        public string MagicId { get; set; } = string.Empty;
        public int MagicLevel { get; set; }
        public JxqyFloat2 Destination { get; set; }
        public int ActiveMagicVisualCount { get; set; }
    }
}
#endif
