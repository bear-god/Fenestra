namespace Fenestra.Entity;

/// <summary>元素尺寸模式。</summary>
public enum VirtualListItemSizeMode
{
    /// <summary>定高：免测量。</summary>
    Fixed,

    /// <summary>变高：逐项测量并缓存。</summary>
    Variable,
}
