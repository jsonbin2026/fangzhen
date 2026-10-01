using System;
using System.Collections.Generic;

namespace CncSim.Core.Parsing
{
    public enum CodeKind
    {
        Motion,
        CannedCycle,
        Plane,
        Distance,
        Units,
        FeedMode,
        WorkOffset,
        CutterComp,
        LengthComp,
        Spindle,
        Coolant,
        Stop,
        ProgramControl,
        Rtcp,
        Scaling,
        Rotation,
        Other
    }

    /// <summary>一条 G 或 M 指令的元数据。DescriptionKey 为 Loc 词条键。</summary>
    public class CodeInfo
    {
        public double Code;
        public char Letter; // 'G' 或 'M'
        public CodeKind Kind;
        public string NameKey;
        public string DescKey;
        /// <summary>是否为模态（持续生效）指令。</summary>
        public bool Modal;
        /// <summary>该指令是否有运动学含义（会打断或改变运动）。</summary>
        public bool IsMotion;
        /// <summary>分组号：同组指令在语义上互斥（如 G90/G91）。</summary>
        public int Group;

        public string CodeText => Letter + FormatCode(Code);

        public static string FormatCode(double code)
        {
            // G00/G01、G54、G54.1、G43.4 等
            if (Math.Abs(code - Math.Round(code)) < 1e-9)
            {
                int i = (int)Math.Round(code);
                return i < 10 ? i.ToString("00") : i.ToString();
            }
            return code.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public CodeInfo(double code, char letter, CodeKind kind, string nameKey, string descKey, bool modal, int group, bool isMotion = false)
        {
            Code = code;
            Letter = letter;
            Kind = kind;
            NameKey = nameKey;
            DescKey = descKey;
            Modal = modal;
            Group = group;
            IsMotion = isMotion;
        }

        public string Name => Localization.Loc.Get(NameKey);
        public string Description => Localization.Loc.Get(DescKey);
    }

    /// <summary>
    /// G/M 指令数据库。既用于解析校验（识别未知指令），也用于单行解释与速查手册（功能 17、68、69）。
    /// </summary>
    public static class CodeDatabase
    {
        private static readonly Dictionary<double, CodeInfo> G = new Dictionary<double, CodeInfo>();
        private static readonly Dictionary<int, CodeInfo> M = new Dictionary<int, CodeInfo>();
        private static readonly Dictionary<int, List<CodeInfo>> GByGroup = new Dictionary<int, List<CodeInfo>>();

        public static IEnumerable<CodeInfo> AllG => G.Values;
        public static IEnumerable<CodeInfo> AllM => M.Values;

