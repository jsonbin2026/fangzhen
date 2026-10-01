using System;
using System.Collections.Generic;

namespace CncSim.Core.Parsing
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>语法/语义/范围诊断信息。MessageKey 为 Loc 词条键，Args 为格式化参数。</summary>
    [Serializable]
    public class Diagnostic
    {
        public DiagnosticSeverity Severity;
        /// <summary>0 基行号。</summary>
        public int Line;
        public int Column;
        public int Length;
        public string MessageKey;
        public object[] Args;

        public Diagnostic(DiagnosticSeverity severity, int line, int column, int length, string key, params object[] args)
        {
            Severity = severity;
            Line = line;
            Column = column;
            Length = length;
            MessageKey = key;
            Args = args;
        }

        public string Message => Localization.Loc.Format(MessageKey, Args);

        public override string ToString() => $"[{Severity}] L{Line + 1}:{Column + 1} {Message}";
    }

    public class DiagnosticList : List<Diagnostic>
    {
        public int ErrorCount => FindAll(d => d.Severity == DiagnosticSeverity.Error).Count;
        public int WarningCount => FindAll(d => d.Severity == DiagnosticSeverity.Warning).Count;
        public bool HasErrors => Exists(d => d.Severity == DiagnosticSeverity.Error);

        public void Error(int line, int col, int len, string key, params object[] args) =>
            Add(new Diagnostic(DiagnosticSeverity.Error, line, col, len, key, args));

        public void Warning(int line, int col, int len, string key, params object[] args) =>
            Add(new Diagnostic(DiagnosticSeverity.Warning, line, col, len, key, args));

        public void Info(int line, int col, int len, string key, params object[] args) =>
            Add(new Diagnostic(DiagnosticSeverity.Info, line, col, len, key, args));

        /// <summary>出错行集合（用于行号栏标记）。</summary>
        public HashSet<int> LinesWithSeverity(DiagnosticSeverity min)
        {
            var set = new HashSet<int>();
            foreach (var d in this) if (d.Severity >= min) set.Add(d.Line);
            return set;
        }

        public List<Diagnostic> ForLine(int line) => FindAll(d => d.Line == line);
    }
}
