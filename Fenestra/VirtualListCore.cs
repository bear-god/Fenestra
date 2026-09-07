namespace Fenestra;

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fenestra.Abstraction;
using Fenestra.Core;
using Fenestra.Entity;
using ObservableCollections;
using R3;

/// <summary>
/// 虚拟列表编排器（纯 C#，无引擎依赖）：集合绑定、窗口管理、元素生命周期、滚动/输入/尺寸入口、R3 事件流。
/// 由宿主驱动：Unity 视图壳转发指针/尺寸/tick，测试直接调用。可 headless 运行（设计 D11）。
/// 唯一测试与使用入口（设计 D14）。
/// </summary>
public sealed class VirtualListCore : IDisposable
{
    private const int MaxReconcilePasses = 8;
    private readonly Dictionary<int, IItemView> _activeViews = new();
    private readonly CancellationTokenSource _cts = new();

    private readonly VirtualListLayout _layout;
    private readonly IVirtualListLogger _logger;
    private readonly ScrollPhysics _physics;
    private readonly IItemProvider _provider;
    private readonly Subject<Unit> _reachedBottom = new();
    private readonly Subject<Unit> _reachedTop = new();
    private readonly Dictionary<int, bool> _shownState = new();
    private readonly Subject<VisibleWindow> _visibleWindowChanged = new();
    private IDisposable? _collectionSubscriptions;
    private VirtualListConfig _config;

    private Func<int>? _countAccessor;
    private bool _disposed;
    private bool _isVariableSize;
    private Func<int, object>? _itemAccessor;
    private float _lastOffset;
    private VisibleWindow _lastWindow = new(-1, -1);
    private int _reconcileDepth;
    private bool _reconcilePending;

