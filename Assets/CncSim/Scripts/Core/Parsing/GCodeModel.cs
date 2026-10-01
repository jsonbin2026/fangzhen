using System;
using System.Collections.Generic;

namespace CncSim.Core.Parsing
{
    /// <summary>
    /// 机床轴位置：X/Y/Z 为直线轴（mm），A/B/C 为旋转轴（度）。
    /// 解析结果中统一使用机床坐标系（已叠加工件坐标系偏置、已换算为公制）。
    /// </summary>
    [Serializable]
    public struct AxisVector : IEquatable<AxisVector>
    {
        public double X, Y, Z, A, B, C;

        public AxisVector(double x, double y, double z, double a = 0, double b = 0, double c = 0)
        {
            X = x;
            Y = y;
            Z = z;
            A = a;
            B = b;
            C = c;
        }

        public Vec3d Linear
        {
            get => new Vec3d(X, Y, Z);
            set
            {
                X = value.X;
                Y = value.Y;
                Z = value.Z;
            }
        }

        public double this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    case 3: return A;
                    case 4: return B;
                    default: return C;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    case 3: A = value; break;
                    case 4: B = value; break;
                    default: C = value; break;
                }
            }
        }

        public const int AxisCount = 6;
        public static readonly char[] AxisLetters = { 'X', 'Y', 'Z', 'A', 'B', 'C' };

        public static int AxisIndex(char letter)
        {
            switch (char.ToUpperInvariant(letter))
            {
                case 'X': return 0;
                case 'Y': return 1;
                case 'Z': return 2;
                case 'A': return 3;
                case 'B': return 4;
                case 'C': return 5;
                default: return -1;
            }
        }

        public static AxisVector Lerp(AxisVector a, AxisVector b, double t)
        {
            var r = new AxisVector();
            for (int i = 0; i < AxisCount; i++) r[i] = a[i] + (b[i] - a[i]) * t;
            return r;
        }

        public static AxisVector operator +(AxisVector a, AxisVector b)
        {
            var r = new AxisVector();
            for (int i = 0; i < AxisCount; i++) r[i] = a[i] + b[i];
            return r;
        }

        public static AxisVector operator -(AxisVector a, AxisVector b)
        {
            var r = new AxisVector();
            for (int i = 0; i < AxisCount; i++) r[i] = a[i] - b[i];
            return r;
        }

        public double LinearDistance(AxisVector other) => Vec3d.Distance(Linear, other.Linear);

        public double MaxRotaryDelta(AxisVector other) =>
            Math.Max(Math.Abs(A - other.A), Math.Max(Math.Abs(B - other.B), Math.Abs(C - other.C)));

        public bool ApproximatelyEquals(AxisVector other, double tolerance = 1e-6)
        {
            for (int i = 0; i < AxisCount; i++)
                if (Math.Abs(this[i] - other[i]) > tolerance) return false;
            return true;
        }

        public bool Equals(AxisVector o) => X == o.X && Y == o.Y && Z == o.Z && A == o.A && B == o.B && C == o.C;
        public override bool Equals(object obj) => obj is AxisVector v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, A, B, C);

        public override string ToString() =>
            string.Format("X{0:0.###} Y{1:0.###} Z{2:0.###} A{3:0.###} B{4:0.###} C{5:0.###}", X, Y, Z, A, B, C);
    }

    public enum MotionType
    {
        Rapid,
        Linear,
        ArcCW,
        ArcCCW,
        Dwell,
        ToolChange,
        /// <summary>非运动事件（主轴/冷却/暂停等），用于仿真在该时刻更新状态。</summary>
        Event
    }

    public enum Plane
    {
        XY = 17,
        ZX = 18,
        YZ = 19
    }

    public enum SpindleState
    {
        Off,
        CW,
        CCW
    }

    public enum FeedMode
    {
        /// <summary>G94 每分钟进给 mm/min</summary>
        PerMinute,
        /// <summary>G95 每转进给 mm/rev</summary>
        PerRevolution,
        /// <summary>G93 反比时间进给</summary>
        InverseTime
    }

    public enum CutterCompMode
    {
        Off = 40,
        Left = 41,
        Right = 42
    }

    public enum ProgramEventType
    {
        None,
        SpindleCW,
        SpindleCCW,
        SpindleStop,
        CoolantMist,
        CoolantFlood,
        CoolantOff,
        ProgramStop,
        OptionalStop,
        ProgramEnd,
        ToolChange,
        ThroughSpindleCoolant
    }

    /// <summary>
    /// 模态状态快照。每个程序段解析后都会保存一份，用于单行解释、从任意行开始仿真。
    /// </summary>
    [Serializable]
    public class ModalState
    {
        public MotionType Motion = MotionType.Rapid;
        /// <summary>固定循环 G 代码（81/82/83/73/84/85/86/89），0 表示无。</summary>
        public int CannedCycle;
        public Plane Plane = Plane.XY;
        public bool Metric = true;
        public bool Absolute = true;
        /// <summary>圆弧 IJK 是否为绝对值（G90.1），默认增量（G91.1）。</summary>
        public bool ArcAbsolute;
        public FeedMode FeedMode = FeedMode.PerMinute;
        /// <summary>工件坐标系索引：0..5 = G54..G59；6 以后为 G54.1 P1..</summary>
        public int WorkOffsetIndex;
        public CutterCompMode CutterComp = CutterCompMode.Off;
        public int CutterCompD;
        public bool LengthCompActive;
        public int LengthCompH;
        /// <summary>G98 返回初始平面；G99 返回 R 平面。</summary>
        public bool CannedReturnToInitial = true;
        /// <summary>RTCP / TCPM（G43.4 / G234 / TRAORI）开启。</summary>
        public bool Rtcp;
        public double Feed;
        public double SpindleSpeed;
        public SpindleState Spindle = SpindleState.Off;
        public bool CoolantMist;
        public bool CoolantFlood;
        public int CurrentTool;
        public int PreparedTool;
        /// <summary>当前机床坐标位置（控制点）。</summary>
        public AxisVector Position;
        /// <summary>固定循环参数。</summary>
        public double CannedR, CannedQ, CannedP, CannedZ;
        /// <summary>G51 缩放、G68 旋转坐标（XY 平面）。</summary>
        public double RotationDeg;
        public Vec3d RotationCenter;
        public double Scale = 1.0;

        public ModalState Clone() => (ModalState)MemberwiseClone();
    }

    /// <summary>
    /// 运动段。所有位置为控制点（刀尖）在机床坐标系中的位置。
    /// 圆弧支持螺旋（非圆弧平面轴线性变化）与整圆。
    /// </summary>
    [Serializable]
    public class MotionSegment
    {
        public int Index;
        /// <summary>来源程序行（0 基）。</summary>
        public int LineIndex;
        public MotionType Type;
        public AxisVector Start;
        public AxisVector End;
        /// <summary>圆弧圆心（机床坐标，仅圆弧平面两个分量有意义，第三分量等于起点值）。</summary>
        public Vec3d Center;
        public Plane Plane = Plane.XY;
        /// <summary>额外整圈数（L 或螺旋多圈）。</summary>
        public int ExtraTurns;
        /// <summary>编程进给率（mm/min，G95/G93 已换算）。快移时为机床快移速度。</summary>
        public double Feed;
        public double SpindleSpeed;
        public SpindleState Spindle;
        public bool Coolant;
        public int ToolNumber;
        public double DwellSeconds;
        public bool Rtcp;
        public ProgramEventType Event;
        /// <summary>是否为固定循环展开生成。</summary>
        public bool FromCannedCycle;
        /// <summary>估算时长（秒），由 TimeEstimator 填写。</summary>
        public double Duration;
        /// <summary>从程序开始到本段开始的累计时间（秒）。</summary>
        public double StartTime;

        public bool IsMotion => Type == MotionType.Rapid || Type == MotionType.Linear || Type == MotionType.ArcCW || Type == MotionType.ArcCCW;
        public bool IsArc => Type == MotionType.ArcCW || Type == MotionType.ArcCCW;
        public bool IsCutting => Type == MotionType.Linear || IsArc;

        /// <summary>圆弧平面内的两个轴索引与法向轴索引。</summary>
        public static void PlaneAxes(Plane plane, out int a0, out int a1, out int normal)
        {
            switch (plane)
            {
                case Plane.ZX:
                    a0 = 2; a1 = 0; normal = 1;
                    break;
                case Plane.YZ:
                    a0 = 1; a1 = 2; normal = 0;
                    break;
                default:
                    a0 = 0; a1 = 1; normal = 2;
                    break;
            }
        }

        /// <summary>圆弧半径。</summary>
        public double Radius
        {
            get
            {
                if (!IsArc) return 0;
                PlaneAxes(Plane, out int a0, out int a1, out _);
                double dx = Start[a0] - Center[a0], dy = Start[a1] - Center[a1];
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        /// <summary>圆弧扫掠角（弧度，恒为正值），方向由 Type 决定。</summary>
        public double SweepAngle
        {
            get
            {
                if (!IsArc) return 0;
                PlaneAxes(Plane, out int a0, out int a1, out _);
                double sa = Math.Atan2(Start[a1] - Center[a1], Start[a0] - Center[a0]);
                double ea = Math.Atan2(End[a1] - Center[a1], End[a0] - Center[a0]);
                double sweep = Type == MotionType.ArcCCW ? ea - sa : sa - ea;
                sweep = MathUtil.NormalizeAngle(sweep);
                if (sweep < 1e-9) sweep = 2 * Math.PI; // 起终点重合 => 整圆
                return sweep + ExtraTurns * 2 * Math.PI;
            }
        }

        /// <summary>段长度（mm），圆弧含螺旋分量；纯旋转运动返回 0。</summary>
        public double Length
        {
            get
            {
                if (!IsMotion) return 0;
                if (!IsArc) return Start.LinearDistance(End);
                PlaneAxes(Plane, out _, out _, out int n);
                double arc = Radius * SweepAngle;
                double h = End[n] - Start[n];
                return Math.Sqrt(arc * arc + h * h);
            }
        }

        /// <summary>按参数 t∈[0,1] 求位置。</summary>
        public AxisVector Evaluate(double t)
        {
            t = MathUtil.Clamp01(t);
            if (!IsArc) return AxisVector.Lerp(Start, End, t);
            PlaneAxes(Plane, out int a0, out int a1, out int n);
            double r = Radius;
            double sa = Math.Atan2(Start[a1] - Center[a1], Start[a0] - Center[a0]);
            double sweep = SweepAngle;
            double ang = Type == MotionType.ArcCCW ? sa + sweep * t : sa - sweep * t;
            var p = AxisVector.Lerp(Start, End, t);
            if (t >= 1)
            {
                p = End;
            }
            else
            {
                p[a0] = Center[a0] + r * Math.Cos(ang);
                p[a1] = Center[a1] + r * Math.Sin(ang);
            }
            p[n] = Start[n] + (End[n] - Start[n]) * t;
            return p;
        }

        /// <summary>
        /// 离散为折线点（含起终点）。chordTolerance 为弦高误差（mm），maxAngleStepDeg 限制旋转轴每步变化。
        /// </summary>
        public List<AxisVector> Discretize(double chordTolerance = 0.01, double maxStepLength = double.MaxValue, double maxAngleStepDeg = 2.0)
        {
            var pts = new List<AxisVector>();
            if (!IsMotion)
            {
                pts.Add(Start);
                return pts;
            }
            int steps = 1;
            if (IsArc)
            {
                double r = Math.Max(Radius, 1e-6);
                double tol = Math.Min(chordTolerance, r * 0.5);
                double stepAngle = 2 * Math.Acos(1 - tol / r);
                if (stepAngle <= 1e-6 || double.IsNaN(stepAngle)) stepAngle = 0.1;
                steps = Math.Max(steps, (int)Math.Ceiling(SweepAngle / stepAngle));
            }
            double len = Length;
            if (maxStepLength > 0 && maxStepLength < double.MaxValue && len > maxStepLength)
                steps = Math.Max(steps, (int)Math.Ceiling(len / maxStepLength));
            double rot = Start.MaxRotaryDelta(End);
            if (rot > maxAngleStepDeg && maxAngleStepDeg > 0)
                steps = Math.Max(steps, (int)Math.Ceiling(rot / maxAngleStepDeg));
            steps = Math.Min(steps, 100000);
            for (int i = 0; i <= steps; i++) pts.Add(Evaluate((double)i / steps));
            return pts;
        }

        public override string ToString() => $"#{Index} L{LineIndex + 1} {Type} {Start} -> {End} F{Feed:0.#}";
    }

    /// <summary>解析后的单个程序段（一行）。</summary>
    [Serializable]
    public class GCodeBlock
    {
        public int LineIndex;
        public string RawText;
        public int? SequenceNumber;
        public bool BlockDelete;
        public string Comment;
        /// <summary>G 代码（以数值表示，如 1、2、54.1、43.4）。</summary>
        public readonly List<double> GCodes = new List<double>();
        public readonly List<int> MCodes = new List<int>();
        /// <summary>地址字（除 G/M/N 外），键为大写字母。</summary>
        public readonly Dictionary<char, double> Words = new Dictionary<char, double>();
        /// <summary>该段执行后的模态状态快照。</summary>
        public ModalState StateAfter;
        /// <summary>该段产生的运动段在 ParseResult.Segments 中的范围。</summary>
        public int FirstSegment = -1;
        public int SegmentCount;

        public bool Has(char letter) => Words.ContainsKey(letter);
        public double Get(char letter, double fallback = 0) => Words.TryGetValue(letter, out var v) ? v : fallback;
        public bool HasG(double code) => GCodes.Exists(g => Math.Abs(g - code) < 1e-6);
        public bool HasM(int code) => MCodes.Contains(code);
        public bool IsEmpty => GCodes.Count == 0 && MCodes.Count == 0 && Words.Count == 0;
    }

    /// <summary>解析结果。</summary>
    public class ParseResult
    {
        public readonly List<GCodeBlock> Blocks = new List<GCodeBlock>();
        public readonly List<MotionSegment> Segments = new List<MotionSegment>();
        public readonly DiagnosticList Diagnostics = new DiagnosticList();
        public readonly SortedSet<int> ToolsUsed = new SortedSet<int>();
        /// <summary>切削运动（G01/G02/G03）的刀尖包围盒。</summary>
        public Aabb CuttingBounds = Aabb.Empty;
        /// <summary>全部运动（含快移）的刀尖包围盒。</summary>
        public Aabb AllBounds = Aabb.Empty;
        public double TotalCuttingLength;
        public double TotalRapidLength;
        public bool UsesRotaryAxes;
        public int SourceVersion = -1;

        /// <summary>找到行对应的第一个运动段索引（向后查找最近有运动的行），无则返回 -1。</summary>
        public int FirstSegmentAtOrAfterLine(int line)
        {
            foreach (var s in Segments)
                if (s.LineIndex >= line) return s.Index;
            return -1;
        }

        /// <summary>行对应的模态状态（该行执行前）。</summary>
        public ModalState StateBeforeLine(int line)
        {
            ModalState last = null;
            foreach (var b in Blocks)
            {
                if (b.LineIndex >= line) break;
                if (b.StateAfter != null) last = b.StateAfter;
            }
            return last;
        }
    }
}
