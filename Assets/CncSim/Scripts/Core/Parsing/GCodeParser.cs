using System;
using System.Collections.Generic;
using CncSim.Core.Tooling;

namespace CncSim.Core.Parsing
{
    /// <summary>
    /// 单行解析结果（供单行解释与逐行调试）。
    /// </summary>
    public class BlockExplanation
    {
        public int LineIndex;
        public string Summary = string.Empty;
        public readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();
        /// <summary>该行产生的运动段数量。</summary>
        public int SegmentCount;
        public MotionType Motion = MotionType.Event;
        /// <summary>执行后的刀尖位置（世界坐标）。</summary>
        public Vec3d EndPosition;
        public double Feed;
        public double SpindleSpeed;
        public int Tool;
    }

    /// <summary>
    /// G 代码解析器：把文本双向推进到 程序段 / 运动段 / 诊断。
    /// 支持绝对与增量编程、公制英制换算、工件坐标系偏置、G68 旋转、G51 缩放、
    /// 直线/圆弧/螺旋插补、固定循环展开、刀具半径补偿近似、RTCP（五轴刀尖跟随）。
    /// </summary>
    public class GCodeParser
    {
        private readonly MachineProfile _machine;
        private readonly ToolLibrary _tools;
        private readonly ParseOptions _options;

        public GCodeParser(MachineProfile machine = null, ToolLibrary tools = null, ParseOptions options = null)
        {
            _machine = machine ?? MachineProfile.Create3Axis();
            _tools = tools;
            _options = options ?? new ParseOptions();
        }

        /// <summary>解析整个程序。</summary>
        public ParseResult Parse(IReadOnlyList<string> lines, int version = -1)
        {
            var result = new ParseResult { SourceVersion = version };
            var modal = new ModalState();
            var cycle = new CannedCycleState();
            var blocks = new List<GCodeBlock>(lines.Count);

            for (int i = 0; i < lines.Count; i++)
            {
                var block = new GCodeBlock { LineIndex = i, RawText = lines[i] ?? string.Empty };
                blocks.Add(block);
                ParseBlock(block, modal, cycle, result);
            }

            // 交叉检查需要访问全部程序段
            PostValidate(result);
            return result;
        }

        /// <summary>仅解析单行（不推进全局模态），用于单行解释。state 为进入该行前的模态。</summary>
        public BlockExplanation ExplainLine(string line, int lineIndex, ModalState state, ParseOptions options = null)
        {
            var exp = new BlockExplanation { LineIndex = lineIndex };
            var result = new ParseResult();
            var modal = state != null ? state.Clone() : new ModalState();
            var cycle = new CannedCycleState();
            if (modal.CannedCycle != 0)
            {
                cycle.Code = modal.CannedCycle;
                cycle.R = modal.CannedR;
                cycle.Z = modal.CannedZ;
                cycle.Q = modal.CannedQ;
                cycle.P = modal.CannedP;
                cycle.ReturnToInitial = modal.CannedReturnToInitial;
                cycle.HasR = true;
            }
            var block = new GCodeBlock { LineIndex = lineIndex, RawText = line ?? string.Empty };
            int segStart = result.Segments.Count;
            ParseBlock(block, modal, cycle, result);
            exp.SegmentCount = result.Segments.Count - segStart;
            exp.Diagnostics.AddRange(result.Diagnostics);
            exp.EndPosition = modal.Position.Linear;
            exp.Feed = modal.Feed;
            exp.SpindleSpeed = modal.SpindleSpeed;
            exp.Tool = modal.CurrentTool;
            exp.Motion = block.FirstSegment >= 0 && block.SegmentCount > 0
                ? result.Segments[block.FirstSegment].Type
                : (block.HasG(4) ? MotionType.Dwell : MotionType.Event);
            exp.Summary = ProgramExplainer.ExplainBlock(block, _tools, exp);
            return exp;
        }

