using System;
using VirtualList;

namespace VirtualList.Core;

    /// <summary>
    /// 滚动物理：手势状态机、速度估算、惯性衰减、Clamped/Elastic 越界。
    /// 纯数学，固定 dt 步进结果确定。实现细节（D14）：仅由编排器使用，不对外测试。
    /// 速度估算按「拖拽事件近似逐帧（60fps）」假设，保证确定性；后续可改为时间戳注入。
    /// </summary>
    internal sealed class ScrollPhysics
    {
        private const float Friction = 6f;
        private const float SpringStiffness = 140f;
        // 近临界阻尼（含全局 Friction 后阻尼比略 >1）：弹性回弹逼近边界单调收敛，不越过边界漂移（设计 §2.1 弹性回弹）。
        private const float SpringDamping = 18f;
        private const float VelocityThreshold = 8f;
        private const float ElasticDragResistance = 0.35f;
        private const float AssumedFrameRate = 60f;

        private VirtualListOverflow _overflow;
        private float _min;
        private float _max;
        private float _position;
        private float _velocity;
        private bool _dragging;
        private bool _scrolling;
        private float _dragStartPosition;
        private float _dragStartOffset;
        private float _previousDragPosition;
        private float _lastDragPosition;
        private bool _hasTwoDragSamples;

        /// <summary>初始化滚动物理。</summary>
        /// <param name="overflow">越界行为。</param>
        public ScrollPhysics(VirtualListOverflow overflow)
        {
            _overflow = overflow;
        }

        /// <summary>是否正在拖拽。</summary>
        public bool IsDragging => _dragging;

        /// <summary>是否惯性/回弹进行中。</summary>
        public bool IsScrolling => _scrolling;

        /// <summary>当前主轴位置（弹性越界时可能超出 [min, max]）。</summary>
        public float Position => _position;

        /// <summary>运行时更新越界行为（经编排器 ApplyConfig 转发）。</summary>
        /// <param name="overflow">新越界行为。</param>
        public void SetOverflow(VirtualListOverflow overflow)
        {
            _overflow = overflow;
        }

        /// <summary>设置滚动边界（每次布局变化后由编排器更新）。</summary>
        /// <param name="min">最小偏移。</param>
        /// <param name="max">最大偏移。</param>
        public void SetBounds(float min, float max)
        {
            _min = min;
            _max = Math.Max(min, max);
        }

        /// <summary>直接置位（程序化滚动用）。</summary>
        /// <param name="position">目标位置。</param>
        public void SetPosition(float position)
        {
            _position = position;
        }

        /// <summary>按下。</summary>
        /// <param name="mainPos">主轴坐标。</param>
        public void HandlePointerDown(float mainPos)
        {
            _dragging = true;
            _scrolling = false;
            _velocity = 0f;
            _dragStartPosition = mainPos;
            _dragStartOffset = _position;
            _previousDragPosition = mainPos;
            _lastDragPosition = mainPos;
            _hasTwoDragSamples = false;
        }

        /// <summary>拖动。偏移 = 按下时偏移 + (按下位置 - 当前位置)（手指上移内容下滚）。</summary>
        /// <param name="mainPos">主轴坐标。</param>
        public void HandlePointerDrag(float mainPos)
        {
            if (!_dragging)
            {
                return;
            }

            _previousDragPosition = _lastDragPosition;
            _lastDragPosition = mainPos;
            _hasTwoDragSamples = true;

            var raw = _dragStartOffset + (_dragStartPosition - mainPos);
            _position = ApplyDragBounds(raw);
        }

        /// <summary>松手：估算速度进入惯性；越界时进入回弹。</summary>
        /// <param name="mainPos">主轴坐标。</param>
        public void HandlePointerUp(float mainPos)
        {
            if (!_dragging)
            {
                return;
            }

            HandlePointerDrag(mainPos);
            _dragging = false;
            _velocity = EstimateVelocity();
            _scrolling = Math.Abs(_velocity) >= VelocityThreshold || IsOutOfBounds();
        }

        /// <summary>打断惯性/回弹（外部干预时）。</summary>
        public void Cancel()
        {
            _dragging = false;
            _scrolling = false;
            _velocity = 0f;
        }

        /// <summary>推进一帧，返回本帧目标位置。Clamped 硬夹；Elastic 允许越界并回弹。</summary>
        /// <param name="dt">帧时长。</param>
        /// <param name="min">最小偏移。</param>
        /// <param name="max">最大偏移。</param>
        /// <returns>目标位置。</returns>
        public float Step(float dt, float min, float max)
        {
            SetBounds(min, max);
            if (_dragging || !_scrolling)
            {
                return _position;
            }

            var decay = (float)Math.Exp(-Friction * dt);
            _velocity *= decay;
            _position += _velocity * dt;

            if (_position < _min || _position > _max)
            {
                if (_overflow == VirtualListOverflow.Clamped)
                {
                    _position = Math.Max(_min, Math.Min(_max, _position));
                    _velocity = 0f;
                    _scrolling = false;
                }
                else
                {
                    var boundary = _position < _min ? _min : _max;
                    _velocity += (boundary - _position) * SpringStiffness * dt;
                    _velocity *= (float)Math.Exp(-SpringDamping * dt);
                }
            }

            if (Math.Abs(_velocity) < VelocityThreshold && !IsOutOfBounds())
            {
                _velocity = 0f;
                _scrolling = false;
                _position = Math.Max(_min, Math.Min(_max, _position));
            }

            return _position;
        }

        private float ApplyDragBounds(float raw)
        {
            if (_overflow == VirtualListOverflow.Clamped)
            {
                return Math.Max(_min, Math.Min(_max, raw));
            }

            if (raw < _min)
            {
                return _min + ((raw - _min) * ElasticDragResistance);
            }

            if (raw > _max)
            {
                return _max + ((raw - _max) * ElasticDragResistance);
            }

            return raw;
        }

        private float EstimateVelocity()
        {
            if (!_hasTwoDragSamples)
            {
                return 0f;
            }

            return (_previousDragPosition - _lastDragPosition) * AssumedFrameRate;
        }

        private bool IsOutOfBounds()
        {
            return _position < _min || _position > _max;
        }
    }