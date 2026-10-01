using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;

namespace CncSim.Core.Simulation
{
    public enum SimulationState
    {
        Idle,
        Running,
        Paused,
        Stopped,
        Finished
    }

    /// <summary>仿真每一帧的状态快照，供 UI 与渲染层读取。</summary>
    public struct SimulationFrame
    {
        public bool Valid;
        /// <summary>当前运动段索引。</summary>
        public int SegmentIndex;
        /// <summary>来源程序行。</summary>
        public int LineIndex;
        /// <summary>段内参数 0-1。</summary>
        public double SegmentT;
        /// <summary>刀尖位置（机床坐标）。</summary>
        public Vec3d Position;
        public Vec3d ToolDirection;
        public AxisVector Axes;
        /// <summary>累计时间（秒）。</summary>
        public double ElapsedTime;
        /// <summary>进度 0-1。</summary>
        public double Progress;
        public double Feed;
        public double SpindleSpeed;
        public int ToolNumber;
        public bool SpindleOn;
        public bool CoolantOn;
        /// <summary>该段是否在切削。</summary>
        public bool Cutting;
    }

    /// <summary>
    /// 仿真控制器（功能 45-51）：连续/单步执行、暂停/继续/停止、速度调节、进度、从指定行开始。
    /// 与渲染解耦：调用方每帧调用 Advance(deltaTime)，读取 Current 帧并据此更新场景。
    /// 材料去除由外部订阅 FrameAdvanced 事件或调用 ApplyCutting 完成。
    /// </summary>
    public class SimulationController
    {
        private ParseResult _program;
        private MachineProfile _machine;
        private Kinematics.MachineKinematics _kinematics;
        private double[] _segmentStartTimes;
        private double _clock;
        private int _segmentIndex;
        private SimulationState _state = SimulationState.Idle;

        /// <summary>仿真速度倍率（1 = 真实时间）。</summary>
        public double SpeedMultiplier = 1.0;
        /// <summary>是否跳过空行程（快移加速）。</summary>
        public bool FastForwardRapids;
        /// <summary>目标帧率无关的最小时间步，防止大 delta 跳段。</summary>
        public double MaxTimeStep = 0.05;

        /// <summary>断点行集合（0 基行号）。运行到这些行首时自动暂停。</summary>
        public readonly HashSet<int> Breakpoints = new HashSet<int>();
        /// <summary>是否启用断点。</summary>
        public bool BreakpointsEnabled = true;
        /// <summary>命中断点时触发（参数为行号）。</summary>
        public event Action<int> BreakpointHit;

        public SimulationState State => _state;
        public SimulationFrame Current { get; private set; }
        public double TotalTime { get; private set; }
        public ParseResult Program => _program;

        /// <summary>每帧推进后触发。参数为当前帧。</summary>
        public event Action<SimulationFrame> FrameAdvanced;
        /// <summary>进入新运动段时触发（段起点）。</summary>
        public event Action<MotionSegment> SegmentEntered;
        /// <summary>状态变化。</summary>
        public event Action<SimulationState> StateChanged;
        /// <summary>仿真结束。</summary>
        public event Action Finished;

        public void Load(ParseResult program, MachineProfile machine)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            _machine = machine ?? MachineProfile.Create3Axis();
            _kinematics = new Kinematics.MachineKinematics(_machine);
            _segmentStartTimes = new double[_program.Segments.Count + 1];
            double t = 0;
            for (int i = 0; i < _program.Segments.Count; i++)
            {
                _program.Segments[i].StartTime = t;
                _segmentStartTimes[i] = t;
                if (_program.Segments[i].Duration <= 0)
                    _program.Segments[i].Duration = TimeEstimator.SegmentDuration(_program.Segments[i], _machine);
                t += _program.Segments[i].Duration;
            }
            _segmentStartTimes[_program.Segments.Count] = t;
            TotalTime = t;
            SeekTime(0);
            SetState(SimulationState.Idle);
        }

        public void Play()
        {
            if (_program == null || _program.Segments.Count == 0) return;
            if (_state == SimulationState.Finished) SeekTime(0);
            SetState(SimulationState.Running);
        }

        public void Pause()
        {
            if (_state == SimulationState.Running) SetState(SimulationState.Paused);
        }

        public void TogglePlayPause()
        {
            if (_state == SimulationState.Running) Pause();
            else Play();
        }

        public void Stop()
        {
            SeekTime(0);
            SetState(SimulationState.Stopped);
        }

        /// <summary>从指定程序行开始仿真（功能 50）。会重放到该行的模态状态。</summary>
        public void StartFromLine(int line)
        {
            if (_program == null) return;
            int seg = _program.FirstSegmentAtOrAfterLine(line);
            if (seg < 0) seg = _program.Segments.Count;
            SeekSegment(seg, 0);
            SetState(SimulationState.Running);
        }

        /// <summary>定位到指定时刻。</summary>
        public void SeekTime(double time)
        {
            if (_program == null) return;
            time = Math.Max(0, Math.Min(time, TotalTime));
            int idx = FindSegmentAtTime(time);
            double segStart = idx < _segmentStartTimes.Length ? _segmentStartTimes[idx] : TotalTime;
            double dur = idx < _program.Segments.Count ? Math.Max(1e-9, _program.Segments[idx].Duration) : 1;
            double t = idx < _program.Segments.Count ? MathUtil.Clamp01((time - segStart) / dur) : 1;
            SeekSegment(idx, t);
        }

        public void SeekProgress(double progress)
        {
            SeekTime(MathUtil.Clamp01(progress) * TotalTime);
        }

