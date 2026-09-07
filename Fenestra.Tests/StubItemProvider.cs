namespace Fenestra.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fenestra.Abstraction;

/// <summary>桩 Provider：记录取还次数、活跃集合、全部创建视图，复用视图（模拟对象池）。</summary>
internal sealed class StubItemProvider : IItemProvider
{
    private readonly Func<int, IItemView> _factory;
    private readonly Stack<IItemView> _pool = new();
    private Func<CancellationToken, UniTask<IItemView>>? _manualGet;

    /// <summary>初始化桩 Provider。</summary>
    /// <param name="factory">视图工厂（参数为创建序号）。</param>
    public StubItemProvider(Func<int, IItemView>? factory = null)
    {
        _factory = factory ?? (i => new StubItemView());
    }

    /// <summary>GetAsync 调用次数。</summary>
    public int GetCount { get; private set; }

    /// <summary>Return 调用次数。</summary>
    public int ReturnCount { get; private set; }

    /// <summary>当前借出中的视图。</summary>
    public HashSet<IItemView> Active { get; } = new();

    /// <summary>全部创建过的视图。</summary>
    public List<IItemView> Created { get; } = new();

    /// <summary>手动完成模式下尚未完成（仍挂起）的获取请求。</summary>
    public List<UniTaskCompletionSource<IItemView>> PendingGets { get; } = new();

    /// <summary>
    /// 启用手动完成模式：GetAsync 返回未完成的 UniTask，由用例通过 <see cref="PendingGets" /> 逐个完成。
    /// 用于测未预热（GetAsync 未完成）路径。
    /// </summary>
    public void SetManualGet()
    {
        _manualGet = _ =>
        {
            var pending = new UniTaskCompletionSource<IItemView>();
            PendingGets.Add(pending);
            return pending.Task;
        };
    }

    /// <inheritdoc />
    public UniTask<IItemView> GetAsync(CancellationToken ct)
    {
        GetCount++;
        if (_manualGet is not null)
        {
            return _manualGet(ct);
        }

        var view = _pool.Count > 0 ? _pool.Pop() : CreateNew();
        Active.Add(view);
        return UniTask.FromResult(view);
    }

    /// <inheritdoc />
    public void Return(IItemView view)
    {
        ReturnCount++;
        Active.Remove(view);
        _pool.Push(view);
    }

    /// <summary>新建视图并记录到 <see cref="Created" />（仅新建时记录，池复用不重复记账）。</summary>
    private IItemView CreateNew()
    {
        var view = _factory(GetCount);
        Created.Add(view);
        return view;
    }
}
