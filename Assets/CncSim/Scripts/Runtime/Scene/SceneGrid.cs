using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;

namespace CncSim.Runtime
{
    /// <summary>
    /// 坐标系与网格显示（功能 44）。
    /// 生成地面网格、XYZ 坐标轴箭头（CNC 约定：Z 向上）与尺寸刻度。
    /// </summary>
    public class SceneGrid : MonoBehaviour
    {
        [Header("Grid")]
        public float Extent = 500f;
        public float CellSize = 10f;
        public int MajorEvery = 10;
        public Color MinorColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
        public Color MajorColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
        public Color AxisXColor = new Color(0.88f, 0.24f, 0.19f);
        public Color AxisYColor = new Color(0.24f, 0.80f, 0.24f);
        public Color AxisZColor = new Color(0.24f, 0.36f, 0.80f);

        [Header("Axes")]
        public bool ShowAxes = true;
        public float AxisLength = 120f;
        public float AxisThickness = 2f;

        private GameObject _gridRoot;
        private GameObject _axesRoot;
        private Material _lineMaterial;

        private void OnEnable() => Rebuild();
        private void OnValidate() { if (isActiveAndEnabled) Rebuild(); }

        public void Rebuild()
        {
            EnsureMaterial();
            BuildGrid();
            BuildAxes();
        }

        private void EnsureMaterial()
        {
            if (_lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                _lineMaterial = new Material(shader) { name = "CncSimGridLine" };
            }
        }

        private void Clear(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void BuildGrid()
        {
            Clear(_gridRoot);
            _gridRoot = new GameObject("Grid");
            _gridRoot.transform.SetParent(transform, false);

            var minor = new List<Vector3>();
            var major = new List<Vector3>();
            int steps = Mathf.CeilToInt(Extent / CellSize);
            for (int i = -steps; i <= steps; i++)
            {
                float c = i * CellSize;
                var target = (i % MajorEvery == 0) ? major : minor;
                target.Add(new Vector3(c, 0f, -Extent));
                target.Add(new Vector3(c, 0f, Extent));
                target.Add(new Vector3(-Extent, 0f, c));
                target.Add(new Vector3(Extent, 0f, c));
            }

            AddLineObject(_gridRoot.transform, "Minor", minor, MinorColor);
            AddLineObject(_gridRoot.transform, "Major", major, MajorColor);
        }

        private void AddLineObject(Transform parent, string name, List<Vector3> points, Color color)
        {
            if (points.Count == 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = _lineMaterial;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = 0.5f;
            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());
        }

        private void BuildAxes()
        {
            Clear(_axesRoot);
            if (!ShowAxes) return;
            _axesRoot = new GameObject("Axes");
            _axesRoot.transform.SetParent(transform, false);

            // CNC: X→Unity+X, Y→Unity+Z, Z→Unity+Y
            AddAxis("X", Vector3.right, AxisXColor);
            AddAxis("Y", Vector3.forward, AxisYColor);
            AddAxis("Z", Vector3.up, AxisZColor);
        }

        private void AddAxis(string name, Vector3 dir, Color color)
        {
            var go = new GameObject("Axis" + name);
            go.transform.SetParent(_axesRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = _lineMaterial;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = AxisThickness;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, dir * AxisLength);
        }

        /// <summary>返回坐标轴的世界方向（供其他组件复用）。</summary>
        public static Vector3 AxisDirection(int cncAxis) => cncAxis switch
        {
            0 => Vector3.right,    // X
            1 => Vector3.forward,  // Y
            _ => Vector3.up        // Z
        };
    }
}
