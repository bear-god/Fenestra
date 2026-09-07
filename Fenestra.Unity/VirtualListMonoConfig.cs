namespace Fenestra.Unity;

using System;
using Fenestra.Entity;
using UnityEngine;

/// <summary>
/// <see cref="VirtualListMono" /> 的 Inspector 配置（Unity 侧镜像），经 <see cref="ToCore" /> 转换为
/// 纯 C# 的 <see cref="VirtualListConfig" /> 供核心使用。
/// 转换时按字段间约束归一化：Grid 启用时强制定高。
/// 条件显隐（Grid 相关字段等）由 <c>Fenestra.Unity.Editor</c> 的自定义 Inspector 处理。
/// </summary>
[Serializable]
public sealed class VirtualListMonoConfig
{
    /// <summary>滚动主轴。</summary>
    [SerializeField]
    [Tooltip("列表滚动方向：垂直（自上而下）或水平（自左向右）。")]
    private VirtualListAxis _axis = VirtualListAxis.Vertical;

    /// <summary>是否沿主轴反向排列。</summary>
    [SerializeField]
    [Tooltip("沿主轴反向排列（类似 CSS RowReversed / ColumnReversed）：起始元素置于主轴末端，滚动原点在末端。可在运行时经 ApplyConfig 改变。")]
    private bool _reversed;

    /// <summary>初始滚动锚点。</summary>
    [SerializeField]
    [Tooltip(
        "初始滚动位置（Bind / Reset / 改变锚点时定位）：OffsetMin=偏移最小值（0），正向显示首元素、反向显示末元素（默认）；OffsetMax=偏移最大值，正向显示末元素、反向显示首元素（Android reverseLayout 行为）。可在运行时经 ApplyConfig / SetInitialAnchor 改变。")]
    private VirtualListInitialAnchor _initialAnchor = VirtualListInitialAnchor.OffsetMin;

    /// <summary>是否启用 Grid。</summary>
    [SerializeField]
    [Tooltip("启用网格布局（等高 cell 沿交叉轴分列，仅支持定高）。")]
    private bool _gridEnabled;

    /// <summary>按视口交叉轴尺寸自动推断交叉轴元素数。</summary>
    [SerializeField]
    [Tooltip("按视口交叉轴尺寸自动推断交叉轴元素数。")]
    private bool _autoFit;

    /// <summary>AutoFit 参考的单元格交叉轴尺寸（通常取元素预制件交叉轴尺寸）。</summary>
    [SerializeField]
    [Tooltip("自动交叉轴元素数参考的单元格交叉轴尺寸（通常取元素预制件交叉轴尺寸）。")]
    private float _crossSize = 100f;

    /// <summary>固定交叉轴元素数（AutoFit 关闭时生效）。</summary>
    [SerializeField]
    [Tooltip("固定交叉轴元素数（自动推断关闭时生效）。")]
    private int _crossCount = 2;

    /// <summary>交叉轴间距。</summary>
    [SerializeField]
    [Tooltip("交叉轴间距。")]
    private float _crossSpacing;

    /// <summary>元素尺寸模式（Grid 启用时忽略，恒为定高）。</summary>
    [SerializeField]
    [Tooltip("元素尺寸模式：定高（免测量）或变高（逐项测量）。Grid 布局仅支持定高。")]
    private VirtualListItemSizeMode _sizeMode = VirtualListItemSizeMode.Fixed;

    /// <summary>定高模式的元素主轴尺寸（Grid 启用时作为单元格主轴尺寸）。</summary>
    [SerializeField]
    [Tooltip("定高模式的元素主轴尺寸（Grid 布局下作为单元格主轴尺寸）。")]
    private float _size = 100f;

    /// <summary>主轴间距。</summary>
    [SerializeField]
    [Tooltip("元素沿主轴方向的间距（Grid 下为行间距）。")]
    private float _spacing;

    /// <summary>过扫描（元素个数，Grid 下按主轴方向扩展）。</summary>
    [SerializeField]
    [Tooltip("窗口外预实例化的元素个数（Grid 下按主轴方向扩展）。")]
    private int _overscan = 2;

    /// <summary>边缘越界行为。</summary>
    [SerializeField]
    [Tooltip("滚动到边缘时的行为：Clamped 夹紧 / Elastic 回弹。")]
    private VirtualListOverflow _overflow = VirtualListOverflow.Clamped;

    /// <summary>是否要求适配层挂裁剪（弹性越界需要）。</summary>
    [SerializeField]
    [Tooltip("是否要求适配层挂载裁剪（弹性越界需要）。初始化时生效，不可在运行时动态改变。")]
    private bool _maskViewport = true;

    /// <summary>滚动主轴（只读）。</summary>
    public VirtualListAxis Axis => _axis;

    /// <summary>是否沿主轴反向排列（只读）。</summary>
    public bool Reversed => _reversed;

    /// <summary>初始滚动锚点（只读）。</summary>
    public VirtualListInitialAnchor InitialAnchor => _initialAnchor;

    /// <summary>元素尺寸模式（只读）。</summary>
    public VirtualListItemSizeMode SizeMode => _sizeMode;

    /// <summary>是否要求适配层挂裁剪（只读）。</summary>
    public bool MaskViewport => _maskViewport;

    /// <summary>转换为核心配置，并归一化（Grid 启用时强制定高）。</summary>
    /// <returns>核心配置。</returns>
    public VirtualListConfig ToCore()
    {
        var config = new VirtualListConfig
        {
            Axis = _axis,
            Reversed = _reversed,
            InitialAnchor = _initialAnchor,
            SizeMode = _sizeMode,
            FixedItemSize = _size,
            Spacing = _spacing,
            Overscan = _overscan,
            Overflow = _overflow,
            MaskViewport = _maskViewport,
            Grid = new VirtualListGridConfig
            {
                Enabled = _gridEnabled,
                CrossCount = _crossCount,
                AutoFit = _autoFit,
                CrossSize = _crossSize,
                CrossSpacing = _crossSpacing,
            },
        };
        if (config.Grid.Enabled)
        {
            config.SizeMode = VirtualListItemSizeMode.Fixed;
        }

        return config;
    }
}
