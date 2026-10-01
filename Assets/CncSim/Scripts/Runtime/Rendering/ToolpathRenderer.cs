using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.Simulation;
using CncSim.Core.Trajectory;

namespace CncSim.Runtime
{
    /// <summary>
    /// 刀具轨迹显示（功能 37 实时显示、功能 38 线框/实体切换）。
    /// 线框模式用 MeshTopology.Lines 一次绘制全部轨迹；
    /// 实体模式用 ToolpathBuilder.BuildSolid 生成管道网格。
    /// 实时显示通过"已显示段数"逐段追加，避免每帧重建整个网格。
    /// </summary>
    public class ToolpathRenderer : MonoBehaviour
    {
        [Header("References")]
        public CncSimBehaviour Simulator;

        [Header("Settings")]
        public TrajectoryStyle Style = TrajectoryStyle.Lines;
        public bool ShowRapid = true;
        public bool ShowCutting = true;
        public float TubeRadius = 0.4f;
        public int TubeSides = 6;
        public Color CuttingColor = new Color(0.13f, 0.80f, 0.33f);
        public Color RapidColor = new Color(1f, 0.27f, 0.27f);
        public Color PlungeColor = new Color(1f, 0.67f, 0.13f);
        public Color ArcColor = new Color(0.2f, 0.73f, 1f);

        [Header("Live Update")]
        [Tooltip("是否随仿真高亮已走轨迹（其余淡化）")]
        public bool ProgressiveHighlight = true;
        [Range(0f, 1f)]
        public float PassedAlpha = 0.35f;

        private GameObject _lineRoot;
        private MeshFilter _lineFilter;
        private MeshRenderer _lineRenderer;
        private MeshFilter _solidFilter;
        private MeshRenderer _solidRenderer;
        private Mesh _lineMesh;
        private Mesh _solidMesh;

        private readonly List<TrajectoryRange> _ranges = new List<TrajectoryRange>();
        private Color[] _baseColors;
        private int _highlightedSegment = -1;

        private void Awake()
        {
            BuildObjects();
        }

        private void OnEnable()
        {
            if (Simulator != null) Simulator.FrameUpdated += OnFrameUpdated;
        }

        private void OnDisable()
        {
            if (Simulator != null) Simulator.FrameUpdated -= OnFrameUpdated;
        }

        private void OnFrameUpdated(SimulationFrame frame)
        {
            if (frame.Valid) HighlightSegment(frame.SegmentIndex);
        }

        private void BuildObjects()
        {
            _lineRoot = new GameObject("ToolpathLines");
            _lineRoot.transform.SetParent(transform, false);
            _lineFilter = _lineRoot.AddComponent<MeshFilter>();
            _lineRenderer = _lineRoot.AddComponent<MeshRenderer>();
            _lineRenderer.sharedMaterial = CreateLineMaterial();

            var solid = new GameObject("ToolpathSolid");
            solid.transform.SetParent(transform, false);
            _solidFilter = solid.AddComponent<MeshFilter>();
            _solidRenderer = solid.AddComponent<MeshRenderer>();
            _solidRenderer.sharedMaterial = CreateSolidMaterial();
        }

        private Material CreateLineMaterial()
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "CncSimToolpathLine" };
            m.vertexColors = true;
            return m;
        }

        private Material CreateSolidMaterial()
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "CncSimToolpathSolid" };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            return m;
        }

        private TrajectorySettings BuildSettings()
        {
            return new TrajectorySettings
            {
                Style = Style,
                ColorMode = TrajectoryColorMode.ByMotionType,
                TubeRadius = TubeRadius,
                TubeSides = TubeSides,
                ShowRapid = ShowRapid,
                ShowCutting = ShowCutting,
                CuttingColor = "#" + ColorUtility.ToHtmlStringRGB(CuttingColor),
                RapidColor = "#" + ColorUtility.ToHtmlStringRGB(RapidColor),
                PlungeColor = "#" + ColorUtility.ToHtmlStringRGB(PlungeColor),
                ArcColor = "#" + ColorUtility.ToHtmlStringRGB(ArcColor)
            };
        }

        /// <summary>重建整条轨迹。</summary>
        public void Rebuild()
        {
            if (Simulator?.Sim?.Program == null) return;
            var settings = BuildSettings();
            var segments = Simulator.Sim.Program.Segments;

            bool solid = Style == TrajectoryStyle.Solid;
            _lineRoot.SetActive(!solid);
            _solidFilter.gameObject.SetActive(solid);

            if (solid)
            {
                var mesh = ToolpathBuilder.BuildSolid(segments, settings);
                AssignSolid(mesh);
                _ranges.Clear();
                _baseColors = null;
            }
            else
            {
                var mesh = ToolpathBuilder.BuildLines(segments, settings, out var ranges);
                _ranges.Clear();
                _ranges.AddRange(ranges);
                AssignLine(mesh);
                _baseColors = null;
                _highlightedSegment = -1;
            }
        }

        private void AssignLine(Core.MeshData data)
        {
            if (_lineMesh != null) MeshBuilder.SafeDestroy(ref _lineMesh);
            _lineMesh = MeshBuilder.Build(data, withColors: true);
            _lineFilter.sharedMesh = _lineMesh;
            if (_lineMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                _baseColors = _lineMesh.colors;
        }

        private void AssignSolid(Core.MeshData data)
        {
            if (_solidMesh != null) MeshBuilder.SafeDestroy(ref _solidMesh);
            _solidMesh = MeshBuilder.Build(data, withColors: false);
            _solidFilter.sharedMesh = _solidMesh;
            if (_solidRenderer.sharedMaterial != null && _solidRenderer.sharedMaterial.HasProperty("_Color"))
                _solidRenderer.sharedMaterial.color = CuttingColor;
        }

        public void SetStyle(int styleIndex) => SetStyle((TrajectoryStyle)styleIndex);

        public void SetStyle(TrajectoryStyle style)
        {
            if (Style == style) return;
            Style = style;
            Rebuild();
        }

        public void ToggleStyle() => SetStyle(Style == TrajectoryStyle.Lines ? TrajectoryStyle.Solid : TrajectoryStyle.Lines);

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>
        /// 高亮指定运动段：已走过的段淡化，当前位置的段保持原色（功能 37 实时显示）。
        /// 仅对线框模式生效。
        /// </summary>
        public void HighlightSegment(int segmentIndex)
        {
            if (!ProgressiveHighlight) return;
            if (Style != TrajectoryStyle.Lines || _lineMesh == null || _baseColors == null || _ranges.Count == 0) return;
            if (segmentIndex == _highlightedSegment) return;
            _highlightedSegment = segmentIndex;

            var colors = new Color[_baseColors.Length];
            System.Array.Copy(_baseColors, colors, _baseColors.Length);
            foreach (var range in _ranges)
            {
                bool passed = range.SegmentIndex < segmentIndex;
                if (!passed) continue;
                for (int i = 0; i < range.VertexCount; i++)
                {
                    int idx = range.StartVertex + i;
                    if (idx < 0 || idx >= colors.Length) continue;
                    var c = colors[idx];
                    c.a = PassedAlpha;
                    colors[idx] = c;
                }
            }
            _lineMesh.colors = colors;
            _lineMesh.UploadMeshData(false);
        }

        private void OnDestroy()
        {
            MeshBuilder.SafeDestroy(ref _lineMesh);
            MeshBuilder.SafeDestroy(ref _solidMesh);
        }
    }
}
