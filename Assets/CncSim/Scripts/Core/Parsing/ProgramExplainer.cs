using System;
using System.Collections.Generic;
using System.Text;

namespace CncSim.Core.Parsing
{
    /// <summary>
    /// 单行解释器（功能 17）：用自然语言说明每个程序段的含义。
    /// </summary>
    public static class ProgramExplainer
    {
        public static string ExplainBlock(GCodeBlock block, Tooling.ToolLibrary tools, BlockExplanation exp)
        {
            if (block == null) return string.Empty;
            if (block.IsEmpty && string.IsNullOrEmpty(block.Comment)) return Localization.Loc.Get("explain.empty");

            var sb = new StringBuilder();
            MotionSegment motion = exp != null && exp.Motion != MotionType.Event
                ? new MotionSegment { Type = exp.Motion } : null;

            bool first = true;
            foreach (double g in block.GCodes)
            {
                string s = ExplainG(block, g);
                if (string.IsNullOrEmpty(s)) continue;
                if (!first) sb.Append("；");
                sb.Append(s);
                first = false;
            }
            foreach (int m in block.MCodes)
            {
                string s = ExplainM(block, m);
                if (string.IsNullOrEmpty(s)) continue;
                if (!first) sb.Append("；");
                sb.Append(s);
                first = false;
            }
            if (!string.IsNullOrEmpty(block.Comment))
            {
                if (!first) sb.Append("  ");
                sb.Append('(').Append(block.Comment.Trim()).Append(')');
            }
            if (first && string.IsNullOrEmpty(block.Comment)) return Localization.Loc.Get("explain.empty");
            return sb.ToString();
        }

        private static string ExplainG(GCodeBlock block, double g)
        {
            int gi = (int)Math.Round(g);
            switch (gi)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    return ExplainMotion(block, g);
                case 4:
                {
                    double sec = (block.Has('P') ? block.Get('P') / 1000.0 : 0) + (block.Has('X') ? block.Get('X') : 0);
                    return Localization.Loc.Format("explain.dwell", sec);
                }
                case 17:
                case 18:
                case 19:
                    break;
                case 80:
                    return Localization.Loc.Get("explain.cycle_cancel");
                case 54:
                case 55:
                case 56:
                case 57:
                case 58:
                case 59:
                    return Localization.Loc.Format("explain.work_offset", "G" + CodeInfo.FormatCode(g));
                case 20:
                    return Localization.Loc.Format("explain.units", Localization.Loc.Get("unit.inch"));
                case 21:
                    return Localization.Loc.Format("explain.units", Localization.Loc.Get("unit.mm"));
                case 43:
                    if (Math.Abs(g - 43.4) < 1e-6)
                        return Localization.Loc.Get("explain.rtcp_on");
                    break;
                default:
                    if (CncSim.Core.Parsing.GCodeParserStatic.IsCycle(g))
                        return Localization.Loc.Format("explain.cycle_enter", "G" + CodeInfo.FormatCode(g));
                    break;
            }
            if (CodeDatabase.TryGetG(g, out var info) && info.Kind != CodeKind.Motion)
                return info.Name;
            return null;
        }

        private static string ExplainMotion(GCodeBlock block, double g)
        {
            int gi = (int)Math.Round(g);
            string prefix = gi == 0 ? Localization.Loc.Get("explain.prefix_rapid")
                : gi == 1 ? Localization.Loc.Get("explain.prefix_linear")
                : gi == 2 ? Localization.Loc.Get("explain.prefix_arc_cw")
                : Localization.Loc.Get("explain.prefix_arc_ccw");
            var sb = new StringBuilder(prefix);
            bool any = false;
            foreach (char c in new[] { 'X', 'Y', 'Z', 'A', 'B', 'C' })
                if (block.Has(c))
                {
                    if (any) sb.Append(' ');
                    sb.Append(c).Append(block.Get(c).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    any = true;
                }
            if (!any && (block.Has('I') || block.Has('J') || block.Has('K')))
                any = true;
            sb.Append(block.StateAfter != null && block.StateAfter.Absolute
                ? Localization.Loc.Get("explain.at_absolute")
                : string.Empty);
            if (gi != 0 && block.Has('F'))
            {
                switch (block.StateAfter != null ? block.StateAfter.FeedMode : FeedMode.PerMinute)
                {
                    case FeedMode.PerRevolution:
                        sb.Append(Localization.Loc.Format("explain.feed_per_rev", block.Get('F')));
                        break;
                    default:
                        sb.Append(Localization.Loc.Format("explain.feed", block.Get('F')));
                        break;
                }
            }
            if ((gi == 2 || gi == 3) && (block.Has('I') || block.Has('J') || block.Has('K')))
            {
                double dx = block.Get('I'), dy = block.Get('J');
                double r = Math.Sqrt(dx * dx + dy * dy);
                if (r > 1e-9) sb.Append(Localization.Loc.Format("explain.radius", r));
            }
            else if ((gi == 2 || gi == 3) && block.Has('R'))
            {
                sb.Append(Localization.Loc.Format("explain.radius", Math.Abs(block.Get('R'))));
            }
            return sb.ToString();
        }

        private static string ExplainM(GCodeBlock block, int m)
        {
            switch (m)
            {
                case 3:
                    return Localization.Loc.Format("explain.spindle_on_cw", block.Has('S') ? block.Get('S') : 0);
                case 4:
                    return Localization.Loc.Format("explain.spindle_on_ccw", block.Has('S') ? block.Get('S') : 0);
                case 5:
                    return Localization.Loc.Get("explain.spindle_stop");
                case 6:
                    return Localization.Loc.Format("explain.tool_change", block.Has('T') ? (int)block.Get('T') : 0);
                case 7:
                case 8:
                    return Localization.Loc.Get("explain.coolant_on");
                case 9:
                    return Localization.Loc.Get("explain.coolant_off");
                case 0:
                    return Localization.Loc.Get("explain.program_stop");
                case 1:
                    return Localization.Loc.Get("explain.optional_stop");
                case 2:
                case 30:
                    return Localization.Loc.Get("explain.program_end");
                default:
                    return CodeDatabase.TryGetM(m, out var info) ? info.Name : null;
            }
        }
    }

    /// <summary>避免 ProgramExplainer 依赖 GCodeParser 的循环判断而暴露的静态辅助。</summary>
    public static class GCodeParserStatic
    {
        public static bool IsCycle(double g) =>
            Math.Abs(g - 73) < 1e-6 || Math.Abs(g - 81) < 1e-6 || Math.Abs(g - 82) < 1e-6 ||
            Math.Abs(g - 83) < 1e-6 || Math.Abs(g - 84) < 1e-6 || Math.Abs(g - 85) < 1e-6 ||
            Math.Abs(g - 86) < 1e-6 || Math.Abs(g - 89) < 1e-6;
    }
}
