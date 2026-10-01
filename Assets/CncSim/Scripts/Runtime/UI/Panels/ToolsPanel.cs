using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Tooling;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>刀具面板（功能 18-24）：刀具列表、增删、类型/直径/圆角/刃长/伸出编辑，并同步三维模型。</summary>
    public class ToolsPanel : CncUiPanel
    {
        private ToolDefinition _current;
        private RectTransform _list;
        private TextMeshProUGUI _header;

        private TMP_InputField _numField, _diamField, _cornerField, _fluteField, _stickField;
        private TMP_Dropdown _typeDd;

        protected override void Build()
        {
            Section("ui.tools.list");

            var scroll = UIFactory.ScrollList(Root, Theme, out _list);
            UIFactory.Size(scroll.gameObject, minH: 120f, prefH: 180f, flexH: 0f);
            RebuildList();

            Row(t =>
            {
                Btn(t, "ui.tools.add", AddTool, true, 28f);
                Btn(t, "ui.tools.remove", RemoveTool, false, 28f);
            }, 30f);

            UIFactory.HLine(Root, Theme.Divider);

            _header = UIFactory.Label(Root, "", Theme, 13f, false);
            UIFactory.Size(_header.gameObject, minH: 22f, prefH: 22f);

            if (Ctx.Sim?.Sim != null)
                _current = Ctx.Sim.Sim.Tools.Get(Ctx.Sim.CurrentToolNumber) ?? Ctx.Sim.Sim.Tools.GetOrCreate(1);

            BuildEditorFields();
        }

        private void BuildEditorFields()
        {
            var typeNames = new[] { "Flat", "Ball", "Bull", "Drill", "Tap", "Chamfer", "Thread", "Probe" };
            _typeDd = UIFactory.Dropdown(Root, Ctx.T("ui.tools.type"), Theme, typeNames,
                _current != null ? Mathf.Max(0, (int)_current.Type) : 0, OnTypeChanged);
            RegisterText(() =>
            {
                var t = _typeDd.captionText;
                t.text = _current != null ? TypeLabel(_current.Type) : _typeDd.options[0].text;
            });

            _numField = UIFactory.NumberRow(Root, Theme, Ctx.T("ui.tools.number"),
                _current != null ? _current.Number.ToString() : "1", v =>
                {
                    if (_current != null && int.TryParse(v, out var n)) { _current.Number = n; ApplyToScene(); RebuildList(); }
                });
            _diamField = UIFactory.NumberRow(Root, Theme, Ctx.T("ui.tools.diameter"),
                _current != null ? _current.Diameter.ToString("0.###") : "6", v =>
                {
                    if (_current != null && double.TryParse(v, out var d)) { _current.Diameter = d; _current.Normalize(); ApplyToScene(); }
                });
            _cornerField = UIFactory.NumberRow(Root, Theme, Ctx.T("ui.tools.corner"),
                _current != null ? _current.CornerRadius.ToString("0.###") : "0", v =>
                {
                    if (_current != null && double.TryParse(v, out var r)) { _current.CornerRadius = r; ApplyToScene(); }
                });
            _fluteField = UIFactory.NumberRow(Root, Theme, Ctx.T("ui.tools.flute"),
                _current != null ? _current.FluteLength.ToString("0.###") : "25", v =>
                {
                    if (_current != null && double.TryParse(v, out var l)) { _current.FluteLength = l; ApplyToScene(); }
                });
            _stickField = UIFactory.NumberRow(Root, Theme, Ctx.T("ui.tools.stickout"),
                _current != null ? _current.StickOut.ToString("0.###") : "60", v =>
                {
                    if (_current != null && double.TryParse(v, out var s)) { _current.StickOut = s; ApplyToScene(); }
                });
        }

        public override void Tick(float deltaTime)
        {
            if (_header != null && _current != null)
                _header.text = Ctx.T("ui.tools.selected") + "  T" + _current.Number + "  Ø" + _current.Diameter.ToString("0.##");
        }

        private string TypeLabel(ToolType t)
        {
            string key = t switch
            {
                ToolType.BallEndMill => "ui.tool.ball",
                ToolType.BullNoseEndMill => "ui.tool.bull",
                ToolType.Drill => "ui.tool.drill",
                ToolType.Tap => "ui.tool.tap",
                ToolType.ChamferMill => "ui.tool.chamfer",
                ToolType.ThreadMill => "ui.tool.thread",
                ToolType.Probe => "ui.tool.probe",
                _ => "ui.tool.flat"
            };
            return Ctx.T(key);
        }

        private void OnTypeChanged(int idx)
        {
            if (_current == null) return;
            _current.Type = (ToolType)idx;
            _current.Normalize();
            ApplyToScene();
            RefreshEditorValues();
        }

        private void RefreshEditorValues()
        {
            if (_current == null) return;
            if (_numField != null) _numField.SetTextWithoutNotify(_current.Number.ToString());
            if (_diamField != null) _diamField.SetTextWithoutNotify(_current.Diameter.ToString("0.###"));
            if (_cornerField != null) _cornerField.SetTextWithoutNotify(_current.CornerRadius.ToString("0.###"));
            if (_fluteField != null) _fluteField.SetTextWithoutNotify(_current.FluteLength.ToString("0.###"));
            if (_stickField != null) _stickField.SetTextWithoutNotify(_current.StickOut.ToString("0.###"));
        }

        private void ApplyToScene()
        {
            Ctx.Entities?.SetToolNumber(_current.Number);
            _header.text = Ctx.T("ui.tools.selected") + "  T" + _current.Number + "  Ø" + _current.Diameter.ToString("0.##");
        }

        private void AddTool()
        {
            var lib = Ctx.Sim.Sim.Tools;
            int n = 1;
            while (lib.Get(n) != null) n++;
            var tool = new ToolDefinition { Number = n, Diameter = 6, CornerRadius = 0 };
            lib.Add(tool);
            _current = tool;
            RebuildList();
            RefreshEditorValues();
        }

        private void RemoveTool()
        {
            if (_current == null) return;
            Ctx.Sim.Sim.Tools.Remove(_current.Number);
            _current = Ctx.Sim.Sim.Tools.Get(Ctx.Sim.CurrentToolNumber) ?? Ctx.Sim.Sim.Tools.GetOrCreate(1);
            RebuildList();
            RefreshEditorValues();
        }

        private void RebuildList()
        {
            if (_list == null) return;
            for (int i = _list.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_list.GetChild(i).gameObject);

            if (Ctx.Sim?.Sim == null) return;
            var tools = new List<ToolDefinition>(Ctx.Sim.Sim.Tools.Tools);
            foreach (var tool in tools)
            {
                var captured = tool;
                bool selected = _current != null && _current.Number == tool.Number;
                var btn = UIFactory.Button(_list, "T" + tool.Number + "  Ø" + tool.Diameter.ToString("0.##"),
                    Theme, () =>
                    {
                        _current = captured;
                        Ctx.Sim.SelectTool(captured.Number);
                        ApplyToScene();
                        RebuildList();
                        RefreshEditorValues();
                    }, 26f, selected, 12f);
                var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                txt.enableWordWrapping = false;
            }
        }
    }
}
