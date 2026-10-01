using System;
using System.Collections.Generic;

namespace CncSim.Core
{
    /// <summary>
    /// 与引擎无关的网格数据。Core 层所有几何（刀具、毛坯、轨迹管道等）都输出为 MeshData，
    /// 由 Unity 层转换为 UnityEngine.Mesh。坐标为 CNC 坐标系（Z 向上，mm）。
    /// </summary>
    public class MeshData
    {
        public readonly List<Float3> Vertices = new List<Float3>();
        public readonly List<Float3> Normals = new List<Float3>();
        public readonly List<ColorRgba> Colors = new List<ColorRgba>();
        public readonly List<int> Indices = new List<int>();

        /// <summary>true 表示 Indices 为线段索引对（MeshTopology.Lines），否则为三角形。</summary>
        public bool IsLines;

        public int VertexCount => Vertices.Count;
        public int TriangleCount => IsLines ? 0 : Indices.Count / 3;

        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Colors.Clear();
            Indices.Clear();
        }

        public int AddVertex(Vec3d p, Vec3d n)
        {
            Vertices.Add(Float3.From(p));
            Normals.Add(Float3.From(n));
            return Vertices.Count - 1;
        }

        public int AddVertex(Vec3d p, Vec3d n, ColorRgba c)
        {
            Colors.Add(c);
            return AddVertex(p, n);
        }

        public void AddTriangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        public void AddQuad(int a, int b, int c, int d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        /// <summary>追加另一个网格（可带平移）。</summary>
        public void Append(MeshData other, Vec3d offset = default)
        {
            int baseIndex = Vertices.Count;
            bool colorsAligned = Colors.Count == Vertices.Count && other.Colors.Count == other.Vertices.Count;
            foreach (var v in other.Vertices)
                Vertices.Add(new Float3(v.X + (float)offset.X, v.Y + (float)offset.Y, v.Z + (float)offset.Z));
            Normals.AddRange(other.Normals);
            if (colorsAligned) Colors.AddRange(other.Colors);
            foreach (int i in other.Indices) Indices.Add(i + baseIndex);
        }

        /// <summary>按三角面重新计算平滑法线。</summary>
        public void RecalculateNormals()
        {
            if (IsLines) return;
            var acc = new Vec3d[Vertices.Count];
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                Vec3d a = Vertices[Indices[i]].ToVec3d();
                Vec3d b = Vertices[Indices[i + 1]].ToVec3d();
                Vec3d c = Vertices[Indices[i + 2]].ToVec3d();
                Vec3d n = Vec3d.Cross(b - a, c - a);
                acc[Indices[i]] += n;
                acc[Indices[i + 1]] += n;
                acc[Indices[i + 2]] += n;
            }
            Normals.Clear();
            for (int i = 0; i < acc.Length; i++) Normals.Add(Float3.From(acc[i].Normalized));
        }

        public Aabb ComputeBounds()
        {
            var box = Aabb.Empty;
            foreach (var v in Vertices) box.Encapsulate(v.ToVec3d());
            return box;
        }

        /// <summary>
        /// 射线与三角网格求交（Möller–Trumbore），返回最近交点距离，未命中返回 false。
        /// </summary>
        public bool Raycast(Vec3d origin, Vec3d dir, out double distance)
        {
            distance = double.MaxValue;
            bool hit = false;
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                Vec3d v0 = Vertices[Indices[i]].ToVec3d();
                Vec3d v1 = Vertices[Indices[i + 1]].ToVec3d();
                Vec3d v2 = Vertices[Indices[i + 2]].ToVec3d();
                if (RayTriangle(origin, dir, v0, v1, v2, out double t) && t < distance)
                {
                    distance = t;
                    hit = true;
                }
            }
            return hit;
        }

        public static bool RayTriangle(Vec3d o, Vec3d d, Vec3d v0, Vec3d v1, Vec3d v2, out double t)
        {
            t = 0;
            Vec3d e1 = v1 - v0, e2 = v2 - v0;
            Vec3d p = Vec3d.Cross(d, e2);
            double det = Vec3d.Dot(e1, p);
            if (Math.Abs(det) < 1e-12) return false;
            double inv = 1.0 / det;
            Vec3d s = o - v0;
            double u = Vec3d.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            Vec3d q = Vec3d.Cross(s, e1);
            double v = Vec3d.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return false;
            t = Vec3d.Dot(e2, q) * inv;
            return t > 1e-9;
        }
    }
}
