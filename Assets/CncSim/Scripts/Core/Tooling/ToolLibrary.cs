using System;
using System.Collections.Generic;
using CncSim.Core;

namespace CncSim.Core.Tooling
{
    /// <summary>刀具类型。</summary>
    public enum ToolType
    {
        FlatEndMill,
        BallEndMill,
        BullNoseEndMill,
        Drill,
        CenterDrill,
        Tap,
        ChamferMill,
        ThreadMill,
        FaceMill,
        BoringBar,
        Reamer,
        Probe
    }

    /// <summary>刀具几何。直径、圆角半径、刃长等均为毫米。</summary>
    [Serializable]
    public class ToolDefinition
    {
        /// <summary>刀号（T 编号）。</summary>
        public int Number = 1;
        public string Name = "Tool";
        public ToolType Type = ToolType.FlatEndMill;

        /// <summary>切削直径（mm）。</summary>
        public double Diameter = 6;
        /// <summary>刀尖圆弧半径（mm）；平底刀为 0，球头刀为 Diameter/2。</summary>
        public double CornerRadius;
        /// <summary>刀具总长度（mm）。</summary>
        public double Length = 80;
        /// <summary>刃长（mm）。</summary>
        public double FluteLength = 25;
        /// <summary>螺旋角（度），用于切削力估算。</summary>
        public double HelixAngle = 30;
        /// <summary>刃数。</summary>
        public int Flutes = 2;
        /// <summary>刀具伸出/装夹长度（mm，刀柄到刀尖）。</summary>
        public double StickOut = 60;
        /// <summary>刀柄直径（mm）。</summary>
        public double ShankDiameter = 6;
        /// <summary>刀尖角度（钻头/倒角刀，度）。</summary>
        public double PointAngle = 118;
        /// <summary>螺距（丝锥，mm）。</summary>
        public double Pitch = 1;
        /// <summary>刀具半径磨损补偿量（mm），用于过切/欠切判定。</summary>
        public double WearOffset;

        /// <summary>长度补偿偏置（mm，对应 H 值）。</summary>
        public double LengthOffset;
        /// <summary>半径补偿偏置（mm，对应 D 值）。</summary>
        public double RadiusOffset;

        /// <summary>刀具材质（影响推荐切削参数）。</summary>
        public string Material = "Carbide";

        public double Radius => Diameter * 0.5;
        public double EffectiveRadius => Diameter * 0.5 + RadiusOffset;

        public ToolDefinition Clone() => (ToolDefinition)MemberwiseClone();

        public void Normalize()
        {
            if (Diameter <= 0) Diameter = 1;
            switch (Type)
            {
                case ToolType.BallEndMill:
                    CornerRadius = Diameter * 0.5;
                    break;
                case ToolType.FlatEndMill:
                case ToolType.FaceMill:
                    CornerRadius = 0;
                    break;
                case ToolType.BullNoseEndMill:
                    CornerRadius = Math.Max(0, Math.Min(CornerRadius, Diameter * 0.5));
                    break;
                default:
                    CornerRadius = 0;
                    break;
            }
            Flutes = Math.Max(1, Flutes);
            FluteLength = Math.Max(0.1, FluteLength);
            Length = Math.Max(FluteLength, Length);
            StickOut = Math.Max(0.1, StickOut);
            ShankDiameter = Math.Max(0, Math.Min(ShankDiameter <= 0 ? Diameter : ShankDiameter, Diameter * 3));
        }

        /// <summary>生成刀具旋转体轮廓 (r,z)，刀尖在 z=0，向上为 +z。</summary>
        public List<(double r, double z)> BuildRevolveProfile(int drillSegments = 16)
        {
            Normalize();
            var p = new List<(double r, double z)>();
            double r = Radius, cr = CornerRadius, fl = FluteLength, len = Length, sd = ShankDiameter;
            switch (Type)
            {
                case ToolType.BallEndMill:
                {
                    int n = Math.Max(6, drillSegments);
                    for (int i = 0; i <= n; i++)
                    {
                        double a = -Math.PI / 2 + (Math.PI / 2) * i / n;
                        p.Add((cr + cr * Math.Cos(a), cr + cr * Math.Sin(a)));
                    }
                    // 圆柱段到刃长
                    p.Add((r, fl));
                    p.Add((sd, fl));
                    p.Add((sd, len));
                    p.Add((0, len));
                    break;
                }
                case ToolType.BullNoseEndMill:
                {
                    int n = Math.Max(4, drillSegments / 2);
                    for (int i = 0; i <= n; i++)
                    {
                        double a = -Math.PI / 2 + (Math.PI / 2) * i / n;
                        double rr = (r - cr) + cr * Math.Cos(a);
                        double zz = cr + cr * Math.Sin(a);
                        p.Add((rr, zz));
                    }
                    p.Add((r, fl));
                    p.Add((sd, fl));
                    p.Add((sd, len));
                    p.Add((0, len));
                    break;
                }
                case ToolType.Drill:
                case ToolType.CenterDrill:
                {
                    double half = Math.Min(r, r);
                    double tipHeight = half / Math.Tan(PointAngle * MathUtil.Deg2Rad * 0.5);
                    p.Add((0, tipHeight));
                    p.Add((r, 0));
                    p.Add((r, fl));
                    p.Add((sd, fl));
                    p.Add((sd, len));
                    p.Add((0, len));
                    break;
                }
                case ToolType.Tap:
                    p.Add((0, 0));
                    p.Add((r, 0));
                    p.Add((r, fl));
                    p.Add((sd, fl));
                    p.Add((sd, len));
                    p.Add((0, len));
                    break;
                case ToolType.ChamferMill:
                {
                    double h = r / Math.Tan(Math.Max(1, PointAngle) * MathUtil.Deg2Rad * 0.5);
                    p.Add((0, 0));
                    p.Add((r, h));
                    p.Add((r, fl));
                    p.Add((sd, fl));
                    p.Add((sd, len));
                    p.Add((0, len));
                    break;
                }
                default: // Flat / Face / Thread / Boring / Reamer / Probe
                    p.Add((0, 0));
                    p.Add((r, 0));
                    p.Add((r, fl));
                    p.Add((sd, fl));
                    p.Add((sd, len));
                    p.Add((0, len));
                    break;
            }
            return p;
        }
    }

