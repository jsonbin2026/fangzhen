using System;
using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.Collision;
using CncSim.Core.Kinematics;
using CncSim.Core.Parsing;
using CncSim.Core.Simulation;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;
using CncSim.Core.Trajectory;

namespace CncSim.Runtime
{
    /// <summary>
    /// Unity 与 Core 的桥梁（功能 45-55 仿真控制、材料去除、切削动画）。
    /// 负责：加载程序、推进仿真、把刀尖位置/朝向应用到场景、按需刷新材料去除网格。
    /// 这是 UI 层唯一需要直接操作的组件；UI 通过公开属性/方法控制。
    /// </summary>
    [DisallowMultipleComponent]
    public class CncSimBehaviour : MonoBehaviour
    {
        [Header("References")]
        public CameraRig CameraRig;
        public SceneEntityRenderer Entities;
        public ToolpathRenderer Toolpath;

        [Header("Simulation")]
        [Tooltip("每秒仿真时间倍率（1 = 实时，>1 加速）")]
        public float SpeedMultiplier = 5f;
        [Tooltip("每帧最多推进的真实秒数，避免卡顿")]
        public float MaxDeltaTime = 0.05f;
        [Tooltip("材料去除网格刷新间隔（秒），0 表示每段刷新")]
        public float RemovalRefreshInterval = 0.05f;
        [Tooltip("是否实时应用切削到材料去除")]
        public bool LiveRemoval = true;

        public CncSimulator Sim { get; private set; }
        public int CurrentToolNumber { get; private set; } = 1;

        /// <summary>选择当前刀具并刷新三维刀具模型。</summary>
        public void SelectTool(int number)
        {
            CurrentToolNumber = number;
            Entities?.SetToolNumber(number);
        }

        /// <summary>仿真进度变化（0-1）。</summary>
        public event Action<double> ProgressChanged;
        /// <summary>状态变化。</summary>
        public event Action<SimulationState> StateChanged;
        /// <summary>当前帧更新。</summary>
        public event Action<SimulationFrame> FrameUpdated;
        /// <summary>仿真完成。</summary>
        public event Action Finished;

        private float _removalTimer;
        private bool _pendingRemovalRefresh;
        private readonly Queue<CollisionEvent> _collisionQueue = new Queue<CollisionEvent>();
        public IReadOnlyCollection<CollisionEvent> PendingCollisions => _collisionQueue;

        private void Awake()
        {
            Sim = new CncSimulator();
            Sim.Simulation.FrameAdvanced += OnFrame;
            Sim.Simulation.StateChanged += s => StateChanged?.Invoke(s);
            Sim.Simulation.Finished += () => Finished?.Invoke();
        }

        /// <summary>加载程序文本并准备仿真。</summary>
        public void LoadProgram(string text)
        {
            Sim.Document.SetText(text, recordUndo: false, markDirty: false);
            Sim.Parse(force: true);
            Sim.LoadSimulation();
            CurrentToolNumber = FirstTool();
            Entities?.SetToolNumber(CurrentToolNumber);
            Entities?.RebuildAll();
            Toolpath?.Rebuild();
            UpdateToolTransform(Sim.Simulation.Current);
            _pendingRemovalRefresh = true;
        }

        private int FirstTool()
        {
            foreach (var t in Sim.Program.ToolsUsed) return t;
            return 1;
        }

        public void Play() => Sim.Simulation.Play();
        public void Pause() => Sim.Simulation.Pause();
        public void Stop()
        {
            Sim.Simulation.Stop();
            if (LiveRemoval) Sim.LoadSimulation();
            Entities?.UpdateRemovalMesh();
            UpdateToolTransform(Sim.Simulation.Current);
        }

        public void TogglePlayPause() => Sim.Simulation.TogglePlayPause();
        public void StepForward() => Sim.Simulation.StepForward();
        public void StepBackward() => Sim.Simulation.StepBackward();
        public void StartFromLine(int line)
        {
            if (LiveRemoval) Sim.LoadSimulation();
            Sim.Simulation.StartFromLine(line);
            Entities?.UpdateRemovalMesh();
        }

