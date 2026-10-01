using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core;
using CncSim.Core.Stock;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>毛坯面板（功能 25-30）：形状、XYZ 尺寸、材料、工件坐标系 G54-G59、夹具、三点对刀。</summary>
    public class BlankPanel : CncUiPanel
    {
        private BlankDefinition Blank => Ctx.Facade.Blank;

        protected override void Build()
        {
            Section("ui.blank.shape");
            var shapeNames = new[] { Ctx.T("ui.blank.box"), Ctx.T("ui.blank.cylinder"), Ctx.T("ui.blank.custom") };
            var shapeDd = UIFactory.Dropdown(Root, Ctx.T("ui.blank.shape"), Theme, shapeNames,
                (int)Blank.Shape, i =>
                {
                    Ctx.Facade.SetBlankShape((BlankShape)i);
                    Ctx.Sim.FrameWorkpiece();
                });
            RegisterText(() =>
            {
                shapeDd.options[0].text = Ctx.T("ui.blank.box");
                shapeDd.options[1].text = Ctx.T("ui.blank.cylinder");
                shapeDd.options[2].text = Ctx.T("ui.blank.custom");
                shapeDd.RefreshShownValue();
            });

            Section("ui.blank.material");
            var mats = MaterialDefinition.Builtin;
            var matNames = new string[mats.Length];
            for (int i = 0; i < mats.Length; i++) matNames[i] = Ctx.T(mats[i].NameKey);
            int matIndex = Array.FindIndex(mats, m => m.Id == Blank.MaterialId);
            if (matIndex < 0) matIndex = 0;
            var matDd = UIFactory.Dropdown(Root, Ctx.T("ui.blank.material"), Theme, matNames, matIndex, i =>
            {
                Ctx.Facade.SetMaterial(mats[i].Id);
                Refresh();
            });
            RegisterText(() =>
            {
                for (int i = 0; i < mats.Length; i++) matDd.options[i].text = Ctx.T(mats[i].NameKey);
                matDd.RefreshShownValue();
            });

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.blank.shape");
            UIFactory.NumberRow(Root, Theme, Ctx.T("ui.blank.size_x"), Blank.Size.X.ToString("0.##"),
                v => SetSize(0, v));
            UIFactory.NumberRow(Root, Theme, Ctx.T("ui.blank.size_y"), Blank.Size.Y.ToString("0.##"),
                v => SetSize(1, v));
            UIFactory.NumberRow(Root, Theme, Ctx.T("ui.blank.size_z"), Blank.Size.Z.ToString("0.##"),
                v => SetSize(2, v));

            UIFactory.HLine(Root, Theme.Divider);

            // 工件坐标系 G54-G59
            Section("ui.blank.offset");
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                var off = Ctx.Facade.GetWorkOffset(idx);
                var row = UIFactory.Node(Root, "WO" + idx);
                UIFactory.HStack(row, 6f).childForceExpandWidth = true;
                UIFactory.Size(row.gameObject, minH: 26f, prefH: 26f);
                var lbl = UIFactory.Label(row, "G" + (54 + idx), Theme, 12f, true);
                UIFactory.Size(lbl.gameObject, minW: 34f, prefW: 34f, flexW: 0f);
                AddOffsetInput(row, idx, 0, off.X);
                AddOffsetInput(row, idx, 1, off.Y);
                AddOffsetInput(row, idx, 2, off.Z);
            }

            UIFactory.HLine(Root, Theme.Divider);

            // 夹具
            Chk("ui.blank.fixture", Ctx.Facade.Fixture.Enabled, v =>
            {
                Ctx.Facade.Fixture.Enabled = v;
                Ctx.Entities?.RebuildAll();
            });

            UIFactory.HLine(Root, Theme.Divider);

            // 三点对刀
            Section("ui.blank.touch");
            _touchX = UIFactory.NumberRow(Root, Theme, "X", "0", _ => { });
            _touchY = UIFactory.NumberRow(Root, Theme, "Y", "0", _ => { });
            _touchZ = UIFactory.NumberRow(Root, Theme, "Z", "0", _ => { });
            _probe = UIFactory.NumberRow(Root, Theme, Ctx.T("ui.scene.measure"), "3", _ => { });
            Btn(Root, "ui.blank.touch", ApplyTouch, true, 30f);
            _touchResult = UIFactory.Label(Root, "", Theme, 12f, true);
            UIFactory.Size(_touchResult.gameObject, minH: 20f, prefH: 20f);
        }

        private TMP_InputField _touchX, _touchY, _touchZ, _probe;
        private TextMeshProUGUI _touchResult;

        private void AddOffsetInput(Transform parent, int woIndex, int axis, double value)
        {
            var input = UIFactory.Input(parent, Theme, value.ToString("0.##"), "", v =>
            {
                if (!double.TryParse(v, out var d)) return;
                var cur = Ctx.Facade.GetWorkOffset(woIndex);
                switch (axis)
                {
                    case 0: cur.X = d; break;
                    case 1: cur.Y = d; break;
                    default: cur.Z = d; break;
                }
                Ctx.Facade.SetWorkOffset(woIndex, cur);
            }, 26f);
            UIFactory.Size(input.gameObject, flexW: 1f, minH: 26f, prefH: 26f);
        }

        private void SetSize(int axis, string text)
        {
            if (!double.TryParse(text, out var v) || v <= 0) return;
            var size = Blank.Size;
            switch (axis)
            {
                case 0: size.X = v; break;
                case 1: size.Y = v; break;
                default: size.Z = v; break;
            }
            Ctx.Facade.SetBlankSize(size);
            Ctx.Entities?.RebuildAll();
        }

        private void ApplyTouch()
        {
            double.TryParse(_touchX.text, out var x);
            double.TryParse(_touchY.text, out var y);
            double.TryParse(_touchZ.text, out var z);
            double.TryParse(_probe.text, out var r);
            var origin = Ctx.Facade.ComputeOriginFromTouchPoints(
                new Vec3d(x, 0, 0), new Vec3d(0, y, 0), new Vec3d(0, 0, z), r, Vec3d.Zero);
            Ctx.Facade.SetWorkOffset(0, origin);
            if (_touchResult != null)
                _touchResult.text = string.Format("G54 = X {0:0.###} Y {1:0.###} Z {2:0.###}", origin.X, origin.Y, origin.Z);
        }

        private void Refresh()
        {
            Ctx.Entities?.RebuildAll();
        }
    }
}
