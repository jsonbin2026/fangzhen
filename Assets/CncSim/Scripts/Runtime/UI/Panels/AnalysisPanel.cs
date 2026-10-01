using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Simulation;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>分析面板（功能 56-70）：加工时间、碰撞、过切/欠切、路径优化、推荐切削参数与刀具寿命。</summary>
    public class AnalysisPanel : CncUiPanel
    {
        private TextMeshProUGUI _timeText, _collisionText, _machiningText, _optimizeText, _paramText;

        protected override void Build()
        {
            Row(t => Btn(t, "ui.analysis.run", RunAll, true, 30f), 32f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.analysis.time");
            _timeText = UIFactory.Label(Root, "", Theme, 12f, true);
            _timeText.enableWordWrapping = true;
            UIFactory.Size(_timeText.gameObject, minH: 62f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.analysis.collision");
            _collisionText = UIFactory.Label(Root, "", Theme, 12f, true);
            _collisionText.enableWordWrapping = true;
            UIFactory.Size(_collisionText.gameObject, minH: 44f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.analysis.machining");
            _machiningText = UIFactory.Label(Root, "", Theme, 12f, true);
            _machiningText.enableWordWrapping = true;
            UIFactory.Size(_machiningText.gameObject, minH: 44f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.analysis.optimize");
            _optimizeText = UIFactory.Label(Root, "", Theme, 12f, true);
            _optimizeText.enableWordWrapping = true;
            UIFactory.Size(_optimizeText.gameObject, minH: 60f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.analysis.params");
            _paramText = UIFactory.Label(Root, "", Theme, 12f, true);
            _paramText.enableWordWrapping = true;
            UIFactory.Size(_paramText.gameObject, minH: 70f);

            RunAll();
        }

        private static string Fmt(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m {ts.Seconds:00}s"
                : $"{ts.Minutes:00}m {ts.Seconds:00}s";
        }

        private void RunAll()
        {
            if (Ctx.Sim?.Sim == null) return;
            try { ShowTime(Ctx.Facade.AnalyzeTime()); } catch (Exception e) { Saf(_timeText, e); }
            try { ShowCollisions(Ctx.Facade.DetectCollisions()); } catch (Exception e) { Saf(_collisionText, e); }
            try { ShowMachining(Ctx.Facade.AnalyzeMachining(0.1)); } catch (Exception e) { Saf(_machiningText, e); }
            try { ShowOptimize(Ctx.Facade.OptimizePath()); } catch (Exception e) { Saf(_optimizeText, e); }
            try { ShowParams(Ctx.Facade.RecommendParameters(Ctx.Sim.CurrentToolNumber, 0.5)); } catch (Exception e) { Saf(_paramText, e); }
        }

        private void ShowTime(TimeEstimator.Breakdown b)
        {
            if (_timeText == null || b == null) return;
            var sb = new StringBuilder();
            sb.AppendLine(Fmt(b.Total));
            sb.AppendLine("Cut " + Fmt(b.Cutting) + "  Rapid " + Fmt(b.Rapid));
            sb.Append("Dwell " + Fmt(b.Dwell) + "  ATC " + Fmt(b.ToolChange));
            _timeText.text = sb.ToString();
        }

        private void ShowCollisions(System.Collections.Generic.List<Core.Collision.CollisionEvent> list)
        {
            if (_collisionText == null) return;
            if (list == null || list.Count == 0)
            {
                _collisionText.text = "OK · " + Ctx.T("ui.analysis.none");
                return;
            }
            var sb = new StringBuilder();
            int n = Math.Min(list.Count, 6);
            for (int i = 0; i < n; i++)
            {
                var c = list[i];
                sb.AppendLine("L" + (c.LineIndex + 1) + " · " + c.Type + " · d=" + c.Depth.ToString("0.##"));
            }
            if (list.Count > n) sb.Append("… +" + (list.Count - n));
            _collisionText.text = sb.ToString();
        }

        private void ShowMachining(Core.Analysis.MachiningAnalysisResult r)
        {
            if (_machiningText == null || r == null) return;
            if (!r.HasIssues)
            {
                _machiningText.text = "OK · " + Ctx.T("ui.analysis.none");
                return;
            }
            _machiningText.text = string.Format("Overcut {0} (max {1:0.###})\nUndercut {2} (max {3:0.###})",
                r.OvercutCount, r.MaxOvercutDepth, r.UndercutCount, r.MaxUndercutDepth);
        }

        private void ShowOptimize(System.Collections.Generic.List<Core.Optimization.PathSuggestion> list)
        {
            if (_optimizeText == null) return;
            if (list == null || list.Count == 0)
            {
                _optimizeText.text = Ctx.T("ui.analysis.none");
                return;
            }
            var sb = new StringBuilder();
            int n = Math.Min(list.Count, 5);
            for (int i = 0; i < n; i++)
            {
                var s = list[i];
                sb.Append("• ").Append(s.Message);
                if (s.EstimatedSavingSeconds > 0.05)
                    sb.Append("  (-").Append(Fmt(s.EstimatedSavingSeconds)).Append(")");
                sb.AppendLine();
            }
            _optimizeText.text = sb.ToString();
        }

        private void ShowParams(CuttingParameters p)
        {
            if (_paramText == null) return;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(p.Note)) sb.AppendLine(p.Note);
            sb.AppendLine("S " + p.SpindleSpeed.ToString("0") + " rpm   F " + p.FeedRate.ToString("0.##") + " mm/min");
            sb.AppendLine("Vc " + p.SurfaceSpeed.ToString("0.#") + " m/min   fz " + p.FeedPerTooth.ToString("0.####") + " mm/z");
            sb.Append("ap " + p.AxialDepth.ToString("0.##") + "  ae " + p.RadialWidth.ToString("0.##") +
                      "  P " + p.PowerKw.ToString("0.##") + " kW");
            _paramText.text = sb.ToString();
        }

        private void Saf(TextMeshProUGUI label, Exception e)
        {
            if (label != null) label.text = "— (" + e.GetType().Name + ")";
        }
    }
}