        public void SeekProgress(double progress) => Sim.Simulation.SeekProgress(progress);
        public void SeekTime(double time) => Sim.Simulation.SeekTime(time);

        public void SetSpeed(float multiplier)
        {
            SpeedMultiplier = Mathf.Max(0.01f, multiplier);
            Sim.Simulation.SpeedMultiplier = SpeedMultiplier;
        }

        private void Update()
        {
            if (Sim == null) return;
            if (Sim.Simulation.State == SimulationState.Running || Sim.Simulation.State == SimulationState.Paused)
            {
                float dt = Mathf.Min(Time.deltaTime, MaxDeltaTime) * SpeedMultiplier;
                if (dt > 0f)
                {
                    Sim.AdvanceSimulation(dt, applyCutting: LiveRemoval);
                    ProgressChanged?.Invoke(Sim.Simulation.Current.Progress);
                }
            }

            if (_pendingRemovalRefresh && LiveRemoval && Entities != null)
            {
                _removalTimer += Time.deltaTime;
                bool intervalElapsed = RemovalRefreshInterval <= 0f || _removalTimer >= RemovalRefreshInterval;
                if (intervalElapsed || Sim.Simulation.State != SimulationState.Running)
                {
                    Entities.UpdateRemovalMesh();
                    _removalTimer = 0f;
                    _pendingRemovalRefresh = false;
                }
            }
        }

        private void OnFrame(SimulationFrame frame)
        {
            UpdateToolTransform(frame);
            if (LiveRemoval) _pendingRemovalRefresh = true;
            if (frame.ToolNumber > 0 && frame.ToolNumber != CurrentToolNumber)
            {
                CurrentToolNumber = frame.ToolNumber;
                Entities?.SetToolNumber(CurrentToolNumber);
            }
            FrameUpdated?.Invoke(frame);
        }

        private void UpdateToolTransform(SimulationFrame frame)
        {
            if (Entities == null || !frame.Valid) return;
            ToolTransformUpdate(frame.Position, frame.ToolDirection);
            Entities.UpdateRotaryPose(frame.Axes);
        }

        /// <summary>按刀尖位置与刀轴方向摆放刀具。</summary>
        public void ToolTransformUpdate(Vec3d tip, Vec3d direction)
        {
            if (Entities?.ToolTransform == null) return;
            var t = Entities.ToolTransform;
            Vector3 pos = MeshBuilder.ToUnity(tip);
            Vector3 dir = MeshBuilder.ToUnityDirection(direction);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.down;
            // 刀具模型默认沿 +Y（Unity）建模，轴向下：
            t.position = pos;
            t.rotation = Quaternion.FromToRotation(Vector3.down, dir.normalized);
        }

        // ---------- 分析类 API（转发给 Core，供 UI 调用）----------

        public TimeEstimator.Breakdown AnalyzeTime() => Sim.AnalyzeTime();
        public List<CollisionEvent> DetectCollisions() => Sim.DetectCollisions();
        public List<Core.Optimization.PathSuggestion> OptimizePath() => Sim.OptimizePath();
        public Core.Analysis.MachiningAnalysisResult AnalyzeMachining(double tol = 0.1) => Sim.AnalyzeMachining(tol);
        public Core.Simulation.CuttingParameters Recommend(int tool, double fraction = 0.5) => Sim.RecommendParameters(tool, fraction);

        /// <summary>聚焦整个工件包围盒。</summary>
        public void FrameWorkpiece(ViewPreset preset = ViewPreset.Isometric)
        {
            var bounds = Sim.Blank.Bounds;
            var min = MeshBuilder.ToUnity(bounds.Min);
            var max = MeshBuilder.ToUnity(bounds.Max);
            var unityBounds = new Bounds((min + max) * 0.5f, max - min);
            CameraRig?.FrameAll(unityBounds, preset);
        }

        private void OnDestroy()
        {
            if (Sim != null)
            {
                Sim.Simulation.FrameAdvanced -= OnFrame;
            }
        }
    }
}
