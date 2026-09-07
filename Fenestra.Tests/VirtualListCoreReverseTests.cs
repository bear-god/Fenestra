namespace Fenestra.Tests;

using System;
using Fenestra.Entity;
using NUnit.Framework;
using R3;

/// <summary>
/// 编排器黑盒测试：反向排列（Reversed）专项用例——
/// 全部在构建时即启用 Reversed（不经 <c>SetReversed</c> / <c>ApplyConfig</c>），
/// 专测反向布局逻辑本身：内容度量不因反向改变、全滚动范围可见窗口、变高 / Grid / 间距参与反向镜像、
/// 拖拽跟手、边界事件物理语义、集合增补、overscan 实例化窗口。
/// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase" />。
/// </summary>
[TestFixture]
public class VirtualListCoreReverseTests : VirtualListCoreTestBase
{
    /// <summary>
    /// 反向只镜像摆放，不改变内容度量（ContentSize / MaxScrollOffset 与正向一致）。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列的内容度量不变式。
    /// 期望：20×100 定高、视口 1000 下，反向与正向 ContentSize 均为 2000、MaxScrollOffset 均为 1000、偏移均 0。
    /// </remarks>
    [Test]
    public void R1_Reverse_ContentMetricsMatchForward()
    {
        var reversedConfig = TestListFactory.FixedConfig();
        reversedConfig.Reversed = true;
        var (reversedList, _, _) = CreateList(reversedConfig);
        BindInts(reversedList, 20);

        var (forwardList, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(forwardList, 20);

        var reversed = reversedList.Snapshot();
        var forward = forwardList.Snapshot();
        Assert.That(reversed.ContentSize, Is.EqualTo(forward.ContentSize).Within(0.01f));
        Assert.That(reversed.MaxScrollOffset, Is.EqualTo(forward.MaxScrollOffset).Within(0.01f));
        Assert.That(reversed.ContentSize, Is.EqualTo(2000f).Within(0.01f));
        Assert.That(reversed.MaxScrollOffset, Is.EqualTo(1000f).Within(0.01f));
        Assert.That(reversed.Offset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// 反向列表滚动到中段偏移时，可见窗口为正确的中间元素段（首尾两端之外的窗口正确性）。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列全滚动范围的可见窗口。
    /// 期望：20×100、视口 1000、偏移 500（物理窗口 [500,1500]）→ 窗口 [5,14]，
    /// index10 MainOffset=900、index5 MainOffset=1400。
    /// </remarks>
    [Test]
    public void R2_Reverse_MiddleScroll_VisibleWindowIsMiddleSegment()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, provider, _) = CreateList(config);
        BindInts(list, 20);

        list.ScrollToOffset(500f);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(5));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(14));
        Assert.That(View(provider, 10).LastPlacement.MainOffset, Is.EqualTo(900f).Within(0.01f));
        Assert.That(View(provider, 5).LastPlacement.MainOffset, Is.EqualTo(1400f).Within(0.01f));
    }

    /// <summary>
    /// 反向 + 变高：元素按测量尺寸自主轴末端向起点镜像摆放。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列（变高尺寸）的摆放换算。
    /// 期望：高表 [100,50,200]、ContentSize=350：index2（200）MainOffset=0、index1（50）=200、index0（100）=250。
    /// </remarks>
    [Test]
    public void R3_Reverse_VariableSize_PlacesFromEnd()
    {
        var heights = new float[]
        {
            100, 50, 200,
        };
        var config = TestListFactory.VariableConfig();
        config.Reversed = true;
        var provider = new StubItemProvider(_ => new StubItemView
        {
            HeightProvider = i => heights[Math.Min(i, heights.Length - 1)],
        });
        var (list, _, _) = CreateList(config, provider);
        BindInts(list, 3);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.ContentSize, Is.EqualTo(350f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(2));
        Assert.That(View(provider, 2).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.MainSize, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 0).LastPlacement.MainOffset, Is.EqualTo(250f).Within(0.01f));
    }

    /// <summary>
    /// 反向 + Grid：交叉轴分列不变，主轴按行自末端向起点镜像（末行置于物理起点）。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列（Grid）的摆放换算。
    /// 期望：3 列 × 9 项、ContentSize=300：行 2（index6/7/8）MainOffset=0、行 1（index3）MainOffset=100、
    /// 行 0（index0/2）MainOffset=200；CrossOffset 按列 0/100/200。
    /// </remarks>
    [Test]
    public void R4_Reverse_Grid_PlacesRowsFromEnd()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            CrossCount = 3,
            CrossSpacing = 0f,
        };
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 300f);
        BindInts(list, 9);

        Assert.That(list.Snapshot().FirstVisibleIndex, Is.EqualTo(0));
        Assert.That(list.Snapshot().LastVisibleIndex, Is.EqualTo(8));
        Assert.That(View(provider, 6).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 6).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 8).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 8).LastPlacement.CrossOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 0).LastPlacement.MainOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.MainOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.CrossOffset, Is.EqualTo(200f).Within(0.01f));
    }

    /// <summary>
    /// 反向 + 间距：主轴间距参与反向镜像换算，内容度量仍为正向公式。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列（Spacing）的镜像换算。
    /// 期望：12 项 × 100、间距 10 → ContentSize=1310、max=310；偏移 0 时窗口 [2,11]，
    /// index11 MainOffset=0、index10=110、index1=1100、index0=1210（实例化但超出视口）。
    /// </remarks>
    [Test]
    public void R5_Reverse_Spacing_ParticipatesInMirror()
    {
        var config = TestListFactory.FixedConfig(spacing: 10f);
        config.Reversed = true;
        var (list, provider, _) = CreateList(config);
        BindInts(list, 12);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.ContentSize, Is.EqualTo(1310f).Within(0.01f));
        Assert.That(snapshot.MaxScrollOffset, Is.EqualTo(310f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(2));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(11));
        Assert.That(View(provider, 11).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 10).LastPlacement.MainOffset, Is.EqualTo(110f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(1100f).Within(0.01f));
        Assert.That(View(provider, 0).LastPlacement.MainOffset, Is.EqualTo(1210f).Within(0.01f));
    }

    /// <summary>
    /// 反向列表拖拽跟手（物理偏移语义），窗口随偏移向低索引段移动，越出视口的末元素被隐藏。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列的拖拽输入与可见性。
    /// 期望：偏移 0→100 后窗口 [10,19]→[9,18]，index9 可见、index19（物理 [0,100)）隐藏。
    /// </remarks>
    [Test]
    public void R6_Reverse_Drag_FollowsPhysicalOffset()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, provider, _) = CreateList(config);
        BindInts(list, 20);
        Assert.That(list.Snapshot().FirstVisibleIndex, Is.EqualTo(10)); // 偏移 0 显示末元素段

        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f); // 手指上移 → 偏移 +100

        var snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(9));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(18));
        Assert.That(View(provider, 9).IsShown, Is.True);
        Assert.That(View(provider, 19).IsShown, Is.False);
    }

    /// <summary>
    /// 反向不翻转边界事件：ReachedTop / ReachedBottom 保持物理偏移语义（0 / MaxScrollOffset）。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列的边界事件物理语义。
    /// 期望：滚到 max（显示首元素）触发 ReachedBottom 一次；滚回 0（显示末元素）触发 ReachedTop 一次。
    /// </remarks>
    [Test]
    public void R7_Reverse_BoundaryEvents_KeepPhysicalSemantics()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, _, _) = CreateList(config);
        var topCount = 0;
        var bottomCount = 0;
        Subscriptions.Add(list.ReachedTop.Subscribe(_ => topCount++));
        Subscriptions.Add(list.ReachedBottom.Subscribe(_ => bottomCount++));
        BindInts(list, 20);

        list.ScrollToOffset(list.Snapshot().MaxScrollOffset);
        Assert.That(bottomCount, Is.EqualTo(1));
        Assert.That(topCount, Is.EqualTo(0));

        list.ScrollToOffset(0f);
        Assert.That(topCount, Is.EqualTo(1));
        Assert.That(bottomCount, Is.EqualTo(1));
    }

    /// <summary>
    /// 反向列表集合增补：内容尺寸扩展、偏移保持 0，新元素进入末段窗口并按反向摆放。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列的集合增补（ObserveAdd）。
    /// 期望：10 项（max=0）全可见 [0,9]；增补至 20 项后 ContentSize=2000、max=1000、偏移仍 0，
    /// 窗口 [10,19]，index19 MainOffset=0、index10=900。
    /// </remarks>
    [Test]
    public void R8_Reverse_CollectionAppend_ExpandsEndWindow()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, provider, _) = CreateList(config);
        var collection = BindInts(list, 10);
        Assert.That(list.Snapshot().LastVisibleIndex, Is.EqualTo(9));

        for (var i = 10; i < 20; i++)
        {
            collection.Add(i);
        }

        var snapshot = list.Snapshot();
        Assert.That(snapshot.ContentSize, Is.EqualTo(2000f).Within(0.01f));
        Assert.That(snapshot.MaxScrollOffset, Is.EqualTo(1000f).Within(0.01f));
        Assert.That(snapshot.Offset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(10));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(19));
        Assert.That(View(provider, 19).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 10).LastPlacement.MainOffset, Is.EqualTo(900f).Within(0.01f));
    }

    /// <summary>
    /// 反向列表 overscan 对称扩展实例化窗口：偏移 0 与 max 两端各扩 2 行。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列的 overscan 实例化窗口。
    /// 期望：20×100、视口 1000、overscan 2：偏移 0 → 实例化 [8,19]（12 个）；偏移 max → 实例化 [0,11]（12 个）。
    /// </remarks>
    [Test]
    public void R9_Reverse_Overscan_ExtendsInstantiatedWindow()
    {
        var config = TestListFactory.FixedConfig(overscan: 2);
        config.Reversed = true;
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);

        var start = list.Snapshot();
        Assert.That(start.FirstInstantiatedIndex, Is.EqualTo(8));
        Assert.That(start.LastInstantiatedIndex, Is.EqualTo(19));
        Assert.That(start.InstantiatedCount, Is.EqualTo(12));

        list.ScrollToOffset(list.Snapshot().MaxScrollOffset);
        var end = list.Snapshot();
        Assert.That(end.FirstInstantiatedIndex, Is.EqualTo(0));
        Assert.That(end.LastInstantiatedIndex, Is.EqualTo(11));
        Assert.That(end.InstantiatedCount, Is.EqualTo(12));
    }

    /// <summary>
    /// 反向列表集合删除：窗口随缩容向低索引段收缩，越界的末段视图被归还、其余按反向重摆
    /// （回归 <see cref="VirtualListCore" /> 中 RebindAllActiveViews 对缩容后活跃索引取数越界的修复）。
    /// </summary>
    /// <remarks>
    /// 功能：反向排列的集合删除（ObserveRemove）。
    /// 期望：20 项（偏移 0 窗口 [10,19]）自末项删除 5 个至 15 项 → ContentSize=1500、max=500、偏移仍 0，
    /// 窗口 [5,14]，index14 MainOffset=0、index10=400；活跃数保持 12。
    /// </remarks>
    [Test]
    public void R10_Reverse_CollectionRemove_ShrinksEndWindow()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, provider, _) = CreateList(config);
        var collection = BindInts(list, 20);
        Assert.That(list.Snapshot().FirstVisibleIndex, Is.EqualTo(10));

        for (var i = 19; i >= 15; i--)
        {
            collection.RemoveAt(i);
        }

        var snapshot = list.Snapshot();
        Assert.That(snapshot.ContentSize, Is.EqualTo(1500f).Within(0.01f));
        Assert.That(snapshot.MaxScrollOffset, Is.EqualTo(500f).Within(0.01f));
        Assert.That(snapshot.Offset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(5));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(14));
        Assert.That(View(provider, 14).LastPlacement.MainOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 10).LastPlacement.MainOffset, Is.EqualTo(400f).Within(0.01f));
        Assert.That(provider.Active.Count, Is.EqualTo(12));
    }
}