        private void ParseBlock(GCodeBlock block, ModalState modal, CannedCycleState cycle, ParseResult result)
        {
            var tokens = GCodeLexer.Tokenize(block.RawText);
            var gCodes = new List<double>();
            var mCodes = new List<int>();
            var words = block.Words;
            string comment = null;
            bool sawM02M30 = false;

            // 注释合并
            foreach (var t in tokens)
            {
                if (t.Type == TokenType.Comment)
                {
                    string c = t.Text;
                    if (c.StartsWith("(")) c = c.Substring(1, Math.Max(0, c.Length - 2));
                    else if (c.StartsWith(";")) c = c.Substring(1);
                    comment = comment == null ? c.Trim() : comment + " " + c.Trim();
                }
            }
            block.Comment = comment;

            // 1) 收集地址字与词
            foreach (var t in tokens)
            {
                switch (t.Type)
                {
                    case TokenType.Comment:
                    case TokenType.Whitespace:
                    case TokenType.Percent:
                        break;
                    case TokenType.BlockDelete:
                        block.BlockDelete = true;
                        break;
                    case TokenType.LineNumber:
                        if (t.Error == null) block.SequenceNumber = (int)Math.Round(t.Value);
                        else AddTokenError(result, block, t);
                        break;
                    case TokenType.ProgramNumber:
                        if (t.Error == null) words['O'] = t.Value;
                        else AddTokenError(result, block, t);
                        break;
                    case TokenType.GCode:
                        if (t.Error == null) gCodes.Add(t.Value);
                        else AddTokenError(result, block, t);
                        break;
                    case TokenType.MCode:
                        if (t.Error == null) mCodes.Add((int)Math.Round(t.Value));
                        else AddTokenError(result, block, t);
                        break;
                    case TokenType.AxisWord:
                    case TokenType.ArcWord:
                    case TokenType.ToolWord:
                    case TokenType.FeedWord:
                    case TokenType.SpeedWord:
                    case TokenType.ParameterWord:
                        if (t.Error == null)
                        {
                            if (words.ContainsKey(t.Letter))
                                result.Diagnostics.Warning(block.LineIndex, t.Start, t.Length, "diag.duplicate_same_line", t.Letter);
                            words[t.Letter] = t.Value;
                        }
                        else AddTokenError(result, block, t);
                        break;
                    case TokenType.Error:
                        if (t.Error == "UnexpectedCharacter" || t.Error == "UnknownAddress")
                            result.Diagnostics.Error(block.LineIndex, t.Start, t.Length, "diag.unexpected_char", t.Text);
                        else if (t.Error == "UnclosedComment")
                            result.Diagnostics.Error(block.LineIndex, t.Start, t.Length, "diag.unclosed_comment");
                        break;
                }
            }

            block.GCodes.AddRange(gCodes);
            block.MCodes.AddRange(mCodes);

            // 2) M 指令语义化（结果按顺序可产生事件段）
            ProcessMCodes(block, modal, mCodes, result, ref sawM02M30);

            if (sawM02M30)
            {
                block.StateAfter = modal.Clone();
                return;
            }

            // 3) G 指令分组，处理模态冲突与切换
            var gThisBlock = new List<double>();
            foreach (double g in gCodes)
            {
                if (!CodeDatabase.IsKnownG(g))
                {
                    result.Diagnostics.Warning(block.LineIndex, 0, 0, "diag.unknown_g", CodeInfo.FormatCode(g));
                    continue;
                }
                var info = CodeDatabase.TryGetG(g, out var ci) ? ci : null;
                if (info != null && info.Group > 0 && !gThisBlock.Exists(x => Math.Abs(x - g) < 1e-6) &&
                    gThisBlock.Exists(x => CodeDatabase.GroupOf(x) == info.Group))
                {
                    double other = gThisBlock.Find(x => CodeDatabase.GroupOf(x) == info.Group);
                    result.Diagnostics.Warning(block.LineIndex, 0, 0, "diag.modal_conflict",
                        "G" + CodeInfo.FormatCode(other), "G" + CodeInfo.FormatCode(g));
                }
                gThisBlock.Add(g);
            }

            // 固定循环是否在本段被显式取消/切换
            bool cycleCancelled = false;
            foreach (double g in gThisBlock)
            {
                if (Math.Abs(g - 80) < 1e-6)
                {
                    cycle.Code = 0;
                    modal.CannedCycle = 0;
                    cycleCancelled = true;
                }
                else if (IsCycleCode(g))
                {
                    cycle.Code = (int)Math.Round(g);
                    modal.CannedCycle = cycle.Code;
                }
            }

            // 4) 处理非 G80 的模态/状态类 G 指令
            bool motionHandled = false;
            double motionCode = -1;
            foreach (double g in gThisBlock)
            {
                if (IsCycleCode(g) || Math.Abs(g - 80) < 1e-6) continue;
                if (g == 0 || g == 1 || g == 2 || g == 3)
                {
                    motionCode = g;
                    motionHandled = true;
                }
            }

            foreach (double g in gThisBlock)
            {
                if (g == 0 || g == 1 || g == 2 || g == 3 || IsCycleCode(g) || Math.Abs(g - 80) < 1e-6) continue;
                ApplyModalG(block, modal, ref cycle, result, g);
            }

            // 5) 是否触发固定循环（有孔位坐标且处于循环状态）
            bool hasAxisWord = words.ContainsKey('X') || words.ContainsKey('Y') || words.ContainsKey('Z');
            if (cycle.Code != 0 && !cycleCancelled && hasAxisWord)
            {
                // 若本段有 G01/G02/G03，则视为普通运动而非循环
                bool hasLinearMotion = motionHandled && motionCode != 0;
                if (!hasLinearMotion)
                {
                    ExpandCannedCycle(block, modal, cycle, result);
                    block.StateAfter = modal.Clone();
                    return;
                }
            }

            // 6) 运动指令
            if (motionHandled)
            {
                switch ((int)Math.Round(motionCode))
                {
                    case 0: EmitMotion(block, modal, result, MotionType.Rapid, null); break;
                    case 1: EmitMotion(block, modal, result, MotionType.Linear, null); break;
                    case 2: EmitMotion(block, modal, result, MotionType.ArcCW, null); break;
                    case 3: EmitMotion(block, modal, result, MotionType.ArcCCW, null); break;
                }
            }
            else if (block.HasG(4))
            {
                EmitDwell(block, modal, result);
            }

            block.StateAfter = modal.Clone();
        }

