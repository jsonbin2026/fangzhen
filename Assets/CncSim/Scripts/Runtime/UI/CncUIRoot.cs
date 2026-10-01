using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CncSim.Core.Parsing;
using CncSim.Core.Simulation;
using CncSim.Runtime.UI.Panels;

namespace CncSim.Runtime.UI
{
    /// <summary>
    /// CNC 仿真 UI 总装（功能 1-94 的界面层）。
    /// 运行时用 UGUI 代码生成一套自适应界面，包含：
    ///   - 顶栏：标题、机床类型、语言、主题
    ///   - 左侧标签栏 + 面板区：文件/仿真/刀具/毛坯/分析/场景/录制/项目/教程/手册
    ///   - 底部播放条：播放/暂停/停止/单步、速度、进度、坐标与状态读数
    ///   - 右侧浮动视图控件：视角预设、缩放、截面、透明、测量
    /// 挂到 CncSceneBootstrap 所在的 GameObject 上，或在 Inspector 里指定 bootstrap。
    /// 所有面板通过 CncUiContext 访问功能，语言/主题变化时自动刷新。
    /// </summary>
    [DisallowMultipleComponent]
    public class CncUIRoot : MonoBehaviour
    {
        [Header("References")]
        public CncSceneBootstrap Bootstrap;

        [Header("Layout")]
        [Tooltip("界面缩放参考分辨率（横屏/竖屏自适应）")]
        public Vector2 ReferenceResolution = new Vector2(1280f, 720f);
        [Tooltip("启动时默认展开的标签")]
        public PanelId DefaultTab = PanelId.Simulation;

        public enum PanelId
        {
            File,
            Simulation,
            Tools,
            Blank,
            Analysis,
            Scene,
            Media,
            Project,
            Tutorial,
            Manual
        }

        public enum Orientation { Auto, Landscape, Portrait }

        [Header("Orientation")]
        public Orientation TargetOrientation = Orientation.Auto;

        // 运行时对象
        public Canvas Canvas { get; private set; }
        public CncUiContext Context { get; private set; }
        public PanelId ActiveTab { get; private set; }

        private RectTransform _canvasRoot;
        private RectTransform _panelHost;
        private RectTransform _tabRail;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _coordText;
        private Slider _progress;
        private TextMeshProUGUI _progressLabel;
        private TextMeshProUGUI _speedLabel;
        private TextMeshProUGUI _machineLabel;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _langText;
        private Button _playButton;
        private TextMeshProUGUI _clockText;
        private RectTransform _viewControls;
        private RectTransform _panelRoot;
        private bool _panelVisible;
        private GCodeEditorView _editorView;
        private Button _editorButton;

        private readonly Dictionary<PanelId, CncUiPanel> _panels = new Dictionary<PanelId, CncUiPanel>();
        private readonly Dictionary<PanelId, Button> _tabButtons = new Dictionary<PanelId, Button>();
        private readonly Dictionary<PanelId, TextMeshProUGUI> _tabTexts = new Dictionary<PanelId, TextMeshProUGUI>();
        private static readonly PanelId[] TabOrder =
        {
            PanelId.File, PanelId.Simulation, PanelId.Tools, PanelId.Blank,
            PanelId.Analysis, PanelId.Scene, PanelId.Media, PanelId.Project,
            PanelId.Tutorial, PanelId.Manual
        };

        private bool _updatingProgress;

        private void Update()
        {
            if (_panels.TryGetValue(ActiveTab, out var panel) && _panelVisible)
                panel.Tick(Time.unscaledDeltaTime);
        }

        private void Awake()
        {
            if (Bootstrap == null) Bootstrap = GetComponent<CncSceneBootstrap>();
            if (Bootstrap == null)
            {
                Debug.LogError("CncUIRoot: 未找到 CncSceneBootstrap，请挂到同一 GameObject 或手动指定。");
                enabled = false;
                return;
            }
        }

