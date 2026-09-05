using System;
using System.Collections.Generic;
using VirtualList;

namespace VirtualList.Tests;

    /// <summary>桩视图：记录生命周期与摆放，可配置按索引高度（假测量）。</summary>
    internal sealed class StubItemView : IItemView
    {
        /// <summary>按索引提供测量高度的委托（优先于 MeasuredSize）。</summary>
        public Func<int, float> HeightProvider { get; set; }

        /// <summary>默认测量高度。</summary>
        public float MeasuredSize { get; set; } = 100f;

        /// <summary>Measure 调用次数。</summary>
        public int MeasureCount { get; set; }

        /// <summary>Bind 调用次数。</summary>
        public int BindCount { get; set; }

        /// <summary>SetPlacement 调用次数。</summary>
        public int PlacementCount { get; set; }

        /// <summary>OnShow 次数。</summary>
        public int ShowCount { get; set; }

        /// <summary>OnHide 次数。</summary>
        public int HideCount { get; set; }

        /// <summary>OnUnbind 次数。</summary>
        public int UnbindCount { get; set; }

        /// <summary>最近绑定的数据。</summary>
        public object BoundItem { get; set; }

        /// <summary>最近绑定的索引。</summary>
        public int BoundIndex { get; set; }

        /// <summary>最近一次 Measure 收到的主轴。</summary>
        public VirtualListAxis LastMeasureAxis { get; set; }

        /// <summary>最近一次摆放参数。</summary>
        public ItemPlacement LastPlacement { get; set; }

        /// <summary>当前是否处于显示状态。</summary>
        public bool IsShown { get; set; }

        /// <summary>生命周期方法名序列（Bind/Measure/Place/Show/Hide/Unbind）。</summary>
        public List<string> Log { get; } = new();

        /// <inheritdoc/>
        public void Bind(object item, int index)
        {
            BindCount++;
            BoundItem = item;
            BoundIndex = index;
            Log.Add("Bind");
        }

        /// <inheritdoc/>
        public float Measure(VirtualListAxis axis)
        {
            MeasureCount++;
            LastMeasureAxis = axis;
            Log.Add("Measure");
            return HeightProvider is null ? MeasuredSize : HeightProvider(BoundIndex);
        }

        /// <inheritdoc/>
        public void SetPlacement(ItemPlacement placement)
        {
            PlacementCount++;
            LastPlacement = placement;
            Log.Add("Place");
        }

        /// <inheritdoc/>
        public void OnShow()
        {
            IsShown = true;
            ShowCount++;
            Log.Add("Show");
        }

        /// <inheritdoc/>
        public void OnHide()
        {
            IsShown = false;
            HideCount++;
            Log.Add("Hide");
        }

        /// <inheritdoc/>
        public void OnUnbind()
        {
            UnbindCount++;
            Log.Add("Unbind");
        }
    }