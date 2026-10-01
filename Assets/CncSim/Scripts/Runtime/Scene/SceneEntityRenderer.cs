using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.MachineModel;
using CncSim.Core.Parsing;

namespace CncSim.Runtime
{
    /// <summary>
    /// 机床与工件实体渲染（功能 32-36）。
    /// 从 Core 的 MachineModelBuilder / BlankDefinition / FixtureDefinition / ToolLibrary 取几何，
    /// 转换为 Unity 网格并组织挂点层级，供仿真驱动更新运动部件位置。
    /// 场景结构：
    ///   SceneRoot
    ///     Machine/Base, Column, Table, Spindle
    ///     Workpiece/Blank, Removal
    ///     Fixture
    ///     Tool
    /// </summary>
    public class SceneEntityRenderer : MonoBehaviour, IMeasurable
    {
        [Header("References")]
        public CncSimBehaviour Simulator;

        [Header("Materials")]
        public Material MachineMaterial;
        public Material BlankMaterial;
        public Material FixtureMaterial;
        public Material ToolMaterial;
        public Material RemovalMaterial;

        [Header("Visibility")]
        public bool ShowMachine = true;
        public bool ShowBlank = true;
        public bool ShowFixture = true;
        public bool ShowTool = true;
        public bool ShowRemoval = true;

        public Transform TableTransform { get; private set; }
        public Transform SpindleTransform { get; private set; }
        public Transform ToolTransform { get; private set; }
        public Transform RotaryTableTransform { get; private set; }
        public Transform SwivelHeadTransform { get; private set; }

        private GameObject _root;
        private MeshFilter _tableFilter, _spindleFilter, _baseFilter, _columnFilter;
        private MeshFilter _rotaryFilter, _swivelFilter;
        private MeshFilter _blankFilter, _fixtureFilter, _toolFilter, _removalFilter;
        private Mesh _blankMesh, _removalMesh, _toolMesh, _fixtureMesh;

        private readonly Dictionary<string, GameObject> _parts = new Dictionary<string, GameObject>();

        private void Awake()
        {
            BuildHierarchy();
        }

        private void BuildHierarchy()
        {
            _root = new GameObject("SceneEntities");
            _root.transform.SetParent(transform, false);

            var machine = new GameObject("Machine");
            machine.transform.SetParent(_root.transform, false);
            TableTransform = CreatePart(machine.transform, "Table", MachineMaterial ?? DefaultMaterial(new Color(0.55f, 0.57f, 0.6f)), out _tableFilter);
            SpindleTransform = CreatePart(machine.transform, "Spindle", MachineMaterial ?? DefaultMaterial(new Color(0.4f, 0.42f, 0.5f)), out _spindleFilter);
            CreatePart(machine.transform, "Base", MachineMaterial ?? DefaultMaterial(new Color(0.3f, 0.31f, 0.34f)), out _baseFilter);
            CreatePart(machine.transform, "Column", MachineMaterial ?? DefaultMaterial(new Color(0.3f, 0.31f, 0.34f)), out _columnFilter);
            RotaryTableTransform = CreatePart(machine.transform, "RotaryTable", MachineMaterial ?? DefaultMaterial(new Color(0.45f, 0.47f, 0.52f)), out _rotaryFilter);
            SwivelHeadTransform = CreatePart(machine.transform, "SwivelHead", MachineMaterial ?? DefaultMaterial(new Color(0.45f, 0.47f, 0.52f)), out _swivelFilter);
            RotaryTableTransform.gameObject.SetActive(false);
            SwivelHeadTransform.gameObject.SetActive(false);

            var workpiece = new GameObject("Workpiece");
            workpiece.transform.SetParent(_root.transform, false);
            CreatePart(workpiece.transform, "Blank", BlankMaterial ?? DefaultMaterial(new Color(0.7f, 0.7f, 0.72f)), out _blankFilter);
            CreatePart(workpiece.transform, "Fixture", FixtureMaterial ?? DefaultMaterial(new Color(0.24f, 0.43f, 0.62f)), out _fixtureFilter);
            CreatePart(workpiece.transform, "Removal", RemovalMaterial ?? DefaultMaterial(new Color(0.9f, 0.78f, 0.44f)), out _removalFilter);

            ToolTransform = CreatePart(_root.transform, "Tool", ToolMaterial ?? DefaultMaterial(new Color(0.85f, 0.85f, 0.88f)), out _toolFilter);

            RebuildAll();
        }

        private Transform CreatePart(Transform parent, string name, Material mat, out MeshFilter filter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            _parts[name] = go;
            return go.transform;
        }

