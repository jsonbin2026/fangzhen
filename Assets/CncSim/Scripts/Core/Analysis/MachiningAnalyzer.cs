using System;
using System.Collections.Generic;
using CncSim.Core.MaterialRemoval;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;

namespace CncSim.Core.Analysis
{
    /// <summary>过切 / 欠切检测报告（功能 59、60）。</summary>
    public class MachiningAnalysisResult
    {
        /// <summary>过切区域数量（切削超出目标形状）。</summary>
        public int OvercutCount;
        /// <summary>欠切区域数量（目标形状未切削到位）。</summary>
        public int UndercutCount;
        /// <summary>最大过切深度 mm。</summary>
        public double MaxOvercutDepth;
        /// <summary>最大欠切深度 mm。</summary>
        public double MaxUndercutDepth;
        /// <summary>过切采样点。</summary>
        public readonly List<Vec3d> OvercutPoints = new List<Vec3d>();
        /// <summary>欠切采样点。</summary>
        public readonly List<Vec3d> UndercutPoints = new List<Vec3d>();
        public double TargetVolume;
        public double ActualRemovedVolume;

        public bool HasIssues => OvercutCount > 0 || UndercutCount > 0;
    }

    /// <summary>
    /// 通过比较“仿真后的剩余材料”与“理想目标形状”判定过切/欠切。
    /// 目标形状用有符号距离场或解析形状（本例用 Aabb 目标；扩展可传 IMaterialRemoval 目标）。
    /// </summary>
    public static class MachiningAnalyzer
    {
        /// <summary>比较高度图结果与目标形状（三轴场景）。</summary>
        public static MachiningAnalysisResult AnalyzeHeightMap(HeightMapRemoval map, BlankDefinition blank, TargetShape target, double tolerance)
        {
            var result = new MachiningAnalysisResult();
            if (map == null || blank == null) return result;
            var b = blank.Bounds;
            int nx = Math.Max(2, (int)Math.Ceiling(b.Size.X / map.CellSize));
            int ny = Math.Max(2, (int)Math.Ceiling(b.Size.Y / map.CellSize));
            double cellArea = map.CellSize * map.CellSize;
            double sampleStep = Math.Max(1, cellArea / (map.CellSize * map.CellSize));
            for (int j = 0; j <= ny; j++)
            {
                double y = b.Min.Y + j * map.CellSize;
                for (int i = 0; i <= nx; i++)
                {
                    double x = b.Min.X + i * map.CellSize;
                    double remaining = map.SampleHeight(x, y);
                    double targetHeight = target.SurfaceZ(x, y);
                    if (double.IsNaN(remaining))
                    {
                        // 材料已被完全去除
                        if (!target.IsEmptyAt(x, y))
                        {
                            double undercut = targetHeight - b.Min.Z;
                            if (undercut > tolerance)
                            {
                                result.UndercutCount++;
                                result.MaxUndercutDepth = Math.Max(result.MaxUndercutDepth, undercut);
                                result.UndercutPoints.Add(new Vec3d(x, y, targetHeight));
                            }
                        }
                        continue;
                    }
                    if (remaining < targetHeight - tolerance)
                    {
                        // 剩余材料低于目标 => 过切
                        double over = targetHeight - remaining;
                        result.OvercutCount++;
                        result.MaxOvercutDepth = Math.Max(result.MaxOvercutDepth, over);
                        if (result.OvercutPoints.Count < 2000) result.OvercutPoints.Add(new Vec3d(x, y, remaining));
                    }
                    else if (remaining > targetHeight + tolerance && !target.IsEmptyAt(x, y))
                    {
                        double under = remaining - targetHeight;
                        result.UndercutCount++;
                        result.MaxUndercutDepth = Math.Max(result.MaxUndercutDepth, under);
                        if (result.UndercutPoints.Count < 2000) result.UndercutPoints.Add(new Vec3d(x, y, remaining));
                    }
                }
            }
            return result;
        }

        /// <summary>由程序直接推断目标形状（无 CAD 时的近似：取切削包围盒底面）。</summary>
        public static TargetShape InferTargetFromProgram(ParseResult result, BlankDefinition blank)
        {
            var box = result.CuttingBounds;
            if (!box.IsValid) return new TargetShape { Mode = TargetMode.None };
            // 目标顶面 = 切削最低点所在平面（假设为等深加工）
            return new TargetShape
            {
                Mode = TargetMode.Flat,
                RegionMin = box.Min,
                RegionMax = box.Max,
                FlatZ = box.Min.Z
            };
        }
    }

    public enum TargetMode
    {
        None,
        Flat,
        /// <summary>保留毛坯顶面，仅切除指定区域。</summary>
        Pocket
    }

    /// <summary>理想目标形状（简化）。可扩展为 STL/CAD 采样的高度场。</summary>
    public class TargetShape
    {
        public TargetMode Mode = TargetMode.Flat;
        public Vec3d RegionMin;
        public Vec3d RegionMax;
        /// <summary>目标顶面高度。</summary>
        public double FlatZ;

        public double SurfaceZ(double x, double y)
        {
            switch (Mode)
            {
                case TargetMode.Flat:
                    if (x < RegionMin.X || x > RegionMax.X || y < RegionMin.Y || y > RegionMax.Y)
                        return double.PositiveInfinity; // 该处无需加工
                    return FlatZ;
                case TargetMode.Pocket:
                    return FlatZ;
                default:
                    return double.NegativeInfinity; // 无需加工
            }
        }

        public bool IsEmptyAt(double x, double y) => Mode == TargetMode.None;
    }
}
