using CncSim.Core;
using CncSim.Core.Localization;
using CncSim.Core.Theming;
using UnityEngine;
using UnityEngine.UI;

namespace CncSim.Runtime.UI
{
    /// <summary>
    /// UI 上下文：把所有 UI 面板需要的引用集中在一起，避免每个面板各自查找。
    /// 由 CncUIRoot 在构建时填充，主题/语言变化时统一广播。
    /// </summary>
    public class CncUiContext
    {
        public CncSceneBootstrap Bootstrap;
        public CncSimBehaviour Sim;
        public CncUIFacade Facade;
        public UILocalizationBridge Localization;
        public CameraRig Camera;
        public SceneEntityRenderer Entities;
        public ToolpathRenderer Toolpath;
        public SectionView Section;
        public TransparencyController Transparency;
        public MeasurementTool Measurement;
        public SceneGrid Grid;
        public RecordReplayController RecordReplay;
        public CaptureService Capture;
        public ModelImportService ModelImport;

        public CncUiTheme Theme;
        /// <summary>Canvas 根，供面板建立全屏遮罩/弹窗。</summary>
        public RectTransform CanvasRoot;
        /// <summary>底部状态栏文本更新入口，由 Root 提供。</summary>
        public System.Action<string> SetStatus;

        public string T(string key) => Localization != null ? Localization.T(key) : Loc.Get(key);
        public string T(string key, params object[] args) =>
            Localization != null ? Localization.T(key, args) : Loc.Format(key, args);

        public Color Hex(string hex) => MeshBuilder.ToUnity(ColorRgba.FromHex(hex));
    }
}
