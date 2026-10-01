using System;

namespace CncSim.Core
{
    /// <summary>轴对齐包围盒。</summary>
    [Serializable]
    public struct Aabb
    {
        public Vec3d Min;
        public Vec3d Max;

        public Aabb(Vec3d min, Vec3d max)
        {
            Min = min;
            Max = max;
        }

        public static Aabb Empty => new Aabb(
            new Vec3d(double.MaxValue, double.MaxValue, double.MaxValue),
            new Vec3d(double.MinValue, double.MinValue, double.MinValue));

        public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;
        public Vec3d Size => IsValid ? Max - Min : Vec3d.Zero;
        public Vec3d Center => (Min + Max) * 0.5;

        public void Encapsulate(Vec3d p)
        {
            Min = Vec3d.Min(Min, p);
            Max = Vec3d.Max(Max, p);
        }

        public void Encapsulate(Aabb other)
        {
            if (!other.IsValid) return;
            Encapsulate(other.Min);
            Encapsulate(other.Max);
        }

        public Aabb Expanded(double margin) =>
            new Aabb(Min - new Vec3d(margin, margin, margin), Max + new Vec3d(margin, margin, margin));

        public bool Contains(Vec3d p, double tol = 0) =>
            p.X >= Min.X - tol && p.X <= Max.X + tol &&
            p.Y >= Min.Y - tol && p.Y <= Max.Y + tol &&
            p.Z >= Min.Z - tol && p.Z <= Max.Z + tol;

        public bool Intersects(Aabb o) =>
            Min.X <= o.Max.X && Max.X >= o.Min.X &&
            Min.Y <= o.Max.Y && Max.Y >= o.Min.Y &&
            Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

        /// <summary>点到盒的有符号距离（内部为负）。</summary>
        public double SignedDistance(Vec3d p)
        {
            Vec3d c = Center;
            Vec3d h = Size * 0.5;
            Vec3d q = new Vec3d(Math.Abs(p.X - c.X) - h.X, Math.Abs(p.Y - c.Y) - h.Y, Math.Abs(p.Z - c.Z) - h.Z);
            Vec3d outside = Vec3d.Max(q, Vec3d.Zero);
            double inside = Math.Min(Math.Max(q.X, Math.Max(q.Y, q.Z)), 0);
            return outside.Length + inside;
        }

        public override string ToString() => $"[{Min} - {Max}]";
    }
}
