namespace VirtualList;

    /// <summary>边缘越界行为。</summary>
    public enum VirtualListOverflow
    {
        /// <summary>硬边界，不允许越界。</summary>
        Clamped,

        /// <summary>弹性回弹，允许越界后回弹。</summary>
        Elastic,
    }