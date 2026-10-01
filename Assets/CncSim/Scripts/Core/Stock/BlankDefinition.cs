using System;
using System.Collections.Generic;

namespace CncSim.Core.Stock
{
    public enum BlankShape
    {
        Box,
        Cylinder,
        Custom
    }

    /// <summary>
    /// 毛坯定义。位置以工件坐标系为参照，通常毛坯顶面为 Z=0。
    /// </summary>
    [Serializable]
    public class BlankDefinition
    {
        public BlankShape Shape = BlankShape.Box;
        /// <summary>长方体尺寸 / 圆柱直径与高 / 自定义包围盒尺寸（mm）。</summary>
        public Vec3d Size = new Vec3d(100, 100, 50);
        /// <summary>毛坯中心（底面中心）在机床坐标中的位置。</summary>
        public Vec3d Center = Vec3d.Zero;

        /// <summary>圆柱分段数。</summary>
        public int RadialSegments = 48;

        /// <summary>自定义毛坯：由外部提供的三角网格。</summary>
        public MeshData CustomMesh;

        /// <summary>材料 id。</summary>
        public string MaterialId = "aluminum";

        /// <summary>Z 轴向上为 +Z；顶面所在高度。</summary>
        public double TopZ => Center.Z + Size.Z;

        /// <summary>毛坯在机床坐标系中的包围盒。</summary>
        public Aabb Bounds
        {
            get
            {
                switch (Shape)
                {
                    case BlankShape.Cylinder:
                        return new Aabb(
                            new Vec3d(Center.X - Size.X * 0.5, Center.Y - Size.X * 0.5, Center.Z),
                            new Vec3d(Center.X + Size.X * 0.5, Center.Y + Size.X * 0.5, Center.Z + Size.Z));
                    default:
                        return new Aabb(
                            new Vec3d(Center.X - Size.X * 0.5, Center.Y - Size.Y * 0.5, Center.Z),
                            new Vec3d(Center.X + Size.X * 0.5, Center.Y + Size.Y * 0.5, Center.Z + Size.Z));
                }
            }
        }

        public BlankDefinition Clone()
        {
            var c = (BlankDefinition)MemberwiseClone();
            c.CustomMesh = null;
            return c;
        }

        /// <summary>生成毛坯线框/实体网格（用于显示，不参与材料去除）。</summary>
        public MeshData BuildDisplayMesh()
        {
            switch (Shape)
            {
                case BlankShape.Cylinder:
                    return MeshPrimitives.Cylinder(new Vec3d(Center.X, Center.Y, Center.Z), Size.X * 0.5, Size.Z, RadialSegments);
                case BlankShape.Custom:
                    return CustomMesh ?? new MeshData();
                default:
                    var b = Bounds;
                    return MeshPrimitives.Box(b.Min, b.Max);
            }
        }

        public bool Contains(Vec3d machinePoint, double margin = 0)
        {
            if (Shape == BlankShape.Cylinder)
            {
                var b = Bounds.Expanded(margin);
                if (machinePoint.Z < b.Min.Z || machinePoint.Z > b.Max.Z) return false;
                double dx = machinePoint.X - Center.X, dy = machinePoint.Y - Center.Y;
                return Math.Sqrt(dx * dx + dy * dy) <= Size.X * 0.5 + margin;
            }
            return Bounds.Expanded(margin).Contains(machinePoint);
        }
    }

    /// <summary>
    /// 毛坯材料：密度、硬度、可切削性，用于时间估算与参数推荐。
    /// </summary>
    [Serializable]
    public class MaterialDefinition
    {
        public string Id = "aluminum";
        /// <summary>多语言名称键或直接名称。</summary>
        public string NameKey = "material.aluminum";
        /// <summary>密度 g/cm^3。</summary>
        public double Density = 2.7;
        /// <summary>表面切削速度基准 m/min（硬质合金）。</summary>
        public double BaseSurfaceSpeed = 300;
        /// <summary>每齿进给基准 mm/tooth（直径 6mm 硬质合金）。</summary>
        public double BaseFeedPerTooth = 0.05;
        /// <summary>相对切削难度系数（1=易，越大越难）。</summary>
        public double MachinabilityFactor = 1.0;
        /// <summary>显示颜色（十六进制）。</summary>
        public string ColorHex = "#B0B0B0";

        public MaterialDefinition Clone() => (MaterialDefinition)MemberwiseClone();

