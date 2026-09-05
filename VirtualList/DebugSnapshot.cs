namespace VirtualList;

    /// <summary>诊断快照（编排器公开接口，见设计 D14），不含业务逻辑。</summary>
    public struct DebugSnapshot
    {
        /// <summary>首可见 index（纯视口，不含 overscan）。</summary>
        public int FirstVisibleIndex;

        /// <summary>尾可见 index（纯视口，不含 overscan）。</summary>
        public int LastVisibleIndex;

        /// <summary>首实例化 index（视口 + overscan）。</summary>
        public int FirstInstantiatedIndex;

        /// <summary>尾实例化 index（视口 + overscan）。</summary>
        public int LastInstantiatedIndex;

        /// <summary>主轴偏移（弹性越界时可能超出 [0, MaxScrollOffset]）。</summary>
        public float Offset;

        /// <summary>主轴总内容尺寸。</summary>
        public float ContentSize;

        /// <summary>最大可滚动偏移。</summary>
        public float MaxScrollOffset;

        /// <summary>当前实例化元素数。</summary>
        public int InstantiatedCount;
    }