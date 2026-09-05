using System.Collections.Generic;
using VirtualList;
using NUnit.Framework;
using ObservableCollections;
using R3;

namespace VirtualList.Tests;

    /// <summary>
    /// 编排器黑盒测试：生命周期、事件流与 Provider 边界用例——获取 / 释放序列、可见性、
    /// 可见窗口与边界事件流、Dispose / Unbind、Provider 装配边界。
    /// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase"/>。
    /// </summary>
    [TestFixture]
    public class VirtualListCoreLifecycleTests : VirtualListCoreTestBase
    {
        /// <summary>
        /// 元素获取序列为 Bind → SetPlacement → OnShow。
        /// </summary>
        /// <remarks>
        /// 功能：获取顺序约定（设计 §6.1，定高模式无 Measure）。
        /// 期望：view0 日志中 Bind 先于 Place 先于 Show。
        /// </remarks>
        [Test]
        public void L1_AcquireSequence_BindPlaceShow()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            BindInts(list, 12);

            var log = View(provider, 0).Log;
            var bindAt = log.IndexOf("Bind");
            var placeAt = log.IndexOf("Place");
            var showAt = log.IndexOf("Show");
            Assert.That(bindAt, Is.GreaterThanOrEqualTo(0));
            Assert.That(placeAt, Is.GreaterThan(bindAt));
            Assert.That(showAt, Is.GreaterThan(placeAt));
        }

        /// <summary>
        /// 元素释放序列为 OnHide → OnUnbind → Return。
        /// </summary>
        /// <remarks>
        /// 功能：释放顺序约定（设计 §6.1）。
        /// 期望：滚到底时 view0 释放，日志含 Hide→Unbind 成对序列，Return 计数增加。
        /// （定高列表窗口规模恒定，滚出的视图会被滚入窗口即时复用，无法断言其留在池中。）
        /// </remarks>
        [Test]
        public void L2_ReleaseSequence_HideUnbindReturn()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            BindInts(list, 100);
            var leaving = View(provider, 0);
            var returnBefore = provider.ReturnCount;

            // 直接滚到底：view0 离开窗口，经 Hide→Unbind→Return 归还，随后被滚入窗口复用。
            list.ScrollToOffset(9000f);

            var log = leaving.Log;
            var hideAt = log.IndexOf("Hide");
            Assert.That(hideAt, Is.GreaterThanOrEqualTo(0), "视图曾处于显示状态并被释放");
            Assert.That(log[hideAt + 1], Is.EqualTo("Unbind"), "释放序列为 OnHide → OnUnbind");
            Assert.That(provider.ReturnCount, Is.GreaterThan(returnBefore));
        }

        /// <summary>
        /// 已显示的部分可见元素不因微小滚动重复触发 Show/Hide 翻转。
        /// </summary>
        /// <remarks>
        /// 边界：部分可见判定（设计 D7，与视口相交即显示、相交期间不重复触发）。
        /// 期望：首次 1px 滚动 index10 以 1px 相交进入窗口（Show 一次，共 11）；再滚 1px 窗口集合不变，不再触发。
        /// </remarks>
        [Test]
        public void L3_OnePixelScroll_NoVisibilityFlip()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            BindInts(list, 12);

            // 首次滚动 1px：index10 以 1px 相交进入视口 → OnShow 一次（窗口 [0,10]）。
            list.ScrollToOffset(1f);
            var afterFirst = 0;
            foreach (var view in provider.Created)
            {
                afterFirst += ((StubItemView)view).ShowCount + ((StubItemView)view).HideCount;
            }

            Assert.That(afterFirst, Is.EqualTo(11)); // 初始 10 项 + index10 进入

            // 再滚动 1px（共 2px）：窗口集合不变，仅部分可见程度变化 → 不重复 OnShow/OnHide。
            list.ScrollToOffset(2f);
            var afterSecond = 0;
            foreach (var view in provider.Created)
            {
                afterSecond += ((StubItemView)view).ShowCount + ((StubItemView)view).HideCount;
            }

            Assert.That(afterSecond, Is.EqualTo(afterFirst));
        }

        /// <summary>
        /// VisibleWindowChanged 流负载随滚动更新。
        /// </summary>
        /// <remarks>
        /// 功能：R3 事件流（设计 D13）。
        /// 期望：绑定后至少 2 次 OnNext；末次负载为 (1,10)。
        /// </remarks>
        [Test]
        public void L4_VisibleWindowChanged_StreamPayloads()
        {
            var (list, _, _) = CreateList(TestListFactory.FixedConfig());
            var payloads = new List<VisibleWindow>();
            Subscriptions.Add(list.VisibleWindowChanged.Subscribe(payloads.Add));
            BindInts(list, 100);

            list.ScrollToOffset(100f);

            Assert.That(payloads.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(payloads[payloads.Count - 1].First, Is.EqualTo(1));
            Assert.That(payloads[payloads.Count - 1].Last, Is.EqualTo(10));
        }

        /// <summary>
        /// ReachedTop / ReachedBottom 在跨越边界时刻各触发一次。
        /// </summary>
        /// <remarks>
        /// 功能：边界事件的一次性触发（设计 §6.3）。
        /// 期望：滚到底 bottom=1，回顶 top=1，均不重复。
        /// </remarks>
        [Test]
        public void L5_ReachedTopBottom_FiredOnceEach()
        {
            var (list, _, _) = CreateList(TestListFactory.FixedConfig());
            var topCount = 0;
            var bottomCount = 0;
            Subscriptions.Add(list.ReachedTop.Subscribe(_ => topCount++));
            Subscriptions.Add(list.ReachedBottom.Subscribe(_ => bottomCount++));
            BindInts(list, 100);

            list.ScrollToOffset(99999f);
            Assert.That(bottomCount, Is.EqualTo(1));

            list.ScrollToOffset(0f);
            Assert.That(topCount, Is.EqualTo(1));
        }

        /// <summary>
        /// 经拖拽（含弹性越界）到达底边界时 ReachedBottom 只触发一次。
        /// </summary>
        /// <remarks>
        /// 功能：边界事件经拖拽输入触发，且在停留在边界时不重复（设计 §6.3）。
        /// 期望：ScrollToOffset 到底触发 1 次；继续越界拖拽不重复触发。
        /// </remarks>
        [Test]
        public void C14_ReachedBottom_DragAtBoundaryFiresOnce()
        {
            var (list, _, _) = CreateList(TestListFactory.FixedConfig(overflow: VirtualListOverflow.Elastic));
            var bottomCount = 0;
            Subscriptions.Add(list.ReachedBottom.Subscribe(_ => bottomCount++));
            BindInts(list, 100);

            list.ScrollToOffset(9000f); // 滚到底 → 触发一次
            Assert.That(bottomCount, Is.EqualTo(1));

            list.HandlePointerDown(500f);
            list.HandlePointerDrag(-1000f); // 越界拖拽（弹性阻尼）
            list.HandlePointerDrag(-2000f);
            Assert.That(bottomCount, Is.EqualTo(1)); // 停留在边界不重复触发
        }

        /// <summary>
        /// Dispose 释放全部元素、完成事件流，且退订集合。
        /// </summary>
        /// <remarks>
        /// 边界：Dispose 后集合变更不再有任何动作。
        /// 期望：Active 为空、事件流 OnCompleted 一次、Get 计数不变。
        /// </remarks>
        [Test]
        public void L6_Dispose_ReleasesAndCompletesStreams()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            var completed = 0;
            Subscriptions.Add(
                list.ReachedBottom
                    .Materialize()
                    .Subscribe(m =>
                    {
                        if (m.Kind == NotificationKind.OnCompleted)
                        {
                            completed++;
                        }
                    }));
            var getBefore = provider.GetCount;

            list.Dispose();

            Assert.That(provider.Active.Count, Is.EqualTo(0));
            Assert.That(completed, Is.EqualTo(1));
            collection.Add(1000);
            Assert.That(provider.GetCount, Is.EqualTo(getBefore));
        }

        /// <summary>
        /// Unbind 退订集合并归还全部元素，且可重新绑定。
        /// </summary>
        /// <remarks>
        /// 功能：Unbind 语义（退订、清空、可重绑）。
        /// 期望：Unbind 后 Active 为空、无实例化；集合变更无动作；重新 Bind 后窗口恢复。
        /// </remarks>
        [Test]
        public void L7_Unbind_ReleasesAndDetaches()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            Assert.That(provider.Active.Count, Is.EqualTo(12));

            list.Unbind();
            Assert.That(provider.Active.Count, Is.EqualTo(0));
            Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(0));

            var getBefore = provider.GetCount;
            collection.Add(999); // 退订后集合变更无动作
            Assert.That(provider.GetCount, Is.EqualTo(getBefore));

            list.Bind(collection); // 可重新绑定
            Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));
            Assert.That(list.Snapshot().Offset, Is.EqualTo(0f));
        }

        /// <summary>
        /// 未预热（GetAsync 未完成）时 reconcile 挂起而非挂死，完成后窗口填满。
        /// </summary>
        /// <remarks>
        /// 边界：未预热路径（设计 E2；GAME_DEBUG 告警尚未实现，仅验证同步语义不被破坏）。
        /// 期望：GetAsync 未完成时无实例化、不崩溃；逐个完成后窗口正常填满。
        /// </remarks>
        [Test]
        public void E2_UnpreheatedGetAsync_DefersPopulation()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            provider.SetManualGet();
            BindInts(list, 12);

            // GetAsync 未完成 → reconcile 挂起：已发起 1 次获取、尚无实例化。
            Assert.That(provider.GetCount, Is.EqualTo(1));
            Assert.That(provider.Active.Count, Is.EqualTo(0));

            // 逐个完成挂起获取 → reconcile 内联续延持续推进直至窗口填满。
            var guard = 0;
            while (provider.PendingGets.Count > 0 && guard++ < 100)
            {
                var pending = provider.PendingGets[0];
                provider.PendingGets.RemoveAt(0);
                pending.TrySetResult(new StubItemView());
            }

            // 未预热路径下 StubItemProvider 的 Active 集合由非手动分支维护，手动完成模式不经其记账，
            // 故窗口填满以编排器侧 InstantiatedCount 为准；每个进入索引各发起一次获取。
            Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));
            Assert.That(provider.GetCount, Is.EqualTo(12));
        }

        /// <summary>
        /// Provider 返回空视图时记录错误并跳过，列表不崩溃。
        /// </summary>
        /// <remarks>
        /// 边界：装配错误（Provider 返回 null）。
        /// 期望：仍尝试获取（GetCount>0）且日志记录错误。
        /// </remarks>
        [Test]
        public void E3_NullViewFromProvider_LogsErrorAndSkips()
        {
            var (list, provider, logger) = CreateList(
                TestListFactory.FixedConfig(),
                new StubItemProvider(_ => null));
            BindInts(list, 100);

            Assert.That(provider.GetCount, Is.GreaterThan(0));
            Assert.That(logger.Errors.Count, Is.GreaterThan(0));
        }

        /// <summary>
        /// Provider 复用归还的视图（对象池语义），不重复创建。
        /// </summary>
        /// <remarks>
        /// 功能：窗口滚出归还、滚回复用池中旧实例（设计 §6.1 取还循环）。
        /// 期望：滚出后 Return 计数增加；滚回后 Created 不再增长、index0 的视图为池中既有实例。
        /// （对象池即时复用，具体索引绑定哪个旧实例由 LIFO 顺序决定，不能断言特定实例。）
        /// </remarks>
        [Test]
        public void E4_ProviderReusesRecycledViews()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            BindInts(list, 100);

            // 滚出：离开窗口的元素经 Hide→Unbind→Return 归还（对象池回收）。
            list.ScrollToOffset(1000f);
            Assert.That(provider.ReturnCount, Is.GreaterThan(0));

            // 滚回：复用池中旧实例重绑，不再新建视图；index0 的视图为池中既有实例。
            var createdAfterScroll = provider.Created.Count;
            list.ScrollToOffset(0f);
            Assert.That(provider.Created.Count, Is.EqualTo(createdAfterScroll));
            Assert.That(provider.Active.Count, Is.EqualTo(12));
            Assert.That(provider.Created.IndexOf(View(provider, 0)), Is.LessThan(createdAfterScroll));
        }
    }