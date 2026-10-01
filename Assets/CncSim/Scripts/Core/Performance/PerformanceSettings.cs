using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CncSim.Core.Parsing;
using CncSim.Core.Tooling;

namespace CncSim.Core.Performance
{
    /// <summary>性能与质量档位（功能 89、90、91）。</summary>
    public enum DetailLevel
    {
        /// <summary>低多边形：优先帧率。</summary>
        LowPoly,
        Balanced,
        HighFidelity
    }

    /// <summary>渲染与计算预算配置。</summary>
    [Serializable]
    public class PerformanceSettings
    {
        public DetailLevel Detail = DetailLevel.Balanced;
        /// <summary>材料去除分辨率。</summary>
        public int RemovalResolution = 200;
        /// <summary>轨迹采样弦高（mm），越大顶点越少。</summary>
        public double TrajectoryTolerance = 0.05;
        /// <summary>轨迹管道侧面数（实体模式）。</summary>
        public int TubeSides = 6;
        /// <summary>毛坯/夹具网格分段。</summary>
        public int RadialSegments = 32;
        /// <summary>是否批量合并静态网格（功能 87）。</summary>
        public bool BatchStaticMeshes = true;
        /// <summary>是否对重复刀具/夹具使用 GPU 实例化（功能 88）。</summary>
        public bool EnableGpuInstancing = true;
        /// <summary>是否后台线程预计算（功能 85）。</summary>
        public bool BackgroundPrecompute = true;
        /// <summary>目标材料去除网格顶点上限（功能 86 网格简化阈值）。</summary>
        public int MaxRemovalVertices = 500_000;

        public void ApplyPreset(DetailLevel level)
        {
            Detail = level;
            switch (level)
            {
                case DetailLevel.LowPoly:
                    RemovalResolution = 96;
                    TrajectoryTolerance = 0.3;
                    TubeSides = 4;
                    RadialSegments = 16;
                    MaxRemovalVertices = 120_000;
                    break;
                case DetailLevel.HighFidelity:
                    RemovalResolution = 400;
                    TrajectoryTolerance = 0.005;
                    TubeSides = 10;
                    RadialSegments = 64;
                    MaxRemovalVertices = 2_000_000;
                    break;
                default:
                    RemovalResolution = 200;
                    TrajectoryTolerance = 0.05;
                    TubeSides = 6;
                    RadialSegments = 32;
                    MaxRemovalVertices = 500_000;
                    break;
            }
        }

        public PerformanceSettings Clone() => (PerformanceSettings)MemberwiseClone();
    }

    /// <summary>
    /// 后台线程预计算（功能 85）：解析、时间估算、轨迹生成、离线材料去除等重活放在线程池，
    /// 通过 Progress 汇报进度，完成后回调主线程。
    /// </summary>
    public class BackgroundPrecomputeService
    {
        private CancellationTokenSource _cts;

        public bool IsRunning { get; private set; }
        public float Progress { get; private set; }
        public event Action<float, string> ProgressChanged;
        public event Action Completed;

        /// <summary>在后台执行一个可报告进度的任务。</summary>
        public Task Run(Action<Action<float, string>, CancellationToken> work)
        {
            Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            IsRunning = true;
            Progress = 0;
            return Task.Run(() =>
            {
                try
                {
                    work((p, msg) =>
                    {
                        Progress = p;
                        ProgressChanged?.Invoke(p, msg);
                    }, token);
                    Progress = 1;
                }
                catch (OperationCanceledException)
                {
                    // 已取消
                }
                finally
                {
                    IsRunning = false;
                    Completed?.Invoke();
                }
            }, token);
        }

        public void Cancel()
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    /// <summary>
    /// 网格简化（功能 86）：顶点聚类（vertex clustering）降面，用于材料去除网格的 LOD。
    /// </summary>
    public static class MeshSimplifier
    {
        /// <summary>
        /// 顶点聚类简化：以 cellSize 为单位把顶点吸附到格点，合并退化三角形。
        /// 结果顶点数显著减少，适合远处渲染或多级 LOD。
        /// </summary>
        public static MeshData Simplify(MeshData source, double cellSize)
        {
            if (source == null || cellSize <= 0) return source;
            var result = new MeshData { IsLines = source.IsLines };
            if (source.IsLines) return SimplifyLines(source, cellSize);

            var map = new Dictionary<long, int>();
            var sums = new Dictionary<int, (Vec3d pos, int count)>();
            var remap = new int[source.VertexCount];

            for (int i = 0; i < source.VertexCount; i++)
            {
                Vec3d p = source.Vertices[i].ToVec3d();
                long key = Key(p, cellSize);
                if (!map.TryGetValue(key, out int idx))
                {
                    idx = sums.Count;
                    map[key] = idx;
                    sums[idx] = (p, 0);
                }
                var cur = sums[idx];
                sums[idx] = (cur.pos + p, cur.count + 1);
                remap[i] = idx;
            }

            var indexed = new List<Vec3d>(sums.Count);
            foreach (var kv in sums) indexed.Add(kv.Value.pos / Math.Max(1, kv.Value.count));
            foreach (var p in indexed) result.AddVertex(p, Vec3d.UnitZ);

            for (int t = 0; t + 2 < source.Indices.Count; t += 3)
            {
                int a = remap[source.Indices[t]];
                int b = remap[source.Indices[t + 1]];
                int c = remap[source.Indices[t + 2]];
                if (a == b || b == c || a == c) continue;
                result.AddTriangle(a, b, c);
            }
            result.RecalculateNormals();
            return result;
        }

        private static MeshData SimplifyLines(MeshData source, double cellSize)
        {
            var result = new MeshData { IsLines = true };
            for (int i = 0; i + 1 < source.Indices.Count; i += 2)
            {
                Vec3d a = source.Vertices[source.Indices[i]].ToVec3d();
                Vec3d b = source.Vertices[source.Indices[i + 1]].ToVec3d();
                if (Vec3d.Distance(a, b) < cellSize * 0.5) continue;
                int i0 = result.AddVertex(Snap(a, cellSize), Vec3d.UnitZ);
                int i1 = result.AddVertex(Snap(b, cellSize), Vec3d.UnitZ);
                result.Indices.Add(i0);
                result.Indices.Add(i1);
            }
            return result;
        }

        private static Vec3d Snap(Vec3d p, double cell) =>
            new Vec3d(Math.Round(p.X / cell) * cell, Math.Round(p.Y / cell) * cell, Math.Round(p.Z / cell) * cell);

        private static long Key(Vec3d p, double cell)
        {
            long x = (long)Math.Round(p.X / cell);
            long y = (long)Math.Round(p.Y / cell);
            long z = (long)Math.Round(p.Z / cell);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        /// <summary>自适应网格精度（功能 89）：根据观察距离返回合适的 cellSize。</summary>
        public static double AdaptiveCellSize(double baseCell, double viewDistance, double referenceDistance)
        {
            double ratio = Math.Max(0.25, viewDistance / Math.Max(1e-6, referenceDistance));
            // 越远越粗糙，但限制在 1x - 4x
            return baseCell * Math.Min(4.0, ratio);
        }
    }
}
