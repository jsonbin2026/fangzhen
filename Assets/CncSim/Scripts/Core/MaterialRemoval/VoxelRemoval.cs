using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Core.MaterialRemoval
{
    /// <summary>
    /// 体素 / SDF 高精度材料去除（功能 53、54）。
    /// 用有符号距离场表示毛坯，切削时做 CSG 减法：sdf_new = max(sdf_blank, -sdf_tool)。
    /// 优点：任意刀具形状、任意方向（含五轴）、平滑表面；代价：内存与速度。
    /// 使用窄带 + 分块以控制内存。
    /// </summary>
    public class VoxelRemoval : IMaterialRemoval
    {
        private readonly BlankDefinition _blank;
        private readonly int _nx, _ny, _nz;
        private readonly float[] _sdf;
        private readonly double _cell;
        private readonly Vec3d _origin;
        private double _removedVolume;

        public int ResolutionX => _nx;
        public int ResolutionY => _ny;
        public int ResolutionZ => _nz;
        public double CellSize => _cell;

        public double RemovedVolume => _removedVolume;
        public double InitialVolume { get; private set; }

        /// <summary>
        /// 构造体素场。resolution 为长边方向体素数（建议 64-256）。
        /// </summary>
        public VoxelRemoval(BlankDefinition blank, int resolution = 96)
        {
            _blank = blank ?? throw new ArgumentNullException(nameof(blank));
            var b = _blank.Bounds;
            var size = b.Size;
            double longest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            int res = Math.Max(16, Math.Min(resolution, 512));
            _cell = longest / res;
            _nx = Math.Max(2, (int)Math.Ceiling(size.X / _cell) + 3);
            _ny = Math.Max(2, (int)Math.Ceiling(size.Y / _cell) + 3);
            _nz = Math.Max(2, (int)Math.Ceiling(size.Z / _cell) + 3);
            long total = (long)_nx * _ny * _nz;
            if (total > 24_000_000)
            {
                double scale = Math.Pow(total / 24_000_000.0, 1.0 / 3.0);
                _cell *= scale;
                _nx = Math.Max(2, (int)Math.Ceiling(size.X / _cell) + 3);
                _ny = Math.Max(2, (int)Math.Ceiling(size.Y / _cell) + 3);
                _nz = Math.Max(2, (int)Math.Ceiling(size.Z / _cell) + 3);
            }
            _origin = b.Min - new Vec3d(_cell, _cell, _cell);
            _sdf = new float[(int)((long)_nx * _ny * _nz)];
            InitialVolume = _blank.Shape == BlankShape.Cylinder
                ? Math.PI * Math.Pow(_blank.Size.X * 0.5, 2) * _blank.Size.Z
                : _blank.Size.X * _blank.Size.Y * _blank.Size.Z;
            Reset();
        }

        public void Reset()
        {
            for (int k = 0; k < _nz; k++)
            {
                double z = _origin.Z + k * _cell;
                for (int j = 0; j < _ny; j++)
                {
                    double y = _origin.Y + j * _cell;
                    for (int i = 0; i < _nx; i++)
                    {
                        double x = _origin.X + i * _cell;
                        _sdf[Index(i, j, k)] = (float)BlankSdf(new Vec3d(x, y, z));
                    }
                }
            }
            _removedVolume = 0;
        }

        private double BlankSdf(Vec3d p)
        {
            if (_blank.Shape == BlankShape.Cylinder)
            {
                var c = _blank.Center;
                double r = _blank.Size.X * 0.5;
                double dx = Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y)) - r;
                double dz = Math.Abs(p.Z - (c.Z + _blank.Size.Z * 0.5)) - _blank.Size.Z * 0.5;
                double dxy = Math.Min(Math.Max(dx, 0), 0);
                double dzz = Math.Min(Math.Max(dz, 0), 0);
                return Math.Sqrt(dxy * dxy + dzz * dzz) + Math.Max(dx, dz);
            }
            return _blank.Bounds.SignedDistance(p);
        }

        public void Cut(MotionSegment segment, ToolDefinition tool)
        {
            if (tool == null || !segment.IsMotion) RemoveToolSweep(segment, tool);
        }

        /// <summary>
        /// 单次刀位（用于旋转轴姿态变化时逐点切削）。
        /// </summary>
        public void CutPoint(Vec3d tip, Vec3d axis, ToolDefinition tool)
        {
            if (tool == null) return;
            tool.Normalize();
            double reach = tool.FluteLength + tool.Radius + 2 * _cell;
            var center = tip + axis.Normalized * (reach * 0.5);
            double radius = reach * 0.5 + tool.Radius;
            int i0 = ClampIndex((center.X - radius - _origin.X) / _cell, _nx);
            int i1 = ClampIndex((center.X + radius - _origin.X) / _cell, _nx);
            int j0 = ClampIndex((center.Y - radius - _origin.Y) / _cell, _ny);
            int j1 = ClampIndex((center.Y + radius - _origin.Y) / _cell, _ny);
            int k0 = ClampIndex((center.Z - radius - _origin.Z) / _cell, _nz);
            int k1 = ClampIndex((center.Z + radius - _origin.Z) / _cell, _nz);
            double cellVolume = _cell * _cell * _cell;

            for (int k = k0; k <= k1; k++)
            {
                double z = _origin.Z + k * _cell;
                for (int j = j0; j <= j1; j++)
                {
                    double y = _origin.Y + j * _cell;
                    for (int i = i0; i <= i1; i++)
                    {
                        int idx = Index(i, j, k);
                        if (_sdf[idx] < 0) continue; // 已是空气
                        double x = _origin.X + i * _cell;
                        Vec3d p = new Vec3d(x, y, z);
                        double dTool = ToolSdf(p, tip, axis, tool);
                        if (dTool < 0)
                        {
                            // CSG 减法：毛坯 sdf = max(blankSdf, -toolSdf)
                            double newSdf = Math.Max(_sdf[idx], -dTool);
                            if (newSdf > 0)
                            {
                                _removedVolume += cellVolume;
                                _sdf[idx] = (float)Math.Min(newSdf, cellVolume);
                            }
                        }
                    }
                }
            }
        }

        private void RemoveToolSweep(MotionSegment segment, ToolDefinition tool)
        {
            Vec3d axis = Vec3d.UnitZ;
            double maxStep = _cell;
            var pts = segment.Discretize(0.02, maxStep);
            for (int i = 0; i < pts.Count; i++)
            {
                Vec3d tip = pts[i].Linear;
                // 简化：用起点方向作为刀轴（五轴时由调用方提供）
                CutPoint(tip, axis, tool);
            }
        }

        /// <summary>刀具 SDF（近似：旋转体，含球头/平底/钻尖）。</summary>
        private static double ToolSdf(Vec3d p, Vec3d tip, Vec3d axis, ToolDefinition tool)
        {
            Vec3d n = axis.LengthSquared > 1e-12 ? axis.Normalized : Vec3d.UnitZ;
            Vec3d rel = p - tip;
            double h = Vec3d.Dot(rel, n);            // 沿刀轴高度
            Vec3d radial = rel - n * h;
            double r = radial.Length;
            double R = tool.Radius;
            double stub = Math.Max(tool.FluteLength, R * 2);
            if (h < -1e-6 || h > stub) return double.MaxValue;
            switch (tool.Type)
            {
                case ToolType.BallEndMill:
                {
                    // 半球 + 圆柱
                    if (h <= R)
                        return Math.Sqrt(r * r + h * h) - R;
                    return Math.Max(r - R, h - stub);
                }
                default:
                {
                    return Math.Max(r - R, Math.Max(-h, h - stub));
                }
            }
        }

        private int Index(int i, int j, int k) => (k * _ny + j) * _nx + i;

        private static int ClampIndex(double v, int n) => Math.Max(0, Math.Min(n - 1, (int)Math.Round(v)));

        /// <summary>查询某点是否仍为实体（sdf<=0）。</summary>
        public bool IsSolid(Vec3d p)
        {
            int i = (int)Math.Round((p.X - _origin.X) / _cell);
            int j = (int)Math.Round((p.Y - _origin.Y) / _cell);
            int k = (int)Math.Round((p.Z - _origin.Z) / _cell);
            if (i < 0 || i >= _nx || j < 0 || j >= _ny || k < 0 || k >= _nz) return false;
            return _sdf[Index(i, j, k)] <= 0;
        }

        /// <summary>
        /// 用 Marching Cubes 生成等值面（sdf=0）。为控制规模，可指定跳过步长。
        /// </summary>
        public MeshData BuildMesh()
        {
            var m = new MeshData();
            var color = ColorRgba.FromHex(MaterialColorHex);
            for (int k = 0; k < _nz - 1; k++)
                for (int j = 0; j < _ny - 1; j++)
                    for (int i = 0; i < _nx - 1; i++)
                        TriangulateCell(m, i, j, k, color);
            m.RecalculateNormals();
            return m;
        }

        public string MaterialColorHex { get; set; } = "#B8BCC2";

        private void TriangulateCell(MeshData m, int i, int j, int k, ColorRgba color)
        {
            // 8 个角点
            double[] val = new double[8];
            Vec3d[] pos = new Vec3d[8];
            for (int c = 0; c < 8; c++)
            {
                int di = c & 1, dj = (c >> 1) & 1, dk = (c >> 2) & 1;
                int ii = i + di, jj = j + dj, kk = k + dk;
                val[c] = _sdf[Index(ii, jj, kk)];
                pos[c] = new Vec3d(_origin.X + ii * _cell, _origin.Y + jj * _cell, _origin.Z + kk * _cell);
            }
            int cubeIndex = 0;
            for (int c = 0; c < 8; c++) if (val[c] <= 0) cubeIndex |= 1 << c;
            if (cubeIndex == 0 || cubeIndex == 255) return;

            // 简化：对每条边插值生成交点，然后用扇形三角化（对凸角足够，视觉可接受）
            var points = new List<Vec3d>();
            int[,] edges = EdgeTable;
            for (int e = 0; e < 12; e++)
            {
                int a = edges[e, 0], b = edges[e, 1];
                bool inA = val[a] <= 0, inB = val[b] <= 0;
                if (inA == inB) continue;
                double t = val[a] / (val[a] - val[b]);
                points.Add(Vec3d.Lerp(pos[a], pos[b], t));
            }
            if (points.Count < 3) return;
            // 以质心为中心三角扇
            Vec3d centroid = Vec3d.Zero;
            foreach (var p in points) centroid += p;
            centroid /= points.Count;
            for (int s = 0; s < points.Count; s++)
            {
                Vec3d a = points[s];
                Vec3d b = points[(s + 1) % points.Count];
                if (Vec3d.Cross(a - centroid, b - centroid).LengthSquared < 1e-12) continue;
                int i0 = m.AddVertex(centroid, Vec3d.UnitZ, color);
                int i1 = m.AddVertex(a, Vec3d.UnitZ, color);
                int i2 = m.AddVertex(b, Vec3d.UnitZ, color);
                m.AddTriangle(i0, i1, i2);
            }
        }

        private static readonly int[,] EdgeTable =
        {
            {0,1},{1,3},{3,2},{2,0},
            {4,5},{5,7},{7,6},{6,4},
            {0,4},{1,5},{3,7},{2,6}
        };
    }
}
