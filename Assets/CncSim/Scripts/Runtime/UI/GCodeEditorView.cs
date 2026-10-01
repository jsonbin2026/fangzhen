using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.CodeEditor;
using CncSim.Core.Theming;

namespace CncSim.Runtime.UI
{
    /// <summary>
    /// G 代码编辑器视图（功能 1-4, 9-17 的界面层）。
    /// 以"行号 + 语法高亮行"的虚拟列表呈现程序，支持：
    ///   - 点击选中行，底部输入框编辑该行并写回文档；
    ///   - 仿真时高亮当前执行行；
    ///   - 出现诊断错误的行标红；
    ///   - 跟随暗/亮主题切换高亮配色。
    /// 由 CncUIRoot 装配到中央区域（覆盖在 3D 视图之上，可显隐）。
    /// </summary>
    public class GCodeEditorView : MonoBehaviour
    {
        public CncUiContext Context;
        public CncUIFacade Facade;
        public CncSimBehaviour Simulator;
        public CncUiTheme Theme;

        public bool WordWrap = false;
        public int FontSize = 15;
        public const float FindBarHeight = 40f;

        private RectTransform _root;
        private RectTransform _content;
        private ScrollRect _scroll;
        private SyntaxHighlighter _highlighter;
        private readonly List<Row> _rows = new List<Row>();
        private TMP_InputField _lineEditor;
        private TextMeshProUGUI _editorHint;
        private int _selectedLine = -1;
        private int _currentLine = -1;
        private TMP_InputField _findField;
        private TMP_InputField _replaceField;
        private TextMeshProUGUI _findResult;
        private readonly List<int> _matches = new List<int>();
        private int _matchCursor = -1;

        private class Row
        {
            public RectTransform Rect;
            public TextMeshProUGUI Number;
            public TextMeshProUGUI Code;
            public Image Background;
            public Image BreakpointDot;
            public int LineIndex;
        }

        public void Build(RectTransform parent, CncUiTheme theme)
        {
            Theme = theme;
            _highlighter = new SyntaxHighlighter(HighlightScheme.Dark());
            ApplyScheme();

            _root = UIFactory.Node(parent, "GCodeEditor");
            var rt = _root;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(UIFactory.TabRailWidth + 12f, UIFactory.BottomBarHeight + 12f);
            rt.offsetMax = new Vector2(-70f, -UIFactory.TopBarHeight - 12f);

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = new Color(Theme.Panel.r, Theme.Panel.g, Theme.Panel.b, 0.96f);

            BuildFindBar();

            // 代码滚动区
            _scroll = UIFactory.ScrollList(_root, theme, out _content);
            var srt = _scroll.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(4f, 64f);
            srt.offsetMax = new Vector2(-4f, -FindBarHeight - 4f);

            // 底部编辑条
            var bar = UIFactory.Panel(_root, "EditorBar", Theme.PanelAlt);
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(1f, 0f);
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);
            bar.rectTransform.sizeDelta = new Vector2(-8f, 56f);
            bar.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            var h = UIFactory.HStack(bar.rectTransform, 6f, new RectOffset(6, 6, 4, 4));

            _editorHint = UIFactory.Label(bar.transform, "L-", Theme, 12f, true);
            UIFactory.Size(_editorHint.gameObject, minW: 44f, prefW: 44f, flexW: 0f);

            string placeholder = Context?.T("ui.editor.placeholder") ?? "Select a line, then edit here";
            _lineEditor = UIFactory.Input(bar.transform, Theme, "", placeholder, OnLineSubmitted, 32f);
            UIFactory.Size(_lineEditor.gameObject, flexW: 1f, minH: 32f, prefH: 32f);

            var apply = UIFactory.Button(bar.transform, Context?.T("ui.editor.apply") ?? "Apply", Theme,
                () => OnLineSubmitted(_lineEditor.text), 32f, true, 13f);
            UIFactory.Size(apply.gameObject, minW: 56f, prefW: 56f, flexW: 0f);

            if (Facade != null)
            {
                Facade.DiagnosticsChanged += OnDiagnosticsChanged;
                Facade.Reanalyzed += OnReanalyzed;
                BindDocument();
            }
            RebuildRows();
        }

