using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;

namespace CncSim.Core.MachineModel
{
    /// <summary>
    /// 机床模型生成：主轴、工作台、床身、龙门等简化部件。
    /// 各部件为独立网格，Unity 层可为每个部件创建 GameObject 以单独控制变换。
    /// </summary>
    public class MachineModelBuilder
    {
        private readonly MachineProfile _machine;

        public MachineModelBuilder(MachineProfile machine)
        {
            _machine = machine ?? MachineProfile.Create3Axis();
        }

        public MachineParts Build()
        {
            var parts = new MachineParts();
            var travel = ComputeTravel();

            // 工作台：覆盖 X/Y 行程，位于 Z 最高行程附近
            var tableMin = new Vec3d(travel.Min.X - 50, travel.Min.Y - 50, travel.Min.Z - 40);
            var tableMax = new Vec3d(travel.Max.X + 50, travel.Max.Y + 50, travel.Min.Z - 5);
            parts.TableMesh = MeshPrimitives.Box(tableMin, tableMax);

            // 床身/底座
            parts.BaseMesh = MeshPrimitives.Box(
                new Vec3d(tableMin.X - 30, tableMin.Y - 30, tableMin.Z - 60),
                new Vec3d(tableMax.X + 30, tableMax.Y + 30, tableMin.Z));

            // 立柱（Z 轴支持）
            double columnX = travel.Max.X + 80;
            parts.ColumnMesh = MeshPrimitives.Box(
                new Vec3d(columnX, tableMin.Y, tableMin.Z - 60),
                new Vec3d(columnX + 90, tableMax.Y, travel.Max.Z + 120));

            // 主轴头/滑枕：可沿 Z 移动，原点在主轴端面中心
            parts.SpindleMesh = BuildSpindle();

            BuildMultiAxisParts(parts, travel);

            return parts;
        }

        private void BuildMultiAxisParts(MachineParts parts, Aabb travel)
        {
            var type = _machine.Type;
            var kin = _machine.Kinematics;
            if (type != MachineType.FourAxis && type != MachineType.FiveAxis && kin == FiveAxisKinematics.None)
                return;

            bool tableRotary = kin != FiveAxisKinematics.HeadHead;
            bool swivelHead = kin == FiveAxisKinematics.HeadTable || kin == FiveAxisKinematics.HeadHead;
            if (type == MachineType.FourAxis && kin == FiveAxisKinematics.None) tableRotary = true;

            double cx = (travel.Min.X + travel.Max.X) * 0.5;
            double cy = (travel.Min.Y + travel.Max.Y) * 0.5;
            double cz = travel.Min.Z - 5;

            if (tableRotary)
            {
                // 旋转工作台：枢轴在工作台中心，局部原点即枢轴
                double r = Math.Min(travel.Max.X - travel.Min.X, travel.Max.Y - travel.Min.Y) * 0.4;
                r = Math.Max(r, 40);
                var disc = MeshPrimitives.Cylinder(new Vec3d(0, 0, 0), r, 20, 48);
                var platter = MeshPrimitives.Cylinder(new Vec3d(0, 0, 10), r * 0.85, 12, 48);
                parts.RotaryTableMesh = new MeshData();
                parts.RotaryTableMesh.Append(disc);
                parts.RotaryTableMesh.Append(platter);
                parts.RotaryTablePivot = new Vec3d(cx, cy, cz);
                parts.HasRotaryTable = true;
            }

            if (swivelHead)
            {
                // 摆动头：枢轴在主轴上方，局部原点即枢轴
                double span = Math.Max(travel.Max.Z - travel.Min.Z, 150) * 0.5;
                double width = 70;
                var yoke = new MeshData();
                yoke.Append(MeshPrimitives.Box(
                    new Vec3d(-width, -width * 0.5, -width * 0.5),
                    new Vec3d(width, width * 0.5, width * 0.5)));
                yoke.Append(MeshPrimitives.Box(
                    new Vec3d(-20, -20, -span),
                    new Vec3d(20, 20, 0)));
                parts.SwivelHeadMesh = yoke;
                parts.SwivelHeadPivot = new Vec3d(cx, cy, travel.Max.Z);
                parts.HasSwivelHead = true;
            }
        }

        private MeshData BuildSpindle()
        {
            var m = new MeshData();
            double bodyH = 120;
            double bodyR = Math.Max(40, _machine.SpindleGaugeLength * 0.4);
            var body = MeshPrimitives.Cylinder(new Vec3d(0, 0, 0), bodyR, bodyH, 32);
            m.Append(body);
            // 主轴锥面/端面
            var nose = MeshPrimitives.Cylinder(new Vec3d(0, 0, -25), bodyR * 0.45, 25, 24);
            m.Append(nose);
            return m;
        }

        private Aabb ComputeTravel()
        {
            var box = Aabb.Empty;
            box.Encapsulate(Vec3d.Zero);
            for (int i = 0; i < 3; i++)
            {
                var a = _machine.Axes[i];
                if (!a.Enabled) continue;
                Vec3d min = Vec3d.Zero, max = Vec3d.Zero;
                min[i] = a.Min;
                max[i] = a.Max;
                box.Encapsulate(min);
                box.Encapsulate(max);
            }
            return box;
        }

        /// <summary>生成单把刀具的三维模型（刀尖在原点，+Z 为刀柄方向）。</summary>
        public static MeshData BuildToolMesh(Tooling.ToolDefinition tool, int segments = 24)
        {
            if (tool == null) return new MeshData();
            tool.Normalize();
            var profile = tool.BuildRevolveProfile();
            var m = MeshPrimitives.Revolve(profile, segments);
            return m;
        }

        /// <summary>生成刀柄（位于刀柄长度以上）的可选模型，用于干涉检查显示。</summary>
        public static MeshData BuildHolderMesh(Tooling.ToolDefinition tool, double holderDiameter = 40, double holderHeight = 60)
        {
            if (tool == null) return new MeshData();
            var m = new MeshData();
            var body = MeshPrimitives.Cylinder(new Vec3d(0, 0, tool.StickOut), holderDiameter * 0.5, holderHeight, 24);
            m.Append(body);
            return m;
        }
    }

    /// <summary>机床各部件网格集合。</summary>
    public class MachineParts
    {
        public MeshData TableMesh = new MeshData();
        public MeshData BaseMesh = new MeshData();
        public MeshData ColumnMesh = new MeshData();
        public MeshData SpindleMesh = new MeshData();

        /// <summary>旋转工作台（4 轴 A 轴 / 5 轴 C 轴），枢轴原点在网格局部 (0,0,0)。</summary>
        public MeshData RotaryTableMesh = new MeshData();
        /// <summary>摆动头（5 轴 B 轴），枢轴原点在网格局部 (0,0,0)。</summary>
        public MeshData SwivelHeadMesh = new MeshData();
        /// <summary>旋转工作台枢轴（机床坐标）。</summary>
        public Vec3d RotaryTablePivot = Vec3d.Zero;
        /// <summary>摆动头枢轴（机床坐标）。</summary>
        public Vec3d SwivelHeadPivot = Vec3d.Zero;
        /// <summary>是否存在旋转工作台 / 摆动头。</summary>
        public bool HasRotaryTable;
        public bool HasSwivelHead;
    }
}
