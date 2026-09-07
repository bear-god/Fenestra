namespace Fenestra.Unity;

using Fenestra.Abstraction;
using Fenestra.Entity;
using UnityEngine;

/// <summary>
/// <see cref="IItemView" /> 的默认抽象实现：下沉各元素视图共通的 Unity 落地逻辑，子类只需实现数据绑定。
/// - <see cref="SetPlacement" /> 已实现：锚定 Content 左上角 (0,1)、pivot(0,1)，按 <see cref="ItemPlacement.Axis" />
/// 分支落地（纵向 anchoredPosition=(CrossOffset,-MainOffset)、横向=(MainOffset,-CrossOffset)）；
/// - <see cref="Measure" /> 默认返回 0（变高模式需覆写）；
/// - <see cref="OnShow" /> / <see cref="OnHide" /> / <see cref="OnUnbind" /> 默认空实现，可按需覆写。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public abstract class ItemViewBase : MonoBehaviour, IItemView
{
    private RectTransform? _rect;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    /// <inheritdoc />
    public abstract void Bind(object item, int index);

    /// <inheritdoc />
    public virtual float Measure(VirtualListAxis axis)
    {
        return 0f;
    }

    /// <inheritdoc />
    public virtual void SetPlacement(ItemPlacement placement)
    {
        if (_rect == null)
        {
            return;
        }

        _rect.pivot = new Vector2(0f, 1f);
        _rect.anchorMin = new Vector2(0f, 1f);
        _rect.anchorMax = new Vector2(0f, 1f);
        if (placement.Axis == VirtualListAxis.Vertical)
        {
            _rect.sizeDelta = new Vector2(placement.CrossSize, placement.MainSize);
            _rect.anchoredPosition = new Vector2(placement.CrossOffset, -placement.MainOffset);
        }
        else
        {
            _rect.sizeDelta = new Vector2(placement.MainSize, placement.CrossSize);
            _rect.anchoredPosition = new Vector2(placement.MainOffset, -placement.CrossOffset);
        }
    }

    /// <inheritdoc />
    public virtual void OnShow()
    {
    }

    /// <inheritdoc />
    public virtual void OnHide()
    {
    }

    /// <inheritdoc />
    public virtual void OnUnbind()
    {
    }
}
