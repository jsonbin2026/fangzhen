using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Simulation;
using CncSim.Core.Trajectory;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>仿真面板（功能 45-55, 85-90）：播放控制、进度、实时去除、轨迹显示与样式、质量档位。</summary>
    public class SimulationPanel : CncUiPanel
    {
        private TextMeshProUGUI _progressLabel;
        private TextMeshProUGUI _stateLabel;
        private Slider _progress;

        protected override void Build()
        {
            Section("ui.sim.controls");
            BuildBody();
        }

        public override void Tick(float deltaTime)
        {
            if (Ctx.Sim?.Sim?.Simulation == null) return;
            var sim = Ctx.Sim.Sim.Simulation;
            OnProgress(sim.Current.Progress);
            UpdateState(sim.State);
        }

        private void BuildBody()
        {

            Row(t =>
            {
                Btn(t, "ui.ctrl.restart", () => { Ctx.Sim.StartFromLine(0); }, false, 30f);
                Btn(t, "ui.ctrl.step_back", () => { Ctx.Sim.StepBackward(); }, false, 30f);
                Btn(t, "ui.ctrl.play", () => { Ctx.Sim.TogglePlayPause(); }, true, 30f);
                Btn(t, "ui.ctrl.step_fwd", () => { Ctx.Sim.StepForward(); }, false, 30f);
                Btn(t, "ui.ctrl.stop", () => { Ctx.Sim.Stop(); }, false, 30f);
            }, 32f);

            _stateLabel = UIFactory.Label(Root, "", Theme, 12f, true);
            UIFactory.Size(_stateLabel.gameObject, minH: 20f, prefH: 20f);

            UIFactory.HLine(Root, Theme.Divider);

            // 进度
            _progress = Sld("ui.sim.progress", 0f, 1f, 0f, v =>
            {
                Ctx.Sim.SeekProgress(v);
                Ctx.Facade.SeekReplay(v);
            }, "P0");
            _progressLabel = UIFactory.Label(Root, "", Theme, 12f, true);

            UIFactory.HLine(Root, Theme.Divider);

            // 实时去除
            Chk("ui.sim.live_removal", Ctx.Sim.LiveRemoval, v => Ctx.Sim.LiveRemoval = v);

            UIFactory.HLine(Root, Theme.Divider);

            // 轨迹显示
            Section("ui.sim.trajectory");
            if (Ctx.Toolpath != null)
            {
                var tp = Ctx.Toolpath;
                Chk("ui.sim.trajectory", tp.gameObject.activeSelf, v => tp.SetVisible(v));
                Chk("ui.sim.rapid", tp.ShowRapid, v => { tp.ShowRapid = v; tp.Rebuild(); });
                Chk("ui.sim.cutting", tp.ShowCutting, v => { tp.ShowCutting = v; tp.Rebuild(); });
            }

            var styleNames = new[] { Ctx.T("ui.sim.style.lines"), Ctx.T("ui.sim.style.solid") };
            var styleDd = UIFactory.Dropdown(Root, Ctx.T("ui.sim.style"), Theme, styleNames,
                Ctx.Toolpath != null && Ctx.Toolpath.Style == TrajectoryStyle.Solid ? 1 : 0,
                i => Ctx.Toolpath?.SetStyle((TrajectoryStyle)i));
            RegisterText(() =>
            {
                styleDd.options[0].text = Ctx.T("ui.sim.style.lines");
                styleDd.options[1].text = Ctx.T("ui.sim.style.solid");
                styleDd.RefreshShownValue();
            });

            UIFactory.HLine(Root, Theme.Divider);

            // 质量档位
            Section("ui.sim.quality");
            var q = Ctx.Sim.Sim.Quality;
            var qualityNames = new[] { Ctx.T("ui.sim.quality.low"), Ctx.T("ui.sim.quality.medium"), Ctx.T("ui.sim.quality.high") };
            var qDd = UIFactory.Dropdown(Root, Ctx.T("ui.sim.quality"), Theme, qualityNames, (int)q, i =>
            {
                Ctx.Sim.Sim.Quality = (Core.QualityPreset)i;
                Ctx.Sim.Sim.LoadSimulation();
            });
            RegisterText(() =>
            {
                qDd.options[0].text = Ctx.T("ui.sim.quality.low");
                qDd.options[1].text = Ctx.T("ui.sim.quality.medium");
                qDd.options[2].text = Ctx.T("ui.sim.quality.high");
                qDd.RefreshShownValue();
            });

            if (Ctx.Sim != null)
            {
                UpdateState(Ctx.Sim.Sim.Simulation.State);
            }
        }

        private void OnProgress(double p)
        {
            if (_progress != null) _progress.SetValueWithoutNotify((float)p);
            if (_progressLabel != null) _progressLabel.text = (p * 100.0).ToString("0.0") + "%";
        }

        private void OnState(SimulationState s) => UpdateState(s);

        private void UpdateState(SimulationState s)
        {
            if (_stateLabel != null)
            {
                string key = "ui.status." + s.ToString().ToLowerInvariant();
                _stateLabel.text = Ctx.T(key);
            }
        }
    }
}
