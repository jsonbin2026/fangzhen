using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;

namespace CncSim.Core.Optimization
{
    public enum SuggestionSeverity
    {
        Info,
        Minor,
        Major
    }

    /// <summary>一条刀具路径优化建议（功能 70）。</summary>
    public class PathSuggestion
    {
        public SuggestionSeverity Severity;
        public string MessageKey;
        public object[] Args;
        /// <summary>相关行（可为空）。</summary>
        public List<int> Lines = new List<int>();
        /// <summary>预计可节省的时间（秒）。</summary>
        public double EstimatedSavingSeconds;

        public string Message => Localization.Loc.Format(MessageKey, Args);
    }

    /// <summary>
    /// 刀具路径优化建议：识别空行程、冗余快移、进给率不一致、下刀方式、重复轮廓等。
    /// </summary>
    public class PathOptimizer
    {
        private readonly MachineProfile _machine;

        public PathOptimizer(MachineProfile machine)
        {
            _machine = machine ?? MachineProfile.Create3Axis();
        }

        public List<PathSuggestion> Analyze(ParseResult result)
        {
            var list = new List<PathSuggestion>();
            DetectRedundantRapids(result, list);
            DetectFeedInconsistency(result, list);
            DetectDeepPlunges(result, list);
            DetectRapidAtCutHeight(result, list);
            DetectToolOrdering(result, list);
            DetectBackAndForthRapids(result, list);
            list.Sort((a, b) => b.EstimatedSavingSeconds.CompareTo(a.EstimatedSavingSeconds));
            return list;
        }

        private void DetectRedundantRapids(ParseResult result, List<PathSuggestion> list)
        {
            var s = new PathSuggestion { Severity = SuggestionSeverity.Minor, MessageKey = "opt.redundant_rapid" };
            double saving = 0;
            foreach (var seg in result.Segments)
            {
                if (seg.Type == MotionType.Rapid && seg.Length < 0.01)
                {
                    s.Lines.Add(seg.LineIndex + 1);
                    saving += seg.Duration;
                }
            }
            if (s.Lines.Count > 0)
            {
                s.EstimatedSavingSeconds = saving;
                list.Add(s);
            }
        }

        private void DetectFeedInconsistency(ParseResult result, List<PathSuggestion> list)
        {
            var s = new PathSuggestion { Severity = SuggestionSeverity.Info, MessageKey = "opt.feed_variation" };
            double min = double.MaxValue, max = double.MinValue;
            foreach (var seg in result.Segments)
                if (seg.IsCutting && seg.Feed > 0)
                {
                    min = Math.Min(min, seg.Feed);
                    max = Math.Max(max, seg.Feed);
                }
            if (max > 0 && min > 0 && (max / min) > 3)
            {
                s.Args = new object[] { min, max, max / min };
                list.Add(s);
            }
        }

        private void DetectDeepPlunges(ParseResult result, List<PathSuggestion> list)
        {
            var s = new PathSuggestion { Severity = SuggestionSeverity.Major, MessageKey = "opt.deep_plunge" };
            foreach (var seg in result.Segments)
            {
                if (!seg.IsCutting) continue;
                bool vertical = Math.Abs(seg.End.X - seg.Start.X) < 1e-6 && Math.Abs(seg.End.Y - seg.Start.Y) < 1e-6;
                double depth = Math.Abs(seg.End.Z - seg.Start.Z);
                if (vertical && depth > 20)
                {
                    s.Lines.Add(seg.LineIndex + 1);
                    s.EstimatedSavingSeconds += seg.Duration * 0.1;
                }
            }
            if (s.Lines.Count > 0) list.Add(s);
        }

        private void DetectRapidAtCutHeight(ParseResult result, List<PathSuggestion> list)
        {
            var s = new PathSuggestion { Severity = SuggestionSeverity.Major, MessageKey = "opt.rapid_low" };
            foreach (var seg in result.Segments)
            {
                if (seg.Type != MotionType.Rapid) continue;
                if (seg.Start.Linear.Z < 2 && seg.End.Linear.Z < 2 && seg.Length > 5)
                {
                    s.Lines.Add(seg.LineIndex + 1);
                    s.EstimatedSavingSeconds += seg.Duration * 0.05;
                }
            }
            if (s.Lines.Count > 0) list.Add(s);
        }

        private void DetectToolOrdering(ParseResult result, List<PathSuggestion> list)
        {
            var order = new List<int>();
            foreach (var seg in result.Segments)
                if (seg.ToolNumber > 0 && (order.Count == 0 || order[order.Count - 1] != seg.ToolNumber))
                    order.Add(seg.ToolNumber);
            // 统计不同刀号被重复切换（同一刀用后再次出现）
            var seen = new HashSet<int>();
            int repeats = 0;
            for (int i = 0; i < order.Count; i++)
            {
                if (!seen.Add(order[i])) repeats++;
            }
            if (repeats > 0)
            {
                var s = new PathSuggestion { Severity = SuggestionSeverity.Minor, MessageKey = "opt.tool_order" };
                s.Args = new object[] { repeats };
                s.EstimatedSavingSeconds = repeats * Simulation.TimeEstimator.ToolChangeSeconds * 0.5;
                list.Add(s);
            }
        }

        private void DetectBackAndForthRapids(ParseResult result, List<PathSuggestion> list)
        {
            // 快移 A->B 后立即 B->A 的往复
            double saving = 0;
            var lines = new List<int>();
            for (int i = 0; i + 1 < result.Segments.Count; i++)
            {
                var a = result.Segments[i];
                var b = result.Segments[i + 1];
                if (a.Type != MotionType.Rapid || b.Type != MotionType.Rapid) continue;
                if (a.Start.Linear.ApproximatelyEquals(b.End.Linear, 1e-3) &&
                    a.End.Linear.ApproximatelyEquals(b.Start.Linear, 1e-3))
                {
                    lines.Add(b.LineIndex + 1);
                    saving += a.Duration + b.Duration;
                }
            }
            if (lines.Count > 0)
                list.Add(new PathSuggestion
                {
                    Severity = SuggestionSeverity.Minor,
                    MessageKey = "opt.back_forth",
                    Lines = lines,
                    EstimatedSavingSeconds = saving
                });
        }
    }
}