        private static bool IsCycleCode(double g) => GCodeParserStatic.IsCycle(g);

        private void ProcessMCodes(GCodeBlock block, ModalState modal, List<int> mCodes, ParseResult result, ref bool sawEnd)
        {
            foreach (int m in mCodes)
            {
                if (!CodeDatabase.IsKnownM(m))
                    result.Diagnostics.Warning(block.LineIndex, 0, 0, "diag.unknown_m", m);

                switch (m)
                {
                    case 3:
                        modal.Spindle = SpindleState.CW;
                        PreserveS(block, modal);
                        AddEvent(result, block, modal);
                        break;
                    case 4:
                        modal.Spindle = SpindleState.CCW;
                        PreserveS(block, modal);
                        AddEvent(result, block, modal);
                        break;
                    case 5:
                        modal.Spindle = SpindleState.Off;
                        AddEvent(result, block, modal);
                        break;
                    case 6:
                    {
                        int tool = modal.PreparedTool;
                        if (block.Has('T')) tool = (int)Math.Round(block.Get('T'));
                        if (tool <= 0)
                            result.Diagnostics.Warning(block.LineIndex, 0, 0, "diag.tool_missing");
                        modal.CurrentTool = tool;
                        if (_tools != null && tool > 0 && !_tools.Contains(tool) && _machine.ToolLibraryIsStrict)
                            result.Diagnostics.Warning(block.LineIndex, 0, 0, "diag.tool_out_of_library", tool);
                        result.ToolsUsed.Add(tool);
                        AddEvent(result, block, modal, ProgramEventType.ToolChange);
                        break;
                    }
                    case 7:
                        modal.CoolantMist = true;
                        AddEvent(result, block, modal);
                        break;
                    case 8:
                        modal.CoolantFlood = true;
                        AddEvent(result, block, modal);
                        break;
                    case 9:
                        modal.CoolantMist = false;
                        modal.CoolantFlood = false;
                        AddEvent(result, block, modal);
                        break;
                    case 0:
                        AddEvent(result, block, modal, ProgramEventType.ProgramStop);
                        break;
                    case 1:
                        AddEvent(result, block, modal, ProgramEventType.OptionalStop);
                        break;
                    case 2:
                    case 30:
                        modal.Spindle = SpindleState.Off;
                        modal.CoolantFlood = false;
                        modal.CoolantMist = false;
                        AddEvent(result, block, modal, ProgramEventType.ProgramEnd);
                        sawEnd = true;
                        break;
                    case 98:
                        AddEvent(result, block, modal, ProgramEventType.ProgramStop);
                        break;
                    case 99:
                        AddEvent(result, block, modal);
                        break;
                }
            }
            // T 预选刀（未与 M06 同段）
            if (block.Has('T') && !block.HasM(6))
            {
                modal.PreparedTool = (int)Math.Round(block.Get('T'));
            }
        }

        private static void PreserveS(GCodeBlock block, ModalState modal)
        {
            if (block.Has('S')) modal.SpindleSpeed = block.Get('S');
        }

        private void AddEvent(ParseResult result, GCodeBlock block, ModalState modal, ProgramEventType type = ProgramEventType.None)
        {
            EnsureSegmentStart(block, result);
            var seg = new MotionSegment
            {
                Index = result.Segments.Count,
                LineIndex = block.LineIndex,
                Type = MotionType.Event,
                Start = modal.Position,
                End = modal.Position,
                Feed = modal.Feed,
                SpindleSpeed = modal.SpindleSpeed,
                Spindle = modal.Spindle,
                Coolant = modal.CoolantFlood || modal.CoolantMist,
                ToolNumber = modal.CurrentTool,
                Rtcp = modal.Rtcp,
                Event = type
            };
            AddSegment(block, result, seg);
        }

