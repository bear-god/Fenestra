using System;

namespace VirtualList;

    /// <summary>Grid 配置。仅支持定高模式（等高 cell，见设计 §2.2）。</summary>
    [Serializable]
    public struct VirtualListGridConfig
    {
        /// <summary>是否启用 Grid。</summary>
        public bool Enabled;

        /// <summary>固定交叉轴元素数；0 表示 AutoFit。</summary>
        public int CrossCount;

        /// <summary>按视口交叉轴尺寸自动推断交叉轴元素数（仅等高 cell）。</summary>
        public bool AutoFit;

        /// <summary>AutoFit 参考的单元格交叉轴尺寸（通常取元素预制件交叉轴尺寸）。</summary>
        public float CrossSize;

        /// <summary>交叉轴间距。</summary>
        public float CrossSpacing;
    }