# Fenestra

> 原名 VirtualList。Fenestra 为拉丁语「窗」——虚拟列表正是在海量数据上开的一扇窗，窗口内才实例化元素。

纯 C# 的虚拟列表核心库：只实例化可见窗口内（含过扫描）的元素，支持定高 / 变高、List / Grid、反向排列、惯性滚动与弹性回弹。

核心层 **不依赖任何引擎**，可 headless 运行；引擎宿主需自行实现适配层，核心只暴露纯 C# 契约与数值接口。

## 特性

- **引擎无关**：纯 C#，`netstandard2.0` / `net10.0`，可独立运行与测试
- **集合即真相源**：绑定 `IReadOnlyObservableList<T>`，增删改 / 移动 / 重置自动重算窗口，列表自身无增删改 API
- **两种尺寸模式**：`Fixed` 定高（免测量，性能最优）/ `Variable` 变高（逐项测量并缓存，支持运行时尺寸变化通知）
- **Grid 支持**：等高单元格，固定交叉轴列数或按视口 `AutoFit`
- **双向滚动**：`Vertical` / `Horizontal`，支持反向排列
- **越界行为**：`Clamped` 硬边界 / `Elastic` 弹性回弹
- **滚动物理**：手势状态机、速度估算、惯性衰减、回弹，固定 dt 步进、结果确定
- **R3 事件流**：可见窗口变化、到达顶部 / 底部
- **运行时热更新**：轴向、反向、尺寸模式、间距、过扫描、越界行为、Grid 均可动态切换
- **可观测性**：`Snapshot()` 诊断快照（窗口区间 / 偏移 / 内容尺寸 / 实例化数量）

## 安装

引用 `Fenestra.csproj` 或通过 NuGet 引入 `Fenestra`。依赖：

