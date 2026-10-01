using System;
using System.Collections.Generic;
using CncSim.Core.Parsing;
using CncSim.Core.MaterialRemoval;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Core.Collision
{
    /// <summary>碰撞类型。</summary>
    public enum CollisionType
    {
        ToolWorkpiece,
        ToolFixture,
        ToolMachine,
        HolderWorkpiece,
        HolderFixture,
        HolderMachine,
        RapidThroughMaterial
    }

    /// <summary>一条碰撞记录。</summary>
    public class CollisionEvent
    {
        public CollisionType Type;
        /// <summary>发生碰撞的运动段索引。</summary>
        public int SegmentIndex;
        /// <summary>来源程序行。</summary>
        public int LineIndex;
        /// <summary>段内发生位置参数 0-1。</summary>
        public double SegmentT;
        /// <summary>碰撞点（机床坐标）。</summary>
        public Vec3d Point;
        /// <summary>穿透深度 mm。</summary>
        public double Depth;
        public double Time;

        public string Description => Localization.Loc.Format(KeyFor(Type), LineIndex + 1, Depth);

        private static string KeyFor(CollisionType t) => "collision." + t.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// 碰撞检测（功能 56、57、58）。
    /// 以采样点 + 刀具包围盒/刀柄包围盒对 毛坯、夹具、机床限位做相交判定。
    /// 精度由采样步长控制；返回碰撞列表与最早发生时间。
    /// </summary>
    public class CollisionDetector
    {
        private readonly MachineProfile _machine;
        private readonly BlankDefinition _blank;
        private readonly FixtureDefinition _fixture;
        private readonly ToolLibrary _tools;
        private readonly MachineLimits _machineLimits;

        /// <summary>采样步长（mm）与角度步长（度）。</summary>
        public double SampleStep = 1.0;
        public double AngleStepDeg = 3.0;
        /// <summary>是否检测刀柄。</summary>
        public bool CheckHolder = true;

        public CollisionDetector(MachineProfile machine, BlankDefinition blank, FixtureDefinition fixture,
            ToolLibrary tools, MachineLimits machineLimits = null)
        {
            _machine = machine ?? MachineProfile.Create3Axis();
            _blank = blank;
            _fixture = fixture;
            _tools = tools;
            _machineLimits = machineLimits ?? new MachineLimits();
        }

        /// <summary>检测整段程序，返回碰撞列表（按时间升序）。</summary>
        public List<CollisionEvent> Detect(ParseResult result)
        {
            var events = new List<CollisionEvent>();
            foreach (var seg in result.Segments)
            {
                if (!seg.IsMotion) continue;
                DetectSegment(seg, events);
            }
            events.Sort((a, b) => a.Time.CompareTo(b.Time));
            return events;
        }

        private void DetectSegment(MotionSegment seg, List<CollisionEvent> events)
        {
            var tool = _tools?.Get(seg.ToolNumber);
            var section = tool != null ? ToolSection.From(tool) : default;
            double toolRadius = tool?.Radius ?? 3;
            double stickOut = tool?.StickOut ?? 60;

            var points = seg.Discretize(0.05, SampleStep, AngleStepDeg);
            double baseTime = seg.StartTime;
            double duration = Math.Max(1e-6, seg.Duration);

            for (int i = 0; i < points.Count; i++)
            {
                var axis = points[i];
                Vec3d tip = axis.Linear;
                double t = (double)i / Math.Max(1, points.Count - 1);
                double time = baseTime + t * duration;

                // 1) 刀具-工件
                if (_blank != null && _blank.Shape != BlankShape.Custom)
                {
                    if (ToolIntersectsBlank(tool, tip, toolRadius, out double depth))
                        Add(events, CollisionType.ToolWorkpiece, seg, t, tip, depth, time);
                }

                // 2) 刀具-夹具 / 刀柄-夹具
                if (_fixture != null && _fixture.Enabled)
                {
                    if (SphereBox(tip, toolRadius, _fixture.Bounds, out double fd))
                        Add(events, CollisionType.ToolFixture, seg, t, tip, fd, time);
                    if (CheckHolder)
                    {
                        Vec3d holderCenter = tip + Vec3d.UnitZ * (stickOut * 0.5);
                        if (SphereBox(holderCenter, stickOut * 0.5, _fixture.Bounds, out double hd))
                            Add(events, CollisionType.HolderFixture, seg, t, holderCenter, hd, time);
                    }
                }

                // 3) 刀具-机床（主轴鼻端/刀柄撞到工作台）
                if (CheckHolder && tool != null)
                {
                    Vec3d holderCenter = tip + Vec3d.UnitZ * (stickOut * 0.5);
                    if (SphereBox(holderCenter, stickOut * 0.5, _machineLimits.TableBox, out double md))
                        Add(events, CollisionType.HolderMachine, seg, t, holderCenter, md, time);
                }
            }

            // 4) 快移穿料（功能 56 的补充，整段判定）
            if (seg.Type == MotionType.Rapid && _blank != null && _blank.Shape != BlankShape.Custom)
            {
                if (RapidThroughBlank(seg, out double t, out Vec3d point, out double depth))
                    Add(events, CollisionType.RapidThroughMaterial, seg, t, point, depth, baseTime + t * duration);
            }
        }

        private bool ToolIntersectsBlank(ToolDefinition tool, Vec3d tip, double toolRadius, out double depth)
        {
            depth = 0;
            var b = _blank.Bounds;
            // 用刀具圆柱包围盒近似
            var toolBox = new Aabb(tip - new Vec3d(toolRadius, toolRadius, 0.5), tip + new Vec3d(toolRadius, toolRadius, Math.Max(toolRadius, 1)));
            if (!toolBox.Intersects(b)) return false;
            // 计算穿透深度：刀尖侵入毛坯的距离
            if (tip.Z < b.Max.Z && tip.Z > b.Min.Z &&
                tip.X > b.Min.X && tip.X < b.Max.X && tip.Y > b.Min.Y && tip.Y < b.Max.Y)
            {
                depth = Math.Min(b.Max.Z - tip.Z, toolRadius);
                return depth > 0.2;
            }
            return false;
        }

        private bool RapidThroughBlank(MotionSegment seg, out double t, out Vec3d point, out double depth)
        {
            t = 0;
            point = Vec3d.Zero;
            depth = 0;
            var b = _blank.Bounds;
            if (b.Contains(seg.Start.Linear) && b.Contains(seg.End.Linear))
            {
                t = 0.5;
                point = Vec3d.Lerp(seg.Start.Linear, seg.End.Linear, 0.5);
                depth = Math.Min(b.Max.Z - point.Z, 1);
                return true;
            }
            return false;
        }

        private void Add(List<CollisionEvent> events, CollisionType type, MotionSegment seg, double t, Vec3d point, double depth, double time)
        {
            // 去重：同一段同类型只保留最深的一次
            foreach (var e in events)
                if (e.SegmentIndex == seg.Index && e.Type == type)
                {
                    if (depth > e.Depth)
                    {
                        e.Depth = depth;
                        e.Point = point;
                        e.Time = time;
                        e.SegmentT = t;
                    }
                    return;
                }
            events.Add(new CollisionEvent
            {
                Type = type,
                SegmentIndex = seg.Index,
                LineIndex = seg.LineIndex,
                SegmentT = t,
                Point = point,
                Depth = depth,
                Time = time
            });
        }

        private static bool SphereBox(Vec3d center, double radius, Aabb box, out double depth)
        {
            depth = 0;
            double d = box.SignedDistance(center);
            if (d >= radius) return false;
            depth = radius - d;
            return depth > 1e-6;
        }
    }

    /// <summary>机床几何限位：工作台盒、行程等，用于刀具-机床碰撞检测。</summary>
    public class MachineLimits
    {
        public Aabb TableBox = Aabb.Empty;
        /// <summary>主轴鼻端相对刀尖的最小距离（刀柄不可低于此）。</summary>
        public double NoseClearance = 0;

        public static MachineLimits FromMachine(MachineProfile m)
        {
            // 桌面位于 Z 轴最低行程附近
            double zTop = m.Axes[2].Min;
            double zBottom = zTop - 40;
            var limits = new MachineLimits
            {
                TableBox = new Aabb(
                    new Vec3d(m.Axes[0].Min - 50, m.Axes[1].Min - 50, zBottom),
                    new Vec3d(m.Axes[0].Max + 50, m.Axes[1].Max + 50, zTop))
            };
            return limits;
        }
    }
}