        private void ApplyModalG(GCodeBlock block, ModalState modal, ref CannedCycleState cycle, ParseResult result,
            double g)
        {
            if (Math.Abs(g - 4) < 1e-6) return; // dwell 单独处理
            switch ((int)Math.Round(g))
            {
                case 17: modal.Plane = Plane.XY; break;
                case 18: modal.Plane = Plane.ZX; break;
                case 19: modal.Plane = Plane.YZ; break;
                case 20: modal.Metric = false; break;
                case 21: modal.Metric = true; break;
                case 40: modal.CutterComp = CutterCompMode.Off; break;
                case 41: modal.CutterComp = CutterCompMode.Left; modal.CutterCompD = (int)block.Get('D'); break;
                case 42: modal.CutterComp = CutterCompMode.Right; modal.CutterCompD = (int)block.Get('D'); break;
                case 43: modal.LengthCompActive = true; modal.LengthCompH = (int)block.Get('H'); break;
                case 49: modal.LengthCompActive = false; break;
                case 50: modal.Scale = 1.0; break;
                case 51: modal.Scale = block.Has('P') ? block.Get('P') : 1.0; break;
                case 52:
                    // 局部坐标系：作为叠加偏置记入 RotationCenter 之外的附加量，这里直接忽略几何效果
                    break;
                case 53:
                    // G53：本段坐标按机床坐标系解释，仅处理一次
                    break;
                case 54: modal.WorkOffsetIndex = 0; break;
                case 55: modal.WorkOffsetIndex = 1; break;
                case 56: modal.WorkOffsetIndex = 2; break;
                case 57: modal.WorkOffsetIndex = 3; break;
                case 58: modal.WorkOffsetIndex = 4; break;
                case 59: modal.WorkOffsetIndex = 5; break;
                case 61:
                case 64:
                    break;
                case 68:
                    modal.RotationDeg = block.Get('R');
                {
                    if (block.Has('X') || block.Has('Y'))
                        modal.RotationCenter = new Vec3d(block.Get('X'), block.Get('Y'), 0);
                    break;
                }
                case 69:
                    modal.RotationDeg = 0;
                    break;
                case 90: modal.Absolute = true; break;
                case 91: modal.Absolute = false; break;
                case 93: modal.FeedMode = FeedMode.InverseTime; break;
                case 94: modal.FeedMode = FeedMode.PerMinute; break;
                case 95: modal.FeedMode = FeedMode.PerRevolution; break;
                case 98: modal.CannedReturnToInitial = true; cycle.ReturnToInitial = true; break;
                case 99: modal.CannedReturnToInitial = false; cycle.ReturnToInitial = false; break;
                default:
                    if (Math.Abs(g - 54.1) < 1e-6)
                    {
                        int p = block.Has('P') ? (int)Math.Round(block.Get('P')) : 1;
                        modal.WorkOffsetIndex = 5 + p;
                    }
                    else if (Math.Abs(g - 90.1) < 1e-6)
                    {
                        modal.ArcAbsolute = true;
                    }
                    else if (Math.Abs(g - 91.1) < 1e-6)
                    {
                        modal.ArcAbsolute = false;
                    }
                    else if (Math.Abs(g - 43.4) < 1e-6)
                    {
                        modal.Rtcp = true;
                    }
                    break;
            }
        }

        private void EmitDwell(GCodeBlock block, ModalState modal, ParseResult result)
        {
            double seconds = 0;
            if (block.Has('P')) seconds += block.Get('P') / 1000.0; // Fanuc 以毫秒计
            if (block.Has('X')) seconds += block.Get('X');          // X 以秒计
            EnsureSegmentStart(block, result);
            var seg = new MotionSegment
            {
                Index = result.Segments.Count,
                LineIndex = block.LineIndex,
                Type = MotionType.Dwell,
                Start = modal.Position,
                End = modal.Position,
                DwellSeconds = seconds,
                Feed = modal.Feed,
                SpindleSpeed = modal.SpindleSpeed,
                Spindle = modal.Spindle,
                ToolNumber = modal.CurrentTool
            };
            AddSegment(block, result, seg);
        }

