namespace TEngine
{
    public interface ISetAssetObject : IMemory
    {
        /// <summary>
        /// 资源定位地址。
        /// </summary>
        string Location { get; }

        /// <summary>
        /// 资源所属包。空字符串表示默认包。
        /// </summary>
        string PackageName { get; }
        
        /// <summary>
        /// Unity资源对象。
        /// </summary>
        public UnityEngine.Object TargetObject { get; set; }

        /// <summary>
        /// 设置资源。
        /// </summary>
        void SetAsset(UnityEngine.Object asset);

        /// <summary>
        /// 是否可以回收。
        /// </summary>
        bool IsCanRelease();
    }
}
