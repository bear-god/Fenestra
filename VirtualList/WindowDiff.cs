namespace VirtualList;

    /// <summary>实例化窗口 diff：自上次消费以来进入/离开的元素索引（内部使用，复用数组避免分配）。</summary>
    internal struct WindowDiff
    {
        /// <summary>进入实例化窗口的索引缓冲区。</summary>
        public int[] Entering;

        /// <summary>进入索引数。</summary>
        public int EnteringCount;

        /// <summary>离开实例化窗口的索引缓冲区。</summary>
        public int[] Leaving;

        /// <summary>离开索引数。</summary>
        public int LeavingCount;
    }