        private GCodeDocument _boundDoc;

        private void BuildFindBar()
        {
            var bar = UIFactory.Panel(_root, "FindBar", Theme.PanelAlt);
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.sizeDelta = new Vector2(-8f, FindBarHeight);
            bar.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            var h = UIFactory.HStack(bar.rectTransform, 6f, new RectOffset(6, 6, 4, 4));

            _findField = UIFactory.Input(bar.transform, Theme, "", "查找", _ => RunFind(), 30f);
            UIFactory.Size(_findField.gameObject, flexW: 1f, minH: 30f, prefH: 30f);

            var prev = UIFactory.IconButton(bar.transform, "▲", Theme, () => CycleMatch(-1), 30f, "上一个");
            var next = UIFactory.IconButton(bar.transform, "▼", Theme, () => CycleMatch(1), 30f, "下一个");
            _findResult = UIFactory.Label(bar.transform, "", Theme, 12f, true);
            UIFactory.Size(_findResult.gameObject, minW: 56f, prefW: 56f, flexW: 0f);

            _replaceField = UIFactory.Input(bar.transform, Theme, "", "替换为", null, 30f);
            UIFactory.Size(_replaceField.gameObject, flexW: 1f, minH: 30f, prefH: 30f);

            var replaceBtn = UIFactory.Button(bar.transform, "替换", Theme, ReplaceCurrent, 30f, false, 13f);
            UIFactory.Size(replaceBtn.gameObject, minW: 52f, prefW: 52f, flexW: 0f);

            var replaceAllBtn = UIFactory.Button(bar.transform, "全部替换", Theme, ReplaceAll, 30f, false, 13f);
            UIFactory.Size(replaceAllBtn.gameObject, minW: 68f, prefW: 68f, flexW: 0f);
        }

        private void RunFind()
        {
            _matches.Clear();
            _matchCursor = -1;
            var doc = Facade?.Document;
            string pattern = _findField != null ? _findField.text : null;
            if (doc == null || string.IsNullOrEmpty(pattern))
            {
                UpdateFindResult();
                return;
            }
            foreach (var m in doc.FindAll(pattern))
                if (!_matches.Contains(m.line)) _matches.Add(m.line);
            if (_matches.Count > 0) CycleMatch(1);
            else UpdateFindResult();
        }

        private void CycleMatch(int dir)
        {
            if (_matches.Count == 0) { UpdateFindResult(); return; }
            _matchCursor = ((_matchCursor + dir) % _matches.Count + _matches.Count) % _matches.Count;
            int line = _matches[_matchCursor];
            SelectLine(line);
            EnsureVisible(line);
            UpdateFindResult();
        }

        private void UpdateFindResult()
        {
            if (_findResult == null) return;
            if (_matches.Count == 0)
                _findResult.text = string.IsNullOrEmpty(_findField?.text) ? "" : "0";
            else
                _findResult.text = (_matchCursor + 1) + "/" + _matches.Count;
        }

        private void ReplaceCurrent()
        {
            if (_selectedLine < 0 || Facade?.Document == null) return;
            string pattern = _findField != null ? _findField.text : null;
            if (string.IsNullOrEmpty(pattern)) return;
            string content = Facade.Document.GetLine(_selectedLine);
            string replaced = ReplaceFirst(content, pattern, _replaceField != null ? _replaceField.text : "");
            if (replaced != content)
            {
                Facade.Document.ReplaceLine(_selectedLine, replaced);
                Facade.Reparse();
                RebuildRows();
                RunFind();
            }
        }

        private void ReplaceAll()
        {
            var doc = Facade?.Document;
            string pattern = _findField != null ? _findField.text : null;
            if (doc == null || string.IsNullOrEmpty(pattern)) return;
            int n = doc.ReplaceAll(pattern, _replaceField != null ? _replaceField.text : "");
            if (n > 0)
            {
                Facade.Reparse();
                RebuildRows();
                RunFind();
                Context?.SetStatus?.Invoke("替换 " + n + " 处");
            }
        }

        private static string ReplaceFirst(string source, string pattern, string replacement)
        {
            int idx = source.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return source;
            return source.Substring(0, idx) + replacement + source.Substring(idx + pattern.Length);
        }