        private void Start()
        {
            // Bootstrap.BuildScene 在 Awake 执行，确保其完成后再建 UI
            BuildCanvas();
            BuildContext();
            BuildTopBar();
            BuildTabRail();
            BuildPanelHost();
            BuildBottomBar();
            BuildViewControls();
            BuildTooltip();
            BuildEditorView();

            if (Context.Localization != null)
            {
                Context.Localization.LanguageChanged += OnLanguageChanged;
                Context.Localization.ThemeChanged += OnThemeChangedInternal;
            }
            if (Context.Sim != null)
            {
                Context.Sim.ProgressChanged += OnProgress;
                Context.Sim.StateChanged += OnStateChanged;
                Context.Sim.FrameUpdated += OnFrame;
                Context.Sim.Finished += OnFinished;
            }

            SelectTab(DefaultTab);
            UpdateMachineLabel();
            OnStateChanged(SimulationState.Idle);
        }

        // ---------------- 画布 ----------------

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("CncUI", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasGo.GetComponent<GraphicRaycaster>() == null)
                canvasGo.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            _canvasRoot = (RectTransform)canvasGo.transform;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        private void BuildContext()
        {
            Context = new CncUiContext
            {
                Bootstrap = Bootstrap,
                Sim = Bootstrap.Simulator,
                Facade = Bootstrap.UI,
                Localization = Bootstrap.Localization,
                Camera = Bootstrap.Rig,
                Entities = Bootstrap.Entities,
                Toolpath = Bootstrap.Toolpath,
                Section = Bootstrap.Section,
                Transparency = Bootstrap.Transparency,
                Measurement = Bootstrap.Measurement,
                Grid = Bootstrap.Grid,
                RecordReplay = Bootstrap.RecordReplay,
                Capture = Bootstrap.Capture,
                ModelImport = Bootstrap.ModelImport,
                CanvasRoot = _canvasRoot,
                Theme = CncUiTheme.From(Bootstrap.Localization?.Theme),
                SetStatus = SetStatus
            };
        }

        // ---------------- 顶栏 ----------------

        private void BuildTopBar()
        {
            var bar = UIFactory.Panel(_canvasRoot, "TopBar", Context.Theme.TopBar);
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.sizeDelta = new Vector2(0f, UIFactory.TopBarHeight);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            var h = UIFactory.HStack(bar.rectTransform, 10f, new RectOffset(12, 12, 6, 6));
            h.childForceExpandWidth = false;
            h.childControlWidth = false;

            _titleText = UIFactory.Text(bar.transform, Context.T("ui.app.title"), Context.Theme, 18f,
                TextAlignmentOptions.MidlineLeft, false);
            _titleText.fontStyle = FontStyles.Bold;
            UIFactory.Size(_titleText.gameObject, minW: 180f, prefW: 180f, flexW: 0f);
            RegisterTopText(_titleText, "ui.app.title");

            UIFactory.Text(bar.transform, "|", Context.Theme, 16f, TextAlignmentOptions.Center, true)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 10f;

            // 机床类型
            _machineLabel = UIFactory.Text(bar.transform, "", Context.Theme, 13f,
                TextAlignmentOptions.MidlineLeft, true);
            UIFactory.Size(_machineLabel.gameObject, flexW: 1f);

            // 语言按钮显示当前语言
            var langBtn = UIFactory.Button(bar.transform, LangCode(), Context.Theme,
                ToggleLanguage, 30f, false, 13f);
            UIFactory.Size(langBtn.gameObject, minW: 66f, prefW: 66f, flexW: 0f);
            _langText = langBtn.GetComponentInChildren<TextMeshProUGUI>();
            RegisterTopText(_langText, null, LangCode);

            _editorButton = UIFactory.IconButton(bar.transform, "</>", Context.Theme, ToggleEditor, 30f,
                Context.T("ui.editor.toggle"));
            UIFactory.Size(_editorButton.gameObject, minW: 44f, prefW: 44f, flexW: 0f);

            var themeBtn = UIFactory.IconButton(bar.transform, "◐", Context.Theme, () =>
            {
                Context.Facade.ToggleTheme();
                SetStatus(Context.T("ui.status.theme_toggled"));
            }, 30f);
            UIFactory.Size(themeBtn.gameObject, minW: 30f, prefW: 30f, flexW: 0f);
        }

        private void ToggleEditor()
        {
            if (_editorView == null) return;
            bool show = !_editorView.gameObject.activeSelf;
            _editorView.SetVisible(show);
            var img = _editorButton != null ? _editorButton.targetGraphic as Image : null;
            if (img != null) img.color = show ? Context.Theme.Accent : Context.Theme.Button;
            SetStatus(Context.T(show ? "ui.editor.shown" : "ui.editor.hidden"));
        }

