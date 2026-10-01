using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Core.Validation
{
    /// <summary>
    /// 校验器：语法检查（解析时已完成）+ 加工范围/超程、切削条件、毛坯范围、刀具-圆弧、
    /// 过切/欠切、快移穿料等检查（功能 15、16、59、60、61 等）。
    /// </summary>
    public class GCodeValidator
    {
        private readonly MachineProfile _machine;
        private readonly ToolLibrary _tools;
        private readonly BlankDefinition _blank;

        public GCodeValidator(MachineProfile machine, ToolLibrary tools = null, BlankDefinition blank = null)
        {
            _machine = machine ?? MachineProfile.Create3Axis();
            _tools = tools;
            _blank = blank;
        }

        /// <summary>在解析结果上追加校验诊断。返回是否存在 Error。</summary>
        public bool Validate(ParseResult result)
        {
            ValidateMotionSegments(result);
            return result.Diagnostics.HasErrors;
        }

        private void ValidateMotionSegments(ParseResult result)
        {
            ModalState prevModal = null;
            foreach (var seg in result.Segments)
            {
                // 1) 软限位
                for (int axis = 0; axis < 6; axis++)
                {
                    var ax = _machine.Axes[axis];
                    if (!ax.Enabled) continue;
                    var diag = _machine.CheckSoftLimit(axis, seg.End[axis], seg.LineIndex);
                    if (diag != null) result.Diagnostics.Add(diag);
                }

                if (seg.IsMotion)
                {
                    var bend = seg.End.Linear;
                    result.AllBounds.Encapsulate(seg.Start.Linear);
                    result.AllBounds.Encapsulate(bend);
                    if (seg.IsCutting)
                    {
                        result.CuttingBounds.Encapsulate(seg.Start.Linear);
                        result.CuttingBounds.Encapsulate(bend);
                        result.TotalCuttingLength += seg.Length;
                    }
                    else
                    {
                        result.TotalRapidLength += seg.Length;
                    }

                    // 2) 切削条件：进给与主轴
                    if (seg.IsCutting)
                    {
                        if (seg.Feed <= 0)
                            result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.feed_zero");
                        if (seg.Spindle == SpindleState.Off)
                            result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.spindle_not_started");
                        else if (seg.SpindleSpeed <= 0)
                            result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.spindle_zero");
                    }

                    // 3) 刀具-圆弧半径
                    if (seg.IsArc && _tools != null && seg.ToolNumber > 0)
                    {
                        var tool = _tools.Get(seg.ToolNumber);
                        if (tool != null && tool.Radius * 2 > seg.Radius * 2 + 1e-6 && seg.Radius > 1e-6)
                            result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.diameter_exceeds_radius", tool.Diameter, seg.Radius * 2);
                    }

                    // 4) 快移穿料
                    if (seg.Type == MotionType.Rapid && _blank != null && _blank.Shape == BlankShape.Box)
                    {
                        if (RapidPassesThrough(seg, _blank))
                            result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.rapid_through_material");
                    }

                    // 5) 毛坯范围（切削空切）
                    if (seg.IsCutting && _blank != null && _blank.Shape != BlankShape.Custom)
                    {
                        var b = _blank.Bounds.Expanded(0.5);
                        if (!b.Contains(seg.Start.Linear) && !b.Contains(seg.End.Linear) &&
                            !SegmentIntersectsBox(seg, b))
                            result.Diagnostics.Info(seg.LineIndex, 0, 0, "diag.outside_blank");
                    }
                }

                // 6) RTCP 五轴
                if (_machine.Kinematics != FiveAxisKinematics.None && seg.IsMotion)
                {
                    bool hasRotary = Math.Abs(seg.End.A - seg.Start.A) > 1e-6 ||
                                     Math.Abs(seg.End.B - seg.Start.B) > 1e-6 ||
                                     Math.Abs(seg.End.C - seg.Start.C) > 1e-6;
                    if (hasRotary && !seg.Rtcp)
                        result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.rtcp_missing");
                    if (hasRotary) result.UsesRotaryAxes = true;
                }
                prevModal = null;
            }
        }

        private static bool RapidPassesThrough(MotionSegment seg, BlankDefinition blank)
        {
            var b = blank.Bounds;
            double startInside = b.SignedDistance(seg.Start.Linear);
            double endInside = b.SignedDistance(seg.End.Linear);
            if (startInside <= 0 && endInside <= 0) return true; // 全部在毛坯内部
            return SegmentIntersectsBox(seg, b) && !(startInside > 0 && endInside > 0);
        }

        /// <summary>线段与包围盒是否相交（采样法，足够用于诊断）。</summary>
        private static bool SegmentIntersectsBox(MotionSegment seg, Aabb box)
        {
            const int samples = 24;
            for (int i = 0; i <= samples; i++)
            {
                var p = seg.Evaluate((double)i / samples).Linear;
                if (box.Contains(p)) return true;
            }
            return false;
        }

        /// <summary>
        /// 过切检查（功能 59）：程序切削位置低于毛坯允许的最终形状或超出设计轮廓。
        /// 简化策略：若切削点低于毛坯底面则判过切；由仿真层提供实际去除量时可做更精确判定。
        /// </summary>
        public static void CheckOvercut(ParseResult result, BlankDefinition blank)
        {
            if (blank == null) return;
            double bottom = blank.Bounds.Min.Z;
            var reported = new HashSet<int>();
            foreach (var seg in result.Segments)
            {
                if (!seg.IsCutting) continue;
                double deepest = Math.Min(seg.Start.Linear.Z, seg.End.Linear.Z);
                if (deepest < bottom - 0.1 && reported.Add(seg.LineIndex))
                    result.Diagnostics.Warning(seg.LineIndex, 0, 0, "diag.overcut", bottom - deepest);
            }
        }

        /// <summary>
        /// 欠切检查（功能 60）：切削区域未覆盖设计的最终形状。
        /// 需要理想目标形状参与，这里仅依据切削包围盒与毛坯交集做初步提示。
        /// </summary>
        public static void CheckUndercut(ParseResult result, BlankDefinition blank, Aabb targetShape)
        {
            if (blank == null) return;
            if (!result.CuttingBounds.IsValid) return;
            if (!result.CuttingBounds.Intersects(targetShape))
                result.Diagnostics.Warning(0, 0, 0, "diag.outside_blank");
        }
    }
}