    /// <summary>初始化编排器。非法配置（Grid + 变高）抛异常。</summary>
    /// <param name="config">列表配置。</param>
    /// <param name="provider">元素提供者。</param>
    /// <param name="logger">日志（可空，默认 Null 实现）。</param>
    public VirtualListCore(VirtualListConfig config, IItemProvider provider, IVirtualListLogger? logger = null)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        _config = config;
        _layout = new VirtualListLayout(config);
        _physics = new ScrollPhysics(config.Overflow);
        _provider = provider;
        _logger = logger ?? NullVirtualListLogger.Instance;
        _isVariableSize = config.SizeMode == VirtualListItemSizeMode.Variable;
    }

    /// <summary>当前生效配置（运行时经 <see cref="ApplyConfig" /> 改变）。</summary>
    public VirtualListConfig Config => _config;

    /// <summary>可见窗口流（首/尾可见 index，与元素级 OnShow/OnHide 同源）。</summary>
    public Observable<VisibleWindow> VisibleWindowChanged => _visibleWindowChanged;

    /// <summary>滚动到顶事件流（跨越边界时刻触发一次）。</summary>
    public Observable<Unit> ReachedTop => _reachedTop;

    /// <summary>滚动到底事件流（跨越边界时刻触发一次）。</summary>
    public Observable<Unit> ReachedBottom => _reachedBottom;

    /// <summary>释放：退订、归还全部元素、事件流完成。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _collectionSubscriptions?.Dispose();
        _collectionSubscriptions = null;
        ReleaseAllViews();
        _visibleWindowChanged.OnCompleted();
        _reachedTop.OnCompleted();
        _reachedBottom.OnCompleted();
        _visibleWindowChanged.Dispose();
        _reachedTop.Dispose();
        _reachedBottom.Dispose();
        _cts.Dispose();
    }

    /// <summary>绑定响应式集合（集合为唯一真相源，列表无增删改 API）。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="collection">响应式集合。</param>
    public void Bind<T>(IReadOnlyObservableList<T> collection)
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        Unbind();

        var captured = collection;
        _countAccessor = () => captured.Count;
        _itemAccessor = i => captured[i]!;
        _layout.SetItemCount(captured.Count);
        ApplyInitialOffset();

        var subscriptions = new DisposableList(6);
        subscriptions.Add(captured.ObserveAdd().Subscribe(_ => HandleCollectionMutated()));
        subscriptions.Add(captured.ObserveRemove().Subscribe(_ => HandleCollectionMutated()));
        subscriptions.Add(captured.ObserveReplace().Subscribe(e => HandleCollectionReplace(e.Index)));
        subscriptions.Add(captured.ObserveMove().Subscribe(_ => HandleCollectionMutated()));
        subscriptions.Add(captured.ObserveReset().Subscribe(_ => HandleCollectionReset()));
        _collectionSubscriptions = subscriptions;

        RequestReconcile();
    }

    /// <summary>退订集合并清空窗口（可重新 Bind）。</summary>
    public void Unbind()
    {
        _collectionSubscriptions?.Dispose();
        _collectionSubscriptions = null;
        _countAccessor = null;
        _itemAccessor = null;
        _layout.SetItemCount(0);
        _layout.ResetWindowCache();
        _physics.Cancel();
        _physics.SetPosition(0f);
        _layout.ScrollTo(0f);
        ReleaseAllViews();
        _lastWindow = new VisibleWindow(-1, -1);
        _lastOffset = 0f;
    }

    /// <summary>设置视口尺寸（主轴 + 交叉轴）。</summary>
    /// <param name="mainSize">主轴视口尺寸。</param>
    /// <param name="crossSize">交叉轴视口尺寸。</param>
    public void SetViewportSize(float mainSize, float crossSize)
    {
        _layout.SetViewportSize(mainSize, crossSize);
        RequestReconcile();
    }

    /// <summary>设置变高模式未测量元素的估计尺寸（见设计 D12；适配层由预制件 rect 注入）。</summary>
    /// <param name="size">估计尺寸。</param>
    public void SetDefaultItemSize(float size)
    {
        _layout.SetDefaultItemSize(size);
        RequestReconcile();
    }

    /// <summary>
    /// 运行时应用新配置（几乎全部可配置项均可动态改变）：轴向、反向排列、尺寸模式、定高尺寸、
    /// 过扫描、间距、越界行为、Grid（含交叉轴列数 / AutoFit / 间距）。
    /// 不可动态改变：元素预制件（Provider 绑定，见适配层）、MaskViewport（初始化时生效的裁剪挂载）。
    /// 轴向 / 尺寸模式变化会使测量语义改变：变高模式下全量失效并重测活跃元素。
    /// </summary>
    /// <param name="config">新列表配置（Grid + 变高非法抛异常）。</param>
    public void ApplyConfig(VirtualListConfig config)
    {
        var axisChanged = _config.Axis != config.Axis;
        var sizeModeChanged = _config.SizeMode != config.SizeMode;
        var initialAnchorChanged = _config.InitialAnchor != config.InitialAnchor;

        _layout.ApplyConfig(config);
        _physics.SetOverflow(config.Overflow);
        _config = config;
        _isVariableSize = config.SizeMode == VirtualListItemSizeMode.Variable;

        if (_isVariableSize && (axisChanged || sizeModeChanged))
        {
            _layout.InvalidateAllMeasuredSizes();
            RemeasureAllActiveViews();
        }

        if (initialAnchorChanged)
        {
            ApplyInitialOffset();
        }

        RequestReconcile();
    }

    /// <summary>运行时切换滚动主轴（昂贵：变高模式下全量失效并重测活跃元素）。适配层需同步校正视口主/交叉轴映射、默认尺寸与 content 定位。</summary>
    /// <param name="axis">新主轴。</param>
    public void SetAxis(VirtualListAxis axis)
    {
        if (_config.Axis == axis)
        {
            return;
        }

        _config.Axis = axis;
        _layout.ApplyConfig(_config);
        if (_isVariableSize)
        {
            _layout.InvalidateAllMeasuredSizes();
            RemeasureAllActiveViews();
        }

        RequestReconcile();
    }

    /// <summary>运行时切换反向排列（仅镜像主轴摆放与滚动原点，重摆但不重测）。</summary>
    /// <param name="reversed">是否沿主轴反向排列。</param>
    public void SetReversed(bool reversed)
    {
        if (_config.Reversed == reversed)
        {
            return;
        }

        _config.Reversed = reversed;
        _layout.ApplyConfig(_config);
        RequestReconcile();
    }

    /// <summary>运行时设置初始滚动锚点（偏移最小值 OffsetMin / 偏移最大值 OffsetMax；改变即重新定位到新锚点）。</summary>
    /// <param name="anchor">新初始锚点。</param>
    public void SetInitialAnchor(VirtualListInitialAnchor anchor)
    {
        if (_config.InitialAnchor == anchor)
        {
            return;
        }

        _config.InitialAnchor = anchor;
        _layout.ApplyConfig(_config);
        ApplyInitialOffset();
        RequestReconcile();
    }

    /// <summary>运行时切换尺寸模式（昂贵：定高→变高全量重测；变高→定高仅失效测量）。</summary>
    /// <param name="mode">新尺寸模式。</param>
    public void SetSizeMode(VirtualListItemSizeMode mode)
    {
        var changed = _config.SizeMode != mode;
        _config.SizeMode = mode;
        _layout.ApplyConfig(_config);
        _isVariableSize = mode == VirtualListItemSizeMode.Variable;
        if (changed)
        {
            _layout.InvalidateAllMeasuredSizes();
            if (_isVariableSize)
            {
                RemeasureAllActiveViews();
            }
        }

        RequestReconcile();
    }

    /// <summary>运行时设置定高尺寸（定高模式生效，重摆但不重测）。</summary>
    /// <param name="size">新定高尺寸。</param>
    public void SetFixedItemSize(float size)
    {
        _config.FixedItemSize = size;
        _layout.ApplyConfig(_config);
        RequestReconcile();
    }

    /// <summary>运行时设置主轴间距（重摆但不重测）。</summary>
    /// <param name="spacing">新间距。</param>
    public void SetSpacing(float spacing)
    {
        _config.Spacing = spacing;
        _layout.ApplyConfig(_config);
        RequestReconcile();
    }

    /// <summary>运行时设置过扫描（仅影响实例化窗口，无需重测；负值按 0 处理）。</summary>
    /// <param name="overscan">新过扫描。</param>
    public void SetOverscan(int overscan)
    {
        _config.Overscan = Math.Max(0, overscan);
        _layout.ApplyConfig(_config);
        RequestReconcile();
    }

    /// <summary>运行时切换越界行为（O(1)：仅更新物理，不触发布局 / 窗口重算）。</summary>
    /// <param name="overflow">新越界行为。</param>
    public void SetOverflow(VirtualListOverflow overflow)
    {
        _config.Overflow = overflow;
        _physics.SetOverflow(overflow);
    }

    /// <summary>运行时替换 Grid 配置（Grid 仅支持定高；变高 + Grid 抛异常，且不改变当前生效配置）。</summary>
    /// <param name="grid">新 Grid 配置。</param>
    public void SetGrid(VirtualListGridConfig grid)
    {
        // 先在副本上校验，抛异常时当前生效配置保持不变（与 ApplyConfig 语义一致）。
        var candidate = _config;
        candidate.Grid = grid;
        _layout.ApplyConfig(candidate);
        _config = candidate;
        RequestReconcile();
    }

    /// <summary>元素尺寸变化通知 → 重新测量并校正布局。</summary>
    /// <param name="index">元素索引。</param>
    public void NotifyItemSizeChanged(int index)
    {
        if (!_activeViews.TryGetValue(index, out var view))
        {
            return;
        }

        _layout.InvalidateMeasuredSize(index);
        var size = view.Measure(_config.Axis);
        _layout.SetMeasuredSize(index, size);
        RequestReconcile();
    }

    /// <summary>一次性滚动定位（对齐 Start/Center/End，越界夹紧；打断进行中的惯性/回弹）。</summary>
    /// <param name="index">目标索引。</param>
    /// <param name="alignment">对齐方式。</param>
    public void ScrollToIndex(int index, ScrollAlignment alignment)
    {
        _physics.Cancel();
        _layout.ScrollToIndex(index, alignment);
        _physics.SetPosition(_layout.Offset);
        OnOffsetChanged();
    }

    /// <summary>滚动到指定偏移（越界夹紧；打断进行中的惯性/回弹）。</summary>
    /// <param name="offset">目标偏移。</param>
    public void ScrollToOffset(float offset)
    {
        _physics.Cancel();
        _layout.ScrollTo(offset);
        _physics.SetPosition(_layout.Offset);
        OnOffsetChanged();
    }

    /// <summary>滚动到逻辑起点（第一个元素）：正向→偏移 0，反向→偏移 MaxScrollOffset。</summary>
    public void ScrollToStart()
    {
        ScrollToOffset(_config.Reversed ? _layout.MaxScrollOffset : 0f);
    }

    /// <summary>滚动到逻辑终点（最后一个元素）：正向→偏移 MaxScrollOffset，反向→偏移 0。</summary>
    public void ScrollToEnd()
    {
        ScrollToOffset(_config.Reversed ? 0f : _layout.MaxScrollOffset);
    }

    /// <summary>滚动到物理主轴向起点（偏移 0）：垂直=顶、水平=左。轴无关。</summary>
    public void ScrollToTop()
    {
        ScrollToOffset(0f);
    }

    /// <summary>滚动到物理主轴向终点（偏移 MaxScrollOffset）：垂直=底、水平=右。轴无关。</summary>
    public void ScrollToBottom()
    {
        ScrollToOffset(_layout.MaxScrollOffset);
    }

    /// <summary>滚动到物理主轴向起点（偏移 0）：水平=左、垂直=顶。轴无关。</summary>
    public void ScrollToLeft()
    {
        ScrollToOffset(0f);
    }

    /// <summary>滚动到物理主轴向终点（偏移 MaxScrollOffset）：水平=右、垂直=底。轴无关。</summary>
    public void ScrollToRight()
    {
        ScrollToOffset(_layout.MaxScrollOffset);
    }

    /// <summary>推进一帧（惯性/回弹；生产由装配层注册到 IScheduleService，测试手动调用）。</summary>
    /// <param name="dt">帧时长。</param>
    public void Step(float dt)
    {
        var position = _physics.Step(dt, 0f, _layout.MaxScrollOffset);
        _layout.ScrollTo(position);
        OnOffsetChanged();
    }

    /// <summary>按下。</summary>
    /// <param name="mainPos">主轴坐标。</param>
    public void HandlePointerDown(float mainPos)
    {
        _physics.SetBounds(0f, _layout.MaxScrollOffset);
        _physics.HandlePointerDown(mainPos);
    }

    /// <summary>拖动。</summary>
    /// <param name="mainPos">主轴坐标。</param>
    public void HandlePointerDrag(float mainPos)
    {
        _physics.SetBounds(0f, _layout.MaxScrollOffset);
        _physics.HandlePointerDrag(mainPos);
        _layout.ScrollTo(_physics.Position);
        OnOffsetChanged();
    }

    /// <summary>松手（进入惯性/回弹）。</summary>
    /// <param name="mainPos">主轴坐标。</param>
    public void HandlePointerUp(float mainPos)
    {
        _physics.SetBounds(0f, _layout.MaxScrollOffset);
        _physics.HandlePointerUp(mainPos);
        _layout.ScrollTo(_physics.Position);
        OnOffsetChanged();
    }

    /// <summary>诊断快照（公开诊断接口，见设计 D14）：窗口区间/偏移/内容尺寸，供测试与诊断。</summary>
    /// <returns>快照。</returns>
    public DebugSnapshot Snapshot()
    {
        var snapshot = _layout.Snapshot();
        snapshot.Offset = _physics.Position;
        snapshot.InstantiatedCount = _activeViews.Count;
        return snapshot;
    }

    private void HandleCollectionMutated()
    {
        if (_countAccessor is null)
        {
            return;
        }

        _layout.SetItemCount(_countAccessor());
        RebindAllActiveViews();
        RequestReconcile();
    }

    private void HandleCollectionReplace(int index)
    {
        if (_countAccessor is null)
        {
            return;
        }

        if (_activeViews.TryGetValue(index, out var view))
        {
            view.Bind(_itemAccessor!(index), index);
            if (_isVariableSize)
            {
                _layout.InvalidateMeasuredSize(index);
                _layout.SetMeasuredSize(index, view.Measure(_config.Axis));
            }
        }

        RequestReconcile();
    }

    private void HandleCollectionReset()
    {
        if (_countAccessor is null)
        {
            return;
        }

        _layout.SetItemCount(_countAccessor());
        ApplyInitialOffset();
        ReleaseAllViews();
        RequestReconcile();
    }

    private void RebindAllActiveViews()
    {
        if (_itemAccessor is null)
        {
            return;
        }

        // 集合缩容后超出新 count 的活跃 index 已无对应数据：跳过重绑，由窗口 diff 将其归还。
        var count = _countAccessor!();
        foreach (var pair in _activeViews)
        {
            var index = pair.Key;
            if (index >= count)
            {
                continue;
            }

            var view = pair.Value;
            view.Bind(_itemAccessor(index), index);
            if (_isVariableSize)
            {
                _layout.InvalidateMeasuredSize(index);
                _layout.SetMeasuredSize(index, view.Measure(_config.Axis));
            }
        }
    }

    private void RemeasureAllActiveViews()
    {
        foreach (var pair in _activeViews)
        {
            var index = pair.Key;
            _layout.SetMeasuredSize(index, pair.Value.Measure(_config.Axis));
        }
    }

    /// <summary>定位到初始滚动偏移（初始锚点；打断惯性，重置边界基线，不触发边界事件）。</summary>
    private void ApplyInitialOffset()
    {
        var initial = _layout.GetInitialOffset();
        _physics.Cancel();
        _physics.SetPosition(initial);
        _layout.ScrollTo(initial);
        _lastOffset = initial;
    }

    private void RequestReconcile()
    {
        if (_disposed)
        {
            return;
        }

        if (_reconcileDepth > 0)
        {
            _reconcilePending = true;
            return;
        }

        ReconcileAsync().Forget();
    }

    private async UniTaskVoid ReconcileAsync()
    {
        if (_disposed)
        {
            return;
        }

        _reconcileDepth++;
        try
        {
            var pass = 0;
            while (pass < MaxReconcilePasses)
            {
                pass++;
                var diff = _layout.TakeDiff();
                if (diff.EnteringCount == 0 && diff.LeavingCount == 0)
                {
                    RefreshPlacementsAndVisibility();
                    break;
                }

                ReleaseLeaving(diff);
                var acquired = await AcquireEnteringAsync(diff);
                RefreshPlacementsAndVisibility();
                if (!acquired)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"虚拟列表 reconcile 异常: {ex}");
        }
        finally
        {
            _reconcileDepth--;
            if (_reconcilePending && !_disposed)
            {
                _reconcilePending = false;
                RequestReconcile();
            }
        }
    }

    private void ReleaseLeaving(WindowDiff diff)
    {
        for (var i = 0; i < diff.LeavingCount; i++)
        {
            ReleaseView(diff.Leaving[i]);
        }
    }

    private async UniTask<bool> AcquireEnteringAsync(WindowDiff diff)
    {
        var any = false;
        for (var i = 0; i < diff.EnteringCount; i++)
        {
            var index = diff.Entering[i];
            if (_activeViews.ContainsKey(index))
            {
                continue;
            }

            // 未预热（GetAsync 返回未完成 Task，装配错误路径）：await 挂起等待完成，
            // 完成后 reconcile 内联续延持续推进直至窗口填满（设计 E2）；已完成任务不切帧（D2）。
            var view = await _provider.GetAsync(_cts.Token);
            if (view is null)
            {
                _logger.Error($"元素提供者返回空视图，index={index}");
                continue;
            }

            _activeViews[index] = view;
            view.Bind(_itemAccessor!(index), index);
            if (_isVariableSize)
            {
                var measured = view.Measure(_config.Axis);
                _layout.SetMeasuredSize(index, measured);
            }

            any = true;
        }

        return any;
    }

    private void RefreshPlacementsAndVisibility()
    {
        // 布局可能已因测量变化 → 重摆全部活跃元素（窗口规模，廉价）。
        foreach (var pair in _activeViews)
        {
            pair.Value.SetPlacement(_layout.GetItemPlacement(pair.Key));
        }

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var first = _layout.FirstVisibleIndex;
        var last = _layout.LastVisibleIndex;
        foreach (var pair in _activeViews)
        {
            var index = pair.Key;
            var shouldShow = index >= first && index <= last;
            var shown = _shownState.TryGetValue(index, out var value) && value;
            if (shouldShow && !shown)
            {
                pair.Value.OnShow();
                _shownState[index] = true;
            }
            else if (!shouldShow && shown)
            {
                pair.Value.OnHide();
                _shownState[index] = false;
            }
        }

        var window = new VisibleWindow(first, last);
        if (window.First != _lastWindow.First || window.Last != _lastWindow.Last)
        {
            _lastWindow = window;
            _visibleWindowChanged.OnNext(window);
        }
    }

    private void ReleaseView(int index)
    {
        if (!_activeViews.TryGetValue(index, out var view))
        {
            return;
        }

        if (_shownState.TryGetValue(index, out var shown) && shown)
        {
            view.OnHide();
        }

        view.OnUnbind();
        _provider.Return(view);
        _activeViews.Remove(index);
        _shownState.Remove(index);
    }

    private void ReleaseAllViews()
    {
        if (_activeViews.Count == 0)
        {
            return;
        }

        var indices = new List<int>(_activeViews.Count);
        foreach (var pair in _activeViews)
        {
            indices.Add(pair.Key);
        }

        foreach (var index in indices)
        {
            ReleaseView(index);
        }
    }

    private void OnOffsetChanged()
    {
        CheckBoundaryEvents();
        RequestReconcile();
    }

    private void CheckBoundaryEvents()
    {
        var offset = _physics.Position;
        var max = _layout.MaxScrollOffset;
        if (offset <= 0f && _lastOffset > 0f)
        {
            _reachedTop.OnNext(Unit.Default);
        }
        else if (offset >= max && max > 0f && _lastOffset < max)
        {
            _reachedBottom.OnNext(Unit.Default);
        }

        _lastOffset = offset;
    }

    /// <summary>订阅集合的轻量容器。</summary>
    private sealed class DisposableList : IDisposable
    {
        private readonly List<IDisposable> _items;

        /// <summary>初始化。</summary>
        /// <param name="capacity">预估订阅数。</param>
        public DisposableList(int capacity)
        {
            _items = new List<IDisposable>(capacity);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var item in _items)
            {
                item.Dispose();
            }

            _items.Clear();
        }

        /// <summary>添加订阅。</summary>
        /// <param name="item">订阅句柄。</param>
        public void Add(IDisposable item)
        {
            _items.Add(item);
        }
    }
}
