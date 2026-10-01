using System;
using System.Collections.Generic;

namespace CncSim.Core.Kinematics
{
    /// <summary>
    /// 机床运动学求解：把 CAM 侧的控制点（刀尖位置 + 刀具方向）转换为各轴坐标，支持五轴。
    /// 三类结构：
    ///   TableTable：工件在双转台上（AC），刀具方向固定，靠转台改变工件姿态。
    ///   HeadTable：B 摆头 + C 转台。
    ///   HeadHead：A/B 双摆头，工件固定。
    /// 当启用 RTCP（G43.4）时，旋转轴变化不会移动刀尖（刀尖跟随）。
    /// </summary>
    public class MachineKinematics
    {
        private readonly Parsing.MachineProfile _machine;

        public MachineKinematics(Parsing.MachineProfile machine)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        public Parsing.MachineType Type => _machine.Type;
        public Parsing.FiveAxisKinematics Kind => _machine.Kinematics;

        /// <summary>
        /// 根据刀尖目标位置与刀轴方向（单位向量，指向远离工件，即刀具轴线朝外）
        /// 求解轴位置。三轴/四轴下仅使用位置，忽略方向。
        /// 返回的 A/B/C 为角度制。
        /// </summary>
        public Parsing.AxisVector Solve(Vec3d tipPosition, Vec3d toolDirection)
        {
            var result = new Parsing.AxisVector(tipPosition.X, tipPosition.Y, tipPosition.Z, 0, 0, 0);
            Vec3d d = toolDirection.LengthSquared > 1e-12 ? toolDirection.Normalized : Vec3d.UnitZ;
            switch (_machine.Kinematics)
            {
                case Parsing.FiveAxisKinematics.TableTable:
                {
                    // AC 双转台：C 绕 Z，A 绕 X。刀尖位置随转台旋转，需反算工件坐标系下的点。
                    double a = Math.Acos(Math.Max(-1, Math.Min(1, d.Z))) * MathUtil.Rad2Deg;
                    double c = Math.Atan2(d.X, d.Y) * MathUtil.Rad2Deg;
                    result.A = a;
                    result.C = c;
                    break;
                }
                case Parsing.FiveAxisKinematics.HeadTable:
                {
                    // B 摆头（绕 Y）+ C 转台（绕 Z）
                    double b = Math.Acos(Math.Max(-1, Math.Min(1, d.Z))) * MathUtil.Rad2Deg;
                    double c = Math.Atan2(d.X, d.Y) * MathUtil.Rad2Deg;
                    result.B = b;
                    result.C = c;
                    break;
                }
                case Parsing.FiveAxisKinematics.HeadHead:
                {
                    // A（绕 X）+ B（绕 Y）双摆头
                    double a = Math.Asin(Math.Max(-1, Math.Min(1, -d.Y))) * MathUtil.Rad2Deg;
                    double b = Math.Asin(Math.Max(-1, Math.Min(1, d.X))) * MathUtil.Rad2Deg;
                    result.A = a;
                    result.B = b;
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// 正向运动学：由各轴坐标求刀尖位置与刀轴方向。
        /// </summary>
        public void Forward(Parsing.AxisVector axes, out Vec3d tipPosition, out Vec3d toolDirection)
        {
            tipPosition = new Vec3d(axes.X, axes.Y, axes.Z);
            toolDirection = Vec3d.UnitZ;
            switch (_machine.Kinematics)
            {
                case Parsing.FiveAxisKinematics.TableTable:
                {
                    // 刀轴方向为机床 Z，转台使工件倾斜；此处置工件坐标的反变换，方向保持 +Z
                    double a = axes.A * MathUtil.Deg2Rad;
                    double c = axes.C * MathUtil.Deg2Rad;
                    // 对 TableTable，刀轴在工件坐标系中的方向：
                    Vec3d dir = MathUtil.RotateZ(MathUtil.RotateX(Vec3d.UnitZ, a), c);
                    toolDirection = dir;
                    break;
                }
                case Parsing.FiveAxisKinematics.HeadTable:
                {
                    double b = axes.B * MathUtil.Deg2Rad;
                    double c = axes.C * MathUtil.Deg2Rad;
                    toolDirection = MathUtil.RotateZ(MathUtil.RotateY(Vec3d.UnitZ, b), c);
                    break;
                }
                case Parsing.FiveAxisKinematics.HeadHead:
                {
                    double a = axes.A * MathUtil.Deg2Rad;
                    double b = axes.B * MathUtil.Deg2Rad;
                    toolDirection = MathUtil.RotateY(MathUtil.RotateX(Vec3d.UnitZ, a), b);
                    break;
                }
            }
        }

        /// <summary>该结构下的换刀/基准旋转中心（机床坐标）。默认取回转台中心在工作台面上。</summary>
        public Vec3d RotaryPivot { get; set; } = Vec3d.Zero;

        /// <summary>
        /// RTCP 刀尖跟随补偿：当旋转轴变化时，为保持刀尖不动，线性轴需产生补偿位移。
        /// 返回补偿后的线性轴坐标（在给定轴坐标基础上叠加）。
        /// </summary>
        public Vec3d RtcpCompensation(Parsing.AxisVector axes, Vec3d previousTip, Vec3d pivot)
        {
            if (_machine.Kinematics == Parsing.FiveAxisKinematics.None) return Vec3d.Zero;
            Forward(axes, out Vec3d tip, out Vec3d dir);
            // 当前刀尖相对回转中心的位置，叠加到期望刀尖
            Vec3d delta = previousTip - tip;
            return delta;
        }

        /// <summary>
        /// 对一段运动进行运动学采样：返回每个采样点的轴坐标列表，
        /// 用于仿真时的旋转轴插补与碰撞检测。
        /// </summary>
        public List<Parsing.AxisVector> SampleSegment(Parsing.MotionSegment seg, double maxLinearStep, double maxRotaryStepDeg)
        {
            var list = new List<Parsing.AxisVector>();
            int steps = 1;
            double len = seg.Length;
            if (maxLinearStep > 0 && len > 0) steps = Math.Max(steps, (int)Math.Ceiling(len / maxLinearStep));
            double rot = seg.Start.MaxRotaryDelta(seg.End);
            if (maxRotaryStepDeg > 0 && rot > 0) steps = Math.Max(steps, (int)Math.Ceiling(rot / maxRotaryStepDeg));
            steps = Math.Min(steps, 20000);
            for (int i = 0; i <= steps; i++)
                list.Add(seg.Evaluate((double)i / steps));
            return list;
        }
    }
}