        private void BuildEditorView()
        {
            var go = new GameObject("GCodeEditor", typeof(RectTransform));
            go.transform.SetParent(_canvasRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _editorView = go.AddComponent<GCodeEditorView>();
            _editorView.Context = Context;
            _editorView.Facade = Context.Facade;
            _editorView.Theme = Context.Theme;
            _editorView.Build(rt, Context.Theme);
            _editorView.Bind(Context.Sim);
            _editorView.SetVisible(false);
        }

        private string LangCode()
        {
            var lang = Context?.Localization?.CurrentLanguage ?? "zh-CN";
            return lang.StartsWith("zh") ? "中文" : "EN";
        }

        private void ToggleLanguage()
        {
            bool zh = Context.Localization.CurrentLanguage.StartsWith("zh");
            Context.Facade.SetLanguage(zh ? "en-US" : "zh-CN");
        }

        // ---------------- 标签栏 ----------------

        private void BuildTabRail()
        {
            var rail = UIFactory.Panel(_canvasRoot, "TabRail", Context.Theme.PanelAlt);
            _tabRail = rail.rectTransform;
            _tabRail.anchorMin = new Vector2(0f, 0f);
            _tabRail.anchorMax = new Vector2(0f, 1f);
            _tabRail.pivot = new Vector2(0f, 1f);
            _tabRail.sizeDelta = new Vector2(UIFactory.TabRailWidth, -(UIFactory.TopBarHeight + UIFactory.BottomBarHeight));
            _tabRail.anchoredPosition = new Vector2(0f, -UIFactory.TopBarHeight);

            var v = UIFactory.VStack(_tabRail, 4f, new RectOffset(6, 6, 6, 6));
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            foreach (var id in TabOrder)
            {
                var btn = UIFactory.Button(_tabRail, TabTitle(id), Context.Theme, () => ToggleTab(id), 46f, false, 12f);
                var text = btn.GetComponentInChildren<TextMeshProUGUI>();
                text.enableWordWrapping = true;
                text.alignment = TextAlignmentOptions.Center;
                _tabButtons[id] = btn;
                _tabTexts[id] = text;
                RegisterTopText(text, TabKey(id));
            }
        }

        private static string TabKey(PanelId id) => id switch
        {
            PanelId.File => "ui.tab.file",
            PanelId.Simulation => "ui.tab.simulation",
            PanelId.Tools => "ui.tab.tools",
            PanelId.Blank => "ui.tab.blank",
            PanelId.Analysis => "ui.tab.analysis",
            PanelId.Scene => "ui.tab.scene",
            PanelId.Media => "ui.tab.media",
            PanelId.Project => "ui.tab.project",
            PanelId.Tutorial => "ui.tab.tutorial",
            _ => "ui.tab.manual"
        };

        private string TabTitle(PanelId id) => Context.T(TabKey(id));

        // ---------------- 面板区 ----------------

        private void BuildPanelHost()
        {
            var host = UIFactory.Panel(_canvasRoot, "PanelHost", new Color(0f, 0f, 0f, 0.001f), false);
            _panelHost = host.rectTransform;
            _panelHost.anchorMin = new Vector2(0f, 0f);
            _panelHost.anchorMax = new Vector2(0f, 1f);
            _panelHost.pivot = new Vector2(0f, 1f);
            _panelHost.sizeDelta = new Vector2(UIFactory.PanelWidth, -(UIFactory.TopBarHeight + UIFactory.BottomBarHeight));
            _panelHost.anchoredPosition = new Vector2(UIFactory.TabRailWidth, -UIFactory.TopBarHeight);

            var bg = UIFactory.Panel(_panelHost, "PanelBg", Context.Theme.Panel);
            UIFactory.Stretch(bg);
            bg.transform.SetAsFirstSibling();

            var scroll = UIFactory.ScrollList(_panelHost, Context.Theme, out var content);
            var scrollImg = scroll.GetComponent<Image>();
            if (scrollImg != null) UIFactory.Stretch(scrollImg);
            _panelRoot = content;

            // 关闭按钮（右上角）
            var close = UIFactory.IconButton(_panelHost, "✕", Context.Theme, () => SetPanelVisible(false), 26f);
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-4f, -4f);
            crt.SetAsLastSibling();
        }

