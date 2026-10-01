using System;
using System.Collections.Generic;

namespace CncSim.Core.Parsing
{
    public enum MachineType
    {
        ThreeAxis,
        FourAxis,
        FiveAxis,
        Custom
    }

    /// <summary>
    /// 五轴结构类型：
    /// TableTable（AC 双转台）、HeadTable（BC 摆头+转台）、HeadHead（AB 双摆头）。
    /// </summary>
    public enum FiveAxisKinematics
    {
        None,
        TableTable,
        HeadTable,
        HeadHead
    }

    /// <summary>轴定义：行程、最大速度、最大加速度、是否旋转轴。</summary>
    [Serializable]
    public class AxisDefinition
    {
        public char Letter = 'X';
        public bool IsRotary;
        public double Min;
        public double Max;
        public double MaxVelocity = 10000;   // mm/min 或 deg/min
        public double MaxAcceleration = 1000; // mm/s^2 或 deg/s^2
        public bool Enabled = true;

        public AxisDefinition() { }

        public AxisDefinition(char letter, double min, double max, double maxVel, double maxAcc, bool rotary = false)
        {
            Letter = letter;
            Min = min;
            Max = max;
            MaxVelocity = maxVel;
            MaxAcceleration = maxAcc;
            IsRotary = rotary;
            Enabled = true;
        }

        public bool Contains(double value) => value >= Min - 1e-9 && value <= Max + 1e-9;
        public double Clamp(double value) => Math.Max(Min, Math.Min(Max, value));
        public AxisDefinition Clone() => (AxisDefinition)MemberwiseClone();
    }

    /// <summary>
    /// 机床配置：轴定义、工件坐标系偏置、快移速度、主轴范围、结构类型与运动学。
    /// 由用户自定义机床时直接修改字段并调用 Validate()。
    /// </summary>
    [Serializable]
    public class MachineProfile
    {
        public string Name = "3-Axis Mill";
        public MachineType Type = MachineType.ThreeAxis;
        public FiveAxisKinematics Kinematics = FiveAxisKinematics.None;

        /// <summary>直线轴（索引 0=X,1=Y,2=Z）与旋转轴（索引 3=A,4=B,5=C）。</summary>
        public AxisDefinition[] Axes = new AxisDefinition[6];

        /// <summary>工件坐标系 G54-G59 的机床坐标偏置。</summary>
        public Vec3d[] WorkOffsets = new Vec3d[6];

        /// <summary>G54.1 P1..P48 附加偏置。</summary>
        public Vec3d[] AdditionalWorkOffsets = new Vec3d[48];

        /// <summary>快移速度（mm/min，所有直线轴共用）。</summary>
        public double RapidFeed = 10000;

        /// <summary>主轴转速范围（rpm）。</summary>
        public double SpindleMin = 0;
        public double SpindleMax = 24000;

        /// <summary>主轴锥孔/夹头到主轴端面的参考距离（用于显示模型）。</summary>
        public double SpindleGaugeLength = 100;

        /// <summary>刀具库校验是否严格（严格时刀号不在库中会告警）。</summary>
        public bool ToolLibraryIsStrict = true;

        public MachineProfile()
        {
            Axes[0] = new AxisDefinition('X', -400, 400, 30000, 3000);
            Axes[1] = new AxisDefinition('Y', -300, 300, 30000, 3000);
            Axes[2] = new AxisDefinition('Z', -300, 100, 30000, 3000);
            Axes[3] = new AxisDefinition('A', -120, 120, 5000, 500, true) { Enabled = false };
            Axes[4] = new AxisDefinition('B', -120, 120, 5000, 500, true) { Enabled = false };
            Axes[5] = new AxisDefinition('C', 0, 360, 10000, 1000, true) { Enabled = false };
        }

        public static MachineProfile Create3Axis() => new MachineProfile();

        public static MachineProfile Create4Axis()
        {
            var m = new MachineProfile();
            m.Name = "4-Axis Mill";
            m.Type = MachineType.FourAxis;
            m.Kinematics = FiveAxisKinematics.None;
            m.Axes[3].Enabled = true; // A 轴
            return m;
        }

