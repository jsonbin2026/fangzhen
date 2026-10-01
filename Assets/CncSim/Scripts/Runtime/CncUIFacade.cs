using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.Analysis;
using CncSim.Core.Collision;
using CncSim.Core.CodeEditor;
using CncSim.Core.Import;
using CncSim.Core.Localization;
using CncSim.Core.Optimization;
using CncSim.Core.Parsing;
using CncSim.Core.PostProcessing;
using CncSim.Core.Projects;
using CncSim.Core.Recording;
using CncSim.Core.Simulation;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;
using CncSim.Core.Tutorial;

namespace CncSim.Runtime
{
    /// <summary>
    /// UI 功能门面（供你写的界面直接调用）。覆盖尚未由可视化组件暴露的功能：
    /// - 1-8  编辑器/文件：新建、打开、保存、另存、导入、导出/分享、最近文件、自动保存/恢复
    /// - 15/17 诊断列表、单行解释
    /// - 18-30 刀具 CRUD、毛坯、材料、工件坐标系、夹具、对刀
    /// - 63-67 时间/参数分析
    /// - 68-69 G/M 速查手册
    /// - 70 路径优化、56-61 碰撞与限位
    /// - 84 自定义后处理
    /// - 92/93 语言/主题
    /// - 94 教程
    /// 把所有 UI 数据绑定写在这里，界面层只需读属性、订阅事件、调方法。
    /// </summary>
    public class CncUIFacade : MonoBehaviour
    {
        [Header("References")]
        public CncSimBehaviour Simulator;
        public UILocalizationBridge Localization;
        public RecordReplayController RecordReplay;
        public CaptureService Capture;
        public ModelImportService ModelImport;

        [Header("Storage")]
        [Tooltip("最近文件与恢复文件的存储目录（留空使用 persistentDataPath）")]
        public string StorageDirectory;

        [Header("Projects")]
        [Tooltip("多项目管理目录（留空使用 persistentDataPath）")]
        public string ProjectDirectory;

        [Header("Auto Save")]
        public bool EnableAutoSave = true;

        public GCodeFileService FileService { get; private set; }
        public AutoSaveService AutoSave { get; private set; }
        public RecentFilesList RecentFiles => FileService?.RecentFiles;
        public ProjectManager Projects { get; private set; }

        /// <summary>诊断/解释/分析刷新事件（解析完成后触发）。</summary>
        public event Action<ParseResult> Reanalyzed;
        /// <summary>诊断变化。</summary>
        public event Action<DiagnosticList> DiagnosticsChanged;

        private void Awake()
        {
            if (Simulator == null) Simulator = GetComponent<CncSimBehaviour>();
            if (Localization == null) Localization = GetComponent<UILocalizationBridge>();
            if (RecordReplay == null) RecordReplay = GetComponent<RecordReplayController>();
            if (Capture == null) Capture = GetComponent<CaptureService>();
            if (ModelImport == null) ModelImport = GetComponent<ModelImportService>();
            Initialize();
        }

        /// <summary>创建文件/项目服务（幂等）。当组件在运行时被重新绑定引用后可再次调用。</summary>
        public void Initialize()
        {
            string dir = string.IsNullOrEmpty(StorageDirectory)
                ? Path.Combine(Application.persistentDataPath, "CncSim")
                : StorageDirectory;
            Directory.CreateDirectory(dir);

            FileService ??= new GCodeFileService(new RecentFilesList(Path.Combine(dir, "recent.txt")));
            AutoSave ??= new AutoSaveService(Path.Combine(dir, "recovery"));
            if (Simulator?.Sim != null)
            {
                AutoSave.Attach(Simulator.Sim.Document);
                AutoSave.Enabled = EnableAutoSave;
            }

            string projDir = string.IsNullOrEmpty(ProjectDirectory)
                ? Path.Combine(dir, "Projects")
                : ProjectDirectory;
            Projects ??= new ProjectManager(projDir);
        }

        private void Update()
        {
            if (AutoSave != null && AutoSave.Enabled) AutoSave.Tick(DateTime.UtcNow);
        }

        private void OnApplicationQuit()
        {
            AutoSave?.EndSessionCleanly();
        }

        // ---------------- 文件操作（1-8）----------------

        public void NewFile(string template = null)
        {
            var doc = FileService.New(template);
            Simulator.Sim.SetDocument(doc);
            AutoSave.Attach(doc);
            Reparse();
        }

        public bool OpenFile(string path)
        {
            var result = FileService.Open(path);
            if (!result.Success) { Debug.LogWarning("Open failed: " + result.Error); return false; }
            Simulator.Sim.SetDocument(result.Value);
            AutoSave.Attach(result.Value);
            Reparse();
            return true;
        }

