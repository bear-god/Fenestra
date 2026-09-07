namespace Fenestra.Abstraction;

using Fenestra.Entity;

/// <summary>
/// 元素视图契约。由编排器统一驱动，调用顺序固定（见设计 §6.1）：
/// 获取：Bind → Measure(变高) → SetPlacement → OnShow；释放：OnHide → OnUnbind → Return。
/// 订阅清理责任在 OnUnbind（每次回收必调）；宿主组件 OnDestroy 兜底。
/// 本接口为纯 C# 契约，不含任何引擎类型；Unity 侧由预制件组件实现。
/// </summary>
public interface IItemView
{
    /// <summary>
    /// 全量绑定：把集合元素数据一次性设全。
    /// 重绑必须覆盖全部状态，不得依赖上次绑定的增量残留。
    /// </summary>
    /// <param name="item">集合元素数据。</param>
    /// <param name="index">当前索引。</param>
    void Bind(object item, int index);

    /// <summary>
    /// 同步测量主轴尺寸（变高模式）。
    /// 必须确定、同步、无 UGUI 布局重建依赖（自算尺寸，如文本高度）。
    /// 传入滚动主轴：纵向返回高度、横向返回宽度（轴向可在运行时经 <c>ApplyConfig</c> 改变）。
    /// </summary>
    /// <param name="axis">当前滚动主轴。</param>
    /// <returns>主轴尺寸。</returns>
    float Measure(VirtualListAxis axis);

    /// <summary>以纯数值摆放元素（位置 + 尺寸）。Unity 端实现为设置 RectTransform。</summary>
    /// <param name="placement">摆放参数。</param>
    void SetPlacement(ItemPlacement placement);

    /// <summary>进入显示状态：元素矩形与视口矩形相交（进入视口）时触发一次。</summary>
    void OnShow();

    /// <summary>完全离开视口时触发一次。</summary>
    void OnHide();

    /// <summary>解绑：释放 R3 订阅、重置状态。归还前由编排器调用。</summary>
    void OnUnbind();
}
