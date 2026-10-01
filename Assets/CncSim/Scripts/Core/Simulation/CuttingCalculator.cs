using System;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Core.Simulation
{
    /// <summary>一组推荐切削参数。</summary>
    public struct CuttingParameters
    {
        /// <summary>主轴转速 rpm。</summary>
        public double SpindleSpeed;
        /// <summary>进给率 mm/min。</summary>
        public double FeedRate;
        /// <summary>切削速度 m/min。</summary>
        public double SurfaceSpeed;
        /// <summary>每齿进给 mm/tooth。</summary>
        public double FeedPerTooth;
        /// <summary>每转进给 mm/rev。</summary>
        public double FeedPerRev;
        /// <summary>轴向切深 mm。</summary>
        public double AxialDepth;
        /// <summary>径向切宽 mm。</summary>
        public double RadialWidth;
        /// <summary>材料去除率 mm^3/min。</summary>
        public double MaterialRemovalRate;
        /// <summary>估算切削功率 kW。</summary>
        public double PowerKw;
        /// <summary>建议说明/警告。</summary>
        public string Note;
    }

    /// <summary>
    /// 切削参数计算与推荐（功能 64、65、66、67）。
    /// </summary>
    public static class CuttingCalculator
    {
        /// <summary>切削速度 Vc = π * D * n / 1000（m/min）。</summary>
        public static double SurfaceSpeed(double diameterMm, double rpm) =>
            Math.PI * diameterMm * rpm / 1000.0;

        /// <summary>由切削速度反算转速 n = 1000 * Vc / (π * D)。</summary>
        public static double RpmFromSurfaceSpeed(double surfaceSpeedMPerMin, double diameterMm)
        {
            if (diameterMm <= 0) return 0;
            return 1000.0 * surfaceSpeedMPerMin / (Math.PI * diameterMm);
        }

        /// <summary>进给率 F = fz * z * n（mm/min）。</summary>
        public static double FeedRatePerTooth(double feedPerTooth, int flutes, double rpm) =>
            feedPerTooth * flutes * rpm;

        /// <summary>进给率 F = f * n（mm/min，每转进给）。</summary>
        public static double FeedRatePerRev(double feedPerRev, double rpm) => feedPerRev * rpm;

        /// <summary>每齿进给 fz = F / (z * n)。</summary>
        public static double FeedPerTooth(double feedRate, int flutes, double rpm) =>
            flutes > 0 && rpm > 0 ? feedRate / (flutes * rpm) : 0;

        /// <summary>每转进给 f = F / n。</summary>
        public static double FeedPerRev(double feedRate, double rpm) => rpm > 0 ? feedRate / rpm : 0;

        /// <summary>材料去除率 Q = ae * ap * F（mm^3/min）。</summary>
        public static double MaterialRemovalRate(double radialWidth, double axialDepth, double feedRate) =>
            radialWidth * axialDepth * feedRate;

        /// <summary>估算切削功率（kW）：Q * 单位切削能系数 / 60000。</summary>
        public static double EstimatePower(double mrr, MaterialDefinition material)
        {
            // 单位切削能（J/mm^3）与材料难加工性相关，铝合金约 0.7，钢约 2.5
            double specificEnergy = 0.7 * material.MachinabilityFactor;
            return mrr * specificEnergy / 60000.0;
        }

        /// <summary>
        /// 基于材料与刀具给出推荐参数（功能 67）。
        /// </summary>
        public static CuttingParameters Recommend(ToolDefinition tool, MaterialDefinition material, double toolDiameterFraction = 0.5)
        {
            tool.Normalize();
            var p = new CuttingParameters();

            double diameter = tool.Diameter;
            double vc = material.BaseSurfaceSpeed;
            // 球头刀/小直径适当降低线速度
            if (tool.Type == ToolType.BallEndMill) vc *= 0.85;
            if (diameter < 3) vc *= 0.8;

            p.SurfaceSpeed = vc;
            p.SpindleSpeed = Math.Max(0, RpmFromSurfaceSpeed(vc, diameter));

            double fz = material.BaseFeedPerTooth;
            // 按刀具直径缩放每齿进给（经验：随直径增大而增大）
            fz *= Math.Max(0.4, Math.Min(2.0, diameter / 6.0));
            if (tool.Type == ToolType.Drill || tool.Type == ToolType.Tap) fz *= 0.6;
            p.FeedPerTooth = fz;

            p.FeedRate = FeedRatePerTooth(fz, tool.Flutes, p.SpindleSpeed);
            p.FeedPerRev = FeedPerRev(p.FeedRate, p.SpindleSpeed);

            p.AxialDepth = diameter * Math.Max(0.1, Math.Min(0.75, toolDiameterFraction));
            p.RadialWidth = diameter * 0.4;
            p.MaterialRemovalRate = MaterialRemovalRate(p.RadialWidth, p.AxialDepth, p.FeedRate);
            p.PowerKw = EstimatePower(p.MaterialRemovalRate, material);

            if (p.SpindleSpeed > 20000)
            {
                p.Note = "rpm.high";
                p.SpindleSpeed = 20000;
                p.FeedRate = FeedRatePerTooth(p.FeedPerTooth, tool.Flutes, p.SpindleSpeed);
            }
            return p;
        }

        /// <summary>根据一次实测/设定值推算其余参数。</summary>
        public static CuttingParameters FromKnown(ToolDefinition tool, MaterialDefinition material,
            double? rpm = null, double? feedRate = null, double? feedPerTooth = null, double? surfaceSpeed = null)
        {
            tool.Normalize();
            var p = new CuttingParameters();
            double n = rpm ?? (surfaceSpeed.HasValue ? RpmFromSurfaceSpeed(surfaceSpeed.Value, tool.Diameter) : 0);
            if (n <= 0 && feedPerTooth.HasValue) n = material.BaseSurfaceSpeed > 0 ? RpmFromSurfaceSpeed(material.BaseSurfaceSpeed, tool.Diameter) : 1000;
            if (n <= 0) n = 1000;
            p.SpindleSpeed = n;
            p.SurfaceSpeed = SurfaceSpeed(tool.Diameter, n);
            p.FeedPerTooth = feedPerTooth ?? FeedPerTooth(feedRate ?? FeedRatePerTooth(material.BaseFeedPerTooth, tool.Flutes, n), tool.Flutes, n);
            p.FeedRate = feedRate ?? FeedRatePerTooth(p.FeedPerTooth, tool.Flutes, n);
            p.FeedPerRev = FeedPerRev(p.FeedRate, n);
            p.AxialDepth = tool.Diameter * 0.5;
            p.RadialWidth = tool.Diameter * 0.4;
            p.MaterialRemovalRate = MaterialRemovalRate(p.RadialWidth, p.AxialDepth, p.FeedRate);
            p.PowerKw = EstimatePower(p.MaterialRemovalRate, material);
            return p;
        }

        /// <summary>对已解析程序统计实际使用的切削速度、进给等区间。</summary>
        public static CuttingParameters AnalyzeProgram(ParseResult result, MachineProfile machine, MaterialDefinition material)
        {
            double feedSum = 0, rpmSum = 0;
            int feedCount = 0, rpmCount = 0;
            double feedMin = double.MaxValue, feedMax = double.MinValue;
            foreach (var seg in result.Segments)
            {
                if (seg.IsCutting && seg.Feed > 0)
                {
                    feedSum += seg.Feed;
                    feedCount++;
                    feedMin = Math.Min(feedMin, seg.Feed);
                    feedMax = Math.Max(feedMax, seg.Feed);
                }
                if (seg.Spindle != SpindleState.Off && seg.SpindleSpeed > 0)
                {
                    rpmSum += seg.SpindleSpeed;
                    rpmCount++;
                }
            }
            var p = new CuttingParameters();
            p.FeedRate = feedCount > 0 ? feedSum / feedCount : 0;
            p.SpindleSpeed = rpmCount > 0 ? rpmSum / rpmCount : 0;
            p.SurfaceSpeed = 0;
            return p;
        }
    }
}
