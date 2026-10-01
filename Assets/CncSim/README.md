# CNC 加工仿真系统 — Unity3D 集成说明

本工程分两层：

- `Assets/CncSim/Scripts/Core/` — 与引擎无关的功能内核（纯 C#，`noEngineReferences`），输出 `MeshData` / `Float3` / `ColorRgba`。
- `Assets/CncSim/Scripts/Runtime/` — Unity 表现层（MonoBehaviour、Mesh、材质、相机交互），把 Core 数据转成 Unity 对象。

UI 布局由你自行编写；Runtime 层只提供功能装配与事件接口。

## 目录与功能对照

| 目录/文件 | 覆盖功能 |
|---|---|
| `Core/CodeEditor/` | 1-8 编辑器、语法高亮、行号、文件、最近文件、导入导出、自动保存恢复 |
| `Core/Parsing/` | 9-17 解析、G/M 指令、坐标提取、运动队列、插补、错误检查、超程、单行解释 |
| `Core/Tooling/`、`Core/Stock/` | 18-30 刀具库、类型/直径/圆弧/长度/伸出/T 号、毛坯尺寸形状材料、G54-G59、夹具、对刀 |
| `Core/Kinematics/`、`Core/Parsing/MachineProfile.cs`、`Core/MachineModel/` | 31、32-36、81-83 机床类型、运动学、自定义机床、各部件几何 |
| `Runtime/Scene/SceneEntityRenderer.cs` | 32-36 主轴/工作台/床身/夹具/刀具/毛坯三维模型、80 转台/摆头零件 |
| `Runtime/Rendering/ToolpathRenderer.cs` | 37-38 轨迹实时显示、线框/实体切换 |
| `Runtime/Camera/CameraRig.cs` | 39-40 多视角、缩放/旋转/平移 |
| `Runtime/Scene/TransparencyController.cs` | 41 透明度 |
| `Runtime/Scene/SectionView.cs` | 42 截面视图 |
| `Runtime/Interaction/MeasurementTool.cs` | 43 距离/角度测量 |
| `Runtime/Scene/SceneGrid.cs` | 44 坐标系/网格 |
| `Core/Simulation/`、`Runtime/CncSimBehaviour.cs` | 45-55 连续/单步/暂停/停止、速度、进度、指定行开始、材料去除、表面更新、切削动画 |
| `Core/Collision/`、`Core/Validation/`、`Core/Analysis/` | 56-61 碰撞、过切/欠切、限位 |
| `Core/Simulation/TimeEstimator.cs`、`CuttingCalculator.cs` | 63-67 时间/切削速度/进给/转速/参数推荐 |
| `Core/Parsing/CodeDatabase.cs` | 68-69 G/M 速查 |
| `Core/Optimization/` | 70 路径优化建议 |
| `Core/PostProcessing/` | 84 自定义后处理 |
| `Core/Performance/`、`Runtime/Rendering/RenderBatcher.cs` | 85-90 后台预计算、网格简化、批量合并、GPU 实例化、自适应精度、低多边形 |
| `Core/Localization/`、`Runtime/UILocalizationBridge.cs` | 92 多语言 |
| `Core/Theming/`、`UILocalizationBridge` | 93 暗/亮主题 |
| `Core/Tutorial/` | 94 内置教程 |
| `Core/Recording/`、`Runtime/Recording/RecordReplayController.cs` | 51、62 仿真录制、回放、加工结果保存/载入 |
| `Core/Import/`、`Runtime/Import/ModelImportService.cs` | 71-74 导入 STL/OBJ（Core 解析）、STEP/glTF/FBX（Unity 资源管线）、毛坯/夹具应用 |
| `Runtime/Capture/CaptureService.cs` | 75-76 导出视频帧序列、截图 |
| `Core/Projects/`、`CncUIFacade.Projects` | 77 多项目管理（新建/保存/载入/复制/重命名） |
| `Core/Kinematics/`、`Core/MachineModel/`、`SceneEntityRenderer.UpdateRotaryPose` | 80 五轴仿真（双转台/摆头+转台/双摆头、转台与摆头零件联动） |
| `Runtime/CncUIFacade.cs` | 1-8 文件/编辑器、15/17 诊断与解释、18-30 刀具毛坯对刀、51/62 录制回放、63-70 分析与手册、71-80 导入/项目/多轴、84 后处理、94 教程，统一暴露给 UI |