        public bool SaveFile()
        {
            var result = FileService.Save(Simulator.Sim.Document);
            return result.Success;
        }

        public bool SaveFileAs(string path)
        {
            var result = FileService.SaveAs(Simulator.Sim.Document, path);
            return result.Success;
        }

        public string ExportFile(string path, bool ensurePercent = true)
        {
            var result = FileService.Export(Simulator.Sim.Document, path, "\r\n", ensurePercent);
            return result.Success ? result.Value : null;
        }

        /// <summary>导出到缓存目录以用于分享（功能 7）。</summary>
        public string ExportForShare()
        {
            string cache = Path.Combine(Application.temporaryCachePath, "CncSimShare");
            var result = FileService.ExportForShare(Simulator.Sim.Document, cache);
            return result.Success ? result.Value : null;
        }

        public IReadOnlyList<string> GetRecentFiles() => RecentFiles?.Items ?? Array.Empty<string>();
        public void RemoveRecentFile(string path) => RecentFiles?.Remove(path);
        public void ClearRecentFiles() => RecentFiles?.Clear();

        /// <summary>获取可恢复会话（功能 8）。</summary>
        public List<RecoverySession> GetRecoverableSessions() => AutoSave.GetRecoverableSessions();

        public bool RecoverSession(RecoverySession session)
        {
            if (session == null) return false;
            var doc = AutoSave.Recover(session);
            Simulator.Sim.SetDocument(doc);
            AutoSave.Attach(doc);
            Reparse();
            return true;
        }

        // ---------------- 编辑与诊断（1-3, 15, 17）----------------

        public GCodeDocument Document => Simulator?.Sim?.Document;

        public void SetText(string text)
        {
            Document.SetText(text, recordUndo: true, markDirty: true);
            Reparse();
        }

        public bool CanUndo => Document.CanUndo;
        public bool CanRedo => Document.CanRedo;
        public void Undo() { Document.Undo(); Reparse(); }
        public void Redo() { Document.Redo(); Reparse(); }

        public List<(int line, int column)> Find(string pattern, bool ignoreCase = true) =>
            Document.FindAll(pattern, ignoreCase);
        public int ReplaceAll(string pattern, string replacement, bool ignoreCase = true) =>
            Document.ReplaceAll(pattern, replacement, ignoreCase);

        public DiagnosticList Diagnostics => Simulator?.Sim?.Program?.Diagnostics;
        public bool HasErrors => Diagnostics?.HasErrors ?? false;

        /// <summary>单行解释（功能 17）。</summary>
        public BlockExplanation ExplainLine(int lineIndex) => Simulator.Sim.ExplainLine(lineIndex);

        /// <summary>解析并广播分析结果。</summary>
        public ParseResult Reparse()
        {
            var result = Simulator.Sim.Parse(force: true);
            DiagnosticsChanged?.Invoke(result.Diagnostics);
            Reanalyzed?.Invoke(result);
            return result;
        }

        // ---------------- 刀具（18-24）----------------

        public ToolLibrary ToolLibrary => Simulator?.Sim?.Tools;
        public IEnumerable<ToolDefinition> Tools => ToolLibrary?.Tools;

        public ToolDefinition GetTool(int number) => ToolLibrary?.Get(number);
        public ToolDefinition GetOrCreateTool(int number) => ToolLibrary?.GetOrCreate(number);
        public void AddTool(ToolDefinition tool) { ToolLibrary?.Add(tool); Simulator.Entities?.SetToolNumber(Simulator.CurrentToolNumber); }
        public bool RemoveTool(int number) => ToolLibrary?.Remove(number) ?? false;

        // ---------------- 毛坯/材料/工件系/夹具/对刀（25-30）----------------

        public BlankDefinition Blank => Simulator?.Sim?.Blank;
        public FixtureDefinition Fixture => Simulator?.Sim?.Fixture;
        public MaterialDefinition Material => Simulator?.Sim?.Material;

        public void SetBlankShape(BlankShape shape) { Blank.Shape = shape; RefreshEntities(); }
        public void SetBlankSize(Vec3d size) { Blank.Size = size; RefreshEntities(); }
        public void SetBlankCenter(Vec3d center) { Blank.Center = center; RefreshEntities(); }

        public void SetMaterial(string materialId)
        {
            Simulator.Sim.SetMaterial(materialId);
            RefreshEntities();
        }

        /// <summary>设置工件坐标系 G54-G59（index 0-5，功能 28）。</summary>
        public void SetWorkOffset(int index, Vec3d machineOrigin)
        {
            if (index < 0 || index >= 6) return;
            Simulator.Sim.Machine.WorkOffsets[index] = machineOrigin;
            Reparse();
        }

