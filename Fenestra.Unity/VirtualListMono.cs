namespace Fenestra.Unity;

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fenestra.Abstraction;
using Fenestra.Entity;
using ObservableCollections;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 虚拟列表 Unity 视图壳：对接 UGUI 与 <see cref="VirtualListCore" />（纯 C# 核心编排器）。
/// 自包含设计（无 DI / 对象池依赖）：
/// - 直接持有 item 预制件引用，内部实现取还复用（<see cref="IItemProvider" />）；
/// - 继承 MonoBehaviour 每帧驱动 <see cref="Step" />（惯性 / 回弹）；
/// - 视口 = 本节点 RectTransform，content 自动创建并随滚动偏移移动；
/// - 指针事件翻译为纯数值转发核心（实现 <see cref="IPointerDownHandler" /> 等）。
/// 使用：挂到列表节点，在 Inspector 配置 <see cref="_config" /> 与 <see cref="_itemPrefab" />，
/// 随后调用 <c>Bind(collection)</c> 绑定响应式集合（集合为唯一真相源，列表无增删改 API）。
/// 运行时改配（轴向 / 反向排列 / 尺寸模式 / Grid 等）经 <see cref="ApplyConfig" />；
/// item 预制件与 Mask Viewport 初始化后不可动态改变。
/// item 预制件根节点须实现 <see cref="IItemView" />；其 <see cref="IItemView.SetPlacement" />
/// 按 Content 左上角为原点摆放（根 pivot 与锚点建议 (0,1)），轴向由 <see cref="ItemPlacement.Axis" />
/// 提供：垂直列表 anchoredPosition=(CrossOffset,-MainOffset)、sizeDelta=(CrossSize,MainSize)；
/// 水平列表 anchoredPosition=(MainOffset,-CrossOffset)、sizeDelta=(MainSize,CrossSize)。
/// 反向排列时 <see cref="ItemPlacement.MainOffset" /> 已换算为物理偏移，本壳无需感知反向。
/// 编辑器中可经自定义 Inspector 开启布局预览（见 <c>Fenestra.Unity.Editor</c>）：
/// 在 Scene 视图以线框示意元素间距与排列，不实例化元素、不改场景层级。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class VirtualListMono : MonoBehaviour,
    IDisposable,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler
{
    /// <summary>列表配置（Inspector 序列化，经 ToCore 转核心配置）。</summary>
    [SerializeField]
    private VirtualListMonoConfig _config;

    /// <summary>item 预制件（根节点须实现 <see cref="IItemView" />）。</summary>
    [SerializeField]
    private GameObject _itemPrefab;

    private Vector2 _appliedContentSize = new(float.NaN, float.NaN);
    private float _appliedOffset = float.NaN;
    private RectTransform _content;

    private bool _disposed;
    private DirectItemProvider _provider;
    private RectTransform _viewport;

    /// <summary>只读暴露核心编排器（诊断 / 高级用途）。</summary>
    public VirtualListCore Core { get; private set; }

    /// <summary>只读暴露 Inspector 配置（编辑器布局预览 / 诊断用途）。</summary>
    public VirtualListMonoConfig Config => _config;

    /// <summary>可见窗口流（首 / 尾可见 index，与元素级 OnShow / OnHide 同源）。</summary>
    public Observable<VisibleWindow> VisibleWindowChanged => Core?.VisibleWindowChanged;

    /// <summary>滚动到顶事件流（跨越边界时刻触发一次）。</summary>
    public Observable<Unit> ReachedTop => Core?.ReachedTop;

    /// <summary>滚动到底事件流（跨越边界时刻触发一次）。</summary>
    public Observable<Unit> ReachedBottom => Core?.ReachedBottom;

    /// <summary>是否已完成初始化。</summary>
    public bool Initialized { get; private set; }

    /// <summary>当前生效主轴（优先取核心配置；运行时经 <see cref="ApplyConfig" /> 可变化）。</summary>
    private VirtualListAxis Axis => Core != null ? Core.Config.Axis : _config.Axis;

    /// <summary>当前生效尺寸模式（优先取核心配置）。</summary>
    private VirtualListItemSizeMode SizeMode => Core != null ? Core.Config.SizeMode : _config.SizeMode;

    private void Awake()
    {
        _viewport = GetComponent<RectTransform>();
        Initialize();
    }

    private void Start()
    {
        // Awake 时 rect 可能尚未完成布局，Start 补推一次（后续变化由 OnRectTransformDimensionsChange 校正）。
        PushViewportSize();
    }

    private void Update()
    {
        if (!Initialized || _disposed)
        {
            return;
        }

        Step(Time.deltaTime);
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (Initialized && Core != null)
        {
            PushViewportSize();
        }
    }

    /// <summary>释放：销毁核心、归还并清理全部元素。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Core != null)
        {
            Core.Dispose();
            Core = null;
        }

        _provider?.Clear();
        _provider = null;
    }

    /// <inheritdoc />
    public void OnDrag(PointerEventData eventData)
    {
        if (TryToLocal(eventData, out var local))
        {
            HandlePointerDrag(local);
        }
    }

    /// <inheritdoc />
    public void OnPointerDown(PointerEventData eventData)
    {
        if (TryToLocal(eventData, out var local))
        {
            HandlePointerDown(local);
        }
    }

    /// <inheritdoc />
    public void OnPointerUp(PointerEventData eventData)
    {
        if (TryToLocal(eventData, out var local))
        {
            HandlePointerUp(local);
        }
    }

    /// <summary>初始化：创建核心编排器、content、挂裁剪并按配置注入默认尺寸。幂等，Awake 已自动调用。</summary>
    public void Initialize()
    {
        if (Initialized)
        {
            return;
        }

        if (_itemPrefab == null)
        {
            throw new InvalidOperationException($"{nameof(VirtualListMono)}: 未配置 _itemPrefab，无法初始化。");
        }

        _config ??= new VirtualListMonoConfig();

        // content 需先于 provider 创建：元素作为 content 子节点，随滚动偏移整体移动。
        SetupContent();
        _provider = new DirectItemProvider(_itemPrefab, _content);
        Core = new VirtualListCore(_config.ToCore(), _provider);
        Initialized = true;
        EnsureMask();
        InjectDefaultItemSize();
        PushViewportSize();
    }

    /// <summary>绑定响应式集合（集合为唯一真相源，列表无增删改 API）。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="collection">响应式集合。</param>
    public void Bind<T>(IReadOnlyObservableList<T> collection)
    {
        EnsureInitialized();
        Core.Bind(collection);
        ApplyContentPosition();
    }

    /// <summary>退订集合并清空窗口（可重新 Bind）。</summary>
    public void Unbind()
    {
        if (Core != null)
        {
            Core.Unbind();
        }
    }

    /// <summary>
    /// 运行时应用新配置（几乎全部可配置项均可动态改变）：轴向、反向排列、尺寸模式、定高尺寸、
    /// 过扫描、间距、越界行为、Grid。不可动态改变：item 预制件（Provider 绑定）、Mask Viewport（初始化时生效）。
    /// 轴向变化会同时校正视口主/交叉轴映射、变高估计值与 content 定位。
    /// </summary>
    /// <param name="config">新列表配置（Grid + 变高非法抛异常）。</param>
    public void ApplyConfig(VirtualListConfig config)
    {
        EnsureInitialized();
        Core.ApplyConfig(config);
        InjectDefaultItemSize();
        PushViewportSize();
        ApplyContentPosition();
    }

    /// <summary>运行时切换滚动主轴。轴向变化会校正视口主/交叉轴映射、变高估计值与 content 定位。</summary>
    /// <param name="axis">新主轴。</param>
    public void SetAxis(VirtualListAxis axis)
    {
        EnsureInitialized();
        Core.SetAxis(axis);
        InjectDefaultItemSize();
        PushViewportSize();
        ApplyContentPosition();
    }

    /// <summary>运行时切换反向排列（摆放由核心重算，本壳无需额外处理）。</summary>
    /// <param name="reversed">是否沿主轴反向排列。</param>
    public void SetReversed(bool reversed)
    {
        EnsureInitialized();
        Core.SetReversed(reversed);
    }

    /// <summary>运行时设置初始滚动锚点（偏移最小值 / 偏移最大值；改变即重新定位到新锚点）。</summary>
    /// <param name="anchor">新初始锚点。</param>
    public void SetInitialAnchor(VirtualListInitialAnchor anchor)
    {
        EnsureInitialized();
        Core.SetInitialAnchor(anchor);
        ApplyContentPosition();
    }

    /// <summary>运行时切换尺寸模式（变高需测量；切换时重新注入默认估计尺寸）。</summary>
    /// <param name="mode">新尺寸模式。</param>
    public void SetSizeMode(VirtualListItemSizeMode mode)
    {
        EnsureInitialized();
        Core.SetSizeMode(mode);
        InjectDefaultItemSize();
        ApplyContentPosition();
    }

    /// <summary>运行时设置定高尺寸。</summary>
    /// <param name="size">新定高尺寸。</param>
    public void SetFixedItemSize(float size)
    {
        EnsureInitialized();
        Core.SetFixedItemSize(size);
        ApplyContentPosition();
    }

    /// <summary>运行时设置主轴间距。</summary>
    /// <param name="spacing">新间距。</param>
    public void SetSpacing(float spacing)
    {
        EnsureInitialized();
        Core.SetSpacing(spacing);
        ApplyContentPosition();
    }

    /// <summary>运行时设置过扫描（仅影响实例化窗口）。</summary>
    /// <param name="overscan">新过扫描。</param>
    public void SetOverscan(int overscan)
    {
        EnsureInitialized();
        Core.SetOverscan(overscan);
    }

    /// <summary>运行时切换越界行为（O(1)，不触发重排）。</summary>
    /// <param name="overflow">新越界行为。</param>
    public void SetOverflow(VirtualListOverflow overflow)
    {
        EnsureInitialized();
        Core.SetOverflow(overflow);
    }

    /// <summary>运行时替换 Grid 配置（Grid 仅支持定高，变高 + Grid 抛异常）。</summary>
    /// <param name="grid">新 Grid 配置。</param>
    public void SetGrid(VirtualListGridConfig grid)
    {
        EnsureInitialized();
        Core.SetGrid(grid);
        ApplyContentPosition();
    }

    /// <summary>一次性滚动定位（对齐 Start / Center / End，越界夹紧；打断进行中的惯性 / 回弹）。</summary>
    /// <param name="index">目标索引。</param>
    /// <param name="alignment">对齐方式。</param>
    public void ScrollToIndex(int index, ScrollAlignment alignment)
    {
        EnsureInitialized();
        Core.ScrollToIndex(index, alignment);
        ApplyContentPosition();
    }

    /// <summary>滚动到指定偏移（越界夹紧；打断进行中的惯性 / 回弹）。</summary>
    /// <param name="offset">目标偏移。</param>
    public void ScrollToOffset(float offset)
    {
        EnsureInitialized();
        Core.ScrollToOffset(offset);
        ApplyContentPosition();
    }

    /// <summary>滚动到逻辑起点（第一个元素；反向列表滚到物理末端）。</summary>
    public void ScrollToStart()
    {
        EnsureInitialized();
        Core.ScrollToStart();
        ApplyContentPosition();
    }

    /// <summary>滚动到逻辑终点（最后一个元素；反向列表滚到物理起点）。</summary>
    public void ScrollToEnd()
    {
        EnsureInitialized();
        Core.ScrollToEnd();
        ApplyContentPosition();
    }

    /// <summary>滚动到物理主轴向起点（垂直=顶、水平=左）。</summary>
    public void ScrollToTop()
    {
        EnsureInitialized();
        Core.ScrollToTop();
        ApplyContentPosition();
    }

    /// <summary>滚动到物理主轴向终点（垂直=底、水平=右）。</summary>
    public void ScrollToBottom()
    {
        EnsureInitialized();
        Core.ScrollToBottom();
        ApplyContentPosition();
    }

    /// <summary>滚动到物理主轴向起点（水平=左、垂直=顶）。</summary>
    public void ScrollToLeft()
    {
        EnsureInitialized();
        Core.ScrollToLeft();
        ApplyContentPosition();
    }

    /// <summary>滚动到物理主轴向终点（水平=右、垂直=底）。</summary>
    public void ScrollToRight()
    {
        EnsureInitialized();
        Core.ScrollToRight();
        ApplyContentPosition();
    }

    /// <summary>推进一帧（惯性 / 回弹）。生产由 Update 每帧驱动，测试可手动调用。</summary>
    /// <param name="dt">帧时长。</param>
    public void Step(float dt)
    {
        if (Core == null)
        {
            return;
        }

        Core.Step(dt);
        ApplyContentPosition();
    }

    /// <summary>指针按下（纯数值入口，测试直调本地坐标；生产经 EventSystem 接口转发）。</summary>
    /// <param name="localPos">视口本地坐标。</param>
    public void HandlePointerDown(Vector2 localPos)
    {
        EnsureInitialized();
        Core.HandlePointerDown(MainComponent(localPos));
    }

    /// <summary>指针拖动（纯数值入口）。</summary>
    /// <param name="localPos">视口本地坐标。</param>
    public void HandlePointerDrag(Vector2 localPos)
    {
        EnsureInitialized();
        Core.HandlePointerDrag(MainComponent(localPos));
    }

    /// <summary>指针松手（纯数值入口）。</summary>
    /// <param name="localPos">视口本地坐标。</param>
    public void HandlePointerUp(Vector2 localPos)
    {
        EnsureInitialized();
        Core.HandlePointerUp(MainComponent(localPos));
    }

    /// <summary>诊断快照（转发核心公开诊断接口）。</summary>
    /// <returns>快照；未初始化时返回默认值。</returns>
    public DebugSnapshot Snapshot()
    {
        return Core != null ? Core.Snapshot() : default;
    }

    private void EnsureInitialized()
    {
        if (!Initialized)
        {
            Initialize();
        }
    }

    private void SetupContent()
    {
        var go = new GameObject("Content", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _content = (RectTransform)go.transform;
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(0f, 1f);
        _content.pivot = new Vector2(0f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = Vector2.zero;
    }

    private void EnsureMask()
    {
        if (_config.MaskViewport && GetComponent<RectMask2D>() == null)
        {
            gameObject.AddComponent<RectMask2D>();
        }
    }

    private void InjectDefaultItemSize()
    {
        if (SizeMode != VirtualListItemSizeMode.Variable)
        {
            return;
        }

        var prefabRect = _itemPrefab.GetComponent<RectTransform>();
        if (prefabRect == null)
        {
            return;
        }

        var size = Axis == VirtualListAxis.Vertical
            ? prefabRect.rect.height
            : prefabRect.rect.width;
        if (size > 0f)
        {
            Core.SetDefaultItemSize(size);
        }
    }

    private void PushViewportSize()
    {
        if (Core == null)
        {
            return;
        }

        var rect = _viewport.rect;
        if (Axis == VirtualListAxis.Vertical)
        {
            Core.SetViewportSize(rect.height, rect.width);
        }
        else
        {
            Core.SetViewportSize(rect.width, rect.height);
        }
    }

    private void ApplyContentPosition()
    {
        if (Core == null || _content == null)
        {
            return;
        }

        var snapshot = Core.Snapshot();
        var offset = snapshot.Offset;
        if (!Mathf.Approximately(offset, _appliedOffset))
        {
            _appliedOffset = offset;
            _content.anchoredPosition = Axis == VirtualListAxis.Vertical
                ? new Vector2(0f, offset)
                : new Vector2(-offset, 0f);
        }

        var rect = _viewport.rect;
        var size = Axis == VirtualListAxis.Vertical
            ? new Vector2(rect.width, snapshot.ContentSize)
            : new Vector2(snapshot.ContentSize, rect.height);
        if (!Mathf.Approximately(size.x, _appliedContentSize.x)
            || !Mathf.Approximately(size.y, _appliedContentSize.y))
        {
            _appliedContentSize = size;
            _content.sizeDelta = size;
        }
    }

    private float MainComponent(Vector2 localPos)
    {
        var rect = _viewport.rect;
        return Axis == VirtualListAxis.Vertical
            ? rect.yMax - localPos.y
            : localPos.x - rect.xMin;
    }

    private bool TryToLocal(PointerEventData eventData, out Vector2 local)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _viewport,
            eventData.position,
            eventData.pressEventCamera,
            out local);
    }

    /// <summary>
    /// 自包含元素提供者：从 item 预制件实例化并复用（归还对象进复用栈，再取优先复用）。
    /// 取用走同步完成 UniTask（装配后运行期不切帧）。
    /// </summary>
    private sealed class DirectItemProvider : IItemProvider
    {
        private readonly Transform _parent;
        private readonly Stack<GameObject> _pool = new();
        private readonly GameObject _prefab;

        /// <summary>初始化。</summary>
        /// <param name="prefab">item 预制件。</param>
        /// <param name="parent">实例挂载父节点（content）。</param>
        public DirectItemProvider(GameObject prefab, Transform parent)
        {
            _prefab = prefab;
            _parent = parent;
        }

        /// <inheritdoc />
        public UniTask<IItemView> GetAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var instance = _pool.Count > 0 ? _pool.Pop() : Instantiate(_prefab, _parent);
            instance.SetActive(true);
            var view = instance.GetComponent<IItemView>();
            if (view == null)
            {
                Destroy(instance);
                throw new InvalidOperationException($"item 预制件 '{_prefab.name}' 根节点未实现 {nameof(IItemView)}");
            }

            return UniTask.FromResult(view);
        }

        /// <inheritdoc />
        public void Return(IItemView view)
        {
            var mb = view as MonoBehaviour;
            if (mb == null)
            {
                return;
            }

            mb.gameObject.SetActive(false);
            _pool.Push(mb.gameObject);
        }

        /// <summary>销毁全部复用实例（Dispose 兜底；常规销毁随场景层级自动回收）。</summary>
        public void Clear()
        {
            while (_pool.Count > 0)
            {
                var instance = _pool.Pop();
                if (instance != null)
                {
                    Destroy(instance);
                }
            }
        }
    }
}
