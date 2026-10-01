using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Runtime;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>场景面板（功能 32-44, 80）：显示开关、截面、透明度、测量、视角与五轴机床切换。</summary>
    public class ScenePanel : CncUiPanel
    {
        protected override void Build()
        {
            // 可见性
            Section("ui.scene.show_machine");
            Chk("ui.scene.show_machine", Ctx.Entities.ShowMachine, v => { Ctx.Entities.ShowMachine = v; Ctx.Entities.RebuildAll(); });
            Chk("ui.scene.show_workpiece", Ctx.Entities.ShowBlank, v => { Ctx.Entities.ShowBlank = v; Ctx.Entities.RebuildAll(); });
            Chk("ui.scene.show_fixture", Ctx.Entities.ShowFixture, v => { Ctx.Entities.ShowFixture = v; Ctx.Entities.RebuildAll(); });
            Chk("ui.sim.trajectory", Ctx.Toolpath != null && Ctx.Toolpath.gameObject.activeSelf,
                v => Ctx.Toolpath?.SetVisible(v));

            if (Ctx.Grid != null)
            {
                Chk("ui.scene.show_grid", Ctx.Grid.ShowAxes, v => { Ctx.Grid.ShowAxes = v; Ctx.Grid.Rebuild(); });
            }

            UIFactory.HLine(Root, Theme.Divider);

            // 截面
            Section("ui.scene.section_enable");
            if (Ctx.Section != null)
            {
                var sec = Ctx.Section;
                Chk("ui.scene.section_enable", sec.Enabled, v => sec.SetEnabled(v));
                var axisNames = new[] { "X", "Y", "Z" };
                UIFactory.Dropdown(Root, Ctx.T("ui.scene.section_axis"), Theme, axisNames,
                    (int)sec.Axis, i => sec.SetAxis((SectionView.SectionAxis)i));
                Sld("ui.scene.section_offset", -200f, 200f, sec.Offset, v => sec.SetOffset(v), "0.#");
            }

            UIFactory.HLine(Root, Theme.Divider);

            // 透明度
            Section("ui.scene.opacity");
            if (Ctx.Transparency != null)
            {
                var tr = Ctx.Transparency;
                Sld("ui.scene.opacity", 0.05f, 1f, tr.Opacity, v => { tr.SetOpacity(v); tr.Apply(); }, "0.00");
            }

            UIFactory.HLine(Root, Theme.Divider);

            // 测量
            Section("ui.scene.measure");
            if (Ctx.Measurement != null)
            {
                var m = Ctx.Measurement;
                var measNames = new[] { Ctx.T("ui.scene.measure.none"), Ctx.T("ui.scene.measure.distance"), Ctx.T("ui.scene.measure.angle") };
                var dd = UIFactory.Dropdown(Root, Ctx.T("ui.scene.measure"), Theme, measNames,
                    (int)m.Type, i => m.SetType((MeasurementTool.MeasureType)i));
                RegisterText(() =>
                {
                    dd.options[0].text = Ctx.T("ui.scene.measure.none");
                    dd.options[1].text = Ctx.T("ui.scene.measure.distance");
                    dd.options[2].text = Ctx.T("ui.scene.measure.angle");
                    dd.RefreshShownValue();
                });
                Btn(Root, "ui.scene.measure.clear", () => m.Clear(), false, 28f);
                _measureText = UIFactory.Label(Root, "", Theme, 12f, true);
                UIFactory.Size(_measureText.gameObject, minH: 20f, prefH: 20f);
            }

            UIFactory.HLine(Root, Theme.Divider);

            // 视角
            Section("ui.view.iso");
            Row(t =>
            {
                Btn(t, "ui.view.top", () => Ctx.Camera?.SetView(ViewPreset.Top), false, 28f);
                Btn(t, "ui.view.front", () => Ctx.Camera?.SetView(ViewPreset.Front), false, 28f);
                Btn(t, "ui.view.side", () => Ctx.Camera?.SetView(ViewPreset.Side), false, 28f);
            }, 30f);
            Row(t =>
            {
                Btn(t, "ui.view.iso", () => Ctx.Camera?.SetView(ViewPreset.Isometric), false, 28f);
                Btn(t, "ui.view.fit", () => Ctx.Sim.FrameWorkpiece(ViewPreset.Isometric), true, 28f);
            }, 30f);

            UIFactory.HLine(Root, Theme.Divider);

            // 五轴机床
            Section("ui.machine.5axis");
            var machineNames = new[] { Ctx.T("ui.machine.3axis"), Ctx.T("ui.machine.4axis"), Ctx.T("ui.machine.5axis") };
            var mDd = UIFactory.Dropdown(Root, Ctx.T("ui.machine.3axis"), Theme, machineNames,
                MachineIndex(), i => OnMachineChanged(i));
            RegisterText(() =>
            {
                mDd.options[0].text = Ctx.T("ui.machine.3axis");
                mDd.options[1].text = Ctx.T("ui.machine.4axis");
                mDd.options[2].text = Ctx.T("ui.machine.5axis");
                mDd.RefreshShownValue();
            });
        }

        private TextMeshProUGUI _measureText;

        public override void Tick(float deltaTime)
        {
            if (_measureText != null && Ctx.Measurement != null)
            {
                var m = Ctx.Measurement;
                if (m.Type == MeasurementTool.MeasureType.Distance)
                    _measureText.text = "d = " + m.DistanceMm.ToString("0.###") + " mm";
                else if (m.Type == MeasurementTool.MeasureType.Angle)
                    _measureText.text = "θ = " + m.AngleDeg.ToString("0.##") + "°";
                else
                    _measureText.text = "";
            }
        }

        private int MachineIndex()
        {
            var type = Ctx.Sim.Sim.Machine.Type;
            return type == Core.Parsing.MachineType.FourAxis ? 1
                : type == Core.Parsing.MachineType.FiveAxis ? 2 : 0;
        }

        private void OnMachineChanged(int i)
        {
            switch (i)
            {
                case 1:
                    Ctx.Facade.SetMachineType(Core.Parsing.MachineType.FourAxis);
                    break;
                case 2:
                    Ctx.Facade.SetMachineType(Core.Parsing.MachineType.FiveAxis,
                        Core.Parsing.FiveAxisKinematics.TableTable);
                    break;
                default:
                    Ctx.Facade.SetMachineType(Core.Parsing.MachineType.ThreeAxis);
                    break;
            }
        }
    }
}