        public static readonly MaterialDefinition[] Builtin =
        {
            new MaterialDefinition { Id = "aluminum", NameKey = "material.aluminum", Density = 2.70, BaseSurfaceSpeed = 300, BaseFeedPerTooth = 0.06, MachinabilityFactor = 1.0, ColorHex = "#C8C8CE" },
            new MaterialDefinition { Id = "steel_1045", NameKey = "material.steel_1045", Density = 7.85, BaseSurfaceSpeed = 120, BaseFeedPerTooth = 0.04, MachinabilityFactor = 2.0, ColorHex = "#8A8F98" },
            new MaterialDefinition { Id = "stainless_304", NameKey = "material.stainless_304", Density = 8.00, BaseSurfaceSpeed = 90, BaseFeedPerTooth = 0.03, MachinabilityFactor = 3.0, ColorHex = "#9AA0A6" },
            new MaterialDefinition { Id = "brass", NameKey = "material.brass", Density = 8.50, BaseSurfaceSpeed = 250, BaseFeedPerTooth = 0.05, MachinabilityFactor = 1.2, ColorHex = "#C9A227" },
            new MaterialDefinition { Id = "copper", NameKey = "material.copper", Density = 8.96, BaseSurfaceSpeed = 200, BaseFeedPerTooth = 0.05, MachinabilityFactor = 1.5, ColorHex = "#B87333" },
            new MaterialDefinition { Id = "cast_iron", NameKey = "material.cast_iron", Density = 7.20, BaseSurfaceSpeed = 150, BaseFeedPerTooth = 0.05, MachinabilityFactor = 1.6, ColorHex = "#5C5C5C" },
            new MaterialDefinition { Id = "titanium", NameKey = "material.titanium", Density = 4.51, BaseSurfaceSpeed = 50, BaseFeedPerTooth = 0.02, MachinabilityFactor = 5.0, ColorHex = "#A8A29A" },
            new MaterialDefinition { Id = "plastic_abs", NameKey = "material.plastic_abs", Density = 1.04, BaseSurfaceSpeed = 500, BaseFeedPerTooth = 0.10, MachinabilityFactor = 0.6, ColorHex = "#2E2E2E" },
            new MaterialDefinition { Id = "wood", NameKey = "material.wood", Density = 0.60, BaseSurfaceSpeed = 600, BaseFeedPerTooth = 0.15, MachinabilityFactor = 0.5, ColorHex = "#B5651D" },
        };

        public static MaterialDefinition Find(string id)
        {
            foreach (var m in Builtin)
                if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
            return Builtin[0];
        }
    }

    /// <summary>夹具（简化模型）。</summary>
    [Serializable]
    public class FixtureDefinition
    {
        public bool Enabled;
        public string Name = "Vise";
        /// <summary>包围盒（机床坐标）。</summary>
        public Aabb Bounds = new Aabb(new Vec3d(-80, -90, -60), new Vec3d(80, 90, 0));
        public string ColorHex = "#3C6E9F";

        /// <summary>简化为钳口 + 底座的夹具模型。</summary>
        public MeshData BuildMesh()
        {
            var m = new MeshData();
            if (!Enabled) return m;
            var b = Bounds;
            var color = ColorRgba.FromHex(ColorHex);
            AddColoredBox(m, b.Min, b.Max, color);
            // 两个钳口
            var jawColor = new ColorRgba(color.R * 0.7f, color.G * 0.7f, color.B * 0.7f);
            double jaw = Math.Min(20, b.Size.Y * 0.2);
            AddColoredBox(m, new Vec3d(b.Min.X, b.Min.Y, b.Max.Z), new Vec3d(b.Max.X, b.Min.Y + jaw, b.Max.Z + 25), jawColor);
            AddColoredBox(m, new Vec3d(b.Min.X, b.Max.Y - jaw, b.Max.Z), new Vec3d(b.Max.X, b.Max.Y, b.Max.Z + 25), jawColor);
            return m;
        }

        private static void AddColoredBox(MeshData m, Vec3d min, Vec3d max, ColorRgba c)
        {
            int start = m.VertexCount;
            MeshPrimitives.AddBox(m, min, max);
            for (int i = start; i < m.VertexCount; i++) m.Colors.Add(c);
        }

        public FixtureDefinition Clone() => (FixtureDefinition)MemberwiseClone();
    }

    /// <summary>
    /// 对刀 / 原点设置：把工件坐标系原点的机床坐标写入机床配置的对应槽位。
    /// </summary>
    public static class WorkpieceSetup
    {
        /// <summary>用探针/对刀块测量值设置 G54 原点。</summary>
        public static void SetOrigin(MachineProfileAccessor machine, int workOffsetIndex, Vec3d machineOrigin)
        {
            machine.SetWorkOffset(workOffsetIndex, machineOrigin);
        }

        /// <summary>
        /// 通过“碰边”计算原点：给定 X/Y/Z 三个接触点的机床坐标和探针半径，计算原点。
        /// </summary>
        public static Vec3d FromTouchPoints(Vec3d xTouch, Vec3d yTouch, Vec3d zTouch, double probeRadius, Vec3d nominalOrigin)
        {
            return new Vec3d(
                xTouch.X - probeRadius,
                yTouch.Y - probeRadius,
                zTouch.Z - probeRadius + nominalOrigin.Z);
        }
    }

    /// <summary>为 Core 层提供对机床工件坐标系的可写抽象。</summary>
    public interface MachineProfileAccessor
    {
        void SetWorkOffset(int index, Vec3d value);
        Vec3d GetWorkOffsetValue(int index);
    }
}
