namespace Fenestra.Tests;

using System;
using System.Collections.Generic;
using Fenestra.Entity;
using NUnit.Framework;
using ObservableCollections;

/// <summary>
/// 编排器黑盒测试公共基建：各用例共享的列表/订阅生命周期管理与桩依赖辅助。
/// 用例类继承本基类；测试原则见设计 D14（仅经 <see cref="VirtualListCore" /> 公开面验证，
/// 不直接测试 VirtualListLayout / ScrollPhysics）。
/// </summary>
public abstract class VirtualListCoreTestBase
{
    /// <summary>固定步长（1/60 秒），保证惯性/回弹测试确定性。</summary>
    protected const float Dt = 1f / 60f;

    /// <summary>各用例创建的编排器，TearDown 统一释放。</summary>
    internal List<VirtualListCore> Lists { get; } = new();

    /// <summary>各用例创建的 R3 订阅，TearDown 统一释放。</summary>
    internal List<IDisposable> Subscriptions { get; } = new();

    /// <summary>
    /// 绑定 n 个递增整数到列表并返回可变集合（供用例执行增删改）。
    /// </summary>
    /// <param name="list">编排器。</param>
    /// <param name="count">元素个数。</param>
    /// <returns>已绑定的可变集合。</returns>
    internal static ObservableList<int> BindInts(VirtualListCore list, int count)
    {
        var collection = new ObservableList<int>();
        for (var i = 0; i < count; i++)
        {
            collection.Add(i);
        }

        list.Bind(collection);
        return collection;
    }

    /// <summary>
    /// 按 BoundIndex 查找桩视图，未找到时断言失败。
    /// </summary>
    /// <param name="provider">桩 Provider。</param>
    /// <param name="index">目标索引。</param>
    /// <returns>匹配的桩视图。</returns>
    internal static StubItemView View(StubItemProvider provider, int index)
    {
        foreach (var view in provider.Created)
        {
            if (view is StubItemView stub && stub.BoundIndex == index)
            {
                return stub;
            }
        }

        Assert.Fail($"找不到 BoundIndex={index} 的视图");
        return null!;
    }

    /// <summary>
    /// 创建编排器并登记到 TearDown。
    /// </summary>
    /// <param name="config">列表配置。</param>
    /// <returns>编排器与桩依赖（Provider/日志）。</returns>
    internal (VirtualListCore, StubItemProvider, StubLogger) CreateList(VirtualListConfig config)
    {
        var (list, provider, logger) = TestListFactory.Create(config);
        Lists.Add(list);
        return (list, provider, logger);
    }

    /// <summary>
    /// 以指定桩 Provider 创建编排器并登记到 TearDown。
    /// </summary>
    /// <param name="config">列表配置。</param>
    /// <param name="provider">桩 Provider。</param>
    /// <returns>编排器与桩依赖（Provider/日志）。</returns>
    internal (VirtualListCore, StubItemProvider, StubLogger) CreateList(
        VirtualListConfig config,
        StubItemProvider provider)
    {
        var (list, _, logger) = TestListFactory.Create(config, provider);
        Lists.Add(list);
        return (list, provider, logger);
    }

    /// <summary>
    /// 创建变高编排器，视图高度按索引取 heights（越界回退 100；null 时全 100）。
    /// </summary>
    /// <param name="heights">按索引的高度表。</param>
    /// <returns>编排器与桩依赖（Provider/日志）。</returns>
    internal (VirtualListCore, StubItemProvider, StubLogger) CreateVariableList(float[]? heights)
    {
        var config = TestListFactory.VariableConfig();
        var provider = new StubItemProvider(_ => new StubItemView
        {
            HeightProvider = i => heights is null ? 100f : heights[Math.Min(i, heights.Length - 1)],
        });
        return CreateList(config, provider);
    }

    /// <summary>释放本用例创建的列表与订阅，避免跨用例状态泄漏。</summary>
    [TearDown]
    protected void TearDown()
    {
        foreach (var list in Lists)
        {
            list.Dispose();
        }

        Lists.Clear();
        foreach (var subscription in Subscriptions)
        {
            subscription.Dispose();
        }

        Subscriptions.Clear();
    }

    /// <summary>测试用的可变数据项。</summary>
    protected sealed class TestItem
    {
        /// <summary>初始化。</summary>
        /// <param name="value">初始值。</param>
        public TestItem(int value)
        {
            Value = value;
        }

        /// <summary>值。</summary>
        public int Value { get; set; }
    }
}
