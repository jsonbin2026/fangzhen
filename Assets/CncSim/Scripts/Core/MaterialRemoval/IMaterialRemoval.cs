using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;
using CncSim.Core.Tooling;

namespace CncSim.Core.MaterialRemoval
{
    /// <summary>材料去除引擎接口。两种实现：高度图（快）与体素/SDF（精）。</summary>
    public interface IMaterialRemoval
    {
        /// <summary>用一把刀具沿一段运动切削。</summary>
        void Cut(MotionSegment segment, ToolDefinition tool);

        /// <summary>重置为初始毛坯。</summary>
        void Reset();

        /// <summary>材料去除进度 0-1。</summary>
        double RemovedVolume { get; }

        /// <summary>初始体积 mm^3。</summary>
        double InitialVolume { get; }

        /// <summary>导出可供渲染的网格。</summary>
        MeshData BuildMesh();
    }

    /// <summary>刀具端部形状的解析截面（用于高度图与体素求交）。</summary>
    public struct ToolSection
    {
        public ToolType Type;
        public double Radius;
        public double CornerRadius;
        public double TipAngleRad;

        public static ToolSection From(ToolDefinition tool)
        {
            tool.Normalize();
            return new ToolSection
            {
                Type = tool.Type,
                Radius = tool.Radius,
                CornerRadius = tool.CornerRadius,
                TipAngleRad = tool.PointAngle * MathUtil.Deg2Rad * 0.5
            };
        }

        /// <summary>在局部坐标（z 为刀尖向上）中，给定半径 r 处的刀具下表面高度 z（相对刀尖）。</summary>
        public double SurfaceZ(double r)
        {
            double rr = Math.Abs(r);
            if (rr > Radius + 1e-9) return double.PositiveInfinity; // 超出刀具
            switch (Type)
            {
                case ToolType.BallEndMill:
                {
                    // 球面：z = R - sqrt(R^2 - r^2)
                    double R = Radius;
                    if (rr >= R) return R;
                    return R - Math.Sqrt(R * R - rr * rr);
                }
                case ToolType.BullNoseEndMill:
                {
                    double cr = CornerRadius;
                    double flat = Radius - cr;
                    if (rr <= flat) return 0;
                    double d = rr - flat;
                    if (d >= cr) return cr;
                    return cr - Math.Sqrt(cr * cr - d * d);
                }
                case ToolType.Drill:
                case ToolType.CenterDrill:
                {
                    if (TipAngleRad <= 1e-6) return 0;
                    return Math.Abs(rr) / Math.Tan(TipAngleRad);
                }
                case ToolType.ChamferMill:
                {
                    if (TipAngleRad <= 1e-6) return 0;
                    return Math.Max(0, Radius - Math.Abs(rr)) / Math.Tan(TipAngleRad);
                }
                default:
                    return 0; // 平底
            }
        }

        /// <summary>对竖直方向的刀具做高度图切削：返回该 xy 处刀具下缘的高度（世界坐标）。</summary>
        public double CuttingZ(double toolTipX, double toolTipY, double toolTipZ, double px, double py)
        {
            double dx = px - toolTipX, dy = py - toolTipY;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r > Radius + 1e-9) return double.PositiveInfinity;
            return toolTipZ + SurfaceZ(r);
        }
    }
}
