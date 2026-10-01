using System;
using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;

namespace CncSim.Runtime
{
    /// <summary>
    /// 测量工具（功能 43）：距离与角度测量。
    /// 支持两种取点方式：
    /// 1) 物理射线拾取（PickMode.Physics）：对挂载 Collider 的对象做 Raycast；
    /// 2) 网格射线拾取（PickMode.Mesh）：直接对 Core.MeshData 做解析求交，精度更高。
    /// 结果以毫米（mm）为单位输出，与 CNC 坐标一致。
    /// </summary>
    public class MeasurementTool : MonoBehaviour
    {
        public enum PickMode { Physics, Mesh }
        public enum MeasureType { None, Distance, Angle }

        [Header("Config")]
        public PickMode Mode = PickMode.Physics;
        public MeasureType Type = MeasureType.None;
        public Camera RayCamera;
        public float MaxDistance = 5000f;
        public LayerMask PickMask = ~0;

        [Header("Mesh Picking (Mode=Mesh)")]
        [Tooltip("参与网格拾取的 Core 网格提供者")]
        public MonoBehaviour[] MeshProviders;

        [Header("Display")]
        public Material LineMaterial;
        public Color PointColor = Color.yellow;
        public Color LineColor = Color.cyan;
        public float PointSize = 4f;
        public float LineWidth = 0.8f;

        /// <summary>距离测量结果（mm）。</summary>
        public float DistanceMm { get; private set; }
        /// <summary>角度测量结果（度）。</summary>
        public float AngleDeg { get; private set; }

        public event Action<MeasureType, float> Measured;

        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private LineRenderer _line;
        private readonly List<GameObject> _markers = new List<GameObject>();

        private void Awake()
        {
            if (RayCamera == null) RayCamera = Camera.main;
        }

        public void SetType(MeasureType type)
        {
            Type = type;
            if (type == MeasureType.None) Clear();
        }

        /// <summary>在屏幕位置尝试取点。</summary>
        public bool TryPick(Vector2 screenPos)
        {
            if (RayCamera == null) return false;
            Ray ray = RayCamera.ScreenPointToRay(screenPos);
            bool hit = Mode == PickMode.Physics ? PickPhysics(ray, out var p, out var n) : PickMesh(ray, out p, out n);
            if (!hit) return false;

            _points.Add(p);
            _normals.Add(n);
            UpdateVisualization();
            Recalculate();
            return true;
        }

        private bool PickPhysics(Ray ray, out Vector3 point, out Vector3 normal)
        {
            if (Physics.Raycast(ray, out var hit, MaxDistance, PickMask))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = default;
            normal = default;
            return false;
        }

        private bool PickMesh(Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = default;
            if (MeshProviders == null) return false;

            Vec3d origin = MeshBuilder.ToCnc(ray.origin);
            Vec3d dir = MeshBuilder.ToCnc(ray.direction);
            // Unity 方向转换：ToCnc 交换 y/z 分量，方向同样适用
            double best = double.MaxValue;
            bool found = false;
            foreach (var provider in MeshProviders)
            {
                var data = GetMeshData(provider);
                if (data == null) continue;
                if (data.Raycast(origin, dir, out double t) && t < best)
                {
                    best = t;
                    found = true;
                }
            }
            if (!found) return false;
            Vec3d hitCnc = origin + dir * best;
            point = MeshBuilder.ToUnity(hitCnc);
            normal = Vector3.up;
            return true;
        }

        private static Core.MeshData GetMeshData(MonoBehaviour provider)
        {
            return provider switch
            {
                IMeasurable m => m.GetMeasurementMesh(),
                _ => null
            };
        }

        private void Recalculate()
        {
            if (_points.Count < 2) { DistanceMm = 0; AngleDeg = 0; return; }

            if (Type == MeasureType.Distance)
            {
                var a = MeshBuilder.ToCnc(_points[_points.Count - 2]);
                var b = MeshBuilder.ToCnc(_points[_points.Count - 1]);
                DistanceMm = (float)Vec3d.Distance(a, b);
                Measured?.Invoke(MeasureType.Distance, DistanceMm);
            }
            else if (Type == MeasureType.Angle && _points.Count >= 3)
            {
                Vector3 v1 = _points[_points.Count - 2] - _points[_points.Count - 3];
                Vector3 v2 = _points[_points.Count - 1] - _points[_points.Count - 2];
                AngleDeg = Vector3.Angle(v1, v2);
                Measured?.Invoke(MeasureType.Angle, AngleDeg);
            }
        }

        private void UpdateVisualization()
        {
            EnsureLine();
            EnsureMarkerCount(_points.Count);
            for (int i = 0; i < _points.Count; i++)
                _markers[i].transform.position = _points[i];

            if (Type == MeasureType.Distance && _points.Count >= 2)
            {
                _line.positionCount = 2;
                _line.SetPosition(0, _points[_points.Count - 2]);
                _line.SetPosition(1, _points[_points.Count - 1]);
            }
            else if (Type == MeasureType.Angle && _points.Count >= 3)
            {
                _line.positionCount = 3;
                _line.SetPosition(0, _points[_points.Count - 3]);
                _line.SetPosition(1, _points[_points.Count - 2]);
                _line.SetPosition(2, _points[_points.Count - 1]);
            }
        }

        private void EnsureLine()
        {
            if (_line != null) return;
            var go = new GameObject("MeasureLine");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.material = LineMaterial != null
                ? LineMaterial
                : new Material(Shader.Find("Sprites/Default"));
            _line.startColor = _line.endColor = LineColor;
            _line.startWidth = _line.endWidth = LineWidth;
            _line.useWorldSpace = true;
        }

        private void EnsureMarkerCount(int count)
        {
            while (_markers.Count < count)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "MeasurePoint" + _markers.Count;
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * PointSize;
                var r = go.GetComponent<Renderer>();
                r.material = new Material(Shader.Find("Unlit/Color"));
                r.material.color = PointColor;
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                _markers.Add(go);
            }
        }

        public void Clear()
        {
            _points.Clear();
            _normals.Clear();
            if (_line != null) _line.positionCount = 0;
            foreach (var m in _markers) if (m != null) m.SetActive(false);
            DistanceMm = 0;
            AngleDeg = 0;
        }

        private void OnDestroy()
        {
            if (_line != null && _line.material != null && LineMaterial == null)
                Destroy(_line.material);
        }
    }

    /// <summary>为测量工具提供可拾取网格数据的组件实现此接口。</summary>
    public interface IMeasurable
    {
        Core.MeshData GetMeasurementMesh();
    }
}