        private void SelectTab(PanelId id)
        {
            ActiveTab = id;
            SetPanelVisible(true);

            foreach (var kv in _tabButtons)
            {
                var img = kv.Value.targetGraphic as Image;
                if (img != null) img.color = kv.Key == id ? Context.Theme.Accent : Context.Theme.Button;
            }

            if (!_panels.TryGetValue(id, out var panel))
            {
                panel = CreatePanel(id);
                _panels[id] = panel;
            }

            ClearPanelContent();
            panel.Attach(_panelRoot, Context);
            panel.RefreshTexts();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRoot);
        }

        private void ToggleTab(PanelId id)
        {
            if (_panelVisible && ActiveTab == id)
            {
                SetPanelVisible(false);
                return;
            }
            SelectTab(id);
        }

        private void SetPanelVisible(bool visible)
        {
            _panelVisible = visible;
            _panelHost.gameObject.SetActive(visible);
            if (!visible)
            {
                foreach (var kv in _tabButtons)
                {
                    var img = kv.Value.targetGraphic as Image;
                    if (img != null) img.color = Context.Theme.Button;
                }
            }
        }

        private void ClearPanelContent()
        {
            for (int i = _panelRoot.childCount - 1; i >= 0; i--)
                Destroy(_panelRoot.GetChild(i).gameObject);
        }

        private CncUiPanel CreatePanel(PanelId id)
        {
            switch (id)
            {
                case PanelId.File: return new FilePanel();
                case PanelId.Tools: return new ToolsPanel();
                case PanelId.Blank: return new BlankPanel();
                case PanelId.Analysis: return new AnalysisPanel();
                case PanelId.Scene: return new ScenePanel();
                case PanelId.Media: return new MediaPanel();
                case PanelId.Project: return new ProjectPanel();
                case PanelId.Tutorial: return new TutorialPanel();
                case PanelId.Manual: return new ManualPanel();
                default: return new SimulationPanel();
            }
        }

        // ---------------- 底部播放条 ----------------

        private void BuildBottomBar()
        {
            var bar = UIFactory.Panel(_canvasRoot, "BottomBar", Context.Theme.BottomBar);
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(1f, 0f);
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);
            bar.rectTransform.sizeDelta = new Vector2(0f, UIFactory.BottomBarHeight);
            bar.rectTransform.anchoredPosition = Vector2.zero;

            var root = UIFactory.Node(bar.transform, "Content");
            UIFactory.Stretch(root.gameObject.AddComponent<Image>()).color = new Color(0f, 0f, 0f, 0f);
            var layout = UIFactory.VStack(root, 4f, new RectOffset(10, 10, 6, 6));

            // 进度行
            var progRow = UIFactory.Node(root, "ProgressRow");
            UIFactory.Size(progRow.gameObject, minH: 22f, prefH: 22f);
            var ph = UIFactory.HStack(progRow, 8f);
            var sliderRt = UIFactory.Node(progRow, "ProgressSlider");
            UIFactory.Size(sliderRt.gameObject, flexW: 1f, minH: 20f, prefH: 20f);
            _progress = BuildProgressSlider(sliderRt);
            _progressLabel = UIFactory.Label(progRow, "0%", Context.Theme, 12f, true);
            UIFactory.Size(_progressLabel.gameObject, minW: 44f, prefW: 44f, flexW: 0f);
            _progressLabel.alignment = TextAlignmentOptions.MidlineRight;
            _clockText = UIFactory.Label(progRow, "00:00 / 00:00", Context.Theme, 12f, true);
            UIFactory.Size(_clockText.gameObject, minW: 108f, prefW: 108f, flexW: 0f);
            _clockText.alignment = TextAlignmentOptions.MidlineRight;

            // 控制行
            var ctrlRow = UIFactory.Node(root, "ControlRow");
            UIFactory.Size(ctrlRow.gameObject, minH: 30f, prefH: 30f);
            var ch = UIFactory.HStack(ctrlRow, 6f);

