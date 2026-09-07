namespace Fenestra.Tests;

using System;
using Fenestra.Entity;
using NUnit.Framework;

/// <summary>
/// 编排器黑盒测试：运行时配置（Cfg）与独立 setter（Set）快速路径用例，以及初始锚点（Anchor）用例——
/// <see cref="VirtualListCore.ApplyConfig" /> 轴向 / 尺寸模式 / Grid 动态改变、非法配置抛异常，
/// SetAxis / SetReversed / SetSizeMode / SetFixedItemSize / SetSpacing / SetOverscan / SetOverflow /
/// SetGrid 各自的行为，以及 InitialAnchor 的初始 / 重置定位与运行时切换。
/// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase" />。
/// </summary>
[TestFixture]
public class VirtualListCoreConfigTests : VirtualListCoreTestBase
{
    /// <summary>
    /// 运行时切换主轴：ItemPlacement.Axis 与 Config 同步更新，定高下偏移语义不变。
    /// </summary>
    /// <remarks>
    /// 功能：ApplyConfig 轴向动态改变（定高）。
    /// 期望：Vertical→Horizontal 后 Config.Axis=Horizontal、index1 摆放 Axis=Horizontal 且 MainOffset 仍 100。
    /// </remarks>
    [Test]
    public void Cfg1_ApplyConfig_AxisChangeUpdatesPlacementAxis()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        BindInts(list, 20);
        Assert.That(View(provider, 1).LastPlacement.Axis, Is.EqualTo(VirtualListAxis.Vertical));

        config.Axis = VirtualListAxis.Horizontal;
        list.ApplyConfig(config);

