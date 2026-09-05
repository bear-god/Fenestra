namespace VirtualList;

    /// <summary>可见窗口（首/尾可见 index），VisibleWindowChanged 流负载。</summary>
    public readonly struct VisibleWindow
    {
        /// <summary>首可见 index。</summary>
        public readonly int First;

        /// <summary>尾可见 index。</summary>
        public readonly int Last;

        /// <summary>初始化可见窗口。</summary>
        /// <param name="first">首可见 index。</param>
        /// <param name="last">尾可见 index。</param>
        public VisibleWindow(int first, int last)
        {
            First = first;
            Last = last;
        }
    }