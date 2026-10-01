using System;
using System.Collections.Generic;
using UnityEngine;
using CncSim.Core;

namespace CncSim.Runtime
{
    /// <summary>
    /// Core.MeshData → UnityEngine.Mesh 的转换与生命周期管理。
    /// 所有渲染组件通过本类取得 Mesh，避免重复分配；Destroy 时统一释放。
    /// 坐标系约定：Core 为右手系 Z 向上（mm），Unity 为左手系 Y 向上。
    /// 转换时把 (x, y, z) 映射为 (x, z, y)，并翻转三角形绕序以保持正面朝外。
    /// </summary>
    public static class MeshBuilder
    {
        /// <summary>CNC (x,y,z) → Unity (x,z,y)。</summary>
        public static Vector3 ToUnity(Vec3d p) => new Vector3((float)p.X, (float)p.Z, (float)p.Y);
        public static Vector3 ToUnity(Float3 p) => new Vector3(p.X, p.Z, p.Y);

        /// <summary>CNC 向量 → Unity 向量。</summary>
        public static Vector3 ToUnityDirection(Vec3d d) => new Vector3((float)d.X, (float)d.Z, (float)d.Y);

        /// <summary>Unity 世界坐标 → CNC 坐标。</summary>
        public static Vec3d ToCnc(Vector3 v) => new Vec3d(v.x, v.z, v.y);

        public static Color ToUnity(ColorRgba c) => new Color(c.R, c.G, c.B, c.A);

        /// <summary>
        /// 把 Core 网格写入一个 Unity Mesh。使用 32 位索引，支持顶点色。
        /// </summary>
        public static Mesh Build(Core.MeshData data, bool calculateBounds = true, bool withColors = true)
        {
            var mesh = new Mesh { name = "CncSimMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            if (data == null || data.VertexCount == 0)
            {
                mesh.vertices = Array.Empty<Vector3>();
                mesh.triangles = Array.Empty<int>();
                return mesh;
            }

            int n = data.VertexCount;
            var verts = new Vector3[n];
            for (int i = 0; i < n; i++) verts[i] = ToUnity(data.Vertices[i]);

            var indices = new int[data.Indices.Count];
            if (data.IsLines)
            {
                for (int i = 0; i < indices.Length; i++) indices[i] = data.Indices[i];
                mesh.SetVertices(verts);
                mesh.SetIndices(indices, MeshTopology.Lines, 0, calculateBounds: calculateBounds);
            }
            else
            {
                // 翻转绕序：CNC 右手系 → Unity 左手系
                for (int i = 0; i + 2 < data.Indices.Count; i += 3)
                {
                    indices[i] = data.Indices[i + 2];
                    indices[i + 1] = data.Indices[i + 1];
                    indices[i + 2] = data.Indices[i];
                }
                mesh.SetVertices(verts);
                mesh.SetTriangles(indices, 0, calculateBounds: calculateBounds);
            }

            if (data.Normals.Count == n)
            {
                var norms = new Vector3[n];
                for (int i = 0; i < n; i++) norms[i] = ToUnity(data.Normals[i]);
                mesh.SetNormals(norms);
            }
            else if (!data.IsLines)
            {
                mesh.RecalculateNormals();
            }

            if (withColors && data.Colors.Count == n)
            {
                var cols = new Color[n];
                for (int i = 0; i < n; i++) cols[i] = ToUnity(data.Colors[i]);
                mesh.SetColors(cols);
            }

            if (calculateBounds) mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>把 Core 网格转换为 Mesh，并指定统一顶点色（覆盖网格自带颜色）。</summary>
        public static Mesh BuildWithColor(Core.MeshData data, ColorRgba color)
        {
            var mesh = Build(data, withColors: false);
            if (data != null && data.VertexCount > 0)
            {
                var cols = new Color[data.VertexCount];
                var c = ToUnity(color);
                for (int i = 0; i < cols.Length; i++) cols[i] = c;
                mesh.SetColors(cols);
            }
            return mesh;
        }

        /// <summary>用 Core 网格更新已有 Mesh（原地替换），避免频繁分配。</summary>
        public static void Update(Mesh mesh, Core.MeshData data, bool withColors = true)
        {
            if (mesh == null) return;
            mesh.Clear();
            if (data == null || data.VertexCount == 0) return;

            int n = data.VertexCount;
            var verts = new List<Vector3>(n);
            for (int i = 0; i < n; i++) verts.Add(ToUnity(data.Vertices[i]));

            var indices = new List<int>(data.Indices.Count);
            if (data.IsLines)
            {
                indices.AddRange(data.Indices);
                mesh.SetVertices(verts);
                mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0, calculateBounds: true);
            }
            else
            {
                for (int i = 0; i + 2 < data.Indices.Count; i += 3)
                {
                    indices.Add(data.Indices[i + 2]);
                    indices.Add(data.Indices[i + 1]);
                    indices.Add(data.Indices[i]);
                }
                mesh.SetVertices(verts);
                mesh.SetTriangles(indices.ToArray(), 0, calculateBounds: true);
            }

            if (data.Normals.Count == n)
            {
                var norms = new List<Vector3>(n);
                for (int i = 0; i < n; i++) norms.Add(ToUnity(data.Normals[i]));
                mesh.SetNormals(norms);
            }
            else if (!data.IsLines)
            {
                mesh.RecalculateNormals();
            }

            if (withColors && data.Colors.Count == n)
            {
                var cols = new List<Color>(n);
                for (int i = 0; i < n; i++) cols.Add(ToUnity(data.Colors[i]));
                mesh.SetColors(cols);
            }
        }

        /// <summary>安全销毁 Mesh，避免编辑器/运行时内存泄漏。</summary>
        public static void SafeDestroy(ref Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
