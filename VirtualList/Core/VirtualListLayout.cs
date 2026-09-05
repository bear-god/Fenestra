using System;
using VirtualList;

namespace VirtualList.Core;

    /// <summary>
    /// 布局核心：区间、布局（主轴偏移/交叉轴列/尺寸）、ScrollToIndex、集合变更 remap、窗口 diff。
    /// 纯数学，不引用引擎类型。实现细节（D14）：仅由编排器使用，不对外测试。
    /// </summary>
    internal sealed class VirtualListLayout
    {
        private const float MinSize = 1f;

        private VirtualListConfig _config;
        private float[] _measuredSizes = Array.Empty<float>();
        private float _estimateSize;
        private bool _estimateSeeded;
        private float _viewportMain;
        private float _viewportCross;
        private int _itemCount;
        private float _offset;
        private int _lastFirstInstantiated = -1;
        private int _lastLastInstantiated = -1;
        private int[] _enteringBuffer = new int[16];
        private int[] _leavingBuffer = new int[16];

        /// <summary>初始化布局核心。非法配置（Grid + 变高）抛异常。</summary>
        /// <param name="config">列表配置。</param>
        public VirtualListLayout(VirtualListConfig config)
        {
            Validate(config);
            _config = config;
        }

        /// <summary>首可见 index（纯视口，不含 overscan）；无内容时为 -1。</summary>
        public int FirstVisibleIndex
        {
            get
            {
                ComputeVisibleRows(out var first, out _);
                return first < 0 ? -1 : first * ColumnCount;
            }
        }

        /// <summary>尾可见 index（纯视口，不含 overscan）；无内容时为 -1。</summary>
        public int LastVisibleIndex
        {
            get
            {
                ComputeVisibleRows(out _, out var last);
                return last < 0 ? -1 : Math.Min(_itemCount - 1, ((last + 1) * ColumnCount) - 1);
            }
        }

        /// <summary>首实例化 index（视口 + overscan）；无内容时为 -1。</summary>
        public int FirstInstantiatedIndex
        {
            get
            {
                ComputeInstantiatedRange(out var first, out _);
                return first;
            }
        }

        /// <summary>尾实例化 index（视口 + overscan）；无内容时为 -1。</summary>
        public int LastInstantiatedIndex
        {
            get
            {
                ComputeInstantiatedRange(out _, out var last);
                return last;
            }
        }

        /// <summary>主轴总内容尺寸。</summary>
        public float ContentSize
        {
            get
            {
                var rows = RowCount;
                if (rows == 0)
                {
                    return 0f;
                }

                float total = 0f;
                for (var r = 0; r < rows; r++)
                {
                    total += RowItemSize(r);
                }

                return total + (_config.Spacing * (rows - 1));
            }
        }

        /// <summary>最大可滚动偏移（不小于 0）。</summary>
        public float MaxScrollOffset => Math.Max(0f, ContentSize - _viewportMain);

        /// <summary>主轴偏移（已夹紧）。</summary>
        public float Offset => _offset;

        /// <summary>Grid 交叉轴元素数（非 Grid 为 1）。</summary>
        public int ColumnCount
        {
            get
            {
                if (!_config.Grid.Enabled)
                {
                    return 1;
                }

                if (_config.Grid.AutoFit)
                {
                    var cell = Math.Max(MinSize, _config.Grid.CrossSize);
                    var spacing = _config.Grid.CrossSpacing;
                    var cols = (int)((_viewportCross + spacing) / (cell + spacing));
                    return Math.Max(1, cols);
                }

                return Math.Max(1, _config.Grid.CrossCount);
            }
        }

        private float EstimateSize
        {
            get
            {
                if (_estimateSeeded)
                {
                    return _estimateSize;
                }

                // 未注入估计值时用 1px 临时值，保证窗口计算不退化；首个测量后以测量值兜底。
                return Math.Max(MinSize, _config.FixedItemSize);
            }
        }

        private float CellCrossSize
        {
            get
            {
                var cols = ColumnCount;
                if (cols <= 1)
                {
                    return _viewportCross;
                }

                return (_viewportCross - ((cols - 1) * _config.Grid.CrossSpacing)) / cols;
            }
        }

        private int RowCount
        {
            get
            {
                var cols = ColumnCount;
                return (_itemCount + cols - 1) / cols;
            }
        }

        /// <summary>运行时应用新配置（Grid + 变高非法抛异常）。测量语义变化（轴向 / 尺寸模式）由编排器负责失效重测。</summary>
        /// <param name="config">新列表配置。</param>
        public void ApplyConfig(VirtualListConfig config)
        {
            Validate(config);
            _config = config;
        }

        /// <summary>初始滚动偏移（初始锚点：偏移最小值 OffsetMin / 偏移最大值 OffsetMax；与反向排列正交）。</summary>
        /// <returns>初始滚动偏移。</returns>
        public float GetInitialOffset()
        {
            return _config.InitialAnchor == VirtualListInitialAnchor.OffsetMax ? MaxScrollOffset : 0f;
        }

        /// <summary>设置视口尺寸（主轴 + 交叉轴）。</summary>
        /// <param name="mainSize">主轴视口尺寸。</param>
        /// <param name="crossSize">交叉轴视口尺寸。</param>
        public void SetViewportSize(float mainSize, float crossSize)
        {
            _viewportMain = Math.Max(0f, mainSize);
            _viewportCross = Math.Max(0f, crossSize);
        }

        /// <summary>设置集合计数。</summary>
        /// <param name="count">元素总数。</param>
        public void SetItemCount(int count)
        {
            _itemCount = Math.Max(0, count);
            EnsureMeasuredCapacity(_itemCount);
        }

        /// <summary>设置变高模式未测量元素的估计尺寸（见设计 D12）。</summary>
        /// <param name="size">估计尺寸。</param>
        public void SetDefaultItemSize(float size)
        {
            _estimateSize = Math.Max(MinSize, size);
            _estimateSeeded = true;
        }

        /// <summary>重置窗口缓存（Unbind 后下次 diff 从空窗口重建，保证重新绑定窗口正确；未预热路径亦用于回退重试）。</summary>
        public void ResetWindowCache()
        {
            _lastFirstInstantiated = -1;
            _lastLastInstantiated = -1;
        }

        /// <summary>上报变高测量结果（幂等；未注入估计值时首个测量值兜底为估计值）。</summary>
        /// <param name="index">元素索引。</param>
        /// <param name="size">测量尺寸。</param>
        public void SetMeasuredSize(int index, float size)
        {
            if (index < 0 || index >= _itemCount)
            {
                return;
            }

            EnsureMeasuredCapacity(_itemCount);
            _measuredSizes[index] = Math.Max(MinSize, size);
            if (!_estimateSeeded)
            {
                _estimateSize = _measuredSizes[index];
                _estimateSeeded = true;
            }
        }

        /// <summary>使测量失效（下次布局按估计值参与累计）。</summary>
        /// <param name="index">元素索引。</param>
        public void InvalidateMeasuredSize(int index)
        {
            if (index >= 0 && index < _measuredSizes.Length)
            {
                _measuredSizes[index] = 0f;
            }
        }

        /// <summary>全量失效测量（轴向 / 尺寸模式变化导致测量语义改变时，由编排器调用后重测活跃元素）。</summary>
        public void InvalidateAllMeasuredSizes()
        {
            for (var i = 0; i < _measuredSizes.Length; i++)
            {
                _measuredSizes[i] = 0f;
            }
        }

        /// <summary>滚动到指定偏移（夹紧到 [0, MaxScrollOffset]）。</summary>
        /// <param name="offset">目标偏移。</param>
        public void ScrollTo(float offset)
        {
            _offset = Math.Max(0f, Math.Min(offset, MaxScrollOffset));
        }

        /// <summary>一次性滚动定位（对齐 Start/Center/End，越界夹紧）。反向排列时按目标元素的主轴末端起点换算物理偏移。</summary>
        /// <param name="index">目标索引。</param>
        /// <param name="alignment">对齐方式。</param>
        public void ScrollToIndex(int index, ScrollAlignment alignment)
        {
            if (index < 0 || index >= _itemCount || _viewportMain <= 0f)
            {
                return;
            }

            var row = RowOf(index);
            var target = RowMainOffset(row);
            var itemMain = RowItemSize(row);
            if (_config.Reversed)
            {
                target = ContentSize - target - itemMain;
            }

            switch (alignment)
            {
                case ScrollAlignment.Center:
                    target -= (_viewportMain - itemMain) * 0.5f;
                    break;
                case ScrollAlignment.End:
                    target -= _viewportMain - itemMain;
                    break;
            }

            ScrollTo(target);
        }

        /// <summary>Grid：index → 列。</summary>
        /// <param name="index">元素索引。</param>
        /// <returns>列号。</returns>
        public int ColumnOf(int index)
        {
            return RowOf(index) >= 0 ? index % ColumnCount : -1;
        }

        /// <summary>元素摆放参数（主轴偏移/交叉轴列/尺寸）。反向排列时主轴偏移换算为自主轴起点（末端）的物理偏移。</summary>
        /// <param name="index">元素索引。</param>
        /// <returns>摆放参数。</returns>
        public ItemPlacement GetItemPlacement(int index)
        {
            var cols = ColumnCount;
            var row = index / cols;
            var col = index % cols;
            var mainOffset = RowMainOffset(row);
            var mainSize = RowItemSize(row);
            if (_config.Reversed)
            {
                mainOffset = ContentSize - mainOffset - mainSize;
            }

            if (!_config.Grid.Enabled)
            {
                return new ItemPlacement(_config.Axis, mainOffset, 0f, mainSize, _viewportCross);
            }

            var crossSize = CellCrossSize;
            var crossOffset = col * (crossSize + _config.Grid.CrossSpacing);
            return new ItemPlacement(_config.Axis, mainOffset, crossOffset, mainSize, crossSize);
        }

        /// <summary>自上次调用以来进入/离开实例化窗口的索引（增量更新，见设计 D9）。</summary>
        /// <returns>窗口 diff。</returns>
        public WindowDiff TakeDiff()
        {
            ComputeInstantiatedRange(out var first, out var last);
            var oldFirst = _lastFirstInstantiated;
            var oldLast = _lastLastInstantiated;
            _lastFirstInstantiated = first;
            _lastLastInstantiated = last;

            var enteringCount = 0;
            var leavingCount = 0;
            if (first < 0 || last < 0)
            {
                // 新窗口为空：旧窗口全部离开。
                if (oldFirst >= 0)
                {
                    for (var i = oldFirst; i <= oldLast; i++)
                    {
                        PushLeaving(i, ref leavingCount);
                    }
                }
            }
            else if (oldFirst < 0)
            {
                // 旧窗口为空：新窗口全部进入。
                for (var i = first; i <= last; i++)
                {
                    PushEntering(i, ref enteringCount);
                }
            }
            else
            {
                for (var i = first; i < oldFirst; i++)
                {
                    PushEntering(i, ref enteringCount);
                }

                for (var i = oldLast + 1; i <= last; i++)
                {
                    PushEntering(i, ref enteringCount);
                }

                for (var i = oldFirst; i < first; i++)
                {
                    PushLeaving(i, ref leavingCount);
                }

                for (var i = last + 1; i <= oldLast; i++)
                {
                    PushLeaving(i, ref leavingCount);
                }
            }

            return new WindowDiff
            {
                Entering = _enteringBuffer,
                EnteringCount = enteringCount,
                Leaving = _leavingBuffer,
                LeavingCount = leavingCount,
            };
        }

        /// <summary>诊断快照（internal，由编排器公开 Snapshot 转发）。</summary>
        /// <returns>快照。</returns>
        public DebugSnapshot Snapshot()
        {
            return new DebugSnapshot
            {
                FirstVisibleIndex = FirstVisibleIndex,
                LastVisibleIndex = LastVisibleIndex,
                FirstInstantiatedIndex = FirstInstantiatedIndex,
                LastInstantiatedIndex = LastInstantiatedIndex,
                Offset = _offset,
                ContentSize = ContentSize,
                MaxScrollOffset = MaxScrollOffset,
                InstantiatedCount = 0,
            };
        }

        private static void Validate(VirtualListConfig config)
        {
            if (config.Grid.Enabled && config.SizeMode == VirtualListItemSizeMode.Variable)
            {
                throw new InvalidOperationException("Grid 仅支持定高模式（等高 cell）；变高请使用非 Grid 列表");
            }
        }

        private float RowItemSize(int row)
        {
            var index = row * ColumnCount;
            if (index < 0 || index >= _itemCount)
            {
                return 0f;
            }

            if (_config.SizeMode == VirtualListItemSizeMode.Fixed)
            {
                return _config.FixedItemSize;
            }

            var measured = _measuredSizes[index];
            return measured > 0f ? measured : EstimateSize;
        }

        private float RowMainOffset(int row)
        {
            if (row <= 0)
            {
                return 0f;
            }

            float offset = 0f;
            for (var r = 0; r < row; r++)
            {
                offset += RowItemSize(r) + _config.Spacing;
            }

            return offset;
        }

        private int RowOf(int index)
        {
            return index < 0 ? -1 : index / ColumnCount;
        }

        private void EnsureMeasuredCapacity(int count)
        {
            if (_measuredSizes.Length >= count)
            {
                return;
            }

            var next = Math.Max(count, Math.Max(16, _measuredSizes.Length * 2));
            Array.Resize(ref _measuredSizes, next);
        }

        private void ComputeVisibleRows(out int firstRow, out int lastRow)
        {
            firstRow = -1;
            lastRow = -1;
            if (_itemCount <= 0 || _viewportMain <= 0f)
            {
                return;
            }

            var rows = RowCount;
            var clamped = Math.Max(0f, Math.Min(_offset, MaxScrollOffset));
            var end = clamped + _viewportMain;
            if (_config.Reversed)
            {
                // 反向排列：末行（逻辑末端）物理起点为 0，自末向首累加（O(rows) 单趟）。
                // 迭代自高行向低行：首命中的是窗口最高行（maxRow），随后持续下探更新窗口最低行（minRow）。
                var minRow = -1;
                var maxRow = -1;
                float cursor = 0f;
                for (var r = rows - 1; r >= 0; r--)
                {
                    var size = RowItemSize(r);
                    var start = cursor;
                    if (start + size > clamped && start < end)
                    {
                        if (maxRow < 0)
                        {
                            maxRow = r;
                        }

                        minRow = r;
                    }
                    else if (maxRow >= 0)
                    {
                        break;
                    }

                    cursor = start + size + _config.Spacing;
                }

                firstRow = minRow;
                lastRow = maxRow;
            }
            else
            {
                float cursor = 0f;
                for (var r = 0; r < rows; r++)
                {
                    var size = RowItemSize(r);
                    var start = cursor;
                    if (start + size > clamped && start < end)
                    {
                        if (firstRow < 0)
                        {
                            firstRow = r;
                        }

                        lastRow = r;
                    }
                    else if (lastRow >= 0)
                    {
                        break;
                    }

                    cursor = start + size + _config.Spacing;
                }
            }

            if (firstRow < 0 && rows > 0)
            {
                firstRow = Math.Max(0, rows - 1);
                lastRow = firstRow;
            }
        }

        private void ComputeInstantiatedRange(out int first, out int last)
        {
            first = -1;
            last = -1;
            ComputeVisibleRows(out var firstRow, out var lastRow);
            if (firstRow < 0)
            {
                return;
            }

            var rows = RowCount;
            var cols = ColumnCount;
            var overscan = Math.Max(0, _config.Overscan);
            var f = Math.Max(0, firstRow - overscan);
            var l = Math.Min(rows - 1, lastRow + overscan);
            first = f * cols;
            last = Math.Min(_itemCount - 1, ((l + 1) * cols) - 1);
        }

        private void PushEntering(int index, ref int count)
        {
            if (count >= _enteringBuffer.Length)
            {
                Array.Resize(ref _enteringBuffer, _enteringBuffer.Length * 2);
            }

            _enteringBuffer[count++] = index;
        }

        private void PushLeaving(int index, ref int count)
        {
            if (count >= _leavingBuffer.Length)
            {
                Array.Resize(ref _leavingBuffer, _leavingBuffer.Length * 2);
            }

            _leavingBuffer[count++] = index;
        }
    }