## 快速开始

1. 新建 Unity 场景，创建一个空 GameObject。
2. 挂上 `CncSceneBootstrap`，按需配置机床类型、毛坯尺寸、材料、初始视角。
3. 运行，然后从你的 UI 代码调用：

```csharp
// 加载 G 代码
bootstrap.LoadProgram(gcodeText);

// 仿真控制
bootstrap.Simulator.Play();
bootstrap.Simulator.SetSpeed(10f);
bootstrap.Simulator.Pause();
bootstrap.Simulator.StepForward();
bootstrap.Simulator.StartFromLine(42);
bootstrap.Simulator.SeekProgress(0.5);

// 视角
bootstrap.Rig.SetView(ViewPreset.Top);
bootstrap.Rig.FrameAll(...); // 或 bootstrap.Simulator.FrameWorkpiece()

// 轨迹样式
bootstrap.Toolpath.ToggleStyle();     // 线框 / 实体
bootstrap.Toolpath.SetVisible(false);

// 截面 / 透明度
bootstrap.CaptureRenderTargets();      // 采集场景渲染器
bootstrap.Section.SetEnabled(true);
bootstrap.Section.SetAxis(SectionView.SectionAxis.Z);
bootstrap.Section.SetOffset(0f);
bootstrap.Transparency.SetOpacity(0.4f);

// 测量
bootstrap.Measurement.SetType(MeasurementTool.MeasureType.Distance);
bootstrap.Measurement.TryPick(screenPos); // 取点
float mm = bootstrap.Measurement.DistanceMm;

// 语言 / 主题
bootstrap.Localization.SetLanguage("en-US");
bootstrap.Localization.ToggleTheme();
```

## 自带 UGUI 界面（CncUIRoot）

`CncSceneBootstrap` 默认 `CreateUI = true`，会在运行时用代码生成一整套自适应 UGUI 界面，
无需任何 Prefab，横屏/竖屏自适应（CanvasScaler 参考分辨率 1280x720）。挂上 bootstrap 即可看到：

- 顶栏：标题、机床类型、语言（中文/EN）、主题切换
- 顶栏右侧 `</>` 按钮：开关内置 G 代码编辑器（行号 + 语法高亮，点击行选中、
  底部输入框编辑并写回，仿真时高亮当前执行行，错误行行号标红）
- 编辑器顶部查找/替换栏：查找并上一个/下一个跳转、替换、全部替换
- 编辑器左侧断点槽：点击圆点增删断点，仿真运行到断点行自动暂停（`SimulationController.Breakpoints`）
- 左侧标签栏 + 可折叠面板区：文件、仿真、刀具、毛坯、分析、场景、录制/媒体、项目、教程、手册
- 底部播放条：停止/单步/播放/从头、速度滑杆、进度滑条、坐标与状态读数
- 右侧浮动视图控件：俯/前/侧/等轴测、缩放、适配

```csharp
// 关闭自带 UI，改用你自己写的界面
bootstrap.CreateUI = false;

// 或手动挂载并指定
var root = gameObject.AddComponent<CncSim.Runtime.UI.CncUIRoot>();
root.Bootstrap = bootstrap;
root.DefaultTab = CncSim.Runtime.UI.CncUIRoot.PanelId.Simulation;
```

界面元素与功能对照：文件面板（1-8）、仿真面板（45-55, 85-90）、刀具面板（18-24）、
毛坯面板（25-30）、分析面板（56-70）、场景面板（32-44, 80）、媒体面板（51/62/75/76）、
项目面板（77）、教程面板（94）、手册面板（68-69）。

