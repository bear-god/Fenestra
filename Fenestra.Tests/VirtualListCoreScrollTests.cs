namespace Fenestra.Tests;

using System;
using Fenestra.Entity;
using NUnit.Framework;

/// <summary>
/// 编排器黑盒测试：滚动与对齐用例——拖拽、惯性、回弹、ScrollToIndex、单轴，
/// 以及逻辑/物理方向便捷滚动（ScrollToStart/End、ScrollToTop/Bottom/Left/Right）。
/// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase" />。
/// </summary>
[TestFixture]
public class VirtualListCoreScrollTests : VirtualListCoreTestBase
{
    /// <summary>
    /// 拖拽偏移跟随手指位移（跟手）。
    /// </summary>
    /// <remarks>
    /// 功能：拖拽输入到偏移的换算（手指上移内容下滚）。
    /// 期望：按下 500 拖到 400 → 偏移 100。
    /// </remarks>
    [Test]
    public void C1_DragFollowsFinger()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);

        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f);

        Assert.That(list.Snapshot().Offset, Is.EqualTo(100f).Within(0.01f));
    }

    /// <summary>
    /// 反向拖拽被 Clamped 硬边界夹紧到 0。
    /// </summary>
    /// <remarks>
    /// 边界：Clamped 模式下向顶部越界。
    /// 期望：反向拖拽后偏移回到 0。
    /// </remarks>
    [Test]
    public void C2_DragReverse_ClampedAtZero()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);

        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f);
        list.HandlePointerDrag(600f);

        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// ScrollToOffset 越界目标被夹紧到最大偏移。
    /// </summary>
    /// <remarks>
    /// 边界：目标偏移超过 MaxScrollOffset。
    /// 期望：100 项定高 100、视口 1000 时偏移停在 9000。
    /// </remarks>
    [Test]
    public void C3_ScrollToOffset_ClampedToMax()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);

        list.ScrollToOffset(99999f);

        Assert.That(list.Snapshot().Offset, Is.EqualTo(9000f).Within(0.01f));
    }

    /// <summary>
    /// 惯性滑行在固定步长下确定性收敛。
    /// </summary>
    /// <remarks>
    /// 功能：松手速度估算与惯性衰减积分。
    /// 边界：固定 dt=1/60 步进 200 次。
    /// 期望：偏移单调不减；抬起与末次拖拽差 100 → 速度 6000，落点 ≈300+950=1250（±5）；停止后不再变化。
    /// 注：离散积分顺序为先衰减速度后积分位置，落点 = 300 + 100·e^(-F·dt)/(1-e^(-F·dt)) ≈ 1250
    /// （F=6、dt=1/60；与「位置先积分」的连续近似 1350 不同，以实际离散积分为准）。
    /// </remarks>
    [Test]
    public void C4_Inertia_DeterministicLanding()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);

        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f);
        list.HandlePointerDrag(300f);

        var previous = list.Snapshot().Offset; // 拖拽跟手位置 200
        Assert.That(previous, Is.EqualTo(200f).Within(0.01f));

        list.HandlePointerUp(200f); // 抬起与末次拖拽差 100 → 速度 = (300-200)*60 = 6000 → 惯性距离 ≈ 950

        for (var i = 0; i < 200; i++)
        {
            list.Step(Dt);
            var offset = list.Snapshot().Offset;
            Assert.That(offset, Is.GreaterThanOrEqualTo(previous - 0.01f));
            previous = offset;
        }

        var final = list.Snapshot().Offset;
        Assert.That(final, Is.EqualTo(1250f).Within(5f)); // 300 + 950 ≈ 1250（离散积分精确值 1249.66）
        var stable = final;
        for (var i = 0; i < 20; i++)
        {
            list.Step(Dt);
        }

        Assert.That(list.Snapshot().Offset, Is.EqualTo(stable).Within(0.01f));
    }

    /// <summary>
    /// Elastic 越界拖拽后经弹簧回弹收敛回边界。
    /// </summary>
    /// <remarks>
    /// 功能：Elastic 越界阻尼拖拽与松手回弹。
    /// 边界：末段零速度拖拽，仅验证回弹收敛。
    /// 期望：拖拽后偏移 > 9000；Step 300 次后收敛回 9000（±1）。
    /// </remarks>
    [Test]
    public void C5_Elastic_OverscrollConvergesBack()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig(overflow: VirtualListOverflow.Elastic));
        BindInts(list, 100);

        list.ScrollToOffset(9000f); // 滚到底
        list.HandlePointerDown(500f);
        list.HandlePointerDrag(-1000f); // 越界拖拽 → 弹性阻尼
        list.HandlePointerDrag(-1000f); // 末段零速度，仅回弹
        list.HandlePointerUp(-1000f);

        Assert.That(list.Snapshot().Offset, Is.GreaterThan(9000f));
        for (var i = 0; i < 300; i++)
        {
            list.Step(Dt);
        }

        Assert.That(list.Snapshot().Offset, Is.EqualTo(9000f).Within(1f));
    }

    /// <summary>
    /// 惯性滑行中按下可打断并跟手新拖拽。
    /// </summary>
    /// <remarks>
    /// 功能：拖拽对惯性/回弹的打断（设计 D4 输入直调）。
    /// 期望：打断后新拖拽 100px → 偏移比打断时 +100。
    /// </remarks>
    [Test]
    public void C6_DragDuringInertia_InterruptsAndFollows()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);

        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f);
        list.HandlePointerDrag(300f);
        list.HandlePointerUp(300f);
        for (var i = 0; i < 5; i++)
        {
            list.Step(Dt);
        }

        var before = list.Snapshot().Offset;
        list.HandlePointerDown(300f);
        list.HandlePointerDrag(200f);

        Assert.That(list.Snapshot().Offset, Is.EqualTo(before + 100f).Within(0.01f));
    }

    /// <summary>
    /// ScrollToIndex 三种对齐（置顶/居中/置底）的目标偏移。
    /// </summary>
    /// <remarks>
    /// 功能：ScrollToIndex 对齐数学与越界夹紧。
    /// 期望：index5 起点 500：Start=500、Center=50、End 夹紧为 0。
    /// </remarks>
    [Test]
    public void C7_ScrollToIndex_Alignments()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 20);

        list.ScrollToIndex(5, ScrollAlignment.Start);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(500f).Within(0.01f));

        list.ScrollToIndex(5, ScrollAlignment.Center);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(50f).Within(0.01f));

        list.ScrollToIndex(5, ScrollAlignment.End);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// 变高模式下 ScrollToIndex 累计已测量与未测量估计高度。
    /// </summary>
    /// <remarks>
    /// 功能：变高累计定位（设计 D12 估计值参与累计）。
    /// 期望：index15 起点 = 已测量 1300 + 估计 300 = 1600。
    /// </remarks>
    [Test]
    public void C8_ScrollToIndex_VariableAccumulation()
    {
        var heights = new float[]
        {
            100, 50, 200, 150, 100, 100, 100, 100, 100, 100, 100, 100,
        };
        var (list, _, _) = CreateVariableList(heights);
        list.SetDefaultItemSize(100f);
        BindInts(list, 30); // 30 条 → ContentSize=1300+18*100=3100、max=2100，目标 1600 不被夹取

        list.ScrollToIndex(15, ScrollAlignment.Start);

        // 0..11 已测量：100+50+200+150+100*8=1300；12..14 未测量按估计 100*3=300
        Assert.That(list.Snapshot().Offset, Is.EqualTo(1600f).Within(1f));
    }

    /// <summary>
    /// ScrollToOffset / ScrollToIndex 越界目标均夹紧到边界。
    /// </summary>
    /// <remarks>
    /// 边界：目标超出内容范围。
    /// 期望：30 项、视口 1000 时偏移停在 2000。
    /// </remarks>
    [Test]
    public void C9_ScrollToIndex_ClampedAtBounds()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 30);

        list.ScrollToOffset(99999f);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(2000f).Within(0.01f));

        list.ScrollToIndex(29, ScrollAlignment.End);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(2000f).Within(0.01f));
    }

    /// <summary>
    /// 水平轴模式下交叉轴坐标恒定、主轴递增。
    /// </summary>
    /// <remarks>
    /// 功能：单轴语义（水平列表以 x 为主轴）。
    /// 期望：CrossOffset 恒为 0，index1 MainOffset=100。
    /// </remarks>
    [Test]
    public void C10_HorizontalAxis_CrossAxisConstant()
    {
        var config = TestListFactory.FixedConfig();
        config.Axis = VirtualListAxis.Horizontal;
        var (list, provider, _) = CreateList(config);
        BindInts(list, 20);

        Assert.That(View(provider, 1).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 5).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// 非零主轴间距参与摆放、内容尺寸与 ScrollToIndex 计算。
    /// </summary>
    /// <remarks>
    /// 功能：Spacing 配置参与布局数学。
    /// 期望：间距 10 时 ContentSize=1310、max=310；index1 起点 110、index5 起点 550。
    /// 12 项时 ScrollToIndex(5) 目标 550 超 max 被夹紧到 310；足够长的列表（100 项）目标 550 可达。
    /// </remarks>
    [Test]
    public void C11_Spacing_OffsetsAndContentSize()
    {
        var (list, provider, _) = CreateList(TestListFactory.FixedConfig(spacing: 10f));
        BindInts(list, 12);

        var snapshot = list.Snapshot();
        Assert.That(snapshot.ContentSize, Is.EqualTo(1310f).Within(0.01f)); // 12*100 + 10*11
        Assert.That(snapshot.MaxScrollOffset, Is.EqualTo(310f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(110f).Within(0.01f));
        Assert.That(View(provider, 5).LastPlacement.MainOffset, Is.EqualTo(550f).Within(0.01f));

        // 12 项内容 1310、max=310：ScrollToIndex(5) 目标 550 被正确夹紧到 max。
        list.ScrollToIndex(5, ScrollAlignment.Start);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(310f).Within(0.01f));

        // 足够长的列表验证 ScrollToIndex 目标计入间距（550 可达，未被夹紧）。
        BindInts(list, 100);
        list.ScrollToIndex(5, ScrollAlignment.Start);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(550f).Within(0.01f));
    }

    /// <summary>
    /// 水平变高模式下主轴偏移按测量宽度累计、交叉轴恒定。
    /// </summary>
    /// <remarks>
    /// 功能：水平轴 + 变高（Measure 返回主轴宽度）。
    /// 期望：index2 起点 = 100+50=150；index3 起点 = 150+200=350；CrossOffset 恒 0。
    /// </remarks>
    [Test]
    public void C12_HorizontalVariable_CumulativeMainOffset()
    {
        var config = TestListFactory.VariableConfig();
        config.Axis = VirtualListAxis.Horizontal;
        var widths = new float[]
        {
            100, 50, 200, 150, 100, 100, 100, 100, 100, 100, 100, 100,
        };
        var provider = new StubItemProvider(_ => new StubItemView
        {
            HeightProvider = i => widths[Math.Min(i, widths.Length - 1)],
        });
        var (list, _, _) = CreateList(config, provider);
        BindInts(list, 12);

        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.MainOffset, Is.EqualTo(150f).Within(0.01f)); // 100 + 50
        Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(350f).Within(0.01f)); // + 200
        Assert.That(View(provider, 2).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
    }

    /// <summary>
    /// 惯性滑行中调用 ScrollToIndex 会打断惯性并精确定位，且不再漂移。
    /// </summary>
    /// <remarks>
    /// 功能：程序化定位对惯性/回弹的打断（设计 D4）。
    /// 期望：ScrollToIndex(5) 后偏移 500；继续 Step 不再滑动。
    /// </remarks>
    [Test]
    public void C13_ScrollToIndex_InterruptsInertia()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 100);

        list.HandlePointerDown(500f);
        list.HandlePointerDrag(400f);
        list.HandlePointerDrag(300f);
        list.HandlePointerUp(200f);
        for (var i = 0; i < 5; i++)
        {
            list.Step(Dt);
        }

        list.ScrollToIndex(5, ScrollAlignment.Start);
        Assert.That(list.Snapshot().Offset, Is.EqualTo(500f).Within(0.01f));

        for (var i = 0; i < 10; i++)
        {
            list.Step(Dt);
        }

        Assert.That(list.Snapshot().Offset, Is.EqualTo(500f).Within(0.01f)); // 惯性已取消，不再滑动
    }

    /// <summary>
    /// 正向列表：ScrollToStart 到偏移 0（首元素），ScrollToEnd 到偏移 max（末元素）。
    /// </summary>
    /// <remarks>
    /// 功能：逻辑起点/终点便捷滚动（正向）。
    /// 期望：20×100、视口 1000（max=1000）：Start→0、窗口 [0,9]；End→1000、窗口 [10,19]。
    /// </remarks>
    [Test]
    public void C15_ScrollToStartEnd_Forward()
    {
        var (list, _, _) = CreateList(TestListFactory.FixedConfig());
        BindInts(list, 20);

        list.ScrollToStart();

        var snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(0));

        list.ScrollToEnd();

        snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(snapshot.MaxScrollOffset).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(10));
    }

    /// <summary>
    /// 反向列表：ScrollToStart 显示首元素（偏移 max），ScrollToEnd 显示末元素（偏移 0）。
    /// </summary>
    /// <remarks>
    /// 功能：逻辑起点/终点便捷滚动（反向，Start=第一个元素）。
    /// 期望：Start→偏移 max=1000、窗口 [0,9]；End→偏移 0、窗口 [10,19]。
    /// </remarks>
    [Test]
    public void C16_ScrollToStartEnd_Reversed()
    {
        var config = TestListFactory.FixedConfig();
        config.Reversed = true;
        var (list, _, _) = CreateList(config);
        BindInts(list, 20);

        list.ScrollToStart();

        var snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(snapshot.MaxScrollOffset).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(0));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(9));

        list.ScrollToEnd();

        snapshot = list.Snapshot();
        Assert.That(snapshot.Offset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(snapshot.FirstVisibleIndex, Is.EqualTo(10));
        Assert.That(snapshot.LastVisibleIndex, Is.EqualTo(19));
    }

    /// <summary>
    /// 物理方向便捷方法：Top/Left→偏移 0、Bottom/Right→偏移 max，轴无关且不受反向影响。
    /// </summary>
    /// <remarks>
    /// 功能：物理主轴向端点便捷滚动（物理语义，与反向无关）。
    /// 期望：正向与反向下 Top/Left 均为 0、Bottom/Right 均为 max（1000）。
    /// </remarks>
    [Test]
    public void C17_PhysicalDirectionAliases()
    {
        foreach (var reversed in new[]
                 {
                     false, true,
                 })
        {
            var config = TestListFactory.FixedConfig();
            config.Reversed = reversed;
            var (list, _, _) = CreateList(config);
            BindInts(list, 20);

            list.ScrollToTop();
            Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));

            list.ScrollToLeft();
            Assert.That(list.Snapshot().Offset, Is.EqualTo(0f).Within(0.01f));

            list.ScrollToBottom();
            Assert.That(list.Snapshot().Offset, Is.EqualTo(1000f).Within(0.01f));

            list.ScrollToRight();
            Assert.That(list.Snapshot().Offset, Is.EqualTo(1000f).Within(0.01f));
        }
    }
}