        private void EmitMotion(GCodeBlock block, ModalState modal, ParseResult result, MotionType type, CannedCycleState cycle)
        {
            if (block.Has('F'))
            {
                double f = block.Get('F');
                modal.Feed = ConvertFeed(block, modal, f);
            }
            double unitScale = modal.Metric ? 1.0 : 25.4;
            bool absolute = modal.Absolute;
            bool isG53 = block.HasG(53);

            AxisVector target = modal.Position;
            var requested = new Dictionary<int, double>();
            foreach (char letter in new[] { 'X', 'Y', 'Z', 'A', 'B', 'C' })
            {
                if (!block.Has(letter)) continue;
                int axis = AxisVector.AxisIndex(letter);
                double value = block.Get(letter);
                requested[axis] = value;
                if (axis >= 3)
                {
                    // 旋转轴不受工件坐标系与 G68 影响
                    target[axis] = absolute ? value : modal.Position[axis] + value;
                    continue;
                }
                if (absolute)
                {
                    // 编程值 -> 工件坐标系下的点
                    var wcs = _machine.GetWorkOffset(modal.WorkOffsetIndex);
                    Vec3d local = new Vec3d(
                        axis == 0 ? value * unitScale : (modal.Position.X - wcs.X),
                        axis == 1 ? value * unitScale : (modal.Position.Y - wcs.Y),
                        axis == 2 ? value * unitScale : (modal.Position.Z - wcs.Z));
                    Vec3d machine = isG53 ? new Vec3d(
                        axis == 0 ? value * unitScale : modal.Position.X,
                        axis == 1 ? value * unitScale : modal.Position.Y,
                        axis == 2 ? value * unitScale : modal.Position.Z) : LocalToMachine(modal, local, wcs);
                    target[axis] = machine[axis];
                }
                else
                {
                    target[axis] = modal.Position[axis] + value * unitScale;
                }
            }

            bool hasLinear = requested.ContainsKey(0) || requested.ContainsKey(1) || requested.ContainsKey(2);
            bool hasRotary = requested.ContainsKey(3) || requested.ContainsKey(4) || requested.ContainsKey(5);

            if (type == MotionType.ArcCW || type == MotionType.ArcCCW)
            {
                EmitArc(block, modal, result, type, target, requested);
                return;
            }

            // 直线 / 快速
            if (!hasLinear && !hasRotary)
            {
                // 只给了 F/S 等，无实际运动
                return;
            }
            if (target.ApproximatelyEquals(modal.Position))
                result.Diagnostics.Info(block.LineIndex, 0, 0, "diag.zero_position_move");

            EnsureSegmentStart(block, result);
            var seg = BuildSegment(block, modal, type, target, result);
            AddSegment(block, result, seg);
            modal.Position = target;
        }

        private void EmitArc(GCodeBlock block, ModalState modal, ParseResult result, MotionType type, AxisVector target,
            Dictionary<int, double> requested)
        {
            MotionSegment.PlaneAxes(modal.Plane, out int a0, out int a1, out int normal);
            char l0 = AxisVector.AxisLetters[a0], l1 = AxisVector.AxisLetters[a1];
            if (!block.Has(l0) && !block.Has(l1))
                result.Diagnostics.Error(block.LineIndex, 0, 0, "diag.arc_missing_axis", l0.ToString(), l1.ToString());

            double unitScale = modal.Metric ? 1.0 : 25.4;
            Vec3d start = modal.Position.Linear;
            Vec3d end = target.Linear;

            Vec3d center = Vec3d.Zero;
            bool hasR = block.Has('R');
            bool hasIjk = block.Has('I') || block.Has('J') || block.Has('K');
            char[] ijkLetters = { 'I', 'J', 'K' };
            double ijkTol = 1e-6;

            if (hasIjk)
            {
                double[] offsets = new double[3];
                for (int k = 0; k < 3; k++)
                {
                    if (block.Has(ijkLetters[k]))
                    {
                        double v = block.Get(ijkLetters[k]) * unitScale;
                        offsets[k] = modal.ArcAbsolute ? v - start[k] : v;
                    }
                }
                center = new Vec3d(start.X + offsets[0], start.Y + offsets[1], start.Z + offsets[2]);
            }
            else if (hasR)
            {
                double r = block.Get('R') * unitScale;
                if (Math.Abs(r) < ijkTol)
                {
                    result.Diagnostics.Error(block.LineIndex, 0, 0, "diag.arc_radius_invalid");
                    center = start;
                }
                else
                {
                    double ax = start[a0], ay = start[a1], bx = end[a0], by = end[a1];
                    double dx = bx - ax, dy = by - ay;
                    double chord = Math.Sqrt(dx * dx + dy * dy);
                    if (chord < ijkTol)
                    {
                        result.Diagnostics.Error(block.LineIndex, 0, 0, "diag.arc_no_endpoint");
                        center = start;
                    }
                    else if (chord > 2 * Math.Abs(r) + 1e-6)
                    {
                        result.Diagnostics.Error(block.LineIndex, 0, 0, "diag.arc_radius_invalid");
                        center = start;
                    }
                    else
                    {
                        double h = Math.Sqrt(Math.Max(0, r * r - (chord / 2) * (chord / 2)));
                        double mx = (ax + bx) / 2, my = (ay + by) / 2;
                        double ux = -dy / chord, uy = dx / chord;
                        // 正 R 为小弧（≤180°），负 R 为大弧。先用小弧侧算中心，
                        // 再检查扫掠角与指令方向是否一致，不一致则翻到另一侧。
                        center[a0] = mx + ux * h;
                        center[a1] = my + uy * h;
                        if (!ArcDirectionMatches(start, end, center, a0, a1, type == MotionType.ArcCCW))
                        {
                            center[a0] = mx - ux * h;
                            center[a1] = my - uy * h;
                        }
                        if (Math.Abs(r) > 0 && ChordIsMajor(ax, ay, bx, by, center[a0], center[a1], r))
                        {
                            // 数据与 |R| 指定的弧大小不符，仅提示
                            result.Diagnostics.Info(block.LineIndex, 0, 0, "diag.arc_radius_mismatch", 0);
                        }
                    }
                }
            }
            else
            {
                result.Diagnostics.Error(block.LineIndex, 0, 0, "diag.arc_no_params");
                center = start;
            }

            if (!modal.ArcAbsolute && hasIjk)
            {
                // IJK 增量时第三分量（法向）本应为 0，这里保持起点
                center[normal] = start[normal];
            }

            // 校验半径一致性
            double rStart = Math.Sqrt(Sq(start[a0] - center[a0]) + Sq(start[a1] - center[a1]));
            if (hasR && rStart > 1e-6)
            {
                double rEnd = Math.Sqrt(Sq(end[a0] - center[a0]) + Sq(end[a1] - center[a1]));
                if (Math.Abs(rStart - rEnd) > Math.Max(0.05, rStart * 0.01))
                    result.Diagnostics.Warning(block.LineIndex, 0, 0, "diag.arc_radius_mismatch", Math.Abs(rStart - rEnd));
            }

            EnsureSegmentStart(block, result);
            var seg = BuildSegment(block, modal, type, target, result);
            seg.Center = center;
            seg.Plane = modal.Plane;
            if (block.Has('L')) seg.ExtraTurns = Math.Max(0, (int)Math.Round(block.Get('L')) - 1);
            AddSegment(block, result, seg);
            modal.Position = target;
        }