自定义扩展：继承 `CncSim.Runtime.UI.CncUiPanel`，实现 `Build()`，通过 `Ctx.Facade` 访问功能、
`Section/Btn/Chk/Sld` 组合控件；在 `CncUIRoot.CreatePanel` 里注册即可。

## UI 功能门面（CncUIFacade）
界面层优先通过 `bootstrap.UI`（`CncUIFacade`）访问功能，它把文件、诊断、刀具、毛坯、对刀、
分析、速查手册、后处理、语言、教程统一暴露：

```csharp
var ui = bootstrap.UI;

// 文件（1-8）
ui.NewFile();
ui.OpenFile(path);
ui.SaveFile();
ui.SaveFileAs(path);
string sharePath = ui.ExportForShare();
var recent = ui.GetRecentFiles();
foreach (var s in ui.GetRecoverableSessions()) ui.RecoverSession(s);

// 诊断与解释（15/17）
var diags = ui.Diagnostics;          // DiagnosticList，含 ErrorCount/WarningCount
var exp = ui.ExplainLine(lineIndex); // 单行中文/英文解释

// 刀具（18-24）
ui.AddTool(tool);
ui.GetOrCreateTool(3).Diameter = 8;

// 毛坯/材料/工件系/对刀（25-30）
ui.SetBlankSize(new Vec3d(120, 80, 30));
ui.SetMaterial("steel_1045");
ui.SetWorkOffset(0, new Vec3d(0, 0, -30)); // G54
var origin = ui.ComputeOriginFromTouchPoints(x, y, z, probeR, nominal);

// 分析与手册（56-61, 63-70）
var time = ui.AnalyzeTime();
var collisions = ui.DetectCollisions();
var suggestions = ui.OptimizePath();
var manual = ui.Manual();            // (titleKey, List<CodeInfo>)[]

// 后处理（84）
ui.UsePostProcessorPreset("fanuc");
string rewritten = ui.PostProcess("1234");

// 教程（94）
var session = ui.StartTutorial("getting-started");
session.Next();

// 录制/回放（51, 62）
ui.StartRecording();
ui.StopRecording();
ui.SaveRecording();                  // 保存到 persistentDataPath/Recordings
ui.LoadRecording(path);
ui.StartReplay();
ui.SeekReplay(0.5);                  // 进度 0-1
ui.SetReplaySpeed(2f);

// 截图 / 视频（75, 76）
string png = ui.SaveScreenshot();
ui.StartVideoCapture();
ui.StopVideoCapture();

// 模型导入（71-74）
var model = ui.ImportModel(path, ModelRole.Workpiece); // STL/OBJ 直接解析
ui.ImportModelFromGameObject(fbxRoot, ModelRole.Fixture); // FBX/glTF 走 Unity 资源

// 多项目管理（77）
var project = ui.CreateProject("Part A");
ui.SaveToProject(project);           // 把当前 GCode/毛坯/刀具写入项目并落盘
ui.LoadFromProject(project);         // 载入项目到当前仿真

// 五轴/多轴机床（80）
ui.SetMachineType(MachineType.FiveAxis, FiveAxisKinematics.HeadTable);
var parts = Simulator.Entities;      // RotaryTableTransform / SwivelHeadTransform 随 A/B/C 角联动
```

`CncUIFacade` 事件：`DiagnosticsChanged(DiagnosticList)`、`Reanalyzed(ParseResult)`。

## 事件（供 UI 订阅）

`CncSimBehaviour`：

- `ProgressChanged(double)` — 仿真进度 0-1，驱动进度条。
- `StateChanged(SimulationState)` — 播放/暂停/停止状态。
- `FrameUpdated(SimulationFrame)` — 每帧刀尖位置、进给、转速、当前段、当前行。
- `Finished()` — 程序执行完成。

`MeasurementTool.Measured(MeasureType, float)` — 测量结果（mm / 度）。

`UILocalizationBridge.LanguageChanged(string)`、`ThemeChanged(ThemeManager)` — 语言/主题刷新。

