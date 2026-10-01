using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;

namespace CncSim.Core.Trajectory
{
    /// <summary>轨迹配色模式。</summary>
    public enum TrajectoryColorMode
    {
        /// <summary>按运动类型：快移与切削分色。</summary>
        ByMotionType,
        /// <summary>按进给率渐变。</summary>
        ByFeedRate,
        /// <summary>按刀具号分色。</summary>
        ByTool,
        /// <summary>单色。</summary>
        Single,
        /// <summary>按 Z 高度渐变。</summary>
        ByHeight
    }

    /// <summary>轨迹样式。</summary>
    public enum TrajectoryStyle
    {
        Lines,
        /// <summary>实体管道（刀路圆管）。</summary>
        Solid
    }

    [Serializable]
    public class TrajectorySettings
    {
        public TrajectoryStyle Style = TrajectoryStyle.Lines;
        public TrajectoryColorMode ColorMode = TrajectoryColorMode.ByMotionType;
        /// <summary>管道半径（mm）。</summary>
        public double TubeRadius = 0.3;
        public string CuttingColor = "#22CC55";
        public string RapidColor = "#FF4444";
        public string PlungeColor = "#FFAA22";
        public string ArcColor = "#33BBFF";
        public string SingleColor = "#EEEEEE";
        /// <summary>按 Z 高度渐变时的高低端颜色。</summary>
        public string LowColor = "#2244FF";
        public string HighColor = "#FF3322";
        /// <summary>显示快移。</summary>
        public bool ShowRapid = true;
        /// <summary>显示切削。</summary>
        public bool ShowCutting = true;
        /// <summary>每段管道侧面数。</summary>
        public int TubeSides = 6;

        public TrajectorySettings Clone() => (TrajectorySettings)MemberwiseClone();
    }

    /// <summary>
    /// 刀具轨迹生成器：把运动段转换为线框线段或实体管道网格。
    /// 输出为 MeshData，Unity 层可直接绑定到 MeshFilter/MeshRenderer。
    /// </summary>
    public static class ToolpathBuilder
    {
        /// <summary>生成线框轨迹（每段两个顶点，拓扑为 Lines）。</summary>
        public static MeshData BuildLines(IReadOnlyList<MotionSegment> segments, TrajectorySettings settings,
            out List<TrajectoryRange> ranges)
        {
            settings = settings ?? new TrajectorySettings();
            var m = new MeshData { IsLines = true };
            ranges = new List<TrajectoryRange>();
            var box = Aabb.Empty;
            foreach (var s in segments)
                if (s.IsMotion)
                {
                    box.Encapsulate(s.Start.Linear);
                    box.Encapsulate(s.End.Linear);
                }
            double zMin = box.IsValid ? box.Min.Z : 0;
            double zRange = box.IsValid ? Math.Max(1e-6, box.Size.Z) : 1;

            double feedMin = double.MaxValue, feedMax = double.MinValue;
            foreach (var s in segments)
                if (s.IsCutting)
                {
                    feedMin = Math.Min(feedMin, s.Feed);
                    feedMax = Math.Max(feedMax, s.Feed);
                }
            if (feedMin > feedMax) { feedMin = 0; feedMax = 1; }

            foreach (var s in segments)
            {
                if (!s.IsMotion) continue;
                if (s.Type == MotionType.Rapid && !settings.ShowRapid) continue;
                if (s.IsCutting && !settings.ShowCutting) continue;
                var color = ColorFor(s, settings, zMin, zRange, feedMin, feedMax);
                int startVertex = m.VertexCount;
                foreach (var p in s.Discretize(0.05))
                {
                    int i0 = m.AddVertex(p.Linear, Vec3d.UnitZ, color);
                    m.Vertices.Add(m.Vertices[i0]);
                    m.Normals.Add(m.Normals[i0]);
                    m.Colors.Add(color);
                }
                // 折线：相邻点两两成段
                int pairs = (m.VertexCount - startVertex) / 2;
                for (int i = 0; i < pairs - 1; i++)
                {
                    m.Indices.Add(startVertex + i * 2);
                    m.Indices.Add(startVertex + i * 2 + 2);
                }
                ranges.Add(new TrajectoryRange { SegmentIndex = s.Index, LineIndex = s.LineIndex, StartVertex = startVertex, VertexCount = m.VertexCount - startVertex });
            }
            return m;
        }