        public static MachineProfile Create5Axis(FiveAxisKinematics kinematics = FiveAxisKinematics.TableTable)
        {
            var m = new MachineProfile();
            m.Name = "5-Axis Mill";
            m.Type = MachineType.FiveAxis;
            m.Kinematics = kinematics;
            m.Axes[3].Enabled = true;
            m.Axes[4].Enabled = true;
            m.Axes[5].Enabled = true;
            m.Axes[5].Min = -360;
            m.Axes[5].Max = 360;
            return m;
        }

        public Vec3d GetWorkOffset(int index)
        {
            if (index >= 0 && index < WorkOffsets.Length) return WorkOffsets[index];
            int add = index - WorkOffsets.Length;
            if (add >= 0 && add < AdditionalWorkOffsets.Length) return AdditionalWorkOffsets[add];
            return Vec3d.Zero;
        }

        public AxisDefinition GetAxis(int axisIndex) => Axes[axisIndex];

        public bool IsRotaryEnabled => Axes[3].Enabled || Axes[4].Enabled || Axes[5].Enabled;

        /// <summary>
        /// 检查某轴位置是否在软限位内，返回诊断信息（null 表示 OK）。
        /// </summary>
        public Diagnostic CheckSoftLimit(int axisIndex, double value, int lineIndex)
        {
            var ax = Axes[axisIndex];
            if (!ax.Enabled || ax.Contains(value)) return null;
            string[] keys = { "diag.soft_limit_x", "diag.soft_limit_y", "diag.soft_limit_z", "diag.soft_limit_a", "diag.soft_limit_b", "diag.soft_limit_c" };
            return new Diagnostic(DiagnosticSeverity.Error, lineIndex, 0, 0, keys[axisIndex], value, ax.Min, ax.Max);
        }

        /// <summary>校验配置合法性，返回问题列表（空表示合法）。</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (RapidFeed <= 0) problems.Add("RapidFeed must be > 0");
            if (SpindleMax <= 0) problems.Add("SpindleMax must be > 0");
            for (int i = 0; i < Axes.Length; i++)
            {
                var a = Axes[i];
                if (a.Max < a.Min) problems.Add($"Axis {a.Letter}: Max < Min");
                if (a.Enabled && a.MaxVelocity <= 0) problems.Add($"Axis {a.Letter}: MaxVelocity must be > 0");
            }
            if (Kinematics != FiveAxisKinematics.None && !IsRotaryEnabled)
                problems.Add("Five-axis kinematics requires enabled rotary axes");
            return problems;
        }

        public MachineProfile Clone()
        {
            var c = (MachineProfile)MemberwiseClone();
            c.Axes = new AxisDefinition[Axes.Length];
            for (int i = 0; i < Axes.Length; i++) c.Axes[i] = Axes[i].Clone();
            c.WorkOffsets = (Vec3d[])WorkOffsets.Clone();
            c.AdditionalWorkOffsets = (Vec3d[])AdditionalWorkOffsets.Clone();
            return c;
        }

        /// <summary>导出为可序列化描述（供用户自定义机床保存/加载）。</summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            sb.Append("\"name\":\"").Append(JsonEscape(Name)).Append("\",");
            sb.Append("\"type\":\"").Append(Type).Append("\",");
            sb.Append("\"kinematics\":\"").Append(Kinematics).Append("\",");
            sb.Append("\"rapidFeed\":").Append(RapidFeed.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"spindleMax\":").Append(SpindleMax.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"axes\":[");
            for (int i = 0; i < Axes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var a = Axes[i];
                sb.Append('{').Append("\"letter\":\"").Append(a.Letter).Append("\",")
                  .Append("\"enabled\":").Append(a.Enabled ? "true" : "false").Append(',')
                  .Append("\"rotary\":").Append(a.IsRotary ? "true" : "false").Append(',')
                  .Append("\"min\":").Append(a.Min.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"max\":").Append(a.Max.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"maxVel\":").Append(a.MaxVelocity.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"maxAcc\":").Append(a.MaxAcceleration.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("],\"workOffsets\":[");
            for (int i = 0; i < WorkOffsets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(WorkOffsets[i].X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(WorkOffsets[i].Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(WorkOffsets[i].Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string JsonEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