## 坐标系约定（重要）

- Core 使用右手系、Z 向上、单位 mm。
- Unity 使用左手系、Y 向上。
- `MeshBuilder.ToUnity` 做 `(x, y, z) -> (x, z, y)` 映射，并翻转三角形绕序；`ToCnc` 为逆变换。
- 一切跨层坐标转换都必须经过 `MeshBuilder`，不要在业务代码里手写轴交换。

## 材料去除实时更新

`CncSimBehaviour.LiveRemoval` 打开时，仿真推进过程中按 `RemovalRefreshInterval` 间隔调用
`SceneEntityRenderer.UpdateRemovalMesh()` 刷新去除网格（功能 54）。若追求流畅，可增大间隔或
关闭实时、改为暂停时刷新。

## 性能档位

`CncSimBehaviour.Sim.Performance.ApplyPreset(DetailLevel.LowPoly | Balanced | HighFidelity)`
控制去除分辨率、轨迹弦高、管道侧面数、毛坯分段与网格简化阈值。

## 自定义 Shader 提示

截面视图要求 Shader 读取 `_ClipPlane`（float4：xyz 法线、w 距离）与
`_ClipEnabled`（0/1）。默认会尝试 Standard，如需真正的剖切效果，请在你的 Shader 中做
`clip(dot(worldPos, _ClipPlane.xyz) - _ClipPlane.w)`。

## 导出 Android APK

本工程是 Unity 工程，APK 需在装有 Unity Editor + Android 模块的机器上导出（CI 或本地）。
本仓库/沙箱环境没有 Unity 与 Android SDK，无法直接产出 APK。步骤如下：

1. Unity Hub 里为当前 Unity 版本安装 `Android Build Support`（含 SDK & NDK、OpenJDK）。
2. 打开工程，`File > Build Settings`，Platform 选 `Android`，点 `Switch Platform`。
3. `Player Settings`：
   - `Company Name` / `Product Name`
   - `Other Settings > Scripting Backend` 选 `IL2CPP`
   - `Target Architectures` 勾 `ARM64`（Google Play 必需），可加 `ARMv7`
   - `Minimum API Level` 建议 24 及以上
   - `Orientation`：按需选 Landscape / Portrait / Auto Rotation（UI 已自适应）
4. 场景列表加入包含 `CncSceneBootstrap` 的场景：`Add Open Scenes`。
5. `Build` 生成 APK，或勾 `Build App Bundle (Google Play)` 出 AAB。
6. 安装到设备：`adb install -r yourapp.apk`。

命令行（可选，需设置好 Unity 与 Android SDK 环境变量）：

```bash
Unity -quit -batchmode -nographics -projectPath /path/to/project \
  -executeMethod CncSim.EditorTools.BuildAndroid.Build -logFile build.log
```

产物输出到 `build/Android/CncSim.apk`。

### 云端构建（无需本地 Unity）
仓库内置 GitHub Actions（`.github/workflows/android.yml`），使用 `game-ci/unity-builder`：
1. 在仓库 `Settings > Secrets and variables > Actions` 添加 `UNITY_LICENSE`（或 `UNITY_EMAIL` + `UNITY_PASSWORD`）
2. 推送到 `master` 或手动触发 workflow
3. 构建完成后在 Actions 运行的 `Artifacts` 里下载 `CncSim-Android-APK`

### 工程约定
- 引擎版本：Unity **2022.3.62f1**（LTS），见 `ProjectSettings/ProjectVersion.txt`
- 主场景：`Assets/Scenes/Main.unity`，唯一入口物体挂 `CncSceneBootstrap`，运行后自动搭建相机/光照/仿真/UI
- 构建脚本：`Assets/Editor/BuildAndroid.cs`，构建前会自动导入 TextMeshPro Essential Resources

注意：TextMeshPro 若缺少 Essential Resources，运行期文本无法显示；中文字体需生成包含中文字形的
TMP Font Asset 并在 `TMP_Settings` 里设为默认，否则中文会显示为方块。