    /// <summary>
    /// 刀具库：以刀号为键的刀具集合，支持増删改查、JSON 持久化、按序复制缺失刀位。
    /// </summary>
    public class ToolLibrary
    {
        private readonly SortedDictionary<int, ToolDefinition> _tools = new SortedDictionary<int, ToolDefinition>();

        public event Action Changed;

        public int Count => _tools.Count;
        public IEnumerable<ToolDefinition> Tools => _tools.Values;

        public bool Contains(int number) => _tools.ContainsKey(number);

        public ToolDefinition Get(int number) => _tools.TryGetValue(number, out var t) ? t : null;

        /// <summary>获取刀具，不存在时返回一个该刀号的默认刀具（不写入库）。</summary>
        public ToolDefinition GetOrCreate(int number)
        {
            if (_tools.TryGetValue(number, out var t)) return t;
            return new ToolDefinition { Number = number, Name = "T" + number };
        }

        public void Add(ToolDefinition tool)
        {
            if (tool == null) return;
            if (tool.Number <= 0) tool.Number = NextFreeNumber();
            tool.Normalize();
            _tools[tool.Number] = tool;
            Changed?.Invoke();
        }

        public bool Remove(int number)
        {
            bool ok = _tools.Remove(number);
            if (ok) Changed?.Invoke();
            return ok;
        }

        public void Clear()
        {
            _tools.Clear();
            Changed?.Invoke();
        }

        public int NextFreeNumber()
        {
            int n = 1;
            while (_tools.ContainsKey(n)) n++;
            return n;
        }

        /// <summary>刀号为空的孔位：返回库中没有的常用刀号。</summary>
        public List<int> MissingNumbers(IEnumerable<int> required)
        {
            var list = new List<int>();
            foreach (int n in required)
                if (!_tools.ContainsKey(n)) list.Add(n);
            return list;
        }

        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var t in _tools.Values)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{')
                  .Append("\"number\":").Append(t.Number).Append(',')
                  .Append("\"name\":\"").Append(Escape(t.Name)).Append("\",")
                  .Append("\"type\":\"").Append(t.Type).Append("\",")
                  .Append("\"diameter\":").Append(Num(t.Diameter)).Append(',')
                  .Append("\"cornerRadius\":").Append(Num(t.CornerRadius)).Append(',')
                  .Append("\"length\":").Append(Num(t.Length)).Append(',')
                  .Append("\"fluteLength\":").Append(Num(t.FluteLength)).Append(',')
                  .Append("\"flutes\":").Append(t.Flutes).Append(',')
                  .Append("\"stickOut\":").Append(Num(t.StickOut)).Append(',')
                  .Append("\"shankDiameter\":").Append(Num(t.ShankDiameter)).Append(',')
                  .Append("\"pointAngle\":").Append(Num(t.PointAngle)).Append(',')
                  .Append("\"pitch\":").Append(Num(t.Pitch)).Append(',')
                  .Append("\"helixAngle\":").Append(Num(t.HelixAngle)).Append(',')
                  .Append("\"material\":\"").Append(Escape(t.Material)).Append("\",")
                  .Append("\"wearOffset\":").Append(Num(t.WearOffset)).Append(',')
                  .Append("\"lengthOffset\":").Append(Num(t.LengthOffset)).Append(',')
                  .Append("\"radiusOffset\":").Append(Num(t.RadiusOffset))
                  .Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Num(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static string Escape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>内置常用刀具库，供快速起步。</summary>
        public static ToolLibrary CreateDefault()
        {
            var lib = new ToolLibrary();
            lib.Add(new ToolDefinition { Number = 1, Name = "T1 Flat 6mm", Type = ToolType.FlatEndMill, Diameter = 6, FluteLength = 20, Length = 70, Flutes = 2, Material = "Carbide" });
            lib.Add(new ToolDefinition { Number = 2, Name = "T2 Flat 10mm", Type = ToolType.FlatEndMill, Diameter = 10, FluteLength = 30, Length = 80, Flutes = 3, Material = "Carbide" });
            lib.Add(new ToolDefinition { Number = 3, Name = "T3 Ball 6mm", Type = ToolType.BallEndMill, Diameter = 6, FluteLength = 20, Length = 70, Flutes = 2, Material = "Carbide" });
            lib.Add(new ToolDefinition { Number = 4, Name = "T4 Drill 5mm", Type = ToolType.Drill, Diameter = 5, FluteLength = 40, Length = 90, PointAngle = 118, Flutes = 2, Material = "HSS" });
            lib.Add(new ToolDefinition { Number = 5, Name = "T5 Tap M6", Type = ToolType.Tap, Diameter = 6, Pitch = 1, FluteLength = 25, Length = 80, Flutes = 3 });
            lib.Add(new ToolDefinition { Number = 6, Name = "T6 Chamfer 10mm", Type = ToolType.ChamferMill, Diameter = 10, PointAngle = 90, FluteLength = 12, Length = 70, Flutes = 2 });
            return lib;
        }
    }
}
