namespace Fenestra.Entity;

using System;

/// <summary>列表配置（纯 C# 值对象，序列化 / Inspector 展示由宿主适配层负责）。</summary>
[Serializable]
public struct VirtualListConfig
{
    /// <summary>滚动主轴。</summary>
    public VirtualListAxis Axis;

    /// <summary>
    /// 是否沿主轴反向排列（类似 CSS RowReversed / ColumnReversed：起始元素置于主轴末端）。
    /// 仅反转主轴顺序与滚动原点；交叉轴列顺序不受影响。可在运行时经 <c>ApplyConfig</c> 改变。
    /// </summary>
    public bool Reversed;

    /// <summary>
    /// 初始滚动锚点（列表初始 / 重置时的滚动位置；正交于反向排列）。
    /// 可在运行时经 <c>ApplyConfig</c> / <c>SetInitialAnchor</c> 改变。
    /// </summary>
    public VirtualListInitialAnchor InitialAnchor;

    /// <summary>元素尺寸模式。</summary>
    public VirtualListItemSizeMode SizeMode;

    /// <summary>定高模式的元素主轴尺寸。</summary>
    public float FixedItemSize;

    /// <summary>过扫描（元素个数，Grid 下按主轴方向扩展）。</summary>
    public int Overscan;

    /// <summary>主轴间距。</summary>
    public float Spacing;

    /// <summary>边缘越界行为。</summary>
    public VirtualListOverflow Overflow;

    /// <summary>Grid 配置（仅支持定高模式，见设计 §2.2）。</summary>
    public VirtualListGridConfig Grid;

    /// <summary>是否要求适配层挂裁剪（弹性越界需要）。</summary>
    public bool MaskViewport;
}
