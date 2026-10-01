using System;
using System.Collections.Generic;

namespace CncSim.Core.Parsing
{
    /// <summary>
    /// 固定循环参数与展开。支持 G81/G82/G83/G73/G84/G85/G86/G89，G98/G99 返回平面。
    /// </summary>
    internal class CannedCycleState
    {
        public int Code;
        public double R;
        public double Q;
        public double P;
        public double Z;
        public bool ReturnToInitial = true;

        public bool HasR;
        public bool HasQ;
        public bool HasP;

        public void Reset()
        {
            Code = 0;
            HasR = HasQ = HasP = false;
        }
    }

    /// <summary>
    /// 解析与运动规划选项。
    /// </summary>
    public class ParseOptions
    {
        /// <summary>G95 每转进给换算用的默认主轴转速（当 S 未给定时）。</summary>
        public double DefaultSpindleSpeed = 1000;
        /// <summary>G93 反比时间进给换算用的默认段长（mm）。</summary>
        public double DefaultInverseTimeLength = 10;
        /// <summary>是否校验毛坯范围（由 Validator 使用）。</summary>
        public bool ValidateBlank = true;
        /// <summary>是否检查刀具半径与圆弧半径关系。</summary>
        public bool ValidateToolAgainstArc = true;
    }
}