            IconButton(ctrlRow, "■", () => RunSafe(() => { Context.Sim.Stop(); Context.Facade.StopReplay(); }), "ui.ctrl.stop");
            IconButton(ctrlRow, "◀◀", () => RunSafe(() => Context.Sim.StepBackward()), "ui.ctrl.step_back");
            _playButton = IconButton(ctrlRow, "▶", () => RunSafe(() => Context.Sim.TogglePlayPause()), "ui.ctrl.play");
            IconButton(ctrlRow, "▶▶", () => RunSafe(() => Context.Sim.StepForward()), "ui.ctrl.step_fwd");
            IconButton(ctrlRow, "⏮", () => RunSafe(() => Context.Sim.StartFromLine(0)), "ui.ctrl.restart");

            UIFactory.Spacer(ctrlRow, 10f, 1f);

            var speedLabel = UIFactory.Label(ctrlRow, Context.T("ui.ctrl.speed"), Context.Theme, 12f, true);
            UIFactory.Size(speedLabel.gameObject, minW: 34f, prefW: 34f, flexW: 0f);
            RegisterTopText(speedLabel, "ui.ctrl.speed");

            var speedSliderRt = UIFactory.Node(ctrlRow, "SpeedSlider");
            UIFactory.Size(speedSliderRt.gameObject, minW: 130f, prefW: 130f, flexW: 0f, minH: 20f, prefH: 20f);
            var speed = BuildFlatSlider(speedSliderRt, 0.1f, 50f, Context.Sim.SpeedMultiplier, v =>
            {
                Context.Sim.SetSpeed(v);
                if (_speedLabel != null) _speedLabel.text = v.ToString("0.#") + "x";
                Context.Facade.SetReplaySpeed(v);
            });
            _speedLabel = UIFactory.Label(ctrlRow, Context.Sim.SpeedMultiplier.ToString("0.#") + "x", Context.Theme, 12f, true);
            UIFactory.Size(_speedLabel.gameObject, minW: 44f, prefW: 44f, flexW: 0f);

            UIFactory.Spacer(ctrlRow, 10f, 1f);
            _coordText = UIFactory.Label(ctrlRow, "", Context.Theme, 12f, true);
            UIFactory.Size(_coordText.gameObject, minW: 220f, prefW: 220f, flexW: 0f);
            _coordText.alignment = TextAlignmentOptions.MidlineRight;
            _coordText.enableWordWrapping = false;
            _coordText.overflowMode = TextOverflowModes.Ellipsis;