        private MotionSegment BuildSegment(GCodeBlock block, ModalState modal, MotionType type, AxisVector target, ParseResult result)
        {
            return new MotionSegment
            {
                Index = result.Segments.Count,
                LineIndex = block.LineIndex,
                Type = type,
                Start = modal.Position,
                End = target,
                Feed = type == MotionType.Rapid ? _machine.RapidFeed : modal.Feed,
                SpindleSpeed = modal.SpindleSpeed,
                Spindle = modal.Spindle,
                Coolant = modal.CoolantFlood || modal.CoolantMist,
                ToolNumber = modal.CurrentTool,
                Rtcp = modal.Rtcp
            };
        }

        private void EnsureSegmentStart(GCodeBlock block, ParseResult result)
        {
            if (block.FirstSegment < 0) block.FirstSegment = result.Segments.Count;
        }

        private void AddSegment(GCodeBlock block, ParseResult result, MotionSegment seg)
        {
            EnsureSegmentStart(block, result);
            result.Segments.Add(seg);
            block.SegmentCount = result.Segments.Count - block.FirstSegment;
        }

        private static bool ArcDirectionMatches(Vec3d start, Vec3d end, Vec3d center, int a0, int a1, bool ccw)
        {
            double sa = Math.Atan2(start[a1] - center[a1], start[a0] - center[a0]);
            double ea = Math.Atan2(end[a1] - center[a1], end[a0] - center[a0]);
            double sweep = ccw ? ea - sa : sa - ea;
            if (sweep <= 1e-9) sweep += 2 * Math.PI;
            // 小于 180° 的弧与所选中心一致
            return sweep <= Math.PI + 1e-6;
        }

        private static bool ChordIsMajor(double ax, double ay, double bx, double by, double cx, double cy, double r)
        {
            double sweepStart = Math.Atan2(ay - cy, ax - cx);
            double sweepEnd = Math.Atan2(by - cy, bx - cx);
            double d = Math.Abs(sweepEnd - sweepStart);
            if (d > Math.PI) d = 2 * Math.PI - d;
            return d > Math.PI / 2 + 1e-6 == (r < 0 ? true : false);
        }

        private double ConvertFeed(GCodeBlock block, ModalState modal, double f)
        {
            if (!modal.Metric) f *= 25.4;
            switch (modal.FeedMode)
            {
                case FeedMode.PerRevolution:
                    double rpm = modal.SpindleSpeed > 0 ? modal.SpindleSpeed : _options.DefaultSpindleSpeed;
                    return f * rpm;
                case FeedMode.InverseTime:
                    return f > 0 ? _options.DefaultInverseTimeLength * f : 0;
                default:
                    return f;
            }
        }

        private static double Sq(double v) => v * v;

        /// <summary>把工件坐标系下的点映射为机床坐标，考虑 G52 局部偏置与 G68 旋转。</summary>
        private Vec3d LocalToMachine(ModalState modal, Vec3d local, Vec3d wcs)
        {
            Vec3d p = local;
            if (Math.Abs(modal.RotationDeg) > 1e-9)
            {
                // 旋转中心位于工件坐标系内
                Vec3d c = new Vec3d(modal.RotationCenter.X, modal.RotationCenter.Y, 0);
                double a = modal.RotationDeg * MathUtil.Deg2Rad;
                double dx = p.X - c.X, dy = p.Y - c.Y;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                p = new Vec3d(c.X + dx * ca - dy * sa, c.Y + dx * sa + dy * ca, p.Z);
            }
            if (Math.Abs(modal.Scale - 1.0) > 1e-9)
                p = p * modal.Scale;
            return new Vec3d(p.X + wcs.X, p.Y + wcs.Y, p.Z + wcs.Z);
        }

