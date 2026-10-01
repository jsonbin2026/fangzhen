using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;

namespace CncSim.Core.Simulation
{
    /// <summary>
    /// 加工时间估算（功能 63）。考虑每轴最大速度/加速度的梯形速度曲线，取约束最紧的轴。
    /// 同时输出切削/快移/换刀分类时间。
    /// </summary>
    public static class TimeEstimator
    {
        /// <summary>为所有运动段计算 Duration 与 StartTime。返回总时间（秒）。</summary>
        public static double Estimate(ParseResult result, MachineProfile machine)
        {
            double clock = 0;
            foreach (var seg in result.Segments)
            {
                seg.StartTime = clock;
                seg.Duration = SegmentDuration(seg, machine);
                clock += seg.Duration;
            }
            return clock;
        }

        public static double SegmentDuration(MotionSegment seg, MachineProfile machine)
        {
            switch (seg.Type)
            {
                case MotionType.Dwell:
                    return seg.DwellSeconds;
                case MotionType.Event:
                    return seg.Event == ProgramEventType.ToolChange ? ToolChangeSeconds : 0;
                case MotionType.Rapid:
                    return AxisLimitedTime(seg, machine, useFeedRate: false);
                default:
                    return AxisLimitedTime(seg, machine, useFeedRate: true);
            }
        }

        public const double ToolChangeSeconds = 6.0;

        /// <summary>
        /// 按各轴速度/加速度约束计算段耗时。
        /// 对直线段，各轴按比例同时到达，段速度由最短耗时轴决定：
        /// 先假设进给率可达，再检查每轴是否超速/超加速，取最小时间。
        /// </summary>
        private static double AxisLimitedTime(MotionSegment seg, MachineProfile machine, bool useFeedRate)
        {
            if (!seg.IsMotion)
                return 0;

            double pathLength = seg.Length;
            if (pathLength < 1e-9)
            {
                // 纯旋转运动
                return RotaryOnlyTime(seg, machine);
            }

            Vec3d delta = seg.End.Linear - seg.Start.Linear;
            Vec3d absDelta = new Vec3d(Math.Abs(delta.X), Math.Abs(delta.Y), Math.Abs(delta.Z));

            // 名义进给速度 mm/s
            double nominal = useFeedRate ? Math.Max(seg.Feed, 1e-6) / 60.0 : machine.RapidFeed / 60.0;
            nominal = Math.Max(nominal, 1e-6);

            // 每轴允许的最大段速度 = 轴速度 * (段长 / 该轴位移)
            double maxSpeed = nominal;
            double minAccelCapability = double.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                var ax = machine.Axes[axis];
                if (!ax.Enabled || absDelta[axis] < 1e-9) continue;
                double ratio = pathLength / absDelta[axis];
                double axisMaxPathSpeed = ax.MaxVelocity / 60.0 * ratio; // mm/min -> mm/s，换算到路径
                maxSpeed = Math.Min(maxSpeed, axisMaxPathSpeed);
                // 该轴沿路径的加速度上限
                double axisAccelPath = ax.MaxAcceleration * ratio;
                minAccelCapability = Math.Min(minAccelCapability, axisAccelPath);
            }
            // 旋转轴约束
            for (int axis = 3; axis < 6; axis++)
            {
                var ax = machine.Axes[axis];
                if (!ax.Enabled) continue;
                double deg = Math.Abs(seg.End[axis] - seg.Start[axis]);
                if (deg < 1e-9) continue;
                double degPerSec = ax.MaxVelocity / 60.0;
                double timeForAxis = deg / Math.Max(1e-9, degPerSec);
                // 旋转限制会拉长段时间，这里以最小可能段速度换算
                double rotationLimitedPathSpeed = pathLength / Math.Max(1e-9, timeForAxis);
                maxSpeed = Math.Min(maxSpeed, rotationLimitedPathSpeed);
            }

            double accel = minAccelCapability == double.MaxValue ? nominal * 2 : minAccelCapability;
            accel = Math.Max(accel, 1e-6);

            // 梯形速度曲线时间
            double time;
            double accelTime = maxSpeed / accel;
            double accelDistance = 0.5 * accel * accelTime * accelTime * 2; // 加速+减速
            if (accelDistance <= pathLength)
            {
                time = accelTime * 2 + (pathLength - accelDistance) / maxSpeed;
            }
            else
            {
                // 三角形曲线
                double peak = Math.Sqrt(pathLength * accel);
                time = 2 * peak / accel;
            }

            // 旋转轴单独的角速度限制再取最大
            double rotTime = RotaryOnlyTime(seg, machine);
            return Math.Max(time, rotTime);
        }

        private static double RotaryOnlyTime(MotionSegment seg, MachineProfile machine)
        {
            double t = 0;
            for (int axis = 3; axis < 6; axis++)
            {
                var ax = machine.Axes[axis];
                if (!ax.Enabled) continue;
                double deg = Math.Abs(seg.End[axis] - seg.Start[axis]);
                if (deg < 1e-9) continue;
                double v = Math.Max(1e-6, ax.MaxVelocity / 60.0); // deg/s
                double a = Math.Max(1e-6, ax.MaxAcceleration);    // deg/s^2
                double accelTime = v / a;
                double accelDist = 0.5 * a * accelTime * accelTime * 2;
                double segTime = accelDist <= deg ? accelTime * 2 + (deg - accelDist) / v : 2 * Math.Sqrt(deg / a);
                t = Math.Max(t, segTime);
            }
            return t;
        }

        /// <summary>统计分类时间。</summary>
        public class Breakdown
        {
            public double Cutting;
            public double Rapid;
            public double Dwell;
            public double ToolChange;
            public double Total => Cutting + Rapid + Dwell + ToolChange;
        }

        public static Breakdown Analyze(ParseResult result, MachineProfile machine)
        {
            var b = new Breakdown();
            foreach (var seg in result.Segments)
            {
                double d = seg.Duration > 0 ? seg.Duration : SegmentDuration(seg, machine);
                switch (seg.Type)
                {
                    case MotionType.Rapid:
                        b.Rapid += d;
                        break;
                    case MotionType.Dwell:
                        b.Dwell += d;
                        break;
                    case MotionType.Event:
                        if (seg.Event == ProgramEventType.ToolChange) b.ToolChange += d;
                        break;
                    default:
                        if (seg.IsCutting) b.Cutting += d;
                        break;
                }
            }
            return b;
        }
    }
}