            // 状态提示（最底）
            _statusText = Context.Localization != null
                ? UIFactory.Text(bar.transform, "", Context.Theme, 11f, TextAlignmentOptions.MidlineLeft, true)
                : null;
            if (_statusText != null)
            {
                _statusText.rectTransform.anchorMin = new Vector2(0f, 0f);
                _statusText.rectTransform.anchorMax = new Vector2(1f, 0f);
                _statusText.rectTransform.pivot = new Vector2(0.5f, 0f);
                _statusText.rectTransform.sizeDelta = new Vector2(-20f, 16f);
                _statusText.rectTransform.anchoredPosition = new Vector2(0f, 1f);
                _statusText.gameObject.SetActive(false);
            }
        }

        private Button IconButton(Transform parent, string glyph, Action onClick, string tooltipKey)
        {
            var btn = UIFactory.IconButton(parent, glyph, Context.Theme, () => RunSafe(onClick), 30f,
                Context.T(tooltipKey));
            UIFactory.Size(btn.gameObject, minW: 34f, prefW: 34f, flexW: 0f);
            return btn;
        }

        private Slider BuildProgressSlider(RectTransform rt)
        {
            var slider = rt.gameObject.AddComponent<Slider>();
            var bg = UIFactory.Panel(rt, "Background", Context.Theme.Button, false);
            bg.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(0f, 8f);
            var fill = UIFactory.Panel(rt, "Fill", Context.Theme.Accent, false);
            fill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            fill.rectTransform.sizeDelta = new Vector2(0f, 8f);
            slider.fillRect = fill.rectTransform;
            slider.targetGraphic = bg;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(v =>
            {
                if (_updatingProgress) return;
                RunSafe(() => { Context.Sim.SeekProgress(v); Context.Facade.SeekReplay(v); });
            });
            return slider;
        }

        private Slider BuildFlatSlider(RectTransform rt, float min, float max, float value, Action<float> onChange)
        {
            var slider = rt.gameObject.AddComponent<Slider>();
            var bg = UIFactory.Panel(rt, "Background", Context.Theme.Button, false);
            bg.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(0f, 6f);
            var handle = UIFactory.Panel(rt, "Handle", Context.Theme.Text, false);
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(14f, 14f);
            var fill = UIFactory.Panel(rt, "Fill", Context.Theme.Accent, false);
            fill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            fill.rectTransform.sizeDelta = new Vector2(0f, 6f);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.transition = Selectable.Transition.None;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v => RunSafe(() => onChange(v)));
            return slider;
        }

        // ---------------- 浮动视图控件 ----------------

        private void BuildViewControls()
        {
            var box = UIFactory.Panel(_canvasRoot, "ViewControls", new Color(0f, 0f, 0f, 0.35f));
            _viewControls = box.rectTransform;
            _viewControls.anchorMin = new Vector2(1f, 0.5f);
            _viewControls.anchorMax = new Vector2(1f, 0.5f);
            _viewControls.pivot = new Vector2(1f, 0.5f);
            _viewControls.sizeDelta = new Vector2(52f, 0f);
            _viewControls.anchoredPosition = new Vector2(-8f, 0f);
            var fitter = box.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = UIFactory.VStack(_viewControls, 4f, new RectOffset(4, 4, 6, 6));
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            ViewButton("俯", () => Context.Camera?.SetView(ViewPreset.Top), "ui.view.top");
            ViewButton("前", () => Context.Camera?.SetView(ViewPreset.Front), "ui.view.front");
            ViewButton("侧", () => Context.Camera?.SetView(ViewPreset.Side), "ui.view.side");
            ViewButton("等", () => Context.Camera?.SetView(ViewPreset.Isometric), "ui.view.iso");
            UIFactory.HLine(_viewControls, Context.Theme.Divider);
            ViewButton("＋", () => Context.Camera?.Zoom(0.8f), null);
            ViewButton("－", () => Context.Camera?.Zoom(1.25f), null);
            UIFactory.HLine(_viewControls, Context.Theme.Divider);
            ViewButton("⤢", () => Context.Sim.FrameWorkpiece(CurrentView()), "ui.view.fit");
        }

        private ViewPreset CurrentView() => ViewPreset.Isometric;

        private Button ViewButton(string glyph, Action onClick, string tipKey)
        {
            var btn = UIFactory.IconButton(_viewControls, glyph, Context.Theme, () => RunSafe(onClick), 40f,
                tipKey == null ? null : Context.T(tipKey));
            UIFactory.Size(btn.gameObject, minH: 40f, prefH: 40f, minW: 44f, prefW: 44f, flexW: 0f);
            return btn;
        }

        private void BuildTooltip()
        {
            var box = UIFactory.Panel(_canvasRoot, "Tooltip", Context.Theme.PanelAlt);
            box.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            box.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            box.rectTransform.pivot = new Vector2(0.5f, 0f);
            box.rectTransform.sizeDelta = new Vector2(360f, 28f);
            box.rectTransform.anchoredPosition = new Vector2(0f, UIFactory.BottomBarHeight + 8f);
            var le = box.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var t = UIFactory.Text(box.transform, "", Context.Theme, 12f, TextAlignmentOptions.Center, false);
            UIFactory.Stretch(t);
            SimpleTooltip.Bind(t);
        }

        // ---------------- 事件 ----------------

        private void OnProgress(double p)
        {
            _updatingProgress = true;
            if (_progress != null) _progress.value = (float)Mathf.Clamp01((float)p);
            if (_progressLabel != null) _progressLabel.text = (p * 100.0).ToString("0") + "%";
            _updatingProgress = false;
        }

        private void OnStateChanged(SimulationState state)
        {
            var glyph = state == SimulationState.Running ? "❚❚" : "▶";
            var text = _playButton != null ? _playButton.GetComponentInChildren<TextMeshProUGUI>() : null;
            if (text != null) text.text = glyph;
            SetStatus(Context.T("ui.status." + state.ToString().ToLowerInvariant()));
        }

        private void OnFinished()
        {
            SetStatus(Context.T("ui.status.finished"));
            OnProgress(1.0);
        }

        private void OnFrame(SimulationFrame f)
        {
            if (_coordText != null && f.Valid)
            {
                _coordText.text = string.Format("X {0:0.##}  Y {1:0.##}  Z {2:0.##}  A {3:0.#}  B {4:0.#}  C {5:0.#}  F {6:0}",
                    f.Position.X, f.Position.Y, f.Position.Z, f.Axes.A, f.Axes.B, f.Axes.C, f.Feed);
            }
            if (_clockText != null)
            {
                var total = Context.Facade?.AnalyzeTime()?.Total ?? 0;
                _clockText.text = FormatTime(f.ElapsedTime) + " / " + FormatTime(total);
            }
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
        }

        // ---------------- 语言/主题 ----------------

        private void OnLanguageChanged(string lang)
        {
            RefreshAllTexts();
        }

        private void OnThemeChangedInternal(CncSim.Core.Theming.ThemeManager theme)
        {
            // 主题变化：重建整套 UI 以换肤（简单可靠）
            Rebuild();
        }

        private void RefreshAllTexts()
        {
            // 顶栏标签
            foreach (var key in _topTextBindings.Keys)
            {
                var (label, keyObj, custom) = _topTextBindings[key];
                if (label == null) continue;
                label.text = custom != null ? custom() : Context.T(keyObj);
            }
            if (_machineLabel != null) UpdateMachineLabel();
            foreach (var kv in _tabTexts) kv.Value.text = TabTitle(kv.Key);
            foreach (var kv in _panels) kv.Value.RefreshTexts();
        }

        private readonly Dictionary<int, (TextMeshProUGUI label, string key, Func<string> custom)> _topTextBindings
            = new Dictionary<int, (TextMeshProUGUI, string, Func<string>)>();

        private void RegisterTopText(TextMeshProUGUI label, string key, Func<string> custom = null)
        {
            if (label == null) return;
            _topTextBindings[label.GetInstanceID()] = (label, key, custom);
        }

        private void UpdateMachineLabel()
        {
            if (_machineLabel == null || Context.Sim?.Sim == null) return;
            var type = Context.Sim.Sim.Machine.Type;
            string key = type switch
            {
                MachineType.FourAxis => "ui.machine.4axis",
                MachineType.FiveAxis => "ui.machine.5axis",
                MachineType.Custom => "ui.machine.custom",
                _ => "ui.machine.3axis"
            };
            var kin = Context.Sim.Sim.Machine.Kinematics;
            string extra = type == MachineType.FiveAxis ? " · " + kin.ToString() : "";
            _machineLabel.text = Context.T(key) + extra;
        }

        private void Rebuild()
        {
            var tab = ActiveTab;
            _panels.Clear();
            _tabButtons.Clear();
            _tabTexts.Clear();
            _topTextBindings.Clear();
            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                Destroy(_canvasRoot.GetChild(i).gameObject);

            Context.Theme = CncUiTheme.From(Context.Localization?.Theme);
            if (_editorView != null)
            {
                Destroy(_editorView);
                _editorView = null;
            }
            BuildTopBar();
            BuildTabRail();
            BuildPanelHost();
            BuildBottomBar();
            BuildViewControls();
            BuildTooltip();
            BuildEditorView();
            SelectTab(tab);
            UpdateMachineLabel();
        }

        // ---------------- 工具 ----------------

        private void SetStatus(string text)
        {
            if (_statusText == null) return;
            _statusText.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _statusText.text = text;
        }

        private static void RunSafe(Action action)
        {
            try { action?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("UI action failed: " + e.Message); }
        }

        private void OnDestroy()
        {
            if (Context?.Localization != null)
            {
                Context.Localization.LanguageChanged -= OnLanguageChanged;
                Context.Localization.ThemeChanged -= OnThemeChangedInternal;
            }
            if (Context?.Sim != null)
            {
                Context.Sim.ProgressChanged -= OnProgress;
                Context.Sim.StateChanged -= OnStateChanged;
                Context.Sim.FrameUpdated -= OnFrame;
                Context.Sim.Finished -= OnFinished;
            }
        }
    }
}