        /// <summary>把机床坐标反算为工件坐标系下的点（用于单行解释显示工件坐标）。</summary>
        private Vec3d MachineToLocal(ModalState modal, Vec3d machine, Vec3d wcs)
        {
            Vec3d p = machine;
            p = new Vec3d(p.X - wcs.X, p.Y - wcs.Y, p.Z - wcs.Z);
            if (Math.Abs(modal.Scale - 1.0) > 1e-9)
                p = p / modal.Scale;
            if (Math.Abs(modal.RotationDeg) > 1e-9)
            {
                Vec3d c = new Vec3d(modal.RotationCenter.X, modal.RotationCenter.Y, 0);
                double a = -modal.RotationDeg * MathUtil.Deg2Rad;
                double dx = p.X - c.X, dy = p.Y - c.Y;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                p = new Vec3d(c.X + dx * ca - dy * sa, c.Y + dx * sa + dy * ca, p.Z);
            }
            return p;
        }

        private void AddTokenError(ParseResult result, GCodeBlock block, Token t)
        {
            switch (t.Error)
            {
                case "MissingValue":
                    result.Diagnostics.Error(block.LineIndex, t.Start, t.Length, "diag.missing_value", t.Letter);
                    break;
                case "InvalidNumber":
                    result.Diagnostics.Error(block.LineIndex, t.Start, t.Length, "diag.invalid_number", t.NumberText);
                    break;
                default:
                    result.Diagnostics.Error(block.LineIndex, t.Start, t.Length, "diag.unexpected_char", t.Text);
                    break;
            }
        }

        private void ExpandCannedCycle(GCodeBlock block, ModalState modal, CannedCycleState cycle, ParseResult result)
        {
            // 更新循环参数
            if (block.Has('R')) { cycle.R = block.Get('R'); cycle.HasR = true; }
            if (block.Has('Q')) { cycle.Q = block.Get('Q'); cycle.HasQ = true; }
            if (block.Has('P')) { cycle.P = block.Get('P'); cycle.HasP = true; }
            if (block.Has('Z')) cycle.Z = block.Get('Z');
            if (cycle.Code == 0 || !cycle.HasR)
            {
                if (!cycle.HasR)
                    result.Diagnostics.Error(block.LineIndex, 0, 0, "diag.cycle.cancel");
                return;
            }

            double unitScale = modal.Metric ? 1.0 : 25.4;
            bool absolute = modal.Absolute;
            var wcs = _machine.GetWorkOffset(modal.WorkOffsetIndex);

            // 应用本段进给率（固定循环常在同一段给出 F）
            if (block.Has('F'))
                modal.Feed = ConvertFeed(block, modal, block.Get('F'));

            // 计算 XY 孔位与深度（在工件坐标系中的编程值）
            double px = modal.Position.X - wcs.X;
            double py = modal.Position.Y - wcs.Y;
            if (block.Has('X')) px = absolute ? block.Get('X') * unitScale : px + block.Get('X') * unitScale;
            if (block.Has('Y')) py = absolute ? block.Get('Y') * unitScale : py + block.Get('Y') * unitScale;
            double rPlane = absolute ? cycle.R * unitScale : modal.Position.Z + cycle.R * unitScale;
            double depth = absolute ? cycle.Z * unitScale : modal.Position.Z + cycle.Z * unitScale;

            double initialZ = modal.Position.Z;
            double topOfStock = initialZ;

            // 1) 快速移动到孔位上方（初始平面）
            var holeMachine = LocalToMachine(modal, new Vec3d(px, py, 0), wcs);
            double holeX = holeMachine.X;
            double holeY = holeMachine.Y;
            AddCycleMove(block, modal, result, MotionType.Rapid, new AxisVector(holeX, holeY, initialZ));
            modal.Position = new AxisVector(holeX, holeY, initialZ);

            // 2) 快速下到 R 平面
            AddCycleMove(block, modal, result, MotionType.Rapid, new AxisVector(holeX, holeY, rPlane));
            modal.Position = new AxisVector(holeX, holeY, rPlane);

            // 3) 按循环类型切削到深度
            double feed = modal.Feed;
            switch (cycle.Code)
            {
                case 81:
                case 82:
                case 85:
                case 86:
                case 89:
                    AddCycleMove(block, modal, result, MotionType.Linear, new AxisVector(holeX, holeY, depth), feed, true);
                    modal.Position = new AxisVector(holeX, holeY, depth);
                    if (cycle.Code == 82 || cycle.Code == 89)
                        AddCycleDwell(block, modal, result, Math.Max(0.05, cycle.P / 1000.0 > 0 ? cycle.P / 1000.0 : 0.2));
                    break;
                case 73:
                case 83:
                {
                    double q = cycle.HasQ && cycle.Q > 0 ? cycle.Q * unitScale : Math.Max(1, Math.Abs(rPlane - depth));
                    bool chipBreak = cycle.Code == 73;
                    double z = rPlane;
                    int guard = 0;
                    while (z > depth + 1e-6 && guard++ < 10000)
                    {
                        double next = Math.Max(depth, z - q);
                        AddCycleMove(block, modal, result, MotionType.Linear, new AxisVector(holeX, holeY, next), feed, true);
                        modal.Position = new AxisVector(holeX, holeY, next);
                        // G83 每级完全退回 R 平面排屑；G73 只退回一小段断屑
                        double retractZ = chipBreak ? Math.Min(rPlane, next + q * 0.5) : rPlane;
                        AddCycleMove(block, modal, result, MotionType.Rapid, new AxisVector(holeX, holeY, retractZ));
                        modal.Position = new AxisVector(holeX, holeY, retractZ);
                        z = next;
                    }
                    break;
                }
                case 84:
                {
                    double pitch = cycle.HasQ && cycle.Q > 0 ? cycle.Q * unitScale : 1.0;
                    AddCycleMove(block, modal, result, MotionType.Linear, new AxisVector(holeX, holeY, depth), feed, true);
                    modal.Position = new AxisVector(holeX, holeY, depth);
                    // 主轴反转并退回
                    var reversed = modal.Clone();
                    reversed.Spindle = modal.Spindle == SpindleState.CW ? SpindleState.CCW : SpindleState.CW;
                    AddCycleEvent(block, modal, result);
                    AddCycleMove(block, modal, result, MotionType.Linear, new AxisVector(holeX, holeY, rPlane), feed);
                    modal.Position = new AxisVector(holeX, holeY, rPlane);
                    break;
                }
                default:
                    AddCycleMove(block, modal, result, MotionType.Linear, new AxisVector(holeX, holeY, depth), feed, true);
                    modal.Position = new AxisVector(holeX, holeY, depth);
                    break;
            }

            // 4) 返回平面
            double returnZ = cycle.ReturnToInitial ? initialZ : rPlane;
            AddCycleMove(block, modal, result, MotionType.Rapid, new AxisVector(holeX, holeY, returnZ));
            modal.Position = new AxisVector(holeX, holeY, returnZ);
        }

