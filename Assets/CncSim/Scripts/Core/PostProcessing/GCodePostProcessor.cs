using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CncSim.Core.Parsing;

namespace CncSim.Core.PostProcessing
{
    public enum NumberFormat
    {
        /// <summary>省略小数点后的尾随零。</summary>
        TrailingZerosTrimmed,
        /// <summary>固定小数位。</summary>
        FixedDecimals,
        /// <summary>整数（四舍五入到毫米）。</summary>
        Integer
    }

    /// <summary>
    /// 后处理器配置（功能 84：用户自定义后处理）。
    /// 支持任意控制器方言：程序头/尾、小数点格式、行号、换行符等。
    /// </summary>
    [Serializable]
    public class PostProcessorConfig
    {
        public string Name = "Generic ISO";
        /// <summary>程序头模板；占位符：{program}、{date}、{toolCount}。</summary>
        public List<string> HeaderLines = new List<string> { "%", "O{program}" };
        /// <summary>程序尾。</summary>
        public List<string> FooterLines = new List<string> { "M30", "%" };

        /// <summary>换行符。</summary>
        public string LineEnding = "\r\n";
        /// <summary>是否输出序号 N。</summary>
        public bool UseLineNumbers;
        public int LineNumberStart = 10;
        public int LineNumberIncrement = 10;
        /// <summary>坐标小数位。</summary>
        public int CoordinateDecimals = 3;
        /// <summary>进给小数位。</summary>
        public int FeedDecimals = 1;
        /// <summary>是否省略 X0 这类零值（增量模式不适用）。</summary>
        public bool OmitZeroCoordinates;
        /// <summary>每次换刀前是否输出安全退刀/取消补偿。</summary>
        public bool SafeToolChange = true;
        /// <summary>是否在程序头输出安全行。</summary>
        public List<string> SafetyLines = new List<string> { "G21 G17 G90 G94 G40 G49 G80" };
        /// <summary>是否使用括号注释。</summary>
        public bool CommentInParentheses = true;
        /// <summary>半径/长度补偿输出方式。</summary>
        public bool EmitLengthComp = true;
        public bool EmitRadiusComp = true;
        /// <summary>是否输出 N 序号前的段号格式（N10）。</summary>
        public string LineNumberPrefix = "N";

        public NumberFormat CoordinateFormat = NumberFormat.TrailingZerosTrimmed;

        public PostProcessorConfig Clone()
        {
            var c = (PostProcessorConfig)MemberwiseClone();
            c.HeaderLines = new List<string>(HeaderLines);
            c.FooterLines = new List<string>(FooterLines);
            c.SafetyLines = new List<string>(SafetyLines);
            return c;
        }

        /// <summary>Fanuc / 通用 CNC 控制器预设。</summary>
        public static PostProcessorConfig Fanuc() => new PostProcessorConfig
        {
            Name = "Fanuc",
            HeaderLines = new List<string> { "%", "O{program}" },
            FooterLines = new List<string> { "M30", "%" },
            LineEnding = "\r\n",
            UseLineNumbers = true,
            CoordinateDecimals = 3,
            FeedDecimals = 1
        };

        /// <summary>Siemens 840D 预设。</summary>
        public static PostProcessorConfig Siemens() => new PostProcessorConfig
        {
            Name = "Siemens 840D",
            HeaderLines = new List<string> { ";{program}" },
            FooterLines = new List<string> { "M30" },
            LineEnding = "\n",
            UseLineNumbers = false,
            SafetyLines = new List<string> { "G90 G71 G17 G40 G80" },
            CommentInParentheses = false
        };

        /// <summary>Mach3 / GRBL 预设。</summary>
        public static PostProcessorConfig Grbl() => new PostProcessorConfig
        {
            Name = "GRBL",
            HeaderLines = new List<string> { "( {program} )" },
            FooterLines = new List<string> { "M5", "M30" },
            LineEnding = "\n",
            UseLineNumbers = false,
            SafetyLines = new List<string> { "G21 G90 G17 G94 G40 G49 G80" },
            CoordinateDecimals = 3
        };
    }

