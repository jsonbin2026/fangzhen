using System;
using System.Collections.Generic;
using System.IO;
using CncSim.Core.Analysis;
using CncSim.Core.CodeEditor;
using CncSim.Core.Collision;
using CncSim.Core.Kinematics;
using CncSim.Core.MaterialRemoval;
using CncSim.Core.Optimization;
using CncSim.Core.Parsing;
using CncSim.Core.Performance;
using CncSim.Core.PostProcessing;
using CncSim.Core.Simulation;
using CncSim.Core.Stock;
using CncSim.Core.Theming;
using CncSim.Core.Tooling;
using CncSim.Core.Trajectory;
using CncSim.Core.Tutorial;
using CncSim.Core.Validation;

namespace CncSim.Core
{
    /// <summary>材料去除算法选择。</summary>
    public enum RemovalAlgorithm
    {
        /// <summary>高度图（Z-Map）：快，适合三轴。</summary>
        HeightMap,
        /// <summary>体素/SDF：精确，支持五轴，慢。</summary>
        Voxel
    }

    /// <summary>仿真分辨率预设。</summary>
    public enum QualityPreset
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// CNC 仿真门面（Facade）：把编辑器、解析、校验、刀具、毛坯、仿真、材料去除、
    /// 碰撞、分析、优化等模块组合为一个易用入口。UI 层只需与 CncSimulator 交互。
    /// </summary>
    public class CncSimulator
    {
        public GCodeDocument Document { get; private set; }
        public MachineProfile Machine { get; set; }
        public ToolLibrary Tools { get; set; }
        public BlankDefinition Blank { get; set; }
        public FixtureDefinition Fixture { get; set; } = new FixtureDefinition();
        public MaterialDefinition Material { get; private set; }
        public Trajectory.TrajectorySettings TrajectorySettings { get; set; } = new Trajectory.TrajectorySettings();
        public ParseOptions ParseOptions { get; set; } = new ParseOptions();

        public ParseResult Program { get; private set; }
        public SimulationController Simulation { get; private set; }
        public IMaterialRemoval Removal { get; private set; }
        public GCodeValidator Validator { get; private set; }
        public MachineKinematics Kinematics { get; private set; }
        public RemovalAlgorithm Algorithm { get; set; } = RemovalAlgorithm.HeightMap;
        public QualityPreset Quality { get; set; } = QualityPreset.Medium;

        /// <summary>后处理器配置（功能 84）。</summary>
        public PostProcessorConfig PostProcessor { get; set; } = PostProcessorConfig.Fanuc();
        /// <summary>界面主题（功能 93）。</summary>
        public ThemeManager Theme { get; private set; } = new ThemeManager();
        /// <summary>性能与质量档位（功能 85-91）。</summary>
        public PerformanceSettings Performance { get; set; } = new PerformanceSettings();
        /// <summary>后台预计算服务（功能 85）。</summary>
        public BackgroundPrecomputeService Precompute { get; } = new BackgroundPrecomputeService();
        /// <summary>教程会话（功能 94），由 UI 按需创建。</summary>
        public TutorialLibrary.Session CurrentTutorial { get; set; }

        /// <summary>解析结果更新事件。</summary>
        public event Action<ParseResult> ProgramParsed;
        /// <summary>材料去除网格更新事件（参数为最新网格）。</summary>
        public event Action<MeshData> RemovalMeshUpdated;

        public bool IsDirtyDocument { get; private set; } = true;
        private int _parsedVersion = -1;

        public CncSimulator(MachineProfile machine = null, ToolLibrary tools = null)
        {
            Document = new GCodeDocument();
            Machine = machine ?? MachineProfile.Create3Axis();
            Tools = tools ?? ToolLibrary.CreateDefault();
            Blank = new BlankDefinition();
            Material = MaterialDefinition.Find(Blank.MaterialId);
            Simulation = new SimulationController();
            Kinematics = new MachineKinematics(Machine);
            Validator = new GCodeValidator(Machine, Tools, Blank);
            Document.Changed += _ =>
            {
                IsDirtyDocument = true;
            };
        }

        public void SetDocument(GCodeDocument doc)
        {
            Document = doc ?? new GCodeDocument();
            IsDirtyDocument = true;
        }

