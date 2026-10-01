using System;

namespace CncSim.Core
{
    /// <summary>通用数学与几何工具。</summary>
    public static class MathUtil
    {
        public const double Epsilon = 1e-9;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static double Clamp01(double v) => Clamp(v, 0, 1);

        /// <summary>角度归一化到 [0, 2PI)。</summary>
        public static double NormalizeAngle(double rad)
        {
            double twoPi = Math.PI * 2;
            rad %= twoPi;
            if (rad < 0) rad += twoPi;
            return rad;
        }

        /// <summary>绕 X 轴旋转（弧度，右手法则）。</summary>
        public static Vec3d RotateX(Vec3d v, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Vec3d(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
        }

        public static Vec3d RotateY(Vec3d v, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Vec3d(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        public static Vec3d RotateZ(Vec3d v, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Vec3d(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        }

        /// <summary>绕指定主轴（0=X,1=Y,2=Z）旋转。</summary>
        public static Vec3d RotateAboutAxis(Vec3d v, int axis, double a)
        {
            switch (axis)
            {
                case 0: return RotateX(v, a);
                case 1: return RotateY(v, a);
                default: return RotateZ(v, a);
            }
        }

        /// <summary>绕任意轴（经过 pivot，方向 dir）旋转点。Rodrigues 公式。</summary>
        public static Vec3d RotateAround(Vec3d p, Vec3d pivot, Vec3d dir, double a)
        {
            Vec3d k = dir.Normalized;
            Vec3d v = p - pivot;
            double c = Math.Cos(a), s = Math.Sin(a);
            Vec3d r = v * c + Vec3d.Cross(k, v) * s + k * (Vec3d.Dot(k, v) * (1 - c));
            return r + pivot;
        }

        /// <summary>点到线段最近点参数 t∈[0,1]。</summary>
        public static double ClosestParamOnSegment(Vec3d p, Vec3d a, Vec3d b)
        {
            Vec3d ab = b - a;
            double len2 = ab.LengthSquared;
            if (len2 < Epsilon) return 0;
            return Clamp01(Vec3d.Dot(p - a, ab) / len2);
        }

        public static double DistancePointSegment(Vec3d p, Vec3d a, Vec3d b)
        {
            double t = ClosestParamOnSegment(p, a, b);
            return Vec3d.Distance(p, Vec3d.Lerp(a, b, t));
        }

        /// <summary>两线段之间最短距离。</summary>
        public static double DistanceSegmentSegment(Vec3d p1, Vec3d q1, Vec3d p2, Vec3d q2)
        {
            Vec3d d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = Vec3d.Dot(d1, d1), e = Vec3d.Dot(d2, d2), f = Vec3d.Dot(d2, r);
            double s, t;
            if (a <= Epsilon && e <= Epsilon) return Vec3d.Distance(p1, p2);
            if (a <= Epsilon)
            {
                s = 0;
                t = Clamp01(f / e);
            }
            else
            {
                double c = Vec3d.Dot(d1, r);
                if (e <= Epsilon)
                {
                    t = 0;
                    s = Clamp01(-c / a);
                }
                else
                {
                    double b = Vec3d.Dot(d1, d2);
                    double denom = a * e - b * b;
                    s = denom > Epsilon ? Clamp01((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0)
                    {
                        t = 0;
                        s = Clamp01(-c / a);
                    }
                    else if (t > 1)
                    {
                        t = 1;
                        s = Clamp01((b - c) / a);
                    }
                }
            }
            return Vec3d.Distance(p1 + d1 * s, p2 + d2 * t);
        }

        /// <summary>任意向量的一个单位正交向量。</summary>
        public static Vec3d AnyPerpendicular(Vec3d v)
        {
            Vec3d n = v.Normalized;
            Vec3d other = Math.Abs(n.Z) < 0.9 ? Vec3d.UnitZ : Vec3d.UnitX;
            return Vec3d.Cross(n, other).Normalized;
        }
    }
}