        private void AddCycleMove(GCodeBlock block, ModalState modal, ParseResult result, MotionType type, AxisVector target,
            double feed = 0, bool cutting = false)
        {
            var seg = new MotionSegment
            {
                Index = result.Segments.Count,
                LineIndex = block.LineIndex,
                Type = type,
                Start = modal.Position,
                End = target,
                Feed = type == MotionType.Rapid ? _machine.RapidFeed : (feed > 0 ? feed : modal.Feed),
                SpindleSpeed = modal.SpindleSpeed,
                Spindle = modal.Spindle,
                Coolant = modal.CoolantFlood || modal.CoolantMist,
                ToolNumber = modal.CurrentTool,
                FromCannedCycle = true,
                Rtcp = modal.Rtcp
            };
            AddSegment(block, result, seg);
        }

        private void AddCycleDwell(GCodeBlock block, ModalState modal, ParseResult result, double seconds)
        {
            EnsureSegmentStart(block, result);
            AddSegment(block, result, new MotionSegment
            {
                Index = result.Segments.Count,
                LineIndex = block.LineIndex,
                Type = MotionType.Dwell,
                Start = modal.Position,
                End = modal.Position,
                DwellSeconds = seconds,
                Feed = modal.Feed,
                Spindle = modal.Spindle,
                ToolNumber = modal.CurrentTool,
                FromCannedCycle = true
            });
        }

        private void AddCycleEvent(GCodeBlock block, ModalState modal, ParseResult result)
        {
            AddEvent(result, block, modal);
        }

        /// <summary>解析后的交叉校验：程序号、结束指令等。</summary>
        private void PostValidate(ParseResult result)
        {
            bool hasProgramNumber = false;
            bool hasEnd = false;
            foreach (var b in result.Blocks)
            {
                if (b.Words.ContainsKey('O')) hasProgramNumber = true;
                foreach (int m in b.MCodes)
                    if (m == 2 || m == 30) hasEnd = true;
            }
            if (result.Blocks.Count > 0 && !hasProgramNumber)
                result.Diagnostics.Info(result.Blocks[0].LineIndex, 0, 0, "diag.no_program_number");
            if (result.Blocks.Count > 0 && !hasEnd)
                result.Diagnostics.Info(result.Blocks[result.Blocks.Count - 1].LineIndex, 0, 0, "diag.no_program_end");
        }
    }
}