        static CodeDatabase()
        {
            // 运动 G00-G03
            AddG(0, CodeKind.Motion, "motion.rapid", "desc.g00", true, 1, true);
            AddG(1, CodeKind.Motion, "motion.linear", "desc.g01", true, 1, true);
            AddG(2, CodeKind.Motion, "motion.arc_cw", "desc.g02", true, 1, true);
            AddG(3, CodeKind.Motion, "motion.arc_ccw", "desc.g03", true, 1, true);
            AddG(4, CodeKind.Motion, "motion.dwell", "desc.g04", true, 0, false);
            AddG(10, CodeKind.Other, "g.canned_setup", "desc.g10", false, 0);
            AddG(17, CodeKind.Plane, "plane.xy", "desc.g17", true, 2);
            AddG(18, CodeKind.Plane, "plane.zx", "desc.g18", true, 2);
            AddG(19, CodeKind.Plane, "plane.yz", "desc.g19", true, 2);
            AddG(20, CodeKind.Units, "units.inch", "desc.g20", true, 3);
            AddG(21, CodeKind.Units, "units.mm", "desc.g21", true, 3);
            AddG(28, CodeKind.Other, "g.return_home", "desc.g28", false, 0, true);
            AddG(30, CodeKind.Other, "g.return_home2", "desc.g30", false, 0, true);
            AddG(40, CodeKind.CutterComp, "comp.cancel", "desc.g40", true, 7);
            AddG(41, CodeKind.CutterComp, "comp.left", "desc.g41", true, 7);
            AddG(42, CodeKind.CutterComp, "comp.right", "desc.g42", true, 7);
            AddG(43, CodeKind.LengthComp, "lencomp.positive", "desc.g43", true, 8);
            AddG(43.4, CodeKind.Rtcp, "rtcp.on", "desc.g43_4", true, 9);
            AddG(49, CodeKind.LengthComp, "lencomp.cancel", "desc.g49", true, 8);
            AddG(50, CodeKind.Scaling, "scale.cancel", "desc.g50", true, 11);
            AddG(51, CodeKind.Scaling, "scale.on", "desc.g51", true, 11);
            AddG(52, CodeKind.WorkOffset, "offset.local", "desc.g52", false, 12);
            AddG(53, CodeKind.WorkOffset, "offset.machine", "desc.g53", false, 12);
            AddG(54, CodeKind.WorkOffset, "offset.g54", "desc.g54", true, 12);
            AddG(55, CodeKind.WorkOffset, "offset.g55", "desc.g55", true, 12);
            AddG(56, CodeKind.WorkOffset, "offset.g56", "desc.g56", true, 12);
            AddG(57, CodeKind.WorkOffset, "offset.g57", "desc.g57", true, 12);
            AddG(58, CodeKind.WorkOffset, "offset.g58", "desc.g58", true, 12);
            AddG(59, CodeKind.WorkOffset, "offset.g59", "desc.g59", true, 12);
            AddG(54.1, CodeKind.WorkOffset, "offset.g54_1", "desc.g54_1", true, 12);
            AddG(61, CodeKind.Other, "g.exact_stop", "desc.g61", true, 13);
            AddG(64, CodeKind.Other, "g.cont_path", "desc.g64", true, 13);
            AddG(68, CodeKind.Rotation, "rotate.on", "desc.g68", true, 14);
            AddG(69, CodeKind.Rotation, "rotate.cancel", "desc.g69", true, 14);
            AddG(73, CodeKind.CannedCycle, "cycle.chip_break", "desc.g73", true, 6, true);
            AddG(80, CodeKind.CannedCycle, "cycle.cancel", "desc.g80", true, 6);
            AddG(81, CodeKind.CannedCycle, "cycle.drill", "desc.g81", true, 6, true);
            AddG(82, CodeKind.CannedCycle, "cycle.drill_dwell", "desc.g82", true, 6, true);
            AddG(83, CodeKind.CannedCycle, "cycle.peck", "desc.g83", true, 6, true);
            AddG(84, CodeKind.CannedCycle, "cycle.tap", "desc.g84", true, 6, true);
            AddG(85, CodeKind.CannedCycle, "cycle.bore", "desc.g85", true, 6, true);
            AddG(86, CodeKind.CannedCycle, "cycle.bore_back", "desc.g86", true, 6, true);
            AddG(89, CodeKind.CannedCycle, "cycle.bore_dwell", "desc.g89", true, 6, true);
            AddG(90, CodeKind.Distance, "distance.absolute", "desc.g90", true, 4);
            AddG(91, CodeKind.Distance, "distance.incremental", "desc.g91", true, 4);
            AddG(90.1, CodeKind.Distance, "arc_center.absolute", "desc.g90_1", true, 5);
            AddG(91.1, CodeKind.Distance, "arc_center.incremental", "desc.g91_1", true, 5);
            AddG(93, CodeKind.FeedMode, "feed.inverse_time", "desc.g93", true, 10);
            AddG(94, CodeKind.FeedMode, "feed.per_minute", "desc.g94", true, 10);
            AddG(95, CodeKind.FeedMode, "feed.per_rev", "desc.g95", true, 10);
            AddG(96, CodeKind.Spindle, "spindle.rpm", "desc.g96", true, 18);
            AddG(97, CodeKind.Spindle, "spindle.css", "desc.g97", true, 18);
            AddG(98, CodeKind.CannedCycle, "cycle.return_initial", "desc.g98", true, 15);
            AddG(99, CodeKind.CannedCycle, "cycle.return_r", "desc.g99", true, 15);

            // 常见 M 指令
            AddM(0, CodeKind.ProgramControl, "program.stop", "desc.m00", false, false);
            AddM(1, CodeKind.ProgramControl, "program.optional_stop", "desc.m01", false, false);
            AddM(2, CodeKind.ProgramControl, "program.end", "desc.m02", false, false);
            AddM(3, CodeKind.Spindle, "spindle.cw", "desc.m03", false, true);
            AddM(4, CodeKind.Spindle, "spindle.ccw", "desc.m04", false, true);
            AddM(5, CodeKind.Spindle, "spindle.stop", "desc.m05", false, true);
            AddM(6, CodeKind.ProgramControl, "tool.change", "desc.m06", false, true);
            AddM(7, CodeKind.Coolant, "coolant.mist", "desc.m07", false, true);
            AddM(8, CodeKind.Coolant, "coolant.flood", "desc.m08", false, true);
            AddM(9, CodeKind.Coolant, "coolant.off", "desc.m09", false, true);
            AddM(30, CodeKind.ProgramControl, "program.end_rewind", "desc.m30", false, false);
            AddM(98, CodeKind.ProgramControl, "sub.call", "desc.m98", false, false);
            AddM(99, CodeKind.ProgramControl, "sub.return", "desc.m99", false, false);
        }

