using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CncSim.Core.Simulation;

namespace CncSim.Core.Recording
{
    /// <summary>单帧录制数据（功能 51 仿真回放/录制）。</summary>
    public struct RecordingFrame
    {
        /// <summary>仿真时钟（秒）。</summary>
        public double Time;
        public int SegmentIndex;
        public int LineIndex;
        public double Progress;
        public double X, Y, Z, A, B, C;
        public double Tx, Ty, Tz; // 刀轴方向
        public double Feed;
        public double SpindleSpeed;
        public int Tool;
        public bool Cutting;
        public bool SpindleOn;
        public bool Coolant;
    }

    /// <summary>
    /// 仿真录制器：把每一帧的刀具状态缓存为关键帧序列，可序列化、回放（功能 51）。
    /// 采样策略：按最小时间间隔或状态变化记录，避免逐帧数据过大。
    /// </summary>
    public class SimulationRecorder
    {
        /// <summary>最小采样间隔（仿真秒）。0 表示记录每一帧。</summary>
        public double MinSampleInterval = 0.02;
        /// <summary>最大关键帧数，超过后按两倍间隔抽稀，保证内存可控。</summary>
        public int MaxFrames = 100_000;

        public bool IsRecording { get; private set; }
        public bool IsLooping { get; set; }

        private readonly List<RecordingFrame> _frames = new List<RecordingFrame>();
        private double _lastRecordedTime = double.NegativeInfinity;

        /// <summary>关联的程序时间总长（录制完成时确定）。</summary>
        public double Duration => _frames.Count > 0 ? _frames[_frames.Count - 1].Time : 0;
        public int FrameCount => _frames.Count;
        public IReadOnlyList<RecordingFrame> Frames => _frames;

        public void Start()
        {
            _frames.Clear();
            _lastRecordedTime = double.NegativeInfinity;
            IsRecording = true;
        }

        public void Stop() => IsRecording = false;

        /// <summary>录制一帧（若不满足采样条件则忽略）。</summary>
        public bool Capture(in SimulationFrame frame)
        {
            if (!IsRecording || !frame.Valid) return false;
            if (frame.ElapsedTime - _lastRecordedTime < MinSampleInterval) return false;
            _frames.Add(Convert(frame));
            _lastRecordedTime = frame.ElapsedTime;
            if (_frames.Count > MaxFrames) Decimate();
            return true;
        }

        private static RecordingFrame Convert(in SimulationFrame f)
        {
            return new RecordingFrame
            {
                Time = f.ElapsedTime,
                SegmentIndex = f.SegmentIndex,
                LineIndex = f.LineIndex,
                Progress = f.Progress,
                X = f.Axes.X, Y = f.Axes.Y, Z = f.Axes.Z,
                A = f.Axes.A, B = f.Axes.B, C = f.Axes.C,
                Tx = f.ToolDirection.X, Ty = f.ToolDirection.Y, Tz = f.ToolDirection.Z,
                Feed = f.Feed,
                SpindleSpeed = f.SpindleSpeed,
                Tool = f.ToolNumber,
                Cutting = f.Cutting,
                SpindleOn = f.SpindleOn,
                Coolant = f.CoolantOn
            };
        }

        private void Decimate()
        {
            var reduced = new List<RecordingFrame>(_frames.Count / 2);
            for (int i = 0; i < _frames.Count; i += 2) reduced.Add(_frames[i]);
            _frames.Clear();
            _frames.AddRange(reduced);
            MinSampleInterval *= 2;
        }

        /// <summary>在录制帧序列上按时间插值，用于回放（功能 51）。</summary>
        public bool Sample(double time, out RecordingFrame frame)
        {
            frame = default;
            if (_frames.Count == 0) return false;
            if (time <= _frames[0].Time) { frame = _frames[0]; return true; }
            int last = _frames.Count - 1;
            if (time >= _frames[last].Time) { frame = _frames[last]; return true; }

            int lo = 0, hi = last;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (_frames[mid].Time <= time) lo = mid; else hi = mid;
            }
            var a = _frames[lo];
            var b = _frames[hi];
            double span = Math.Max(1e-12, b.Time - a.Time);
            double t = (time - a.Time) / span;
            frame = Lerp(a, b, t);
            return true;
        }

        private static RecordingFrame Lerp(in RecordingFrame a, in RecordingFrame b, double t)
        {
            return new RecordingFrame
            {
                Time = a.Time + (b.Time - a.Time) * t,
                SegmentIndex = t < 0.5 ? a.SegmentIndex : b.SegmentIndex,
                LineIndex = t < 0.5 ? a.LineIndex : b.LineIndex,
                Progress = a.Progress + (b.Progress - a.Progress) * t,
                X = a.X + (b.X - a.X) * t,
                Y = a.Y + (b.Y - a.Y) * t,
                Z = a.Z + (b.Z - a.Z) * t,
                A = a.A + (b.A - a.A) * t,
                B = a.B + (b.B - a.B) * t,
                C = a.C + (b.C - a.C) * t,
                Tx = a.Tx + (b.Tx - a.Tx) * t,
                Ty = a.Ty + (b.Ty - a.Ty) * t,
                Tz = a.Tz + (b.Tz - a.Tz) * t,
                Feed = a.Feed + (b.Feed - a.Feed) * t,
                SpindleSpeed = a.SpindleSpeed + (b.SpindleSpeed - a.SpindleSpeed) * t,
                Tool = t < 0.5 ? a.Tool : b.Tool,
                Cutting = a.Cutting && b.Cutting,
                SpindleOn = t < 0.5 ? a.SpindleOn : b.SpindleOn,
                Coolant = t < 0.5 ? a.Coolant : b.Coolant
            };
        }

