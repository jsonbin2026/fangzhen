using System;
using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.Parsing;
using CncSim.Core.Recording;
using CncSim.Core.Simulation;

namespace CncSim.Runtime
{
    /// <summary>
    /// 仿真录制/回放驱动（功能 51）。
    /// - 录制：订阅 <see cref="CncSimBehaviour.FrameUpdated"/>，把帧写入 SimulationRecorder。
    /// - 回放：用 SimulationPlayer 按时间采样，驱动刀具与轨迹高亮。
    /// - 保存/加载：SimulationRecorder 序列化为文件（功能 62 加工结果保存/回放）。
    /// 与实时仿真互斥：回放时不控 Core SimulationController。
    /// </summary>
    public class RecordReplayController : MonoBehaviour
    {
        [Header("References")]
        public CncSimBehaviour Simulator;

        [Header("Recording")]
        [Tooltip("录制采样间隔（仿真秒）")]
        public double SampleInterval = 0.02;

        [Header("Replay")]
        public double ReplaySpeed = 1.0;
        public bool LoopReplay;
        public Color ReplayPathColor = new Color(1f, 0.85f, 0.2f);

        public SimulationRecorder Recorder { get; private set; } = new SimulationRecorder();
        public SimulationPlayer Player { get; private set; }

        public bool IsRecording => Recorder.IsRecording;
        public bool IsReplaying => Player != null && Player.IsPlaying;

        /// <summary>录制状态变化。</summary>
        public event Action<bool> RecordingChanged;
        /// <summary>回放状态变化。</summary>
        public event Action<bool> ReplayChanged;
        /// <summary>回放进度 0-1。</summary>
        public event Action<double> ReplayProgress;

        private void Awake()
        {
            if (Simulator == null) Simulator = GetComponent<CncSimBehaviour>();
        }

        private void OnEnable()
        {
            if (Simulator != null) Simulator.FrameUpdated += OnFrameUpdated;
        }

        private void OnDisable()
        {
            if (Simulator != null) Simulator.FrameUpdated -= OnFrameUpdated;
        }

        private void OnFrameUpdated(SimulationFrame frame)
        {
            if (Recorder.IsRecording) Recorder.Capture(frame);
        }

        // ---------------- 录制 ----------------

        public void StartRecording()
        {
            Recorder.MinSampleInterval = SampleInterval;
            Recorder.Start();
            RecordingChanged?.Invoke(true);
        }

        public void StopRecording()
        {
            Recorder.Stop();
            RecordingChanged?.Invoke(false);
        }

        public void SaveRecording(string path)
        {
            Recorder.Save(path);
        }

        public bool LoadRecording(string path)
        {
            try
            {
                Recorder = SimulationRecorder.Load(path);
                Player = new SimulationPlayer(Recorder) { Speed = ReplaySpeed };
                HookPlayer();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("LoadRecording failed: " + ex.Message);
                return false;
            }
        }

        // ---------------- 回放 ----------------

        public void StartReplay()
        {
            if (Player == null)
            {
                Player = new SimulationPlayer(Recorder) { Speed = ReplaySpeed };
                HookPlayer();
            }
            if (Simulator != null) Simulator.Pause();
            Player.Play();
            ReplayChanged?.Invoke(true);
        }

        public void PauseReplay()
        {
            Player?.Pause();
            ReplayChanged?.Invoke(false);
        }

        public void StopReplay()
        {
            Player?.Stop();
            ReplayChanged?.Invoke(false);
        }

        public void SeekReplay(double progress) => Player?.SeekProgress(Mathf.Clamp01((float)progress));

        public void SetReplaySpeed(float speed)
        {
            ReplaySpeed = speed;
            if (Player != null) Player.Speed = speed;
        }

        private void HookPlayer()
        {
            if (Player == null) return;
            Player.FramePlayed += OnReplayFrame;
            Player.Finished += OnReplayFinished;
        }

        private void OnReplayFrame(RecordingFrame frame)
        {
            if (Simulator == null) return;
            Simulator.ToolTransformUpdate(
                new Vec3d(frame.X, frame.Y, frame.Z),
                new Vec3d(frame.Tx, frame.Ty, frame.Tz));
            Simulator.Toolpath?.HighlightSegment(frame.SegmentIndex);
            double duration = Math.Max(1e-9, Player.Duration);
            ReplayProgress?.Invoke(Player.CurrentTime / duration);
        }

        private void OnReplayFinished()
        {
            if (LoopReplay)
            {
                Player.Seek(0);
                Player.Play();
            }
            else
            {
                ReplayChanged?.Invoke(false);
            }
        }

        private void Update()
        {
            if (Player != null && Player.IsPlaying)
                Player.Advance(Time.deltaTime * 1.0);
        }

        private void OnDestroy()
        {
            if (Simulator != null) Simulator.FrameUpdated -= OnFrameUpdated;
        }
    }
}