        private void BindDocument()
        {            var doc = Facade?.Document;
            if (ReferenceEquals(doc, _boundDoc)) return;
            if (_boundDoc != null) _boundDoc.Changed -= OnDocumentChanged;
            _boundDoc = doc;
            if (_boundDoc != null) _boundDoc.Changed += OnDocumentChanged;
        }

        private void OnReanalyzed(Core.Parsing.ParseResult result)
        {
            BindDocument();
            RebuildRows();
        }

        private void OnDiagnosticsChanged(Core.Parsing.DiagnosticList list) => RefreshErrorMarks();

        public void SetVisible(bool visible) => _root?.gameObject.SetActive(visible);

        public void Bind(CncSimBehaviour sim)
        {
            if (Simulator == sim) return;
            if (Simulator != null)
            {
                Simulator.FrameUpdated -= OnFrame;
                Simulator.StateChanged -= OnState;
                var oldCtrl = Simulator.Sim?.Simulation;
                if (oldCtrl != null) oldCtrl.BreakpointHit -= OnBreakpointHit;
            }
            Simulator = sim;
            if (Simulator != null)
            {
                Simulator.FrameUpdated += OnFrame;
                Simulator.StateChanged += OnState;
            }
            var simCtrl = sim?.Sim?.Simulation;
            if (simCtrl != null) simCtrl.BreakpointHit += OnBreakpointHit;
        }

        private void OnBreakpointHit(int line)
        {
            EnsureVisible(line);
            RefreshBreakpoints();
        }

        private void OnDestroy()
        {
            if (Facade != null)
            {
                Facade.DiagnosticsChanged -= OnDiagnosticsChanged;
                Facade.Reanalyzed -= OnReanalyzed;
            }
            if (_boundDoc != null) _boundDoc.Changed -= OnDocumentChanged;
            if (Simulator != null)
            {
                Simulator.FrameUpdated -= OnFrame;
                Simulator.StateChanged -= OnState;
            }
            var simCtrl = Simulator?.Sim?.Simulation;
            if (simCtrl != null) simCtrl.BreakpointHit -= OnBreakpointHit;
        }

        private void OnDocumentChanged(int version) => RebuildRows();

        public void SetTheme(CncUiTheme theme)
        {
            Theme = theme;
            ApplyScheme();
            RebuildRows();
        }

        private void ApplyScheme()
        {
            if (_highlighter == null) return;
            bool dark = Theme == null || Theme.Panel.grayscale < 0.5f;
            _highlighter.SetScheme(dark ? HighlightScheme.Dark() : HighlightScheme.Light());
        }

        private void OnState(Core.Simulation.SimulationState s) { }

        private void OnFrame(Core.Simulation.SimulationFrame f)
        {
            if (!f.Valid) return;
            int line = f.LineIndex;
            if (line != _currentLine)
            {
                _currentLine = line;
                HighlightCurrent();
                EnsureVisible(line);
            }
        }

