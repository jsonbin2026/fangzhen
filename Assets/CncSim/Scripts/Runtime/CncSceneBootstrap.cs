using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Runtime
{
    /// <summary>
    /// 场景一键搭建器：把相机、光照、网格、实体渲染、轨迹、仿真、UI 桥接组成完整运行场景。
    /// 用法：新建空场景，添加一个空 GameObject，挂上本组件，在 Inspector 里配置可选参数，
    /// 运行后即可通过公开方法/事件接入你自己写的 UI。
    /// 这样 UI 布局由你决定，本组件只负责功能装配。
    /// </summary>
    public class CncSceneBootstrap : MonoBehaviour
    {
        [Header("Machine & Workpiece")]
        public bool UseFiveAxis;
        public FiveAxisKinematics FiveAxisKind = FiveAxisKinematics.TableTable;
        public Vec3d BlankSize = new Vec3d(120, 80, 30);
        public BlankShape BlankShape = BlankShape.Box;
        public string MaterialId = "aluminum";

        [Header("Camera")]
        public bool CreateCamera = true;
        public ViewPreset InitialView = ViewPreset.Isometric;

        [Header("Scene")]
        public bool CreateGrid = true;
        public float GridExtent = 400f;
        public Color BackgroundColor = new Color(0.12f, 0.13f, 0.15f);

        [Header("UI")]
        [Tooltip("是否自动创建整套 UGUI 界面（CncUIRoot）。关闭则自建 UI。")]
        public bool CreateUI = true;

        [Header("Toolpath")]
        public Core.Trajectory.TrajectoryStyle InitialTrajectoryStyle = Core.Trajectory.TrajectoryStyle.Lines;

        // 组装后的组件
        public CncSimBehaviour Simulator { get; private set; }
        public CameraRig Rig { get; private set; }
        public SceneEntityRenderer Entities { get; private set; }
        public ToolpathRenderer Toolpath { get; private set; }
        public MeasurementTool Measurement { get; private set; }
        public SectionView Section { get; private set; }
        public TransparencyController Transparency { get; private set; }
        public UILocalizationBridge Localization { get; private set; }
        public SceneGrid Grid { get; private set; }
        public CncUIFacade UI { get; private set; }
        public RecordReplayController RecordReplay { get; private set; }
        public CaptureService Capture { get; private set; }
        public ModelImportService ModelImport { get; private set; }

        private void Awake()
        {
            BuildScene();
        }

        private void BuildScene()
        {
            // 相机
            Camera cam;
            if (CreateCamera && Camera.main == null)
            {
                var camGo = new GameObject("CncCamera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.transform.position = new Vector3(0, 300, -400);
            }
            else
            {
                cam = Camera.main;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            Rig = cam.gameObject.GetComponent<CameraRig>() ?? cam.gameObject.AddComponent<CameraRig>();

            // 光照
            if (FindLight() == null)
            {
                var lightGo = new GameObject("CncLight");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                lightGo.transform.SetParent(transform, false);
            }

            // 网格与坐标轴
            if (CreateGrid)
            {
                var gridGo = new GameObject("SceneGrid");
                gridGo.transform.SetParent(transform, false);
                Grid = gridGo.AddComponent<SceneGrid>();
                Grid.Extent = GridExtent;
            }

            // 实体渲染
            var entityGo = new GameObject("SceneEntities");
            entityGo.transform.SetParent(transform, false);
            Entities = entityGo.AddComponent<SceneEntityRenderer>();

            // 轨迹
            var pathGo = new GameObject("Toolpath");
            pathGo.transform.SetParent(transform, false);
            Toolpath = pathGo.AddComponent<ToolpathRenderer>();
            Toolpath.Style = InitialTrajectoryStyle;

            // 仿真桥
            Simulator = gameObject.GetComponent<CncSimBehaviour>() ?? gameObject.AddComponent<CncSimBehaviour>();
            Simulator.CameraRig = Rig;
            Simulator.Entities = Entities;
            Simulator.Toolpath = Toolpath;

            ConfigureMachineAndStock();

            Entities.Simulator = Simulator;
            Toolpath.Simulator = Simulator;

            // 测量
            var measureGo = new GameObject("Measurement");
            measureGo.transform.SetParent(transform, false);
            Measurement = measureGo.AddComponent<MeasurementTool>();
            Measurement.RayCamera = cam;

            // 截面
            var sectionGo = new GameObject("SectionView");
            sectionGo.transform.SetParent(transform, false);
            Section = sectionGo.AddComponent<SectionView>();

            // 透明度
            var transGo = new GameObject("Transparency");
            transGo.transform.SetParent(transform, false);
            Transparency = transGo.AddComponent<TransparencyController>();

            // UI / 语言桥
            Localization = gameObject.GetComponent<UILocalizationBridge>() ?? gameObject.AddComponent<UILocalizationBridge>();
            Localization.Simulator = Simulator;
            Localization.TargetCamera = cam;

            // UI 功能门面
            UI = gameObject.GetComponent<CncUIFacade>() ?? gameObject.AddComponent<CncUIFacade>();

            // 录制/回放
            RecordReplay = gameObject.GetComponent<RecordReplayController>() ?? gameObject.AddComponent<RecordReplayController>();
            RecordReplay.Simulator = Simulator;

            // 截图/视频
            Capture = gameObject.GetComponent<CaptureService>() ?? gameObject.AddComponent<CaptureService>();
            Capture.TargetCamera = cam;

            // 模型导入
            ModelImport = gameObject.GetComponent<ModelImportService>() ?? gameObject.AddComponent<ModelImportService>();

            UI.Simulator = Simulator;
            UI.Localization = Localization;
            UI.RecordReplay = RecordReplay;
            UI.Capture = Capture;
            UI.ModelImport = ModelImport;
            UI.Initialize();

            Entities.RebuildAll();
            Simulator.FrameWorkpiece(InitialView);

            // 自动装配界面（用户也可自行挂 CncUIRoot 或自写 UI）
            if (CreateUI)
            {
                var uiRoot = gameObject.GetComponent<CncSim.Runtime.UI.CncUIRoot>()
                             ?? gameObject.AddComponent<CncSim.Runtime.UI.CncUIRoot>();
                uiRoot.Bootstrap = this;
            }
        }

        private Light FindLight()
        {
            return Object.FindObjectOfType<Light>();
        }

        private void ConfigureMachineAndStock()
        {
            var sim = Simulator.Sim;
            sim.Machine = UseFiveAxis
                ? MachineProfile.Create5Axis(FiveAxisKind)
                : MachineProfile.Create3Axis();
            sim.Blank.Shape = BlankShape;
            sim.Blank.Size = BlankSize;
            sim.Blank.Center = new Vec3d(-BlankSize.X * 0.5, -BlankSize.Y * 0.5, -BlankSize.Z);
            sim.SetMaterial(MaterialId);
            sim.Tools = ToolLibrary.CreateDefault();
        }

        /// <summary>加载 G 代码并刷新场景。</summary>
        public void LoadProgram(string gcode)
        {
            Simulator.LoadProgram(gcode);
        }

        /// <summary>采集所有需要截面/透明的渲染器，一键应用。</summary>
        public void CaptureRenderTargets()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var list = new List<Renderer>(renderers);
            if (Section != null) Section.Targets = list.ToArray();
            if (Transparency != null) Transparency.Targets = list.ToArray();
            if (Measurement != null)
            {
                Measurement.MeshProviders = new MonoBehaviour[] { Entities };
            }
        }

        private void OnValidate()
        {
            if (BlankSize.X <= 0) BlankSize.X = 1;
            if (BlankSize.Y <= 0) BlankSize.Y = 1;
            if (BlankSize.Z <= 0) BlankSize.Z = 1;
        }
    }
}
