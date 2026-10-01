using System;

namespace CncSim.Core
{
    /// <summary>双精度三维向量（CNC 坐标系：右手系，Z 轴向上，单位 mm）。</summary>
    [Serializable]
    public struct Vec3d : IEquatable<Vec3d>
    {
        public double X;
        public double Y;
        public double Z;

        public Vec3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d One = new Vec3d(1, 1, 1);
        public static readonly Vec3d UnitX = new Vec3d(1, 0, 0);
        public static readonly Vec3d UnitY = new Vec3d(0, 1, 0);
        public static readonly Vec3d UnitZ = new Vec3d(0, 0, 1);

        public double this[int axis]
        {
            get { return axis == 0 ? X : axis == 1 ? Y : Z; }
            set
            {
                if (axis == 0) X = value;
                else if (axis == 1) Y = value;
                else Z = value;
            }
        }

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSquared => X * X + Y * Y + Z * Z;

        public Vec3d Normalized
        {
            get
            {
                double len = Length;
                return len > 1e-12 ? new Vec3d(X / len, Y / len, Z / len) : Zero;
            }
        }

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.X, -a.Y, -a.Z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.X * s, a.Y * s, a.Z * s);
        public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.X * s, a.Y * s, a.Z * s);
        public static Vec3d operator /(Vec3d a, double s) => new Vec3d(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3d Cross(Vec3d a, Vec3d b) =>
            new Vec3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static double Distance(Vec3d a, Vec3d b) => (a - b).Length;

        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) =>
            new Vec3d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        public static Vec3d Min(Vec3d a, Vec3d b) => new Vec3d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static Vec3d Max(Vec3d a, Vec3d b) => new Vec3d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        /// <summary>两向量夹角（弧度）。</summary>
        public static double Angle(Vec3d a, Vec3d b)
        {
            double denom = a.Length * b.Length;
            if (denom < 1e-12) return 0;
            double c = Dot(a, b) / denom;
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c)));
        }

        public bool ApproximatelyEquals(Vec3d other, double tolerance = 1e-6) =>
            Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance && Math.Abs(Z - other.Z) <= tolerance;

        public bool IsFinite => !(double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z) ||
                                  double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z));

        public bool Equals(Vec3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vec3d v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => string.Format("({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
    }

    /// <summary>单精度三维向量，用于网格数据输出（避免 Core 依赖 UnityEngine.Vector3）。</summary>
    [Serializable]
    public struct Float3
    {
        public float X;
        public float Y;
        public float Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Float3 From(Vec3d v) => new Float3((float)v.X, (float)v.Y, (float)v.Z);
        public Vec3d ToVec3d() => new Vec3d(X, Y, Z);
    }

    /// <summary>RGBA 颜色（0-1），供 Core 层输出顶点色、主题色使用。</summary>
    [Serializable]
    public struct ColorRgba
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public ColorRgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static ColorRgba FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            float Channel(int index) => Convert.ToInt32(hex.Substring(index, 2), 16) / 255f;
            float a = hex.Length >= 8 ? Channel(6) : 1f;
            return new ColorRgba(Channel(0), Channel(2), Channel(4), a);
        }

        public string ToHex() =>
            string.Format("#{0:X2}{1:X2}{2:X2}", (int)Math.Round(R * 255), (int)Math.Round(G * 255), (int)Math.Round(B * 255));

        public static ColorRgba Lerp(ColorRgba a, ColorRgba b, float t) =>
            new ColorRgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }
}
