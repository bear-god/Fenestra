namespace Fenestra.Tests;

using Fenestra.Entity;

/// <summary>测试工厂：构造编排器与桩依赖。</summary>
internal static class TestListFactory
{
    /// <summary>创建编排器（未绑定）。</summary>
    /// <param name="config">列表配置。</param>
    /// <param name="provider">桩 Provider（可空，默认创建）。</param>
    /// <param name="logger">桩日志（可空）。</param>
    /// <returns>编排器与桩依赖。</returns>
    public static (VirtualListCore List, StubItemProvider Provider, StubLogger Logger) Create(
        VirtualListConfig config,
        StubItemProvider provider = null,
        StubLogger logger = null)
    {
        provider ??= new StubItemProvider();
        logger ??= new StubLogger();
        var list = new VirtualListCore(config, provider, logger);
        // 统一默认视口 1000×100；需其它视口的用例在创建后自行 SetViewportSize 覆盖。
        list.SetViewportSize(1000f, 100f);
        return (list, provider, logger);
    }

    /// <summary>创建垂直定高配置。</summary>
    /// <param name="fixedSize">定高尺寸。</param>
    /// <param name="overscan">过扫描。</param>
    /// <param name="overflow">越界行为。</param>
    /// <param name="viewportMain">主轴视口尺寸。</param>
    /// <param name="viewportCross">交叉轴视口尺寸。</param>
    /// <param name="spacing">主轴间距。</param>
    /// <returns>配置。</returns>
    public static VirtualListConfig FixedConfig(
        float fixedSize = 100f,
        int overscan = 2,
        VirtualListOverflow overflow = VirtualListOverflow.Clamped,
        float viewportMain = 1000f,
        float viewportCross = 100f,
        float spacing = 0f)
    {
        return new VirtualListConfig
        {
            Axis = VirtualListAxis.Vertical,
            SizeMode = VirtualListItemSizeMode.Fixed,
            FixedItemSize = fixedSize,
            Overscan = overscan,
            Spacing = spacing,
            Overflow = overflow,
            MaskViewport = true,
        };
    }

    /// <summary>创建垂直变高配置。</summary>
    /// <param name="overscan">过扫描。</param>
    /// <param name="overflow">越界行为。</param>
    /// <param name="viewportMain">主轴视口尺寸。</param>
    /// <param name="spacing">主轴间距。</param>
    /// <returns>配置。</returns>
    public static VirtualListConfig VariableConfig(
        int overscan = 2,
        VirtualListOverflow overflow = VirtualListOverflow.Clamped,
        float viewportMain = 1000f,
        float spacing = 0f)
    {
        return new VirtualListConfig
        {
            Axis = VirtualListAxis.Vertical,
            SizeMode = VirtualListItemSizeMode.Variable,
            Overscan = overscan,
            Spacing = spacing,
            Overflow = overflow,
            MaskViewport = true,
        };
    }
}