        public Vec3d GetWorkOffset(int index) =>
            Simulator.Sim.Machine.GetWorkOffset(index);

        public void SetFixture(FixtureDefinition fixture)
        {
            Simulator.Sim.Fixture = fixture ?? new FixtureDefinition();
            RefreshEntities();
        }

        /// <summary>三点对刀求工件原点（功能 30）。</summary>
        public Vec3d ComputeOriginFromTouchPoints(Vec3d xTouch, Vec3d yTouch, Vec3d zTouch, double probeRadius, Vec3d nominal)
        {
            return WorkpieceSetup.FromTouchPoints(xTouch, yTouch, zTouch, probeRadius, nominal);
        }

        private void RefreshEntities()
        {
            Reparse();
            Simulator.Entities?.RebuildAll();
        }

        // ---------------- 分析（56-61, 63-67, 70）----------------

        public TimeEstimator.Breakdown AnalyzeTime() => Simulator.AnalyzeTime();
        public List<CollisionEvent> DetectCollisions() => Simulator.DetectCollisions();
        public List<PathSuggestion> OptimizePath() => Simulator.OptimizePath();
        public MachiningAnalysisResult AnalyzeMachining(double tolerance = 0.1) => Simulator.AnalyzeMachining(tolerance);
        public CuttingParameters RecommendParameters(int toolNumber, double diameterFraction = 0.5) =>
            Simulator.Sim.RecommendParameters(toolNumber, diameterFraction);
        public CuttingParameters AnalyzeCuttingParameters() =>
            CuttingCalculator.AnalyzeProgram(Simulator.Sim.Program, Simulator.Sim.Machine, Material);

        // ---------------- 速查手册（68-69）----------------

        /// <summary>G/M 速查手册（按类别分组）。</summary>
        public (string titleKey, List<CodeInfo> items)[] Manual() => CodeDatabase.Manual();

        public bool TryDescribeG(double code, out CodeInfo info) => CodeDatabase.TryGetG(code, out info);
        public bool TryDescribeM(int code, out CodeInfo info) => CodeDatabase.TryGetM(code, out info);

        // ---------------- 后处理（84）----------------

        public PostProcessorConfig PostProcessor
        {
            get => Simulator.Sim.PostProcessor;
            set => Simulator.Sim.PostProcessor = value ?? PostProcessorConfig.Fanuc();
        }

        public string PostProcess(string programName = null) => Simulator.Sim.PostProcessProgram(programName);
        public string ExportPostProcessed(string path, string programName = null)
        {
            var r = Simulator.Sim.ExportProgramPostProcessed(path, programName);
            return r.Success ? r.Value : null;
        }

        public IEnumerable<string> PostProcessorNames => PostProcessorRegistry.Names;
        public void UsePostProcessorPreset(string name) => PostProcessor = PostProcessorRegistry.Create(name);

        // ---------------- 语言/主题（92/93）----------------

        public string Language => Loc.Language;
        public void SetLanguage(string lang)
        {
            if (Localization != null) Localization.SetLanguage(lang);
            else Loc.Language = lang;
        }
        public void ToggleTheme() => Localization?.ToggleTheme();

        // ---------------- 教程（94）----------------

        public List<Tutorial> Tutorials => TutorialLibrary.All();
        public TutorialLibrary.Session StartTutorial(string id)
        {
            var tutorial = Tutorials.Find(t => t.Id == id);
            if (tutorial == null) return null;
            var session = new TutorialLibrary.Session(tutorial);
            Simulator.Sim.CurrentTutorial = session;
            return session;
        }

        // ---------------- 录制/回放（51, 62）----------------

        public SimulationRecorder Recorder => RecordReplay?.Recorder;
        public bool IsRecording => RecordReplay?.IsRecording ?? false;
        public bool IsReplaying => RecordReplay?.IsReplaying ?? false;

        public void StartRecording() => RecordReplay?.StartRecording();
        public void StopRecording() => RecordReplay?.StopRecording();
        public void StartReplay() => RecordReplay?.StartReplay();
        public void PauseReplay() => RecordReplay?.PauseReplay();
        public void StopReplay() => RecordReplay?.StopReplay();
        public void SeekReplay(double progress) => RecordReplay?.SeekReplay(progress);
        public void SetReplaySpeed(float speed) => RecordReplay?.SetReplaySpeed(speed);

