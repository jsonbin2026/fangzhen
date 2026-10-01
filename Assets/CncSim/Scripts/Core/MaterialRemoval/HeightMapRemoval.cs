using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Core.MaterialRemoval
{
    /// <summary>
    /// 高度图（Z-Map）材料去除（功能 52）：把毛坯顶面离散为规则网格，记录每格剩余高度。
    /// 只适合三轴或刀具始终竖直的加工，速度快、内存小，可实时更新。
    /// </summary>
    public class HeightMapRemoval : IMaterialRemoval
    {
        private readonly BlankDefinition _blank;
        private double[] _height;      // 每格顶部高度（世界 Z）
        private bool[] _removed;       // 是否已切削到最低层（贯穿）
        private int _nx, _ny;
        private double _minX, _minY, _cellSize, _minZ;
        private double _removedVolume;

        public int ResolutionX { get; }
        public int ResolutionY { get; }
        public double CellSize => _cellSize;

        public double RemovedVolume => _removedVolume;
        public double InitialVolume { get; private set; }

        /// <summary>
        /// 构造高度图。resolution 为长边方向的分辨率（格子数）。
        /// 限制最大格子数避免内存爆炸。
        /// </summary>
        public HeightMapRemoval(BlankDefinition blank, int resolution = 200)
        {
            _blank = blank ?? throw new ArgumentNullException(nameof(blank));
            if (_blank.Shape == BlankShape.Cylinder)
            {
                // 圆柱用外接盒，非圆形区域标记为空
                var b = _blank.Bounds;
                Setup(b, resolution, circular: true);
            }
            else
            {
                Setup(_blank.Bounds, resolution, circular: false);
            }
            Reset();
        }

        private void Setup(Aabb b, int resolution, bool circular)
        {
            _minX = b.Min.X;
            _minY = b.Min.Y;
            _minZ = b.Min.Z;
            var size = b.Size;
            double longest = Math.Max(size.X, size.Y);
            int maxRes = Math.Max(8, Math.Min(resolution, 1024));
            _cellSize = longest / maxRes;
            _nx = Math.Max(2, (int)Math.Ceiling(size.X / _cellSize) + 1);
            _ny = Math.Max(2, (int)Math.Ceiling(size.Y / _cellSize) + 1);
            long total = (long)_nx * _ny;
            if (total > 4_000_000)
            {
                double scale = Math.Sqrt(total / 4_000_000.0);
                _cellSize *= scale;
                _nx = Math.Max(2, (int)Math.Ceiling(size.X / _cellSize) + 1);
                _ny = Math.Max(2, (int)Math.Ceiling(size.Y / _cellSize) + 1);
            }
            _height = new double[_nx * _ny];
            _removed = new bool[_nx * _ny];
            IsCircular = circular;
        }

        public bool IsCircular { get; private set; }

        public void Reset()
        {
            double top = _blank.Bounds.Max.Z;
            Array.Fill(_height, top);
            Array.Fill(_removed, false);
            InitialVolume = _blank.Shape == BlankShape.Cylinder
                ? Math.PI * Math.Pow(_blank.Size.X * 0.5, 2) * _blank.Size.Z
                : _blank.Size.X * _blank.Size.Y * _blank.Size.Z;
            _removedVolume = 0;
        }

        public void Cut(MotionSegment segment, ToolDefinition tool)
        {
            if (tool == null || !segment.IsMotion) return;
            var section = ToolSection.From(tool);
            double maxStep = Math.Max(_cellSize * 0.5, 0.1);
            var points = segment.Discretize(0.02, maxStep);
            for (int i = 0; i + 1 < points.Count; i++)
                CutSegment(section, points[i].Linear, points[i + 1].Linear);
        }

        private void CutSegment(ToolSection section, Vec3d a, Vec3d b)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Vec3d.Distance(a, b) / (_cellSize * 0.5)));
            steps = Math.Min(steps, 4096);
            for (int s = 0; s <= steps; s++)
            {
                Vec3d tip = Vec3d.Lerp(a, b, (double)s / steps);
                ApplyTool(section, tip);
            }
        }

        private void ApplyTool(ToolSection section, Vec3d tip)
        {
            double radius = section.Radius + 1e-6;
            int i0 = Math.Max(0, (int)Math.Floor((tip.X - radius - _minX) / _cellSize));
            int i1 = Math.Min(_nx - 1, (int)Math.Ceiling((tip.X + radius - _minX) / _cellSize));
            int j0 = Math.Max(0, (int)Math.Floor((tip.Y - radius - _minY) / _cellSize));
            int j1 = Math.Min(_ny - 1, (int)Math.Ceiling((tip.Y + radius - _minY) / _cellSize));
            double cellArea = _cellSize * _cellSize;
            double minZ = _blank.Bounds.Min.Z;

            for (int j = j0; j <= j1; j++)
            {
                double py = _minY + j * _cellSize;
                for (int i = i0; i <= i1; i++)
                {
                    double px = _minX + i * _cellSize;
                    if (IsCircular)
                    {
                        double dx = px - _blank.Center.X, dy = py - _blank.Center.Y;
                        if (dx * dx + dy * dy > Math.Pow(_blank.Size.X * 0.5, 2)) continue;
                    }
                    double cutZ = section.CuttingZ(tip.X, tip.Y, tip.Z, px, py);
                    if (double.IsInfinity(cutZ)) continue;
                    int idx = j * _nx + i;
                    if (cutZ < _height[idx] - 1e-6)
                    {
                        double delta = _height[idx] - Math.Max(cutZ, minZ);
                        if (delta > 0)
                        {
                            _height[idx] -= delta;
                            _removedVolume += delta * cellArea;
                        }
                        if (_height[idx] <= minZ + 1e-6) _removed[idx] = true;
                    }
                }
            }
        }

        /// <summary>查询某点剩余材料高度（世界 Z）；无材料返回 NaN。</summary>
        public double SampleHeight(double x, double y)
        {
            int i = (int)Math.Round((x - _minX) / _cellSize);
            int j = (int)Math.Round((y - _minY) / _cellSize);
            if (i < 0 || i >= _nx || j < 0 || j >= _ny) return double.NaN;
            int idx = j * _nx + i;
            if (_removed[idx] || _height[idx] <= _minZ + 1e-6) return double.NaN;
            return _height[idx];
        }

        /// <summary>生成顶面网格（三角形）。会跳过被完全去除的格子之间的连接。</summary>
        public MeshData BuildMesh()
        {
            var m = new MeshData { IsLines = false };
            var color = ColorRgba.FromHex(MaterialColorHex);
            for (int j = 0; j < _ny - 1; j++)
            {
                for (int i = 0; i < _nx - 1; i++)
                {
                    int a = j * _nx + i;
                    if (_removed[a]) continue;
                    double x0 = _minX + i * _cellSize;
                    double y0 = _minY + j * _cellSize;
                    double x1 = x0 + _cellSize;
                    double y1 = y0 + _cellSize;
                    if (IsCircular)
                    {
                        if (!InsideCircle(x0, y0) || !InsideCircle(x1, y0) || !InsideCircle(x1, y1) || !InsideCircle(x0, y1))
                            continue;
                    }
                    Vec3d p00 = new Vec3d(x0, y0, _height[a]);
                    Vec3d p10 = new Vec3d(x1, y0, _height[j * _nx + i + 1]);
                    Vec3d p11 = new Vec3d(x1, y1, _height[(j + 1) * _nx + i + 1]);
                    Vec3d p01 = new Vec3d(x0, y1, _height[(j + 1) * _nx + i]);
                    // 顶面：法线朝上
                    AddQuad(m, p00, p10, p11, p01, color);
                    // 侧壁（与相邻较高格之间）
                    double hRight = i + 2 <= _nx - 1 ? _height[j * _nx + i + 2] : double.NegativeInfinity;
                    if (!_removed[j * _nx + i + 1] && hRight > _height[a] + 1e-6)
                        AddWall(m, p10, p00, _height[a], hRight, color);
                }
            }
            m.RecalculateNormals();
            return m;
        }

        public string MaterialColorHex { get; set; } = "#B8BCC2";

        private bool InsideCircle(double x, double y)
        {
            double dx = x - _blank.Center.X, dy = y - _blank.Center.Y;
            return dx * dx + dy * dy <= Math.Pow(_blank.Size.X * 0.5, 2);
        }

        private static void AddQuad(MeshData m, Vec3d a, Vec3d b, Vec3d c, Vec3d d, ColorRgba color)
        {
            Vec3d n = Vec3d.Cross(b - a, c - a).Normalized;
            int i0 = m.AddVertex(a, n, color);
            int i1 = m.AddVertex(b, n, color);
            int i2 = m.AddVertex(c, n, color);
            int i3 = m.AddVertex(d, n, color);
            m.AddQuad(i0, i1, i2, i3);
        }

        private static void AddWall(MeshData m, Vec3d edgeA, Vec3d edgeB, double zA, double zB, ColorRgba color)
        {
            // 竖直侧壁：从较低顶到较高顶
            double zLow = Math.Min(zA, zB);
            double zHigh = Math.Max(zA, zB);
            if (zHigh - zLow < 1e-6) return;
            Vec3d a = new Vec3d(edgeA.X, edgeA.Y, zLow);
            Vec3d b = new Vec3d(edgeB.X, edgeB.Y, zLow);
            Vec3d c = new Vec3d(edgeB.X, edgeB.Y, zHigh);
            Vec3d d = new Vec3d(edgeA.X, edgeA.Y, zHigh);
            AddQuad(m, a, b, c, d, color);
        }
    }
}