        Assert.That(list.Config.Axis, Is.EqualTo(VirtualListAxis.Horizontal));
        Assert.That(View(provider, 1).LastPlacement.Axis, Is.EqualTo(VirtualListAxis.Horizontal));
        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
    }

    /// <summary>
    /// 变高模式切换轴向：测量语义变化 → 活跃元素以新主轴全量重测。
    /// </summary>
    /// <remarks>
    /// 功能：ApplyConfig 轴向动态改变（变高重测）。
    /// 期望：Vertical→Horizontal 后活跃元素 Measure 收到 Horizontal（LastMeasureAxis），摆放 Axis=Horizontal。
    /// </remarks>
    [Test]
    public void Cfg2_ApplyConfig_VariableAxisChangeRemeasures()
    {
        var config = TestListFactory.VariableConfig();
        var provider = new StubItemProvider(_ => new StubItemView
        {
            MeasuredSize = 100f,
        });
        var (list, _, _) = CreateList(config, provider);
        BindInts(list, 10);
        Assert.That(View(provider, 0).LastMeasureAxis, Is.EqualTo(VirtualListAxis.Vertical));

        config.Axis = VirtualListAxis.Horizontal;
        list.ApplyConfig(config);

        Assert.That(list.Config.Axis, Is.EqualTo(VirtualListAxis.Horizontal));
        Assert.That(View(provider, 0).LastMeasureAxis, Is.EqualTo(VirtualListAxis.Horizontal));
        Assert.That(View(provider, 1).LastPlacement.Axis, Is.EqualTo(VirtualListAxis.Horizontal));
    }

    /// <summary>
    /// 运行时启用 Grid：交叉轴分列摆放即时生效。
    /// </summary>
    /// <remarks>
    /// 功能：ApplyConfig Grid 动态改变。
    /// 期望：视口交叉 300、3 列 → index1 CrossOffset=100、index2=200、index3=0（第二行）且 MainOffset=100。
    /// </remarks>
    [Test]
    public void Cfg3_ApplyConfig_EnableGrid()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 300f);
        BindInts(list, 9);

        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            CrossCount = 3,
            CrossSpacing = 0f,
        };
        list.ApplyConfig(config);

        Assert.That(list.Config.Grid.Enabled, Is.True);
        Assert.That(View(provider, 1).LastPlacement.CrossOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.CrossOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
    }

    /// <summary>
    /// ApplyConfig 传入非法配置（变高 + Grid）抛异常，且不改变当前生效配置。
    /// </summary>
    /// <remarks>
    /// 边界：运行时改配违反「Grid 仅支持定高」约束（设计 §2.2）。
    /// 期望：抛 InvalidOperationException，list.Config.Grid.Enabled 仍为 false。
    /// </remarks>
    [Test]
    public void Cfg4_ApplyConfig_InvalidGridVariableThrows()
    {
        var config = TestListFactory.VariableConfig();
        var (list, _, _) = CreateList(config);
        BindInts(list, 5);

        var invalid = config;
        invalid.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            CrossCount = 2,
        };

        Assert.Throws<InvalidOperationException>(() => list.ApplyConfig(invalid));
        Assert.That(list.Config.Grid.Enabled, Is.False);
    }

    /// <summary>
    /// 运行时从定高切换到变高：活跃元素由免测量转为全量测量。
    /// </summary>
    /// <remarks>
    /// 功能：ApplyConfig 尺寸模式动态改变。
    /// 期望：定高阶段 MeasureCount=0；切换变高后 MeasureCount≥1 且以当前主轴测量。
    /// </remarks>
    [Test]
    public void Cfg5_ApplyConfig_FixedToVariableRemeasures()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        BindInts(list, 10);
        Assert.That(View(provider, 0).MeasureCount, Is.EqualTo(0));

        config.SizeMode = VirtualListItemSizeMode.Variable;
        list.ApplyConfig(config);

        Assert.That(list.Config.SizeMode, Is.EqualTo(VirtualListItemSizeMode.Variable));
        Assert.That(View(provider, 0).MeasureCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(View(provider, 0).LastMeasureAxis, Is.EqualTo(VirtualListAxis.Vertical));
    }

    /// <summary>
    /// SetOverflow：O(1) 仅更新物理与配置，不触发布局 / 窗口重算。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter 快速路径（越界行为）。
    /// 期望：Config.Overflow 更新为 Elastic，可见窗口保持不变。
    /// </remarks>
    [Test]
    public void Set1_SetOverflow_O1NoLayoutChange()
    {
        var config = TestListFactory.FixedConfig();
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);
        var before = list.Snapshot();

        list.SetOverflow(VirtualListOverflow.Elastic);

        Assert.That(list.Config.Overflow, Is.EqualTo(VirtualListOverflow.Elastic));
        Assert.That(list.Snapshot().FirstVisibleIndex, Is.EqualTo(before.FirstVisibleIndex));
        Assert.That(list.Snapshot().LastVisibleIndex, Is.EqualTo(before.LastVisibleIndex));
    }

    /// <summary>
    /// SetReversed：运行时开启反向排列，摆放即时镜像到主轴末端。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（反向排列）。
    /// 期望：20×100、视口 1000、偏移 0 时窗口 [10,19]：index19 MainOffset=0、index18=100、index10=900。
    /// </remarks>
    [Test]
    public void Set2_SetReversedFlipsPlacement()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        BindInts(list, 20);

        list.SetReversed(true);

        Assert.That(list.Config.Reversed, Is.True);
        Assert.That(View(provider, 19).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 18).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 10).LastPlacement.MainOffset, Is.EqualTo(900f).Within(0.01f));
    }

    /// <summary>
    /// SetAxis：变高模式切换主轴 → 活跃元素以新主轴全量重测，摆放轴向同步。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（轴向，变高重测）。
    /// 期望：Vertical→Horizontal 后 Config.Axis=Horizontal、Measure 收到 Horizontal、摆放 Axis=Horizontal。
    /// </remarks>
    [Test]
    public void Set3_SetAxisVariableRemeasures()
    {
        var config = TestListFactory.VariableConfig();
        var provider = new StubItemProvider(_ => new StubItemView
        {
            MeasuredSize = 100f,
        });
        var (list, _, _) = CreateList(config, provider);
        BindInts(list, 10);
        Assert.That(View(provider, 0).LastMeasureAxis, Is.EqualTo(VirtualListAxis.Vertical));

        list.SetAxis(VirtualListAxis.Horizontal);

        Assert.That(list.Config.Axis, Is.EqualTo(VirtualListAxis.Horizontal));
        Assert.That(View(provider, 0).LastMeasureAxis, Is.EqualTo(VirtualListAxis.Horizontal));
        Assert.That(View(provider, 1).LastPlacement.Axis, Is.EqualTo(VirtualListAxis.Horizontal));
    }

    /// <summary>
    /// SetSizeMode：定高→变高，活跃元素由免测量转为全量测量。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（尺寸模式）。
    /// 期望：定高阶段 MeasureCount=0；切换变高后 MeasureCount≥1 且以当前主轴测量。
    /// </remarks>
    [Test]
    public void Set4_SetSizeModeFixedToVariableRemeasures()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        BindInts(list, 10);
        Assert.That(View(provider, 0).MeasureCount, Is.EqualTo(0));

        list.SetSizeMode(VirtualListItemSizeMode.Variable);

        Assert.That(list.Config.SizeMode, Is.EqualTo(VirtualListItemSizeMode.Variable));
        Assert.That(View(provider, 0).MeasureCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(View(provider, 0).LastMeasureAxis, Is.EqualTo(VirtualListAxis.Vertical));
    }

    /// <summary>
    /// SetFixedItemSize：定高尺寸变化即时生效于摆放与内容尺寸。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（定高尺寸）。
    /// 期望：100→50 后 ContentSize 1000→500、index1 MainOffset=50。
    /// </remarks>
    [Test]
    public void Set5_SetFixedItemSizeUpdatesLayout()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        BindInts(list, 10);
        Assert.That(list.Snapshot().ContentSize, Is.EqualTo(1000f).Within(0.01f));

        list.SetFixedItemSize(50f);

        Assert.That(list.Config.FixedItemSize, Is.EqualTo(50f));
        Assert.That(list.Snapshot().ContentSize, Is.EqualTo(500f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(50f).Within(0.01f));
    }

    /// <summary>
    /// SetSpacing：主轴间距变化即时计入内容尺寸。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（主轴间距）。
    /// 期望：10 项 × 100 + 9 个间距 × 10 = 1090。
    /// </remarks>
    [Test]
    public void Set6_SetSpacingUpdatesContentSize()
    {
        var config = TestListFactory.FixedConfig();
        var (list, _, _) = CreateList(config);
        BindInts(list, 10);
        Assert.That(list.Snapshot().ContentSize, Is.EqualTo(1000f).Within(0.01f));

        list.SetSpacing(10f);

        Assert.That(list.Snapshot().ContentSize, Is.EqualTo(1090f).Within(0.01f));
    }

    /// <summary>
    /// SetOverscan：仅扩展实例化窗口，可见窗口不变；负值按 0 处理。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（过扫描）。
    /// 期望：overscan 0→2 后 LastInstantiatedIndex 9→11、可见窗口仍 [0,9]；SetOverscan(-5) 归 0。
    /// </remarks>
    [Test]
    public void Set7_SetOverscanExtendsInstantiatedWindow()
    {
        var config = TestListFactory.FixedConfig(overscan: 0);
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);
        Assert.That(list.Snapshot().LastInstantiatedIndex, Is.EqualTo(9));

        list.SetOverscan(2);

        Assert.That(list.Config.Overscan, Is.EqualTo(2));
        Assert.That(list.Snapshot().LastInstantiatedIndex, Is.EqualTo(11));
        Assert.That(list.Snapshot().FirstVisibleIndex, Is.EqualTo(0));

        list.SetOverscan(-5);
        Assert.That(list.Config.Overscan, Is.EqualTo(0));
    }

    /// <summary>
    /// SetGrid：运行时启用 Grid 分列摆放即时生效。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（Grid）。
    /// 期望：视口交叉 300、3 列 → index1 CrossOffset=100、index2=200、index3=0（第二行）且 MainOffset=100。
    /// </remarks>
    [Test]
    public void Set8_SetGridEnablesColumns()
    {
        var config = TestListFactory.FixedConfig();
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 300f);
        BindInts(list, 9);

        list.SetGrid(
            new VirtualListGridConfig
            {
                Enabled = true,
                CrossCount = 3,
                CrossSpacing = 0f,
            });

        Assert.That(list.Config.Grid.Enabled, Is.True);
        Assert.That(View(provider, 1).LastPlacement.CrossOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.CrossOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
    }

    /// <summary>
    /// SetGrid：变高 + Grid 非法抛异常，且不改变当前生效配置。
    /// </summary>
    /// <remarks>
    /// 边界：独立 setter 违反「Grid 仅支持定高」约束。
    /// 期望：抛 InvalidOperationException，list.Config.Grid.Enabled 仍为 false。
    /// </remarks>
    [Test]
    public void Set9_SetGridVariableThrows()
    {
        var config = TestListFactory.VariableConfig();
        var (list, _, _) = CreateList(config);
        BindInts(list, 5);

        Assert.Throws<InvalidOperationException>(() => list.SetGrid(
            new VirtualListGridConfig
            {
                Enabled = true,
                CrossCount = 2,
            }));
        Assert.That(list.Config.Grid.Enabled, Is.False);
    }

    /// <summary>
    /// 反向 + InitialAnchor.OffsetMax：顺序反转但初始显示首元素（Android reverseLayout 行为）。
    /// </summary>
    /// <remarks>
    /// 功能：初始锚点（反向 + OffsetMax）。
    /// 期望：20×100、视口 1000（max=1000）：Bind 后偏移 1000，窗口 [0,9]，index0 MainOffset=1900。
    /// </remarks>
    [Test]
    public void Anchor1_ReversedEndShowsFirstElement()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        config.InitialAnchor = VirtualListInitialAnchor.OffsetMax;
        var (list, provider, _) = CreateList(config);
        BindInts(list, 20);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(snapshot.MaxScrollOffset).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(9));
        Assert.That(View(provider, 0).LastPlacement.MainOffset, Is.EqualTo(1900f).Within(0.01f));
    }

    /// <summary>
    /// 默认 InitialAnchor.OffsetMin（反向）保持既有 CSS 语义：偏移 0 显示末元素。
    /// </summary>
    /// <remarks>
    /// 功能：初始锚点默认值不改变反向既有行为。
    /// 期望：Bind 后偏移 0，窗口 [10,19]。
    /// </remarks>
    [Test]
    public void Anchor2_DefaultStartKeepsReversedEndFirst()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(10));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(19));
    }

    /// <summary>
    /// 正向 + InitialAnchor.OffsetMax：初始显示末元素（无反转的 Chat 模式）。
    /// </summary>
    /// <remarks>
    /// 功能：初始锚点（正向 + OffsetMax）。
    /// 期望：Bind 后偏移 1000（max），窗口 [10,19]。
    /// </remarks>
    [Test]
    public void Anchor3_ForwardEndShowsLastElement()
    {
        var config = TestListFactory.FixedConfig();
        config.InitialAnchor = VirtualListInitialAnchor.OffsetMax;
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(snapshot.MaxScrollOffset).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(10));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(19));
    }

    /// <summary>
    /// 集合 Reset 后重新定位到初始锚点（物理位置复位，不残留旧偏移）。
    /// </summary>
    /// <remarks>
    /// 功能：初始锚点在 Reset 时生效。
    /// 期望：Bind 后偏移 1000，ScrollToOffset(500) 后 500；Clear 触发 Reset 后空集合锚点取 max=0，
    /// 物理偏移复位为 0（残留旧值 500 视为复位失败）。
    /// </remarks>
    [Test]
    public void Anchor4_ResetRepositionsToAnchor()
    {
        var config = TestListFactory.FixedConfig();
        config.InitialAnchor = VirtualListInitialAnchor.OffsetMax;
        var (list, _, _) = CreateList(config);
        var collection = BindInts(list, 20);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(1000f).Within(0.01f));

        list.ScrollToOffset(500f);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(500f).Within(0.01f));

        collection.Clear();

        Assert.That(list.Snapshot().MaxScrollOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// SetInitialAnchor：运行时改变初始锚点立即重新定位。
    /// </summary>
    /// <remarks>
    /// 功能：独立 setter（初始锚点）。
    /// 期望：OffsetMin→OffsetMax 后偏移 0→1000（max）；OffsetMax→OffsetMin 后回到 0。
    /// </remarks>
    [Test]
    public void Anchor5_SetInitialAnchorRepositions()
    {
        var config = TestListFactory.FixedConfig();
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));

        list.SetInitialAnchor(VirtualListInitialAnchor.OffsetMax);

        Assert.That(list.Config.InitialAnchor, Is.EqualTo(VirtualListInitialAnchor.OffsetMax));
        Assert.That(list.Snapshot().Offset, Is.EqualTo(1000f).Within(0.01f));

        list.SetInitialAnchor(VirtualListInitialAnchor.OffsetMin);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// ApplyConfig 改变 InitialAnchor：立即重新定位到新锚点。
    /// </summary>
    /// <remarks>
    /// 功能：批量改配中的初始锚点变更。
    /// 期望：OffsetMin→OffsetMax 后偏移 0→1000（max）。
    /// </remarks>
    [Test]
    public void Anchor6_ApplyConfigAnchorChangeRepositions()
    {
        var config = TestListFactory.FixedConfig();
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));

        config.InitialAnchor = VirtualListInitialAnchor.OffsetMax;
        list.ApplyConfig(config);

        Assert.That(list.Config.InitialAnchor, Is.EqualTo(VirtualListInitialAnchor.OffsetMax));
        Assert.That(list.Snapshot().Offset, Is.EqualTo(1000f).Within(0.01f));
    }
}