        /// <summary>保存录制结果（功能 62）。</summary>
        public bool SaveRecording(string path = null)
        {
            if (RecordReplay?.Recorder == null) return false;
            string p = path ?? Path.Combine(
                Path.Combine(string.IsNullOrEmpty(StorageDirectory) ? Application.persistentDataPath : StorageDirectory, "Recordings"),
                "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".rec");
            try
            {
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                RecordReplay.Recorder.Save(p);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SaveRecording failed: " + ex.Message);
                return false;
            }
        }

        public bool LoadRecording(string path) => RecordReplay?.LoadRecording(path) ?? false;

        // ---------------- 截图 / 视频（75, 76）----------------

        public string SaveScreenshot(string path = null) => Capture?.SaveScreenshot(path);
        public void StartVideoCapture(string directory = null) => Capture?.StartVideoCapture(directory);
        public void StopVideoCapture() => Capture?.StopVideoCapture();
        public bool IsCapturingVideo => Capture != null && Capture.IsRecording;

        // ---------------- 模型导入（71-74）----------------

        public ImportedModel ImportModel(string path, ModelRole role)
        {
            var model = ModelImport?.ImportFile(path, role);
            if (model != null && model.Success) ApplyImportedModel(model, role);
            return model;
        }

        public ImportedModel ImportModelFromGameObject(GameObject root, ModelRole role)
        {
            var model = ModelImport?.ImportFromGameObject(root, role);
            if (model != null && model.Success) ApplyImportedModel(model, role);
            return model;
        }

        private void ApplyImportedModel(ImportedModel model, ModelRole role)
        {
            switch (role)
            {
                case ModelRole.Workpiece:
                    ModelImport.ApplyAsBlank(Simulator, model);
                    break;
                case ModelRole.Fixture:
                    ModelImport.ApplyAsFixture(Simulator, model);
                    break;
                default:
                    // 机床/刀具模型由场景层按 ImportedModel.Bounds 自行摆放
                    break;
            }
        }

        // ---------------- 多项目管理（77）----------------

        public IReadOnlyList<CncProject> ProjectList => Projects.Projects;
        public CncProject ActiveProject => Projects.Active;

        public CncProject CreateProject(string name = null) => Projects.Create(name);

        /// <summary>把当前仿真状态保存到项目并落盘。</summary>
        public void SaveToProject(CncProject project)
        {
            if (project == null || Simulator?.Sim == null) return;
            project.GCodeText = Simulator.Sim.Document.Text;
            project.BlankSize = Simulator.Sim.Blank.Size;
            project.BlankCenter = Simulator.Sim.Blank.Center;
            project.BlankShape = Simulator.Sim.Blank.Shape.ToString();
            project.MaterialId = Simulator.Sim.Material.Id;
            project.MachineType = Simulator.Sim.Machine.Type.ToString();
            project.Tools.Clear();
            foreach (var t in Simulator.Sim.Tools.Tools) project.Tools.Add(t.Clone());
            Projects.Save(project);
        }

        /// <summary>把项目载入当前仿真。</summary>
        public void LoadFromProject(CncProject project)
        {
            if (project == null || Simulator?.Sim == null) return;
            Projects.SetActive(project);
            if (!string.IsNullOrEmpty(project.GCodeText))
            {
                Simulator.Sim.Document.SetText(project.GCodeText, recordUndo: false, markDirty: false);
            }
            Simulator.Sim.Blank.Size = project.BlankSize;
            Simulator.Sim.Blank.Center = project.BlankCenter;
            if (Enum.TryParse(project.BlankShape, out BlankShape shape)) Simulator.Sim.Blank.Shape = shape;
            Simulator.Sim.SetMaterial(project.MaterialId);
            Reparse();
            Simulator.Entities?.RebuildAll();
        }

        public bool DeleteProject(CncProject project) => Projects.Delete(project);
        public CncProject DuplicateProject(CncProject project) => Projects.Duplicate(project);
        public void RenameProject(CncProject project, string name) => Projects.Rename(project, name);

        // ---------------- 五轴/多轴机床（80）----------------

        /// <summary>切换机床类型并刷新场景。5 轴运动学见 FiveAxisKinematics。</summary>
        public void SetMachineType(MachineType type, FiveAxisKinematics kinematics = FiveAxisKinematics.None)
        {
            MachineProfile profile = type switch
            {
                MachineType.FourAxis => MachineProfile.Create4Axis(),
                MachineType.FiveAxis => MachineProfile.Create5Axis(kinematics == FiveAxisKinematics.None
                    ? FiveAxisKinematics.TableTable : kinematics),
                _ => MachineProfile.Create3Axis()
            };
            Simulator.Sim.SetMachine(profile);
            Simulator.Entities?.RebuildAll();
            Simulator.ToolTransformUpdate(Simulator.Sim.Simulation.Current.Position, Simulator.Sim.Simulation.Current.ToolDirection);
        }

        public MachineType CurrentMachineType => Simulator.Sim.Machine.Type;
        public FiveAxisKinematics CurrentKinematics => Simulator.Sim.Machine.Kinematics;
    }
}