- [R3](https://github.com/Cysharp/R3)
- [UniTask](https://github.com/Cysharp/UniTask)
- [ObservableCollections](https://github.com/Cysharp/ObservableCollections)（及其 `R3` 绑定包）

## 快速上手

### 1. 配置与绑定

```csharp
var config = new VirtualListConfig
{
    Axis = VirtualListAxis.Vertical,          // 滚动主轴
    SizeMode = VirtualListItemSizeMode.Fixed, // 定高 / 变高
    FixedItemSize = 100f,
    Spacing = 8f,
    Overscan = 2,                             // 过扫描（元素个数）
    Overflow = VirtualListOverflow.Elastic,   // 越界行为
    MaskViewport = true,                      // 弹性越界需宿主挂裁剪
};

using var list = new VirtualListCore(config, new MyItemProvider(), new MyLogger());

var items = new ObservableList<MyItem>();
list.Bind(items);                             // 集合是唯一真相源
```

`VirtualListCore` 可选的第三参 `IVirtualListLogger` 用于接收核心日志（不传则静默降级）。

### 2. 实现视图与提供者

`IItemView` 为元素视图契约；`IItemProvider` 负责获取 / 归还视图（生产环境通常包装对象池）。

```csharp
public sealed class MyItemView : IItemView
{
    public void Bind(object item, int index) { /* 全量填充数据 */ }
    public float Measure(VirtualListAxis axis) => 100f; // 变高模式自算主轴尺寸
    public void SetPlacement(ItemPlacement placement) { /* 落地为位置 + 尺寸 */ }
    public void OnShow() { }
    public void OnHide() { }
    public void OnUnbind() { }
}

public sealed class MyItemProvider : IItemProvider
{
    private readonly Stack<MyItemView> _pool = new();

    public UniTask<IItemView> GetAsync(CancellationToken ct) =>
        UniTask.FromResult<IItemView>(_pool.Count > 0 ? _pool.Pop() : new MyItemView());

    public void Return(IItemView view) => _pool.Push((MyItemView)view);
}
```

### 3. 由宿主驱动

核心不自行感知输入与帧，由宿主壳层转发：

```csharp
list.SetViewportSize(viewportMain, viewportCross);   // 主轴 + 交叉轴视口尺寸
list.Step(Time.deltaTime);                           // 每帧推进惯性 / 回弹
list.HandlePointerDown(mainPos);                     // 指针事件（主轴坐标）
list.HandlePointerDrag(mainPos);
list.HandlePointerUp(mainPos);
```

## 滚动定位

| API | 说明 |
| --- | --- |
| `ScrollToIndex(index, alignment)` | 滚动到索引，对齐 `Start` / `Center` / `End` |
| `ScrollToOffset(offset)` | 滚动到指定偏移（越界夹紧） |
| `ScrollToStart()` / `ScrollToEnd()` | 逻辑起点 / 终点（第一个 / 最后一个元素） |
| `ScrollToTop()` / `ScrollToBottom()` | 物理主轴起点 / 终点：垂直=顶 / 底，水平=左 / 右 |
| `ScrollToLeft()` / `ScrollToRight()` | 物理主轴起点 / 终点：水平=左 / 右，垂直=顶 / 底 |

## 事件流（R3）

| 事件 | 类型 | 说明 |
| --- | --- | --- |
| `VisibleWindowChanged` | `Observable<VisibleWindow>` | 首 / 尾可见 index 变化时触发 |
| `ReachedTop` / `ReachedBottom` | `Observable<Unit>` | 跨越顶部 / 底部边界时刻各触发一次 |

```csharp
list.VisibleWindowChanged.Subscribe(w => Debug.Log($"可见区间 {w.First}..{w.Last}"));
```

## 元素生命周期

调用顺序固定：

```
获取：Bind → Measure(仅变高) → SetPlacement → OnShow
释放：OnHide → OnUnbind → Return
```

- `OnShow` / `OnHide`：元素矩形与视口矩形相交 / 完全离开时各触发一次
- 重绑必须覆盖全部状态，不得依赖上次绑定的增量残留

## 运行时动态配置

```csharp
list.ApplyConfig(newConfig);              // 几乎全部配置均可动态改变
list.SetAxis(VirtualListAxis.Horizontal);
list.SetReversed(true);
list.SetSizeMode(VirtualListItemSizeMode.Variable);
list.SetFixedItemSize(120f);
list.SetSpacing(12f);
list.SetOverscan(4);
list.SetOverflow(VirtualListOverflow.Clamped);
list.SetGrid(new VirtualListGridConfig { Enabled = true, CrossCount = 3, CrossSpacing = 8f });
list.SetInitialAnchor(VirtualListInitialAnchor.OffsetMax);
list.SetDefaultItemSize(80f);             // 变高模式未测量元素的估计尺寸
list.NotifyItemSizeChanged(index);        // 元素尺寸变化通知 → 重新测量
```

> 切换主轴 / 尺寸模式较昂贵（变高模式下会全量失效并重测活跃元素）；元素视图（`Provider`）与 `MaskViewport` 裁剪挂载不可运行时改变。

## Grid 配置

Grid 仅支持定高模式（等高 cell）。交叉轴列数可固定或自动推断：

```csharp
var grid = new VirtualListGridConfig
{
    Enabled = true,
    CrossCount = 0,      // 0 表示 AutoFit
    AutoFit = true,      // 按视口交叉轴尺寸自动推断列数
    CrossSize = 300f,    // AutoFit 参考的单元格交叉轴尺寸
    CrossSpacing = 8f,
};
```

## 诊断

```csharp
DebugSnapshot s = list.Snapshot();
// s.FirstVisibleIndex / s.LastVisibleIndex         纯视口可见区间
// s.FirstInstantiatedIndex / s.LastInstantiatedIndex 含 overscan 的实例化区间
// s.Offset / s.ContentSize / s.MaxScrollOffset / s.InstantiatedCount
```

## 运行测试

```bash
dotnet test Fenestra.slnx
```

测试覆盖：窗口计算、定高 / 变高、Grid、反向排列、集合变更、滚动物理、生命周期、动态配置。

## 项目结构

```
Fenestra/
├── Fenestra/                  # 核心库（纯 C#）
│   ├── VirtualListCore.cs     # 编排器（唯一使用与测试入口）
│   ├── Abstraction/           # IItemView / IItemProvider / IVirtualListLogger
│   ├── Entity/                # 配置、枚举、快照等值对象
│   └── Core/                  # 布局与滚动物理（内部实现）
└── Fenestra.Tests/            # NUnit 测试
```

## 设计约定

- 集合为唯一真相源，列表自身不提供增删改 API
- 核心可 headless 运行，不依赖任何引擎类型
- 唯一对外入口为 `VirtualListCore`，其余实现均为内部细节

## 许可证

MIT
