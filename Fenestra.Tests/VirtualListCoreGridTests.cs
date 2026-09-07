namespace Fenestra.Tests;

using System;
using Fenestra.Entity;
using NUnit.Framework;

/// <summary>
/// 编排器黑盒测试：Grid 用例——固定交叉轴元素数、AutoFit 交叉轴元素数推断与非法配置。
/// 基建（列表/订阅生命周期、桩辅助）见 <see cref="VirtualListCoreTestBase" />。
/// </summary>
[TestFixture]
public class VirtualListCoreGridTests : VirtualListCoreTestBase
{
    /// <summary>
    /// Grid 固定交叉轴元素数时按交叉轴偏移、按主轴偏移摆放。
    /// </summary>
    /// <remarks>
    /// 功能：固定交叉轴元素数 Grid 的摆放数学。
    /// 期望：交叉轴分 3 格时 CrossOffset 为 0/100/200 循环，第二行（主轴）MainOffset=100。
    /// </remarks>
    [Test]
    public void G1_CrossCount_PlacementGrid()
    {
        var config = TestListFactory.FixedConfig();
        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            CrossCount = 3,
            CrossSpacing = 0f,
        };
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 300f);
        BindInts(list, 9);

        Assert.That(View(provider, 0).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.CrossOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 2).LastPlacement.CrossOffset, Is.EqualTo(200f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 3).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
    }

    /// <summary>
    /// AutoFit 按视口交叉尺寸与参考 cell 尺寸推断交叉轴元素数。
    /// </summary>
    /// <remarks>
    /// 功能：AutoFit 交叉轴元素数推断。
    /// 期望：视口 320、cell 100、间距 10 → 3 格；cell=100，index2 在交叉轴第 2 格，CrossOffset=2*(100+10)=220。
    /// </remarks>
    [Test]
    public void G2_AutoFit_InfersCrossCount()
    {
        var config = TestListFactory.FixedConfig();
        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            AutoFit = true,
            CrossSize = 100f,
            CrossSpacing = 10f,
        };
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 320f); // (320+10)/(100+10)=3 格
        BindInts(list, 9);

        Assert.That(
            View(provider, 2).LastPlacement.CrossOffset,
            Is.EqualTo(220f).Within(0.01f)); // 2*(cell 100 + 间距 10)
    }

    /// <summary>
    /// AutoFit 随视口交叉尺寸变化重新推断交叉轴元素数。
    /// </summary>
    /// <remarks>
    /// 功能：自适应重排中的交叉轴元素数重推断（设计 R2）。
    /// 期望：视口改为 210 → 2 格，index3 进入主轴第二行的交叉轴第 1 格，CrossOffset=1*(100+10)=110。
    /// </remarks>
    [Test]
    public void G3_AutoFit_ReinfersOnViewportChange()
    {
        var config = TestListFactory.FixedConfig();
        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            AutoFit = true,
            CrossSize = 100f,
            CrossSpacing = 10f,
        };
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 320f);
        BindInts(list, 9);

        list.SetViewportSize(1000f, 210f); // (210+10)/(100+10)=2 格

        Assert.That(
            View(provider, 3).LastPlacement.CrossOffset,
            Is.EqualTo(110f).Within(0.01f)); // 1*(cell 100 + 间距 10)
    }

    /// <summary>
    /// 变高 + Grid 为非法配置，构造抛异常。
    /// </summary>
    /// <remarks>
    /// 边界：违反「Grid 仅支持定高模式（等高 cell）」约束（设计 §2.2）。
    /// 期望：构造 VirtualListCore 抛 InvalidOperationException。
    /// </remarks>
    [Test]
    public void G4_VariablePlusGrid_Throws()
    {
        var config = TestListFactory.VariableConfig();
        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            CrossCount = 3,
        };
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new VirtualListCore(config, new StubItemProvider());
        });
    }

    /// <summary>
    /// AutoFit 在视口交叉轴过窄时交叉轴元素数钳制为至少 1 格。
    /// </summary>
    /// <remarks>
    /// 边界：AutoFit 交叉轴元素数下界。
    /// 期望：视口 50、cell 100、间距 10 → (60/110) 截断为 0 → 钳制 1 格；index1 为主轴第 2 行的交叉轴第 0 格。
    /// </remarks>
    [Test]
    public void G6_AutoFit_MinimumOneCross()
    {
        var config = TestListFactory.FixedConfig();
        config.Grid = new VirtualListGridConfig
        {
            Enabled = true,
            AutoFit = true,
            CrossSize = 100f,
            CrossSpacing = 10f,
        };
        var (list, provider, _) = CreateList(config);
        list.SetViewportSize(1000f, 50f);
        BindInts(list, 9);

        Assert.That(View(provider, 1).LastPlacement.MainOffset, Is.EqualTo(100f).Within(0.01f));
        Assert.That(View(provider, 1).LastPlacement.CrossOffset, Is.EqualTo(0f).Within(0.01f));
        Assert.That(View(provider, 0).LastPlacement.CrossSize, Is.EqualTo(50f).Within(0.01f)); // 单格铺满视口交叉轴
    }
}
