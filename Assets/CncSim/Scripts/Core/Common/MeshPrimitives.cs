using System;
using System.Collections.Generic;

namespace CncSim.Core
{
    /// <summary>基础几何体网格生成（长方体/圆柱/球/圆锥/旋转体/管道），供刀具、机床、夹具、毛坯使用。</summary>
    public static class MeshPrimitives
    {
        public static MeshData Box(Vec3d min, Vec3d max)
        {
            var m = new MeshData();
            AddBox(m, min, max);
            return m;
        }

        public static void AddBox(MeshData m, Vec3d min, Vec3d max)
        {
            Vec3d[] c =
            {
                new Vec3d(min.X, min.Y, min.Z), new Vec3d(max.X, min.Y, min.Z),
                new Vec3d(max.X, max.Y, min.Z), new Vec3d(min.X, max.Y, min.Z),
                new Vec3d(min.X, min.Y, max.Z), new Vec3d(max.X, min.Y, max.Z),
                new Vec3d(max.X, max.Y, max.Z), new Vec3d(min.X, max.Y, max.Z)
            };
            // 每个面独立顶点以获得硬边法线；顶点顺序从外侧看为逆时针（右手系）
            AddFace(m, c[0], c[3], c[2], c[1], -Vec3d.UnitZ);
            AddFace(m, c[4], c[5], c[6], c[7], Vec3d.UnitZ);
            AddFace(m, c[0], c[1], c[5], c[4], -Vec3d.UnitY);
            AddFace(m, c[2], c[3], c[7], c[6], Vec3d.UnitY);
            AddFace(m, c[1], c[2], c[6], c[5], Vec3d.UnitX);
            AddFace(m, c[3], c[0], c[4], c[7], -Vec3d.UnitX);
        }

        private static void AddFace(MeshData m, Vec3d a, Vec3d b, Vec3d c, Vec3d d, Vec3d n)
        {
            int i0 = m.AddVertex(a, n), i1 = m.AddVertex(b, n), i2 = m.AddVertex(c, n), i3 = m.AddVertex(d, n);
            m.AddQuad(i0, i1, i2, i3);
        }

        /// <summary>沿 Z 轴的圆柱（底面中心 baseCenter）。</summary>
        public static MeshData Cylinder(Vec3d baseCenter, double radius, double height, int segments = 32, bool caps = true)
        {
            var profile = new List<(double r, double z)> { (0, 0), (radius, 0), (radius, height), (0, height) };
            if (!caps) profile = new List<(double r, double z)> { (radius, 0), (radius, height) };
            var m = Revolve(profile, segments);
            Translate(m, baseCenter);
            return m;
        }

        /// <summary>
        /// 绕 Z 轴旋转一条 (r,z) 轮廓生成旋转体。刀具、主轴、圆柱毛坯都基于此生成。
        /// 轮廓按 z 自下而上给出即可得到朝外的法线。
        /// </summary>
        public static MeshData Revolve(IList<(double r, double z)> profile, int segments)
        {
            segments = Math.Max(3, segments);
            var m = new MeshData();
            int rings = profile.Count;
            // 每条轮廓边独立生成顶点，保证硬边
            for (int p = 0; p + 1 < rings; p++)
            {
                var (r0, z0) = profile[p];
                var (r1, z1) = profile[p + 1];
                double dr = r1 - r0, dz = z1 - z0;
                // 轮廓边的外法线（2D）：(dz, -dr) 归一化
                double nl = Math.Sqrt(dr * dr + dz * dz);
                if (nl < 1e-12) continue;
                double nr = dz / nl, nz = -dr / nl;
                int start = m.VertexCount;
                for (int s = 0; s <= segments; s++)
                {
                    double a = 2 * Math.PI * s / segments;
                    double ca = Math.Cos(a), sa = Math.Sin(a);
                    Vec3d n = new Vec3d(nr * ca, nr * sa, nz);
                    m.AddVertex(new Vec3d(r0 * ca, r0 * sa, z0), n);
                    m.AddVertex(new Vec3d(r1 * ca, r1 * sa, z1), n);
                }
                for (int s = 0; s < segments; s++)
                {
                    int a0 = start + s * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    m.AddQuad(a0, b0, b1, a1);
                }
            }
            return m;
        }

        public static MeshData Sphere(Vec3d center, double radius, int segments = 24)
        {
            var profile = new List<(double r, double z)>();
            int rings = Math.Max(4, segments / 2);
            for (int i = 0; i <= rings; i++)
            {
                double a = -Math.PI / 2 + Math.PI * i / rings;
                profile.Add((radius * Math.Cos(a), radius * Math.Sin(a)));
            }
            var m = Revolve(profile, segments);
            m.RecalculateNormals();
            Translate(m, center);
            return m;
        }

        /// <summary>沿任意线段生成圆管（用于实体轨迹显示）。</summary>
        public static void AddTube(MeshData m, Vec3d a, Vec3d b, double radius, int sides, ColorRgba color)
        {
            Vec3d axis = b - a;
            if (axis.LengthSquared < 1e-12) return;
            Vec3d u = MathUtil.AnyPerpendicular(axis);
            Vec3d v = Vec3d.Cross(axis.Normalized, u);
            int start = m.VertexCount;
            for (int i = 0; i <= sides; i++)
            {
                double ang = 2 * Math.PI * i / sides;
                Vec3d n = u * Math.Cos(ang) + v * Math.Sin(ang);
                m.AddVertex(a + n * radius, n, color);
                m.AddVertex(b + n * radius, n, color);
            }
            for (int i = 0; i < sides; i++)
            {
                int a0 = start + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                m.AddQuad(a0, b0, b1, a1);
            }
        }

        public static void Translate(MeshData m, Vec3d offset)
        {
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                var v = m.Vertices[i];
                m.Vertices[i] = new Float3(v.X + (float)offset.X, v.Y + (float)offset.Y, v.Z + (float)offset.Z);
            }
        }
    }
}