        private static void AddG(double code, CodeKind kind, string nameKey, string descKey, bool modal, int group, bool motion = false)
        {
            var info = new CodeInfo(code, 'G', kind, "code." + nameKey, descKey, modal, group, motion);
            G[code] = info;
            if (group > 0)
            {
                if (!GByGroup.TryGetValue(group, out var list))
                {
                    list = new List<CodeInfo>();
                    GByGroup[group] = list;
                }
                list.Add(info);
            }
        }

        private static void AddM(int code, CodeKind kind, string key, string descKey, bool modal, bool eventful)
        {
            M[code] = new CodeInfo(code, 'M', kind, "code." + key, descKey, modal, 0, eventful);
        }

        public static bool TryGetG(double code, out CodeInfo info)
        {
            if (G.TryGetValue(code, out info)) return true;
            // 容差匹配（浮点输入）
            foreach (var kv in G)
                if (Math.Abs(kv.Key - code) < 1e-6)
                {
                    info = kv.Value;
                    return true;
                }
            info = null;
            return false;
        }

        public static bool TryGetM(int code, out CodeInfo info) => M.TryGetValue(code, out info);

        public static bool IsKnownG(double code) => TryGetG(code, out _);
        public static bool IsKnownM(int code) => M.ContainsKey(code);

        /// <summary>同组内与指定代码互斥的其它指令。</summary>
        public static IReadOnlyList<CodeInfo> GetGroup(int group) =>
            GByGroup.TryGetValue(group, out var list) ? list : (IReadOnlyList<CodeInfo>)Array.Empty<CodeInfo>();

        public static int GroupOf(double gcode) => TryGetG(gcode, out var i) ? i.Group : 0;

        /// <summary>速查手册条目（按类别分组）。</summary>
        public static (string titleKey, List<CodeInfo> items)[] Manual()
        {
            var groups = new (string, List<CodeInfo>)[]
            {
                ("manual.category.motion", new List<CodeInfo>()),
                ("manual.category.plane_units", new List<CodeInfo>()),
                ("manual.category.compensation", new List<CodeInfo>()),
                ("manual.category.coordinates", new List<CodeInfo>()),
                ("manual.category.feed_spindle", new List<CodeInfo>()),
                ("manual.category.cycles", new List<CodeInfo>()),
                ("manual.category.other_g", new List<CodeInfo>()),
                ("manual.category.misc_m", new List<CodeInfo>())
            };
            foreach (var info in G.Values)
            {
                switch (info.Kind)
                {
                    case CodeKind.Motion: groups[0].Item2.Add(info); break;
                    case CodeKind.Plane:
                    case CodeKind.Units:
                    case CodeKind.Distance:
                        groups[1].Item2.Add(info); break;
                    case CodeKind.CutterComp:
                    case CodeKind.LengthComp:
                    case CodeKind.Rtcp:
                        groups[2].Item2.Add(info); break;
                    case CodeKind.WorkOffset:
                    case CodeKind.Scaling:
                    case CodeKind.Rotation:
                        groups[3].Item2.Add(info); break;
                    case CodeKind.FeedMode:
                    case CodeKind.Spindle:
                        groups[4].Item2.Add(info); break;
                    case CodeKind.CannedCycle: groups[5].Item2.Add(info); break;
                    default: groups[6].Item2.Add(info); break;
                }
            }
            foreach (var info in M.Values) groups[7].Item2.Add(info);
            foreach (var g in groups) g.Item2.Sort((a, b) => a.Code.CompareTo(b.Code));
            return groups;
        }
    }
}