        /// <summary>生成实体管道轨迹。</summary>
        public static MeshData BuildSolid(IReadOnlyList<MotionSegment> segments, TrajectorySettings settings)
        {
            settings = settings ?? new TrajectorySettings();
            var m = new MeshData { IsLines = false };
            var box = Aabb.Empty;
            foreach (var s in segments)
                if (s.IsMotion)
                {
                    box.Encapsulate(s.Start.Linear);
                    box.Encapsulate(s.End.Linear);
                }
            double zMin = box.IsValid ? box.Min.Z : 0;
            double zRange = box.IsValid ? Math.Max(1e-6, box.Size.Z) : 1;
            double feedMin = double.MaxValue, feedMax = double.MinValue;
            foreach (var s in segments)
                if (s.IsCutting)
                {
                    feedMin = Math.Min(feedMin, s.Feed);
                    feedMax = Math.Max(feedMax, s.Feed);
                }
            if (feedMin > feedMax) { feedMin = 0; feedMax = 1; }

            int sides = Math.Max(3, settings.TubeSides);
            foreach (var s in segments)
            {
                if (!s.IsMotion) continue;
                if (s.Type == MotionType.Rapid && !settings.ShowRapid) continue;
                if (s.IsCutting && !settings.ShowCutting) continue;
                var color = ColorFor(s, settings, zMin, zRange, feedMin, feedMax);
                var pts = s.Discretize(0.2);
                for (int i = 0; i + 1 < pts.Count; i++)
                    MeshPrimitives.AddTube(m, pts[i].Linear, pts[i + 1].Linear, settings.TubeRadius, sides, color);
            }
            return m;
        }

        private static ColorRgba ColorFor(MotionSegment s, TrajectorySettings st, double zMin, double zRange, double feedMin, double feedMax)
        {
            switch (st.ColorMode)
            {
                case TrajectoryColorMode.Single:
                    return ColorRgba.FromHex(st.SingleColor);
                case TrajectoryColorMode.ByFeedRate:
                {
                    if (!s.IsCutting) return ColorRgba.FromHex(st.RapidColor);
                    double t = (s.Feed - feedMin) / Math.Max(1e-6, feedMax - feedMin);
                    return ColorRgba.Lerp(ColorRgba.FromHex(st.LowColor), ColorRgba.FromHex(st.HighColor), (float)t);
                }
                case TrajectoryColorMode.ByHeight:
                {
                    double t = (s.Start.Linear.Z - zMin) / zRange;
                    return ColorRgba.Lerp(ColorRgba.FromHex(st.LowColor), ColorRgba.FromHex(st.HighColor), (float)MathUtil.Clamp01(t));
                }
                case TrajectoryColorMode.ByTool:
                {
                    float hue = (s.ToolNumber * 0.37f) % 1f;
                    return FromHue(hue);
                }
                default:
                    if (s.Type == MotionType.Rapid) return ColorRgba.FromHex(st.RapidColor);
                    if (s.IsArc) return ColorRgba.FromHex(st.ArcColor);
                    if (Math.Abs(s.End.Z - s.Start.Z) > 0.1 && Math.Abs(s.End.X - s.Start.X) < 1e-6 && Math.Abs(s.End.Y - s.Start.Y) < 1e-6)
                        return ColorRgba.FromHex(st.PlungeColor);
                    return ColorRgba.FromHex(st.CuttingColor);
            }
        }

        private static ColorRgba FromHue(float h)
        {
            h = h - (float)Math.Floor(h);
            float s = 0.75f, v = 1.0f;
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            switch (i % 6)
            {
                case 0: return new ColorRgba(v, t, p);
                case 1: return new ColorRgba(q, v, p);
                case 2: return new ColorRgba(p, v, t);
                case 3: return new ColorRgba(p, q, v);
                case 4: return new ColorRgba(t, p, v);
                default: return new ColorRgba(v, p, q);
            }
        }
    }

    /// <summary>轨迹顶点范围与来源段的映射，用于按行高亮/裁剪。</summary>
    public struct TrajectoryRange
    {
        public int SegmentIndex;
        public int LineIndex;
        public int StartVertex;
        public int VertexCount;
    }
}
