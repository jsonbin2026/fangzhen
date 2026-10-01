using System;
using System.Collections.Generic;
using System.Text;
using CncSim.Core.Parsing;

namespace CncSim.Core.CodeEditor
{
    /// <summary>语法高亮配色方案（十六进制颜色），可随主题切换。</summary>
    [Serializable]
    public class HighlightScheme
    {
        public string Default = "#D4D4D4";
        public string LineNumber = "#858585";
        public string ProgramNumber = "#C586C0";
        public string GCode = "#569CD6";
        public string GCodeRapid = "#F44747";
        public string MCode = "#C586C0";
        public string Tool = "#4EC9B0";
        public string Feed = "#DCDCAA";
        public string Speed = "#CE9178";
        public string Axis = "#9CDCFE";
        public string Arc = "#B5CEA8";
        public string Parameter = "#D7BA7D";
        public string Comment = "#6A9955";
        public string Percent = "#808080";
        public string BlockDelete = "#808080";
        public string Error = "#FF5555";
        /// <summary>当前执行行背景色（UI 使用）。</summary>
        public string CurrentLineBackground = "#264F78";
        /// <summary>错误行背景色（UI 使用）。</summary>
        public string ErrorLineBackground = "#5A1D1D";
        public string BreakpointColor = "#E51400";

        public static HighlightScheme Dark() => new HighlightScheme();

        public static HighlightScheme Light() => new HighlightScheme
        {
            Default = "#000000",
            LineNumber = "#237893",
            ProgramNumber = "#AF00DB",
            GCode = "#0000FF",
            GCodeRapid = "#CD3131",
            MCode = "#AF00DB",
            Tool = "#267F99",
            Feed = "#795E26",
            Speed = "#A31515",
            Axis = "#001080",
            Arc = "#098658",
            Parameter = "#795E26",
            Comment = "#008000",
            Percent = "#808080",
            BlockDelete = "#808080",
            Error = "#E51400",
            CurrentLineBackground = "#ADD6FF",
            ErrorLineBackground = "#FFD7D7",
            BreakpointColor = "#E51400"
        };

        public string ColorFor(Token t)
        {
            switch (t.Type)
            {
                case TokenType.LineNumber: return LineNumber;
                case TokenType.ProgramNumber: return ProgramNumber;
                case TokenType.GCode: return t.Value == 0 ? GCodeRapid : GCode;
                case TokenType.MCode: return MCode;
                case TokenType.ToolWord: return Tool;
                case TokenType.FeedWord: return Feed;
                case TokenType.SpeedWord: return Speed;
                case TokenType.AxisWord: return Axis;
                case TokenType.ArcWord: return Arc;
                case TokenType.ParameterWord: return Parameter;
                case TokenType.Comment: return Comment;
                case TokenType.Percent: return Percent;
                case TokenType.BlockDelete: return BlockDelete;
                case TokenType.Error: return Error;
                default: return Default;
            }
        }
    }

    /// <summary>单个高亮区段（行内字符范围 + 颜色），供自定义渲染（如逐字符着色）使用。</summary>
    public struct HighlightSpan
    {
        public int Start;
        public int Length;
        public string Color;
        public TokenType Type;
    }

    /// <summary>
    /// 语法高亮器。提供两种输出：
    /// 1) 富文本字符串（TextMeshPro / uGUI Text 的 &lt;color&gt; 标签），直接赋给 TMP_Text.text；
    /// 2) HighlightSpan 列表，供自绘编辑器使用。
    /// 内置按行缓存，仅对变化的行重新分词，适合大文件。
    /// </summary>
    public class SyntaxHighlighter
    {
        private readonly Dictionary<string, string> _richCache = new Dictionary<string, string>();
        private const int MaxCacheEntries = 20000;

        public HighlightScheme Scheme { get; private set; }

        public SyntaxHighlighter(HighlightScheme scheme = null)
        {
            Scheme = scheme ?? HighlightScheme.Dark();
        }

        public void SetScheme(HighlightScheme scheme)
        {
            Scheme = scheme ?? HighlightScheme.Dark();
            _richCache.Clear();
        }

        public List<HighlightSpan> GetSpans(string line)
        {
            var spans = new List<HighlightSpan>();
            foreach (var t in GCodeLexer.Tokenize(line))
            {
                if (t.Type == TokenType.Whitespace) continue;
                spans.Add(new HighlightSpan { Start = t.Start, Length = t.Length, Color = Scheme.ColorFor(t), Type = t.Type });
            }
            return spans;
        }

