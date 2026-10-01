using System;
using System.Collections.Generic;
using System.Globalization;

namespace CncSim.Core.Parsing
{
    public enum TokenType
    {
        Whitespace,
        /// <summary>程序段号 N10</summary>
        LineNumber,
        /// <summary>程序号 O1000 / O0001</summary>
        ProgramNumber,
        /// <summary>G 指令</summary>
        GCode,
        /// <summary>M 指令</summary>
        MCode,
        /// <summary>T 刀号</summary>
        ToolWord,
        /// <summary>F 进给</summary>
        FeedWord,
        /// <summary>S 转速</summary>
        SpeedWord,
        /// <summary>坐标字 X/Y/Z/A/B/C/U/V/W</summary>
        AxisWord,
        /// <summary>圆弧参数 I/J/K/R</summary>
        ArcWord,
        /// <summary>其它参数 P/Q/L/H/D/E</summary>
        ParameterWord,
        /// <summary>注释 ( ... ) 或 ; ...</summary>
        Comment,
        /// <summary>程序起止符 %</summary>
        Percent,
        /// <summary>跳段符 /</summary>
        BlockDelete,
        /// <summary>无法识别的字符</summary>
        Error
    }

    /// <summary>词法记号。Start/Length 为在行内的字符位置，供高亮与错误定位。</summary>
    public struct Token
    {
        public TokenType Type;
        public int Start;
        public int Length;
        public char Letter;
        public double Value;
        public string Text;
        /// <summary>数值部分原始字符串（如 "01"、"54.1"），用于区分 G54.1 等。</summary>
        public string NumberText;
        /// <summary>数值缺失或格式错误时的描述。</summary>
        public string Error;

        public override string ToString() => $"{Type}:{Text}";
    }

    /// <summary>
    /// G 代码行级词法分析器，兼容 ISO/Fanuc 风格：
    /// 字母 + 可选符号 + 数值（允许空格，如 "G 01"、"X -10.5"），括号注释、分号注释、% 与 / 跳段。
    /// </summary>
    public static class GCodeLexer
    {
        public static List<Token> Tokenize(string line)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(line)) return tokens;
            int i = 0;
            int n = line.Length;
            while (i < n)
            {
                char c = line[i];
                int start = i;
                if (char.IsWhiteSpace(c))
                {
                    while (i < n && char.IsWhiteSpace(line[i])) i++;
                    tokens.Add(Make(TokenType.Whitespace, line, start, i));
                    continue;
                }
                if (c == '(')
                {
                    int close = line.IndexOf(')', i + 1);
                    i = close < 0 ? n : close + 1;
                    var t = Make(TokenType.Comment, line, start, i);
                    if (close < 0) t.Error = "UnclosedComment";
                    tokens.Add(t);
                    continue;
                }
                if (c == ';')
                {
                    tokens.Add(Make(TokenType.Comment, line, start, n));
                    break;
                }
                if (c == '%')
                {
                    i++;
                    tokens.Add(Make(TokenType.Percent, line, start, i));
                    continue;
                }
                if (c == '/' && IsOnlyWhitespaceBefore(tokens))
                {
                    i++;
                    // 可选跳段级别数字 /1 ~ /9
                    if (i < n && char.IsDigit(line[i])) i++;
                    tokens.Add(Make(TokenType.BlockDelete, line, start, i));
                    continue;
                }
                if (char.IsLetter(c))
                {
                    char letter = char.ToUpperInvariant(c);
                    i++;
                    int j = i;
                    while (j < n && (line[j] == ' ' || line[j] == '\t')) j++;
                    int numStart = j;
                    if (j < n && (line[j] == '+' || line[j] == '-')) j++;
                    while (j < n && (char.IsDigit(line[j]) || line[j] == '.')) j++;
                    string num = line.Substring(numStart, j - numStart);
                    var tok = new Token { Letter = letter, Start = start };
                    if (num.Length == 0 || num == "+" || num == "-" || num == ".")
                    {
                        // 字母后无数值：可能是 Siemens/Heidenhain 关键字或非法字符，按错误处理并吞掉连续字母
                        int k = i;
                        while (k < n && char.IsLetterOrDigit(line[k])) k++;
                        tok.Type = TokenType.Error;
                        tok.Length = k - start;
                        tok.Text = line.Substring(start, tok.Length);
                        tok.Error = "MissingValue";
                        tokens.Add(tok);
                        i = k;
                        continue;
                    }
                    tok.NumberText = num;
                    tok.Length = j - start;
                    tok.Text = line.Substring(start, tok.Length);
                    if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out tok.Value) || CountChar(num, '.') > 1)
                    {
                        tok.Type = TokenType.Error;
                        tok.Error = "InvalidNumber";
                    }
                    else
                    {
                        tok.Type = Classify(letter);
                        if (tok.Type == TokenType.Error) tok.Error = "UnknownAddress";
                    }
                    tokens.Add(tok);
                    i = j;
                    continue;
                }
                // 其它非法字符
                i++;
                var err = Make(TokenType.Error, line, start, i);
                err.Error = "UnexpectedCharacter";
                tokens.Add(err);
            }
            return tokens;
        }

        public static TokenType Classify(char letter)
        {
            switch (letter)
            {
                case 'N': return TokenType.LineNumber;
                case 'O': return TokenType.ProgramNumber;
                case 'G': return TokenType.GCode;
                case 'M': return TokenType.MCode;
                case 'T': return TokenType.ToolWord;
                case 'F': return TokenType.FeedWord;
                case 'S': return TokenType.SpeedWord;
                case 'X':
                case 'Y':
                case 'Z':
                case 'A':
                case 'B':
                case 'C':
                case 'U':
                case 'V':
                case 'W':
                    return TokenType.AxisWord;
                case 'I':
                case 'J':
                case 'K':
                case 'R':
                    return TokenType.ArcWord;
                case 'P':
                case 'Q':
                case 'L':
                case 'H':
                case 'D':
                case 'E':
                    return TokenType.ParameterWord;
                default:
                    return TokenType.Error;
            }
        }

        private static bool IsOnlyWhitespaceBefore(List<Token> tokens)
        {
            foreach (var t in tokens)
                if (t.Type != TokenType.Whitespace && t.Type != TokenType.LineNumber)
                    return false;
            return true;
        }

        private static int CountChar(string s, char ch)
        {
            int c = 0;
            foreach (char x in s) if (x == ch) c++;
            return c;
        }

        private static Token Make(TokenType type, string line, int start, int end) =>
            new Token { Type = type, Start = start, Length = end - start, Text = line.Substring(start, end - start) };
    }
}