        public void Clear() => _frames.Clear();

        // ---------------- 序列化（功能 62 结果保存/回放）----------------

        private const string Magic = "CNCSIMREC1";

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Magic);
            sb.AppendLine(_frames.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var f in _frames)
            {
                sb.Append(f.Time.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                  .Append(f.SegmentIndex).Append('|')
                  .Append(f.LineIndex).Append('|')
                  .Append(f.Progress.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                  .Append(Num(f.X)).Append(',')
                  .Append(Num(f.Y)).Append(',')
                  .Append(Num(f.Z)).Append(',')
                  .Append(Num(f.A)).Append(',')
                  .Append(Num(f.B)).Append(',')
                  .Append(Num(f.C)).Append('|')
                  .Append(Num(f.Tx)).Append(',')
                  .Append(Num(f.Ty)).Append(',')
                  .Append(Num(f.Tz)).Append('|')
                  .Append(Num(f.Feed)).Append('|')
                  .Append(Num(f.SpindleSpeed)).Append('|')
                  .Append(f.Tool).Append('|')
                  .Append(f.Cutting ? '1' : '0')
                  .Append(f.SpindleOn ? '1' : '0')
                  .Append(f.Coolant ? '1' : '0');
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }

        public static SimulationRecorder Load(string path)
        {
            var recorder = new SimulationRecorder();
            using var reader = new StreamReader(path);
            string magic = reader.ReadLine();
            if (magic != Magic) throw new InvalidDataException("Not a CncSim recording file");
            string countLine = reader.ReadLine();
            if (countLine == null || !int.TryParse(countLine, out int count))
                throw new InvalidDataException("Bad frame count");

            for (int i = 0; i < count && !reader.EndOfStream; i++)
            {
                string line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;
                var f = ParseFrame(line);
                recorder._frames.Add(f);
            }
            if (recorder._frames.Count > 0)
                recorder._lastRecordedTime = recorder._frames[recorder._frames.Count - 1].Time;
            return recorder;
        }

        private static RecordingFrame ParseFrame(string line)
        {
            var f = new RecordingFrame();
            var parts = line.Split('|');
            f.Time = D(parts, 0);
            f.SegmentIndex = (int)D(parts, 1);
            f.LineIndex = (int)D(parts, 2);
            f.Progress = D(parts, 3);
            var pos = Split(parts, 4);
            f.X = pos[0]; f.Y = pos[1]; f.Z = pos[2]; f.A = pos[3]; f.B = pos[4]; f.C = pos[5];
            var dir = Split(parts, 5);
            f.Tx = dir[0]; f.Ty = dir[1]; f.Tz = dir[2];
            f.Feed = D(parts, 6);
            f.SpindleSpeed = D(parts, 7);
            f.Tool = (int)D(parts, 8);
            string flags = parts.Length > 9 ? parts[9] : "000";
            f.Cutting = flags.Length > 0 && flags[0] == '1';
            f.SpindleOn = flags.Length > 1 && flags[1] == '1';
            f.Coolant = flags.Length > 2 && flags[2] == '1';
            return f;
        }

        private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static double D(string[] parts, int i) =>
            parts.Length > i && double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static double[] Split(string[] parts, int i)
        {
            var result = new double[6];
            if (parts.Length <= i) return result;
            var seg = parts[i].Split(',');
            for (int k = 0; k < 6 && k < seg.Length; k++)
                double.TryParse(seg[k], NumberStyles.Float, CultureInfo.InvariantCulture, out result[k]);
            return result;
        }
    }

    /// <summary>
    /// 回放控制器：把录制的关键帧按时间播放，配合渲染层驱动刀具/工件（功能 51）。
    /// </summary>
    public class SimulationPlayer
    {
        private readonly SimulationRecorder _recorder;
        public double Speed { get; set; } = 1.0;
        public double CurrentTime { get; private set; }
        public bool IsPlaying { get; private set; }

        public event Action<RecordingFrame> FramePlayed;
        public event Action Finished;

        public SimulationPlayer(SimulationRecorder recorder)
        {
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        }

        public double Duration => _recorder.Duration;

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0;
            Emit();
        }

        public void Seek(double time)
        {
            CurrentTime = Math.Max(0, Math.Min(Duration, time));
            Emit();
        }

        public void SeekProgress(double progress) => Seek(progress * Duration);

        public void Advance(double deltaTime)
        {
            if (!IsPlaying) return;
            CurrentTime += deltaTime * Speed;
            if (CurrentTime >= Duration)
            {
                CurrentTime = Duration;
                IsPlaying = false;
                Emit();
                Finished?.Invoke();
                return;
            }
            Emit();
        }

        private void Emit()
        {
            if (_recorder.Sample(CurrentTime, out var frame)) FramePlayed?.Invoke(frame);
        }
    }
}