        private static Material DefaultMaterial(Color c)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "CncSimDefault" };
            if (m.HasProperty("_Color")) m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>重建所有几何（机床、毛坯、夹具、刀具、去除网格）。</summary>
        public void RebuildAll()
        {
            if (Simulator == null || Simulator.Sim == null) return;
            var sim = Simulator.Sim;

            var builder = new MachineModelBuilder(sim.Machine);
            var parts = builder.Build();
            SetMesh(_baseFilter, parts.BaseMesh);
            SetMesh(_columnFilter, parts.ColumnMesh);
            SetMesh(_tableFilter, parts.TableMesh);
            SetMesh(_spindleFilter, parts.SpindleMesh);

            if (parts.HasRotaryTable)
            {
                SetMesh(_rotaryFilter, parts.RotaryTableMesh);
                RotaryTableTransform.localPosition = MeshBuilder.ToUnity(parts.RotaryTablePivot);
                RotaryTableTransform.gameObject.SetActive(ShowMachine);
            }
            else RotaryTableTransform.gameObject.SetActive(false);

            if (parts.HasSwivelHead)
            {
                SetMesh(_swivelFilter, parts.SwivelHeadMesh);
                SwivelHeadTransform.localPosition = MeshBuilder.ToUnity(parts.SwivelHeadPivot);
                SwivelHeadTransform.gameObject.SetActive(ShowMachine);
            }
            else SwivelHeadTransform.gameObject.SetActive(false);

            AssignMesh(ref _blankMesh, _blankFilter, sim.BuildBlankMesh());
            AssignMesh(ref _fixtureMesh, _fixtureFilter, sim.Fixture.BuildMesh());

            int toolNumber = Simulator.CurrentToolNumber;
            var tool = sim.Tools.Get(toolNumber) ?? sim.Tools.GetOrCreate(toolNumber);
            AssignMesh(ref _toolMesh, _toolFilter, MachineModelBuilder.BuildToolMesh(tool));

            ApplyVisibility();
        }

        /// <summary>更新旋转轴（4/5 轴机床）姿态（功能 80）。</summary>
        public void UpdateRotaryPose(AxisVector axes)
        {
            // CNC 约定：A 绕 X、B 绕 Y、C 绕 Z（Core 为 Z 轴向上）。
            // Core→Unity 轴映射为 (X,Y,Z)→(X,Z,Y)，故旋转轴映射：
            //   Core X → Unity X，Core Y → Unity Z，Core Z → Unity Y
            if (RotaryTableTransform != null && RotaryTableTransform.gameObject.activeSelf)
            {
                // 转台：TableTable 机型主转轴为 C（绕 Core Z → Unity Y）
                RotaryTableTransform.localRotation = Quaternion.Euler(0f, (float)axes.C, 0f);
            }
            if (SwivelHeadTransform != null && SwivelHeadTransform.gameObject.activeSelf)
            {
                // 摆动头：绕 Core Y → Unity Z 轴摆动 B 角，叠加 A 角绕 Unity X
                SwivelHeadTransform.localRotation = Quaternion.Euler((float)axes.A, 0f, (float)axes.B);
            }
        }

        /// <summary>更新材料去除网格（功能 54 表面实时更新）。</summary>
        public void UpdateRemovalMesh()
        {
            if (Simulator == null || Simulator.Sim == null) return;
            var data = Simulator.Sim.BuildRemovalMesh();
            AssignMesh(ref _removalMesh, _removalFilter, data);
            _removalFilter.gameObject.SetActive(ShowRemoval && data != null && data.VertexCount > 0);
        }

        public void SetToolNumber(int number)
        {
            if (Simulator?.Sim == null) return;
            var tool = Simulator.Sim.Tools.Get(number) ?? Simulator.Sim.Tools.GetOrCreate(number);
            AssignMesh(ref _toolMesh, _toolFilter, MachineModelBuilder.BuildToolMesh(tool));
        }

        private static void SetMesh(MeshFilter filter, Core.MeshData data)
        {
            if (filter == null) return;
            Mesh old = filter.sharedMesh;
            if (old != null) MeshBuilder.SafeDestroy(ref old);
            filter.sharedMesh = MeshBuilder.Build(data);
        }

        private static void AssignMesh(ref Mesh slot, MeshFilter filter, Core.MeshData data)
        {
            if (filter == null) return;
            if (slot != null) MeshBuilder.SafeDestroy(ref slot);
            slot = MeshBuilder.Build(data);
            filter.sharedMesh = slot;
        }

        private void ApplyVisibility()
        {
            SetActive("Table", ShowMachine);
            SetActive("Spindle", ShowMachine);
            SetActive("Base", ShowMachine);
            SetActive("Column", ShowMachine);
            SetActive("Blank", ShowBlank);
            SetActive("Fixture", ShowFixture);
            SetActive("Tool", ShowTool);
        }

        private void SetActive(string name, bool active)
        {
            if (_parts.TryGetValue(name, out var go) && go != null) go.SetActive(active);
        }

        public Core.MeshData GetMeasurementMesh()
        {
            if (Simulator?.Sim == null) return null;
            return Simulator.Sim.BuildBlankMesh();
        }

        private void OnDestroy()
        {
            MeshBuilder.SafeDestroy(ref _blankMesh);
            MeshBuilder.SafeDestroy(ref _removalMesh);
            MeshBuilder.SafeDestroy(ref _toolMesh);
            MeshBuilder.SafeDestroy(ref _fixtureMesh);
        }
    }
}