        /// <summary>
        /// 切换机床配置（功能 80：三轴/四轴/五轴）。
        /// 重建运动学、校验器与仿真，并强制重新解析当前程序。
        /// </summary>
        public void SetMachine(MachineProfile machine)
        {
            Machine = machine ?? MachineProfile.Create3Axis();
            Kinematics = new MachineKinematics(Machine);
            Validator = new GCodeValidator(Machine, Tools, Blank);
            if (Program != null)
            {
                Simulation.Load(Program, Machine);
                Parse(force: true);
            }
        }

        public void SetMaterial(string materialId)
        {
            Material = MaterialDefinition.Find(materialId);
            Blank.MaterialId = Material.Id;
        }

        /// <summary>是否已解析且与文档版本一致。</summary>
        public bool IsParsed => Program != null && _parsedVersion == Document.Version;

        /// <summary>
        /// 解析当前文档。force=false 时若版本未变则跳过。
        /// </summary>
        public ParseResult Parse(bool force = false)
        {
            if (!force && Program != null && _parsedVersion == Document.Version)
                return Program;
            var parser = new GCodeParser(Machine, Tools, ParseOptions);
            Program = parser.Parse(Document.Lines, Document.Version);
            _parsedVersion = Document.Version;

            // 校验与时间估算
            Validator = new GCodeValidator(Machine, Tools, Blank);
            Validator.Validate(Program);
            GCodeValidator.CheckOvercut(Program, Blank);
            TimeEstimator.Estimate(Program, Machine);

            Kinematics = new MachineKinematics(Machine);
            ProgramParsed?.Invoke(Program);
            return Program;
        }

        /// <summary>单行解释（功能 17）。</summary>
        public BlockExplanation ExplainLine(int lineIndex)
        {
            Parse();
            var state = Program.StateBeforeLine(lineIndex);
            var parser = new GCodeParser(Machine, Tools, ParseOptions);
            return parser.ExplainLine(Document.GetLine(lineIndex), lineIndex, state);
        }

        /// <summary>建立材料去除引擎并加载仿真（功能 45-55）。</summary>
        public void LoadSimulation(bool resetRemoval = true)
        {
            Parse();
            if (resetRemoval || Removal == null) CreateRemoval();
            Simulation.Load(Program, Machine);
        }

        public void CreateRemoval()
        {
            int resolution = Quality switch
            {
                QualityPreset.Low => 96,
                QualityPreset.High => 360,
                _ => Performance.RemovalResolution
            };
            if (Algorithm == RemovalAlgorithm.Voxel)
            {
                int voxelRes = Quality switch
                {
                    QualityPreset.Low => 48,
                    QualityPreset.High => 192,
                    _ => 96
                };
                Removal = new VoxelRemoval(Blank, voxelRes)
                {
                    MaterialColorHex = Material.ColorHex
                };
            }
            else
            {
                Removal = new HeightMapRemoval(Blank, resolution)
                {
                    MaterialColorHex = Material.ColorHex
                };
            }
        }

        /// <summary>把当前仿真位置对应的切削应用到材料去除引擎（在 Advance 后调用）。</summary>
        public void ApplyCuttingToCurrentFrame()
        {
            if (Removal == null || Program == null) return;
            var frame = Simulation.Current;
            if (!frame.Valid || !frame.Cutting) return;
            if (frame.SegmentIndex < 0 || frame.SegmentIndex >= Program.Segments.Count) return;
            var seg = Program.Segments[frame.SegmentIndex];
            var tool = Tools.Get(seg.ToolNumber) ?? Tools.GetOrCreate(seg.ToolNumber);
            Removal.Cut(seg, tool);
        }

        /// <summary>推进仿真并按需应用切削。</summary>
        public void AdvanceSimulation(double deltaTime, bool applyCutting = true)
        {
            if (Simulation.State == SimulationState.Idle || Simulation.State == SimulationState.Stopped)
            {
                if (Simulation.State == SimulationState.Idle) LoadSimulation();
            }
            var before = Simulation.Current.SegmentIndex;
            Simulation.Advance(deltaTime);
            if (applyCutting) ApplyCuttingToCurrentFrame();
            if (Simulation.Current.SegmentIndex != before)
                RemovalMeshUpdated?.Invoke(Removal?.BuildMesh());
        }

