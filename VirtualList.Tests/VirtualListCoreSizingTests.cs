using VirtualList;
using NUnit.Framework;

namespace VirtualList.Tests;

    /// <summary>
    /// 编排器黑盒测试：变高尺寸与测量用例——估计值、测量上报、尺寸变化校正。
    /// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase"/>。
    /// </summary>
    [TestFixture]
    public class VirtualListCoreSizingTests : VirtualListCoreTestBase
    {
        /// <summary>
        /// 注入估计值后，内容尺寸为「已测量 + 未测量按估计」的混合。
        /// </summary>
        /// <remarks>
        /// 功能：变高模式未测量估计值参与累计（设计 D12）。
        /// 期望：估计 50 注入后，初始窗口按估计 50 计算（22 个进入并测量为 100，测量值缓存），
        ///       稳定窗口 12 个，ContentSize=22*100 + 28*50=3600。
        /// </remarks>
        [Test]
        public void S1_EstimateInjected_ContentSizeMixesMeasuredAndEstimate()
        {
            var (list, _, _) = CreateVariableList(null);
            list.SetDefaultItemSize(50f);
            BindInts(list, 50);

            var snapshot = list.Snapshot();
            Assert.That(snapshot.InstantiatedCount, Is.EqualTo(12));
            Assert.That(snapshot.ContentSize, Is.EqualTo(3600f).Within(0.5f)); // 22*100（测量缓存）+ 28*50（估计）
        }

        /// <summary>
        /// 未注入估计值时，首个测量值兜底为估计值。
        /// </summary>
        /// <remarks>
        /// 边界：估计值未注入且元素全为 100 高。
        /// 期望：全部测量后 ContentSize=12*100=1200。
        /// </remarks>
        [Test]
        public void S2_EstimateFallsBackToFirstMeasure()
        {
            var (list, _, _) = CreateVariableList(null);
            BindInts(list, 12);

            var snapshot = list.Snapshot();
            Assert.That(snapshot.ContentSize, Is.EqualTo(1200f).Within(0.5f)); // 全部测量为 100
        }

        /// <summary>
        /// 尺寸变化通知重复触发幂等。
        /// </summary>
        /// <remarks>
        /// 边界：同一索引连续两次 NotifyItemSizeChanged。
        /// 期望：ContentSize 与 index3 摆放均不变。
        /// </remarks>
        [Test]
        public void S3_NotifyItemSizeChanged_Idempotent()
        {
            var (list, provider, _) = CreateVariableList(null);
            BindInts(list, 12);
            var before = list.Snapshot().ContentSize;

            list.NotifyItemSizeChanged(2);
            list.NotifyItemSizeChanged(2);

            Assert.That(list.Snapshot().ContentSize, Is.EqualTo(before).Within(0.5f));
            Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(300f).Within(0.01f)); // 全 100 高：0/1/2 行累计 300
        }

        /// <summary>
        /// 尺寸变化经通知校正布局，后续元素重排。
        /// </summary>
        /// <remarks>
        /// 功能：尺寸变化通知的布局校正。
        /// 期望：index1 由 100 变为 300 后，index2 起点 200 → 400，ContentSize=100+300+100*10=1400。
        /// </remarks>
        [Test]
        public void S4_SizeChange_RelayoutsFollowingItems()
        {
            // 用无 HeightProvider 的桩：Measure() 返回 MeasuredSize，可经 NotifyItemSizeChanged 改变尺寸。
            var config = TestListFactory.VariableConfig();
            var provider = new StubItemProvider();
            var (list, _, _) = CreateList(config, provider);
            BindInts(list, 12);
            Assert.That(View(provider, 2).LastPlacement.MainOffset, Is.EqualTo(200f).Within(0.01f));

            View(provider, 1).MeasuredSize = 300f;
            list.NotifyItemSizeChanged(1);

            Assert.That(View(provider, 2).LastPlacement.MainOffset, Is.EqualTo(400f).Within(0.01f));
            Assert.That(list.Snapshot().ContentSize, Is.EqualTo(1400f).Within(0.5f)); // 100 + 300 + 100*10
        }

        /// <summary>
        /// 定高模式不触发测量。
        /// </summary>
        /// <remarks>
        /// 功能：定高免测量的约定。
        /// 期望：全部视图 Measure 调用次数为 0。
        /// </remarks>
        [Test]
        public void S5_FixedMode_NeverMeasures()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            BindInts(list, 12);

            foreach (var view in provider.Created)
            {
                Assert.That(((StubItemView)view).MeasureCount, Is.EqualTo(0));
            }
        }

        /// <summary>
        /// 对非活跃（窗口外）索引的尺寸变化通知为无操作。
        /// </summary>
        /// <remarks>
        /// 边界：窗口外元素尺寸变化通知（未被测量，直接忽略）。
        /// 期望：不崩溃、ContentSize 与窗口均不变。
        /// </remarks>
        [Test]
        public void S6_SizeChange_InactiveIndex_Noop()
        {
            var (list, _, _) = CreateVariableList(null);
            BindInts(list, 50);
            var before = list.Snapshot().ContentSize;

            list.NotifyItemSizeChanged(30); // 窗口外索引

            Assert.That(list.Snapshot().ContentSize, Is.EqualTo(before).Within(0.5f));
            Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));
        }

        /// <summary>
        /// 绑定后再注入估计值会按新估计值重排内容尺寸。
        /// </summary>
        /// <remarks>
        /// 功能：估计值重注入的布局校正（未测量元素参与累计）。
        /// 期望：先注入估计 100（仅 12 个测量）→ ContentSize=12*100 + 38*100=5000；
        ///       重注入 50 后 ContentSize=12*100 + 38*50=3100。
        /// </remarks>
        [Test]
        public void S7_SetDefaultItemSize_Relayouts()
        {
            var (list, _, _) = CreateVariableList(null);
            // 先注入估计 100：窗口按 100 计算，仅实例化 12 个并测量，其余 38 个未测量按估计参与累计。
            list.SetDefaultItemSize(100f);
            BindInts(list, 50);
            Assert.That(list.Snapshot().ContentSize, Is.EqualTo(5000f).Within(0.5f)); // 12*100 + 38*100

            list.SetDefaultItemSize(50f); // 重注入 50：未测量元素按新估计值重算

            Assert.That(list.Snapshot().ContentSize, Is.EqualTo(3100f).Within(0.5f)); // 12*100 + 38*50
        }
    }