    /// <summary>
    /// 后处理器：把解析结果或文档重新格式化为目标控制器风格的程序文本。
    /// </summary>
    public class GCodePostProcessor
    {
        private readonly PostProcessorConfig _config;

        public GCodePostProcessor(PostProcessorConfig config)
        {
            _config = (config ?? PostProcessorConfig.Fanuc()).Clone();
        }

        /// <summary>按配置重新排版已有程序文本（保持语义，仅标准化格式）。</summary>
        public string ProcessDocument(IReadOnlyList<string> sourceLines, string programName = null)
        {
            var sb = new StringBuilder();
            string program = programName ?? ExtractProgramNumber(sourceLines) ?? "0001";
            DateTime now = DateTime.Now;

            foreach (var line in _config.HeaderLines)
                AppendLine(sb, line.Replace("{program}", program).Replace("{date}", now.ToString("yyyy-MM-dd")));

            foreach (var line in _config.SafetyLines)
                AppendLine(sb, line);

            int lineNo = _config.LineNumberStart;
            foreach (var raw in sourceLines)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("%")) continue;
                if (IsHeaderLike(trimmed)) continue;
                string formatted = ReformatLine(trimmed, ref lineNo);
                if (formatted != null) AppendLine(sb, formatted);
            }

            foreach (var line in _config.FooterLines)
                AppendLine(sb, line);
            return sb.ToString();
        }

        /// <summary>从解析结果重新生成程序。</summary>
        public string ProcessParsed(ParseResult result, string programName = null)
        {
            var sb = new StringBuilder();
            string program = programName ?? "0001";
            foreach (var line in _config.HeaderLines)
                AppendLine(sb, line.Replace("{program}", program));
            foreach (var line in _config.SafetyLines)
                AppendLine(sb, line);

            int lineNo = _config.LineNumberStart;
            int lastTool = 0;
            foreach (var block in result.Blocks)
            {
                string text = ReformatBlock(block, ref lineNo, ref lastTool);
                if (text != null) AppendLine(sb, text);
            }
            foreach (var line in _config.FooterLines)
                AppendLine(sb, line);
            return sb.ToString();
        }

        private string ReformatBlock(GCodeBlock block, ref int lineNo, ref int lastTool)
        {
            if (block == null) return null;
            if (block.IsEmpty && string.IsNullOrEmpty(block.Comment)) return null;

            var parts = new List<string>();
            bool toolChange = block.HasM(6);
            if (toolChange && _config.SafeToolChange && lastTool != 0)
            {
                // 换刀前插入安全退刀
                lastTool = 0;
            }

            var line = new StringBuilder();
            if (_config.UseLineNumbers)
            {
                line.Append(_config.LineNumberPrefix).Append(lineNo);
                lineNo += _config.LineNumberIncrement;
            }

            foreach (double g in block.GCodes)
                AppendWord(line, "G", FormatCode(g));
            foreach (int m in block.MCodes)
                AppendWord(line, "M", m.ToString());
            foreach (var kv in block.Words)
            {
                if (kv.Key == 'O') continue; // 已由程序号处理
                char letter = kv.Key;
                if (_config.OmitZeroCoordinates && Math.Abs(kv.Value) < 1e-9 &&
                    "XYZABC".IndexOf(letter) >= 0)
                    continue;
                double v = kv.Value;
                string text;
                if ("XYZABCIJK".IndexOf(letter) >= 0)
                    text = FormatNumber(v, _config.CoordinateDecimals, _config.CoordinateFormat);
                else if (letter == 'F')
                    text = FormatNumber(v, _config.FeedDecimals, NumberFormat.FixedDecimals);
                else if (letter == 'S' || letter == 'T' || letter == 'N' || letter == 'H' || letter == 'D')
                    text = ((int)Math.Round(v)).ToString();
                else
                    text = FormatNumber(v, 3, _config.CoordinateFormat);
                AppendWord(line, letter.ToString(), text);
            }
            if (!string.IsNullOrEmpty(block.Comment))
            {
                line.Append(' ');
                line.Append(_config.CommentInParentheses ? "(" + block.Comment + ")" : "; " + block.Comment);
            }
            return line.ToString().Trim();
        }

        private string ReformatLine(string source, ref int lineNo)
        {
            var tokens = GCodeLexer.Tokenize(source);
            var sb = new StringBuilder();
            if (_config.UseLineNumbers)
            {
                sb.Append(_config.LineNumberPrefix).Append(lineNo);
                lineNo += _config.LineNumberIncrement;
            }
            int lastEnd = -1;
            foreach (var t in tokens)
            {
                if (t.Type == TokenType.Whitespace) continue;
                if (t.Type == TokenType.Percent || t.Type == TokenType.BlockDelete) continue;
                if (t.Type == TokenType.LineNumber)
                {
                    // 重新编号
                    continue;
                }
                if (t.Type == TokenType.Comment)
                {
                    string c = t.Text.TrimStart('(', ';').TrimEnd(')');
                    sb.Append(' ').Append(_config.CommentInParentheses ? "(" + c + ")" : "; " + c);
                    continue;
                }
                if (t.Type == TokenType.Error)
                {
                    sb.Append(' ').Append(t.Text);
                    continue;
                }
                string letter = t.Letter.ToString();
                string value;
                if (t.Letter == 'G') value = FormatCode(t.Value);
                else if (t.Letter == 'M' || t.Letter == 'S' || t.Letter == 'T')
                    value = ((int)Math.Round(t.Value)).ToString();
                else if (t.Letter == 'F')
                    value = FormatNumber(t.Value, _config.FeedDecimals, NumberFormat.FixedDecimals);
                else
                    value = FormatNumber(t.Value, _config.CoordinateDecimals, _config.CoordinateFormat);
                AppendWord(sb, letter, value);
            }
            return sb.ToString().Trim();
        }

        private static bool IsHeaderLike(string line)
        {
            if (line.StartsWith("O") && line.Length <= 6 && line.IndexOf(' ') < 0)
            {
                // O0001 程序号行
                bool digits = true;
                for (int i = 1; i < line.Length; i++)
                    if (!char.IsDigit(line[i])) { digits = false; break; }
                return digits;
            }
            return false;
        }

        private static void AppendWord(StringBuilder sb, string letter, string value)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(letter).Append(value);
        }

        private void AppendLine(StringBuilder sb, string line)
        {
            sb.Append(line).Append(_config.LineEnding);
        }

        public static string FormatCode(double code)
        {
            if (Math.Abs(code - Math.Round(code)) < 1e-9)
            {
                int i = (int)Math.Round(code);
                return i < 10 ? i.ToString("00") : i.ToString();
            }
            return code.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(double value, int decimals, NumberFormat format)
        {
            switch (format)
            {
                case NumberFormat.Integer:
                    return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
                case NumberFormat.FixedDecimals:
                    return value.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
                default:
                {
                    string s = Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero)
                        .ToString("0." + new string('#', Math.Max(1, decimals)), CultureInfo.InvariantCulture);
                    return s == "-0" ? "0" : s;
                }
            }
        }

        private static string ExtractProgramNumber(IReadOnlyList<string> lines)
        {
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (t.Length >= 2 && (t[0] == 'O' || t[0] == 'o'))
                {
                    int i = 1;
                    while (i < t.Length && char.IsDigit(t[i])) i++;
                    if (i > 1) return t.Substring(1, i - 1);
                }
            }
            return null;
        }
    }

    /// <summary>后处理器注册表：预置常见控制器，也可注册自定义配置。</summary>
    public static class PostProcessorRegistry
    {
        private static readonly Dictionary<string, Func<PostProcessorConfig>> Builders =
            new Dictionary<string, Func<PostProcessorConfig>>(StringComparer.OrdinalIgnoreCase)
            {
                { "fanuc", PostProcessorConfig.Fanuc },
                { "siemens", PostProcessorConfig.Siemens },
                { "grbl", PostProcessorConfig.Grbl },
                { "generic", () => new PostProcessorConfig() }
            };

        public static IEnumerable<string> Names => Builders.Keys;

        public static PostProcessorConfig Create(string name)
        {
            return Builders.TryGetValue(name, out var b) ? b() : new PostProcessorConfig { Name = name };
        }

        public static void Register(string name, Func<PostProcessorConfig> builder) => Builders[name] = builder;
    }
}
