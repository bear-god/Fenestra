namespace VirtualList;

    /// <summary>初始滚动锚点（列表初始 / 重置时的滚动位置；与反向排列正交）。
    /// 采用物理偏移语义（偏移最小值 / 偏移最大值），区别于 CSS Flex 的「逻辑 start / end」：
    /// 反向排列时本锚点仍指向滚动范围端点，不随方向翻转。</summary>
    public enum VirtualListInitialAnchor
    {
        /// <summary>偏移最小值（0）：正向列表显示首元素；反向列表显示末元素（默认，保持既有行为）。</summary>
        OffsetMin,

        /// <summary>偏移最大值（MaxScrollOffset）：正向列表显示末元素；反向列表显示首元素（Android reverseLayout 行为）。</summary>
        OffsetMax,
    }