        /// <summary>执行整段程序仿真（离线，可后台线程），全部切削一次完成。</summary>
        public void RunOfflineSimulation(int resolutionOverride = 0)
        {
            Parse();
            if (resolutionOverride > 0)
            {
                Removal = Algorithm == RemovalAlgorithm.Voxel
                    ? (IMaterialRemoval)new VoxelRemoval(Blank, resolutionOverride)
                    : new HeightMapRemoval(Blank, resolutionOverride);
    }
            else CreateRemoval();

            foreach (var seg in Program.Segments)
            {
                if (!seg.IsMotion) continue;
                var tool = Tools.Get(seg.ToolNumber) ?? Tools.GetOrCreate(seg.ToolNumber);
                Removal.Cut(seg, tool);
            }
            RemovalMeshUpdated?.Invoke(Removal.BuildMesh());
        }

        /// <summary>碰撞检测（功能 56-58）。</summary>
        public List<CollisionEvent> DetectCollisions()
        {
            Parse();
            var detector = new CollisionDetector(Machine, Blank, Fixture, Tools, MachineLimits.FromMachine(Machine));
            return detector.Detect(Program);
        }

        /// <summary>路径优化建议（功能 70）。</summary>
        public List<PathSuggestion> OptimizePath()
        {
            Parse();
            return new PathOptimizer(Machine).Analyze(Program);
        }

        /// <summary>加工参数推荐（功能 67）。</summary>
        public CuttingParameters RecommendParameters(int toolNumber, double diameterFraction = 0.5)
        {
            var tool = Tools.Get(toolNumber) ?? Tools.GetOrCreate(toolNumber);
            return CuttingCalculator.Recommend(tool, Material, diameterFraction);
        }

        /// <summary>时间估算分类（功能 63）。</summary>
        public TimeEstimator.Breakdown AnalyzeTime()
        {
            Parse();
            return TimeEstimator.Analyze(Program, Machine);
        }

        /// <summary>过切/欠切分析（功能 59-60）。需要先运行材料去除仿真。</summary>
        public MachiningAnalysisResult AnalyzeMachining(double tolerance = 0.1)
        {
            Parse();
            var target = MachiningAnalyzer.InferTargetFromProgram(Program, Blank);
            if (Removal is HeightMapRemoval map)
                return MachiningAnalyzer.AnalyzeHeightMap(map, Blank, target, tolerance);
            return new MachiningAnalysisResult();
        }

        /// <summary>导出程序。</summary>
        public FileResult<string> ExportProgram(string path, bool ensurePercent = true)
        {
            var service = new GCodeFileService(new RecentFilesList(null));
            return service.Export(Document, path, "\r\n", ensurePercent);
        }

        /// <summary>按后处理器配置重新排版并导出程序（功能 84）。</summary>
        public FileResult<string> ExportProgramPostProcessed(string path, string programName = null)
        {
            Parse();
            var processor = new GCodePostProcessor(PostProcessor);
            string text = processor.ProcessDocument(Document.Lines, programName);
            try
            {
                File.WriteAllText(path, text);
                return FileResult<string>.Ok(text);
            }
            catch (Exception ex)
            {
                return FileResult<string>.Fail(ex.Message, path);
            }
        }

        /// <summary>按后处理器配置生成程序文本（功能 84）。</summary>
        public string PostProcessProgram(string programName = null)
        {
            Parse();
            return new GCodePostProcessor(PostProcessor).ProcessDocument(Document.Lines, programName);
        }

        /// <summary>生成毛坯与夹具的显示网格。</summary>
        public MeshData BuildBlankMesh() => Blank.BuildDisplayMesh();
        public MeshData BuildFixtureMesh() => Fixture.BuildMesh();
        public MeshData BuildRemovalMesh()
        {
            var mesh = Removal?.BuildMesh() ?? new MeshData();
            if (Performance.Detail == DetailLevel.LowPoly && mesh.VertexCount > 40_000)
            {
                var bounds = mesh.ComputeBounds();
                double cell = Math.Max(0.25, bounds.Size.Length / 200.0);
                mesh = MeshSimplifier.Simplify(mesh, cell);
            }
            return mesh;
        }

        /// <summary>生成刀具轨迹网格（功能 37-38）。</summary>
        public MeshData BuildToolpathMesh(out List<Trajectory.TrajectoryRange> ranges)
        {
            Parse();
            if (TrajectorySettings.Style == Trajectory.TrajectoryStyle.Solid)
            {
                ranges = null;
                return Trajectory.ToolpathBuilder.BuildSolid(Program.Segments, TrajectorySettings);
            }
            return Trajectory.ToolpathBuilder.BuildLines(Program.Segments, TrajectorySettings, out ranges);
        }
    }
}
