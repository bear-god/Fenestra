namespace Fenestra.Entity;

/// <summary>元素摆放参数（纯数值，由适配层落地为 RectTransform）。</summary>
public readonly struct ItemPlacement
{
    /// <summary>滚动主轴（供适配层决定 main/cross 映射到 x/y）。</summary>
    public readonly VirtualListAxis Axis;

    /// <summary>主轴起点偏移（垂直=自顶向下；水平=自左向右）。</summary>
    public readonly float MainOffset;

    /// <summary>交叉轴起点偏移。</summary>
    public readonly float CrossOffset;

    /// <summary>主轴尺寸（定高=FixedItemSize；变高=已测量值）。</summary>
    public readonly float MainSize;

    /// <summary>交叉轴尺寸（Grid=单元格宽；非 Grid=视口交叉尺寸）。</summary>
    public readonly float CrossSize;

    /// <summary>初始化摆放参数。</summary>
    /// <param name="axis">滚动主轴。</param>
    /// <param name="mainOffset">主轴起点偏移。</param>
    /// <param name="crossOffset">交叉轴起点偏移。</param>
    /// <param name="mainSize">主轴尺寸。</param>
    /// <param name="crossSize">交叉轴尺寸。</param>
    public ItemPlacement(VirtualListAxis axis, float mainOffset, float crossOffset, float mainSize, float crossSize)
    {
        Axis = axis;
        MainOffset = mainOffset;
        CrossOffset = crossOffset;
        MainSize = mainSize;
        CrossSize = crossSize;
    }
}