        public void RebuildRows()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);
            _rows.Clear();

            var doc = Facade?.Document;
            if (doc == null) return;

            int count = doc.LineCount;
            for (int i = 0; i < count; i++)
                _rows.Add(CreateRow(i, doc.GetLine(i)));

            RefreshErrorMarks();
            RefreshBreakpoints();
            HighlightCurrent();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private Row CreateRow(int index, string text)
        {
            var row = new Row { LineIndex = index };
            row.Rect = UIFactory.Node(_content, "L" + index);
            UIFactory.Size(row.Rect.gameObject, minH: FontSize + 10f, prefH: FontSize + 10f);

            row.Background = UIFactory.Panel(row.Rect, "Bg", new Color(0f, 0f, 0f, 0f), true);
            UIFactory.Stretch(row.Background);
            row.Background.transform.SetAsFirstSibling();

            var h = UIFactory.HStack(row.Rect, 4f, new RectOffset(4, 4, 0, 0));

            row.BreakpointDot = UIFactory.Panel(row.Rect, "Bp", new Color(0f, 0f, 0f, 0f), true);
            UIFactory.Size(row.BreakpointDot.gameObject, minW: 16f, prefW: 16f, flexW: 0f, minH: 16f, prefH: 16f);

            row.Number = UIFactory.Text(row.Rect, (index + 1).ToString(), Theme, FontSize - 2f,
                TextAlignmentOptions.MidlineRight, true);
            UIFactory.Size(row.Number.gameObject, minW: 44f, prefW: 44f, flexW: 0f);

            row.Code = UIFactory.Text(row.Rect, string.IsNullOrEmpty(text) ? " " : _highlighter.HighlightLine(text),
                Theme, FontSize, TextAlignmentOptions.MidlineLeft, false);
            row.Code.enableWordWrapping = WordWrap;
            row.Code.overflowMode = TextOverflowModes.Ellipsis;
            row.Code.richText = true;
            UIFactory.Size(row.Code.gameObject, flexW: 1f);

            int captured = index;
            var btn = row.Rect.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SelectLine(captured));

            var dotBtn = row.BreakpointDot.gameObject.AddComponent<Button>();
            dotBtn.transition = Selectable.Transition.None;
            dotBtn.onClick.AddListener(() => ToggleBreakpoint(captured));

            return row;
        }

        private void ToggleBreakpoint(int line)
        {
            var sim = Simulator?.Sim?.Simulation;
            if (sim == null) return;
            if (!sim.Breakpoints.Remove(line)) sim.Breakpoints.Add(line);
            RefreshBreakpoints();
        }

        private void RefreshBreakpoints()
        {
            var bps = Simulator?.Sim?.Simulation?.Breakpoints;
            foreach (var row in _rows)
            {
                if (row.BreakpointDot == null) continue;
                bool on = bps != null && bps.Contains(row.LineIndex);
                row.BreakpointDot.color = on ? Theme.Error : new Color(0f, 0f, 0f, 0f);
            }
        }

        private static Color Tint(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private void SelectLine(int index)
        {
            _selectedLine = index;
            var doc = Facade?.Document;
            if (doc == null) return;
            if (_editorHint != null) _editorHint.text = "L" + (index + 1);
            if (_lineEditor != null) _lineEditor.SetTextWithoutNotify(doc.GetLine(index));
            RefreshSelectionVisual();
        }

        private void OnLineSubmitted(string value)
        {
            if (_selectedLine < 0 || Facade?.Document == null) return;
            Facade.Document.ReplaceLine(_selectedLine, value);
            Facade.Reparse();
            RebuildRows();
        }

        private void RefreshSelectionVisual()
        {
            foreach (var row in _rows)
            {
                if (row.Background == null) continue;
                if (row.LineIndex == _selectedLine)
                    row.Background.color = Tint(Theme.Accent, 0.35f);
                else if (row.LineIndex == _currentLine)
                    row.Background.color = Tint(Theme.Accent, 0.22f);
                else
                    row.Background.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        private void HighlightCurrent()
        {
            RefreshSelectionVisual();
        }

        private void RefreshErrorMarks()
        {
            var diags = Facade?.Diagnostics;
            if (_rows.Count == 0) return;
            if (diags == null)
            {
                foreach (var r in _rows) r.Number.color = Theme.TextMuted;
                return;
            }
            var byLine = new Dictionary<int, Core.Parsing.DiagnosticSeverity>();
            foreach (var d in diags)
            {
                if (!byLine.TryGetValue(d.Line, out var sev) || d.Severity == Core.Parsing.DiagnosticSeverity.Error)
                    byLine[d.Line] = d.Severity;
            }
            foreach (var r in _rows)
            {
                if (byLine.TryGetValue(r.LineIndex, out var sev))
                    r.Number.color = sev == Core.Parsing.DiagnosticSeverity.Error ? Theme.Error : Theme.Warning;
                else
                    r.Number.color = Theme.TextMuted;
            }
        }

        private void EnsureVisible(int line)
        {
            if (_scroll == null || _content == null || line < 0 || line >= _rows.Count) return;
            float rowH = FontSize + 10f;
            float contentH = _content.rect.height;
            float viewH = _scroll.viewport != null ? _scroll.viewport.rect.height : contentH;
            if (contentH <= viewH) return;
            float target = line * rowH;
            float v = Mathf.Clamp01(1f - (target - viewH * 0.4f) / (contentH - viewH));
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(v);
        }
    }
}