        private void SeekSegment(int index, double t)
        {
            _segmentIndex = Math.Max(0, Math.Min(index, Math.Max(0, _program.Segments.Count - 1)));
            if (_program.Segments.Count == 0)
            {
                _clock = 0;
                Current = default;
                return;
            }
            _clock = _segmentStartTimes[_segmentIndex] + t * _program.Segments[_segmentIndex].Duration;
            UpdateFrame();
        }

        /// <summary>单步：前进一个运动段（功能 46）。</summary>
        public void StepForward()
        {
            if (_program == null) return;
            if (_state == SimulationState.Running) Pause();
            int next = _segmentIndex;
            double dur = _program.Segments.Count > 0 ? Math.Max(1e-9, _program.Segments[_segmentIndex].Duration) : 1;
            double segStart = _segmentStartTimes[_segmentIndex];
            double localT = (double)((_clock - segStart) / dur);
            if (localT < 0.999)
                SeekSegment(_segmentIndex, 1);
            else
            {
                next = Math.Min(_segmentIndex + 1, _program.Segments.Count - 1);
                SeekSegment(next, 0);
            }
            SetState(SimulationState.Paused);
        }

        public void StepBackward()
        {
            if (_program == null) return;
            if (_state == SimulationState.Running) Pause();
            double dur = _program.Segments.Count > 0 ? Math.Max(1e-9, _program.Segments[_segmentIndex].Duration) : 1;
            double localT = (_clock - _segmentStartTimes[_segmentIndex]) / dur;
            if (localT > 0.001)
                SeekSegment(_segmentIndex, 0);
            else
            {
                int prev = Math.Max(0, _segmentIndex - 1);
                SeekSegment(prev, 1);
            }
            SetState(SimulationState.Paused);
        }

        /// <summary>按帧推进仿真（功能 45、48）。返回是否产生了新的切削。</summary>
        public void Advance(double deltaTime)
        {
            if (_state != SimulationState.Running || _program == null || _program.Segments.Count == 0) return;
            double dt = Math.Min(deltaTime, MaxTimeStep) * SpeedMultiplier;
            double remaining = dt;
            int guard = 0;
            while (remaining > 1e-9 && guard++ < 10000)
            {
                var seg = _program.Segments[_segmentIndex];
                double dur = Math.Max(1e-9, seg.Duration);
                if (FastForwardRapids && seg.Type == MotionType.Rapid)
                    dur = Math.Min(dur, 0.02);
                double segStart = _segmentStartTimes[_segmentIndex];
                double localT = MathUtil.Clamp01((_clock - segStart) / dur);
                double timeToEnd = (1 - localT) * dur;
                if (remaining < timeToEnd)
                {
                    _clock += remaining;
                    remaining = 0;
                }
                else
                {
                    remaining -= timeToEnd;
                    _clock = segStart + dur;
                    if (_segmentIndex >= _program.Segments.Count - 1)
                    {
                        _clock = TotalTime;
                        UpdateFrame();
                        SetState(SimulationState.Finished);
                        Finished?.Invoke();
                        return;
                    }
                    _segmentIndex++;
                    var nextSeg = _program.Segments[_segmentIndex];
                    SegmentEntered?.Invoke(nextSeg);
                    _clock = _segmentStartTimes[_segmentIndex];
                    if (BreakpointsEnabled && Breakpoints.Contains(nextSeg.LineIndex))
                    {
                        UpdateFrame();
                        SetState(SimulationState.Paused);
                        BreakpointHit?.Invoke(nextSeg.LineIndex);
                        return;
                    }
                }
            }
            UpdateFrame();
        }

        private void UpdateFrame()
        {
            if (_program == null || _program.Segments.Count == 0)
            {
                Current = default;
                return;
            }
            var seg = _program.Segments[_segmentIndex];
            double dur = Math.Max(1e-9, seg.Duration);
            double t = MathUtil.Clamp01((_clock - _segmentStartTimes[_segmentIndex]) / dur);
            AxisVector axes = seg.Evaluate(t);
            Vec3d tip = axes.Linear;
            Vec3d dir = Vec3d.UnitZ;
            if (_machine.Kinematics != FiveAxisKinematics.None)
                _kinematics.Forward(axes, out tip, out dir);

            // 模态状态：取该段所属行的解析状态
            var state = _program.Blocks.Count > seg.LineIndex ? _program.Blocks[seg.LineIndex].StateAfter : null;

            Current = new SimulationFrame
            {
                Valid = true,
                SegmentIndex = _segmentIndex,
                LineIndex = seg.LineIndex,
                SegmentT = t,
                Position = seg.Evaluate(t).Linear,
                ToolDirection = dir,
                Axes = axes,
                ElapsedTime = _clock,
                Progress = TotalTime > 0 ? _clock / TotalTime : 0,
                Feed = seg.Feed,
                SpindleSpeed = seg.SpindleSpeed,
                ToolNumber = seg.ToolNumber,
                SpindleOn = seg.Spindle != SpindleState.Off,
                CoolantOn = seg.Coolant,
                Cutting = seg.IsCutting
            };
            FrameAdvanced?.Invoke(Current);
        }

        private int FindSegmentAtTime(double time)
        {
            int lo = 0, hi = _program.Segments.Count - 1, result = _program.Segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_segmentStartTimes[mid] <= time)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            return result;
        }

        private void SetState(SimulationState state)
        {
            if (_state == state) return;
            _state = state;
            StateChanged?.Invoke(state);
        }
    }
}
