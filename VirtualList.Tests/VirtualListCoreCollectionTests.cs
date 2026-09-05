using VirtualList;
using NUnit.Framework;
using ObservableCollections;

namespace VirtualList.Tests;

    /// <summary>
    /// 编排器黑盒测试：集合变更与更新语义用例——增删改、Replace/Move/Reset、不重绑。
    /// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase"/>。
    /// </summary>
    [TestFixture]
    public class VirtualListCoreCollectionTests : VirtualListCoreTestBase
    {
        /// <summary>
        /// 窗口上方插入元素时仅局部更新，不整体重建。
        /// </summary>
        /// <remarks>
        /// 功能：插入的增量更新（设计 D9）。
        /// 期望：取/还增量各 ≤2；实例化数保持 12。
        /// </remarks>
        [Test]
        public void M1_InsertAboveWindow_LocalUpdateOnly()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            var getBefore = provider.GetCount;
            var returnBefore = provider.ReturnCount;

            collection.Insert(0, 999);

            Assert.That(provider.GetCount - getBefore, Is.LessThanOrEqualTo(2));
            Assert.That(provider.ReturnCount - returnBefore, Is.LessThanOrEqualTo(2));
            Assert.That(list.Snapshot().InstantiatedCount, Is.EqualTo(12));
        }

        /// <summary>
        /// 插入后窗口内容整体下移，索引与数据对应更新。
        /// </summary>
        /// <remarks>
        /// 功能：插入后索引 remap。
        /// 期望：index0 显示 999，index1 显示原 index0 的 0。
        /// </remarks>
        [Test]
        public void M2_Insert_ContentShiftsDown()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);

            collection.Insert(0, 999);

            Assert.That(View(provider, 1).BoundItem, Is.EqualTo(0));
            Assert.That(View(provider, 0).BoundItem, Is.EqualTo(999));
        }

        /// <summary>
        /// 删除后窗口内容整体上移，索引与数据对应更新。
        /// </summary>
        /// <remarks>
        /// 功能：删除后索引 remap。
        /// 期望：index3 显示原 index4 的 4。
        /// </remarks>
        [Test]
        public void M3_RemoveAt_ContentShiftsUp()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);

            collection.RemoveAt(3);

            Assert.That(View(provider, 3).BoundItem, Is.EqualTo(4));
        }

        /// <summary>
        /// 替换仅重绑目标元素，其余元素不重绑。
        /// </summary>
        /// <remarks>
        /// 功能：Replace 的定向重绑语义。
        /// 期望：index3 重绑一次并显示 999；index4 保持 1 次绑定。
        /// </remarks>
        [Test]
        public void M4_Replace_RebindsTargetOnly()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            var bindBefore = View(provider, 3).BindCount;

            collection[3] = 999;

            Assert.That(View(provider, 3).BoundItem, Is.EqualTo(999));
            Assert.That(View(provider, 3).BindCount, Is.EqualTo(bindBefore + 1));
            Assert.That(View(provider, 4).BindCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Move 后窗口元素全部重绑以反映新索引与数据。
        /// </summary>
        /// <remarks>
        /// 功能：Move 的窗口重绑（设计 §6.2 P0）。
        /// 期望：index0 与 index5 的 Bind 次数均为 2。
        /// </remarks>
        [Test]
        public void M5_Move_RebindsWindow()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);

            collection.Move(0, 5);

            Assert.That(View(provider, 0).BindCount, Is.EqualTo(2));
            Assert.That(View(provider, 5).BindCount, Is.EqualTo(2));
        }

        /// <summary>
        /// 集合清空（Reset）释放全部元素并将偏移归零。
        /// </summary>
        /// <remarks>
        /// 功能：Reset 语义（设计 D8 偏移归零）。
        /// 期望：Active 为空、偏移为 0。
        /// </remarks>
        [Test]
        public void M6_Reset_ReleasesAllAndOffsetZero()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            list.ScrollToOffset(500f);

            collection.Clear();

            Assert.That(provider.Active.Count, Is.EqualTo(0));
            Assert.That(list.Snapshot().Offset, Is.EqualTo(0f));
        }

        /// <summary>
        /// 数据属性变更不触发重绑，视图经引用看到新值。
        /// </summary>
        /// <remarks>
        /// 功能：VM 属性更新由元素自订阅刷新、不重绑（设计 §2.1）。
        /// 期望：Bind 次数不变；绑定数据引用不变；值更新为 99。
        /// </remarks>
        [Test]
        public void M7_PropertyMutation_NoRebind()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = new ObservableList<TestItem>();
            for (var i = 0; i < 10; i++)
            {
                collection.Add(new TestItem(i));
            }

            list.Bind(collection);
            var bindBefore = View(provider, 2).BindCount;

            collection[2].Value = 99;

            Assert.That(View(provider, 2).BindCount, Is.EqualTo(bindBefore));
            Assert.That(View(provider, 2).BoundItem, Is.SameAs(collection[2]));
            Assert.That(collection[2].Value, Is.EqualTo(99));
        }

        /// <summary>
        /// 惯性滑行中插入元素：不崩溃、偏移不变（D8 像素语义）、窗口局部更新。
        /// </summary>
        /// <remarks>
        /// 边界：滚动进行中的集合变更（设计 §7 边界场景）。
        /// 期望：插入后偏移与插入前一致；取数增量 ≤2（不整体重建）；惯性可继续推进。
        /// </remarks>
        [Test]
        public void M8_InsertDuringInertia_Robust()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            list.HandlePointerDown(500f);
            list.HandlePointerDrag(400f);
            list.HandlePointerDrag(300f);
            list.HandlePointerUp(200f);
            for (var i = 0; i < 3; i++)
            {
                list.Step(Dt);
            }

            var offsetBefore = list.Snapshot().Offset;
            var getBefore = provider.GetCount;
            collection.Insert(0, 999); // 惯性进行中，窗口上方插入

            Assert.That(list.Snapshot().Offset, Is.EqualTo(offsetBefore).Within(0.01f)); // D8：偏移不变
            Assert.That(provider.GetCount, Is.LessThanOrEqualTo(getBefore + 2));
            Assert.That(list.Snapshot().InstantiatedCount, Is.GreaterThan(0));

            for (var i = 0; i < 30; i++)
            {
                list.Step(Dt); // 惯性可继续推进，不崩溃
            }

            var snapshot = list.Snapshot();
            Assert.That(snapshot.Offset, Is.GreaterThanOrEqualTo(0f));
            Assert.That(snapshot.Offset, Is.LessThanOrEqualTo(snapshot.MaxScrollOffset + 0.01f));
        }

        /// <summary>
        /// Replace 不扰动滚动位置，仅重绑目标元素。
        /// </summary>
        /// <remarks>
        /// 功能：替换的定向重绑与滚动位置保持。
        /// 期望：滚动后 Replace → 偏移保持 500，目标元素重绑为 999。
        /// </remarks>
        [Test]
        public void M9_Replace_KeepsScrollOffset()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            list.ScrollToOffset(500f);
            Assert.That(View(provider, 5).BoundItem, Is.EqualTo(5));

            collection[5] = 999;

            Assert.That(list.Snapshot().Offset, Is.EqualTo(500f).Within(0.01f));
            Assert.That(View(provider, 5).BoundItem, Is.EqualTo(999));
            Assert.That(View(provider, 5).BindCount, Is.EqualTo(2));
        }

        /// <summary>
        /// 惯性滑行中 Reset：取消惯性、全部归还、偏移归零。
        /// </summary>
        /// <remarks>
        /// 边界：滚动进行中的集合清空（设计 §7 边界场景）。
        /// 期望：Clear 后 Active 为空、偏移 0；继续 Step 不再滑动。
        /// </remarks>
        [Test]
        public void M10_ResetDuringInertia_StopsAndReleases()
        {
            var (list, provider, _) = CreateList(TestListFactory.FixedConfig());
            var collection = BindInts(list, 100);
            list.HandlePointerDown(500f);
            list.HandlePointerDrag(400f);
            list.HandlePointerDrag(300f);
            list.HandlePointerUp(200f);
            for (var i = 0; i < 3; i++)
            {
                list.Step(Dt);
            }

            collection.Clear(); // 惯性中 Reset

            Assert.That(provider.Active.Count, Is.EqualTo(0));
            Assert.That(list.Snapshot().Offset, Is.EqualTo(0f));

            for (var i = 0; i < 10; i++)
            {
                list.Step(Dt);
            }

            Assert.That(list.Snapshot().Offset, Is.EqualTo(0f)); // 惯性已取消
        }
    }