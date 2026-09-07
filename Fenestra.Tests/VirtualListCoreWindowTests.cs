namespace Fenestra.Tests;

using NUnit.Framework;
using ObservableCollections;

/// <summary>
/// 编排器黑盒测试：绑定与窗口用例——窗口区间、实例化数、增量滚动，
/// 以及视口缩放 / 空集合 / 零视口下的窗口自适应（重排 / 收缩 / 不实例化）。
/// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase" />。
/// </summary>
[TestFixture]
public class VirtualListCoreWindowTests : VirtualListCoreTestBase
{
    /// <summary>
    /// 定高模式绑定后仅实例化「可见 + overscan」窗口。
    /// </summary>
    /// <remarks>
    /// 功能：定高模式下窗口区间与实例化数计算。
    /// 期望：可见 [0,9]、实例化 [0,11]、共实例化 12 个。
    /// </remarks>
    [Test]
    public void B1_BindFixed_InstantiatesVisiblePlusOverscan()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 1000);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(9));
        Assert.That(snapshot.FirstInstantiatedIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastInstantiatedIndex, Is.EqualTo(11));
        Assert.That(snapshot.InstantiatedCount, Is.EqualTo(12));
        Assert.That(provider.GetCount, Is.EqualTo(12));
    }

    /// <summary>
    /// 变高模式初始窗口按已测量高度累计，摆放位置正确。
    /// </summary>
    /// <remarks>
    /// 功能：变高模式下测量驱动的窗口与摆放计算。
    /// 期望：视口 1000 时可见 [0,8]（第 9 项起点恰为视口边界）；index3 起点偏移 350。
    /// </remarks>
    [Test]
    public void B2_BindVariable_MeasuredWindowAndPlacements()
    {
        var heights = new float[]
        {
            100, 50, 200, 150, 100, 100, 100, 100, 100, 100, 100, 100,
        };
        var (list, provider, _) = CreateVariableList(heights);
        list.SetDefaultItemSize(100f);
        BindInts(list, 12);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(8)); // 100+50+200+150+100*5=1000 边界
        Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(350f).Within(0.01f));
    }

    /// <summary>
    /// overscan 按配置扩展实例化窗口。
    /// </summary>
    /// <remarks>
    /// 功能：过扫描对实例化窗口的扩展。
    /// 期望：overscan=4 时实例化 [0,13]。
    /// </remarks>
    [Test]
    public void B3_OverscanExtendsInstantiatedWindow()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig(overscan: 4));
        BindInts(list, 100);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.FirstInstantiatedIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastInstantiatedIndex, Is.EqualTo(13));
    }

    /// <summary>
    /// 滚动后窗口跟随偏移平移，仅增量获取新进入元素。
    /// </summary>
    /// <remarks>
    /// 功能：滚动时窗口的增量更新。
    /// 期望：offset=100 时可见 [1,10]、实例化 13 个、Get 增 1、无归还。
    /// </remarks>
    [Test]
    public void B4_Scroll_WindowFollowsWithDeltaAcquire()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);
        Assert.That(provider.GetCount, Is.EqualTo(12));

        list.ScrollToOffset(100f);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(1));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(10));
        Assert.That(snapshot.InstantiatedCount, Is.EqualTo(13));
        Assert.That(provider.GetCount, Is.EqualTo(13));
        Assert.That(provider.ReturnCount, Is.EqualTo(0));
    }

    /// <summary>
    /// 滚动不重绑仍可见的元素。
    /// </summary>
    /// <remarks>
    /// 功能：窗口内复用元素的增量更新语义。
    /// 期望：滚动后 index1 视图 Bind 次数保持 1。
    /// </remarks>
    [Test]
    public void B5_Scroll_VisibleItemsNotRebound()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);
        var view1 = View(provider, 1);

        list.ScrollToOffset(100f);

        Assert.That(view1.BindCount, Is.EqualTo(1));
    }

    /// <summary>
    /// 内容不足一屏时不产生滚动。
    /// </summary>
    /// <remarks>
    /// 边界：内容尺寸小于视口，MaxScrollOffset 为 0。
    /// 期望：拖拽后偏移保持 0。
    /// </remarks>
    [Test]
    public void B6_ContentShorterThanViewport_NoScroll()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 5);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.MaxScrollOffset, Is.EqualTo(0f));
        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f));
    }

    /// <summary>
    /// 再次 Bind 新集合时切换数据源：旧集合退订、偏移归零、窗口重建。
    /// </summary>
    /// <remarks>
    /// 功能：Bind 的切换语义（内部先 Unbind 再绑定新集合）。
    /// 期望：重绑后偏移 0、窗口 12 个；旧集合的后续变更不再有任何动作。
    /// </remarks>
    [Test]
    public void B7_BindTwice_SwitchesCollections()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
        var collectionA = BindInts(list, 100);
        list.ScrollToOffset(500f);
        Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(14)); // 滚动后窗口 [3,16]

        var collectionB = BindInts(list, 50);

        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f));
        Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));
        var getBefore = provider.GetCount;
        collectionA.Add(999); // 旧集合已退订 → 无动作
        Assert.That(provider.GetCount, Is.EqualTo(getBefore));
        Assert.That(collectionB.Count, Is.EqualTo(50));
    }

    /// <summary>
    /// 视口缩小触发重排，窗口随之收缩。
    /// </summary>
    /// <remarks>
    /// 功能：自适应重排（设计 §6.4）。
    /// 期望：视口 1000→500 后可见 [0,4]、实例化至 index6。
    /// </remarks>
    [Test]
    public void R1_ViewportShrink_RelayoutsWindow()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);
        Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));

        list.SetViewportSize(500f, 100f);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(4));
        Assert.That(snapshot.LastInstantiatedIndex, Is.EqualTo(6));
    }

    /// <summary>
    /// 视口放大触发重排，窗口随之扩张。
    /// </summary>
    /// <remarks>
    /// 功能：自适应重排（设计 §6.4）。
    /// 期望：视口 1000→2000 后可见 [0,19]、实例化至 index21、max=8000。
    /// </remarks>
    [Test]
    public void R2_ViewportGrow_ExpandsWindow()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);
        Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));

        list.SetViewportSize(2000f, 100f);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(19));
        Assert.That(snapshot.LastInstantiatedIndex, Is.EqualTo(21));
        Assert.That(snapshot.InstantiatedCount, Is.EqualTo(22));
        Assert.That(snapshot.MaxScrollOffset, Is.EqualTo(8000f).Within(0.01f));
    }

    /// <summary>
    /// 空集合或零视口均不实例化任何元素。
    /// </summary>
    /// <remarks>
    /// 边界：空数据与零尺寸视口。
    /// 期望：两种场景 Get 计数均为 0。
    /// </remarks>
    [Test]
    public void E1_EmptyCollectionOrViewport_NoInstantiation()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
        var empty = new ObservableList<int>();
        list.Bind(empty);
        Assert.That(provider.GetCount, Is.EqualTo(0));

        var (list2, provider2, _) = CreateList(TestListFactory.FixedConfig());
        list2.SetViewportSize(0f, 0f);
        BindInts(list2, 10);
        Assert.That(provider2.GetCount, Is.EqualTo(0));
    }
}