        /// <summary>单行富文本。</summary>
        public string HighlightLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            if (_richCache.TryGetValue(line, out var cached)) return cached;
            var sb = new StringBuilder(line.Length * 2);
            foreach (var t in GCodeLexer.Tokenize(line))
            {
                string escaped = EscapeRichText(t.Text);
                if (t.Type == TokenType.Whitespace)
                {
                    sb.Append(escaped);
                    continue;
                }
                sb.Append("<color=").Append(Scheme.ColorFor(t)).Append('>');
                if (t.Type == TokenType.Error) sb.Append("<u>").Append(escaped).Append("</u>");
                else sb.Append(escaped);
                sb.Append("</color>");
            }
            string result = sb.ToString();
            if (_richCache.Count > MaxCacheEntries) _richCache.Clear();
            _richCache[line] = result;
            return result;
        }

        /// <summary>整篇富文本（可指定行范围，用于虚拟滚动只渲染可见行）。</summary>
        public string HighlightDocument(IReadOnlyList<string> lines, int firstLine = 0, int count = int.MaxValue)
        {
            var sb = new StringBuilder();
            int end = (int)Math.Min((long)firstLine + count, lines.Count);
            for (int i = Math.Max(0, firstLine); i < end; i++)
            {
                if (i > firstLine) sb.Append('\n');
                sb.Append(HighlightLine(lines[i]));
            }
            return sb.ToString();
        }

        /// <summary>TextMeshPro 富文本需要转义尖括号，使用 &lt;noparse&gt; 之外最简单的零宽方式。</summary>
        public static string EscapeRichText(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            return s.Replace("<", "<\u200B");
        }
    }

    /// <summary>行号栏文本生成器。</summary>
    public static class LineNumberGutter
    {
        /// <summary>
        /// 生成行号文本（每行一个号码，右对齐）。可标记当前行、错误行、断点行。
        /// </summary>
        /// <param name="totalLines">文档总行数（决定宽度）</param>
        /// <param name="firstLine">起始行（0 基，用于虚拟滚动）</param>
        /// <param name="count">生成数量</param>
        /// <param name="currentLine">当前执行/光标行（0 基），-1 表示无</param>
        /// <param name="errorLines">错误行集合（0 基）</param>
        /// <param name="breakpoints">断点行集合（0 基）</param>
        /// <param name="scheme">配色方案，为 null 时输出纯文本</param>
        public static string Build(int totalLines, int firstLine, int count, int currentLine = -1,
            ICollection<int> errorLines = null, ICollection<int> breakpoints = null, HighlightScheme scheme = null)
        {
            int width = Math.Max(3, totalLines.ToString().Length);
            var sb = new StringBuilder();
            int end = (int)Math.Min((long)firstLine + count, totalLines);
            for (int i = Math.Max(0, firstLine); i < end; i++)
            {
                if (i > firstLine) sb.Append('\n');
                string num = (i + 1).ToString().PadLeft(width);
                bool bp = breakpoints != null && breakpoints.Contains(i);
                bool err = errorLines != null && errorLines.Contains(i);
                string marker = bp ? "●" : (err ? "!" : " ");
                if (scheme == null)
                {
                    sb.Append(marker).Append(num);
                    continue;
                }
                string markerColor = bp ? scheme.BreakpointColor : scheme.Error;
                sb.Append("<color=").Append(markerColor).Append('>').Append(marker).Append("</color>");
                if (i == currentLine) sb.Append("<b><color=").Append(scheme.Default).Append('>').Append(num).Append("</color></b>");
                else sb.Append("<color=").Append(scheme.LineNumber).Append('>').Append(num).Append("</color>");
            }
            return sb.ToString();
        }

        /// <summary>虚拟滚动：根据滚动偏移与行高计算可见行范围。</summary>
        public static (int first, int count) VisibleRange(double scrollOffsetPixels, double viewportHeight, double lineHeight, int totalLines)
        {
            if (lineHeight <= 0) return (0, totalLines);
            int first = Math.Max(0, (int)Math.Floor(scrollOffsetPixels / lineHeight));
            int count = (int)Math.Ceiling(viewportHeight / lineHeight) + 2;
            return (Math.Min(first, Math.Max(0, totalLines - 1)), count);
        }
    }
}
