using System;
using System.Collections.Generic;
using System.Text;

namespace CncSim.Core.CodeEditor
{
    /// <summary>
    /// G 代码文本文档模型：按行存储、带撤销/重做、修改标记与变更事件。
    /// UI 层（InputField/TMP_InputField/自定义编辑控件）只需调用 SetText / ReplaceLine 等方法，
    /// 并订阅 Changed 事件刷新高亮和行号。
    /// </summary>
    public class GCodeDocument
    {
        private readonly List<string> _lines = new List<string> { string.Empty };
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();
        private const int MaxUndoDepth = 200;

        /// <summary>文档文件路径；为 null 表示未保存的新文档。</summary>
        public string FilePath { get; internal set; }

        public string DisplayName => string.IsNullOrEmpty(FilePath) ? "Untitled.nc" : System.IO.Path.GetFileName(FilePath);

        public bool IsDirty { get; private set; }

        /// <summary>文本内容的版本号，每次修改 +1，便于解析缓存判断是否过期。</summary>
        public int Version { get; private set; }

        public DateTime LastModifiedUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>文本变化（参数为版本号）。</summary>
        public event Action<int> Changed;

        /// <summary>脏标记变化。</summary>
        public event Action<bool> DirtyChanged;

        public int LineCount => _lines.Count;

        public IReadOnlyList<string> Lines => _lines;

        public string GetLine(int index) => index >= 0 && index < _lines.Count ? _lines[index] : string.Empty;

        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < _lines.Count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(_lines[i]);
                }
                return sb.ToString();
            }
        }

        /// <summary>整体设置文本（用户编辑后调用）。recordUndo=false 用于加载文件。</summary>
        public void SetText(string text, bool recordUndo = true, bool markDirty = true)
        {
            text = text ?? string.Empty;
            string current = Text;
            if (current == text) return;
            if (recordUndo) PushUndo(current);
            LoadLines(text);
            OnChanged(markDirty);
        }

        public void ReplaceLine(int index, string content)
        {
            if (index < 0 || index >= _lines.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_lines[index] == content) return;
            PushUndo(Text);
            _lines[index] = content ?? string.Empty;
            OnChanged(true);
        }

        public void InsertLine(int index, string content)
        {
            index = Math.Max(0, Math.Min(index, _lines.Count));
            PushUndo(Text);
            _lines.Insert(index, content ?? string.Empty);
            OnChanged(true);
        }

        public void RemoveLine(int index)
        {
            if (index < 0 || index >= _lines.Count) return;
            PushUndo(Text);
            _lines.RemoveAt(index);
            if (_lines.Count == 0) _lines.Add(string.Empty);
            OnChanged(true);
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Undo()
        {
            if (!CanUndo) return;
            _redo.Push(Text);
            LoadLines(_undo.Pop());
            OnChanged(true);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            _undo.Push(Text);
            LoadLines(_redo.Pop());
            OnChanged(true);
        }

        /// <summary>将字符偏移转换为 (行, 列)，用于光标定位与错误跳转。</summary>
        public (int line, int column) OffsetToPosition(int offset)
        {
            int remaining = Math.Max(0, offset);
            for (int i = 0; i < _lines.Count; i++)
            {
                int len = _lines[i].Length;
                if (remaining <= len) return (i, remaining);
                remaining -= len + 1;
            }
            int last = _lines.Count - 1;
            return (last, _lines[last].Length);
        }

        public int PositionToOffset(int line, int column)
        {
            line = Math.Max(0, Math.Min(line, _lines.Count - 1));
            int offset = 0;
            for (int i = 0; i < line; i++) offset += _lines[i].Length + 1;
            return offset + Math.Max(0, Math.Min(column, _lines[line].Length));
        }

        /// <summary>查找文本，返回 (行, 列) 列表。</summary>
        public List<(int line, int column)> FindAll(string pattern, bool ignoreCase = true)
        {
            var result = new List<(int, int)>();
            if (string.IsNullOrEmpty(pattern)) return result;
            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (int i = 0; i < _lines.Count; i++)
            {
                int idx = 0;
                while ((idx = _lines[i].IndexOf(pattern, idx, cmp)) >= 0)
                {
                    result.Add((i, idx));
                    idx += pattern.Length;
                }
            }
            return result;
        }

        public int ReplaceAll(string pattern, string replacement, bool ignoreCase = true)
        {
            if (string.IsNullOrEmpty(pattern)) return 0;
            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int count = 0;
            var newLines = new List<string>(_lines.Count);
            foreach (var line in _lines)
            {
                var sb = new StringBuilder();
                int pos = 0, idx;
                while ((idx = line.IndexOf(pattern, pos, cmp)) >= 0)
                {
                    sb.Append(line, pos, idx - pos).Append(replacement);
                    pos = idx + pattern.Length;
                    count++;
                }
                sb.Append(line, pos, line.Length - pos);
                newLines.Add(sb.ToString());
            }
            if (count > 0)
            {
                PushUndo(Text);
                _lines.Clear();
                _lines.AddRange(newLines);
                OnChanged(true);
            }
            return count;
        }

        /// <summary>标记为已保存。</summary>
        public void MarkSaved(string path)
        {
            FilePath = path;
            SetDirty(false);
        }

        internal void SetDirty(bool dirty)
        {
            if (IsDirty == dirty) return;
            IsDirty = dirty;
            DirtyChanged?.Invoke(dirty);
        }

        private void LoadLines(string text)
        {
            _lines.Clear();
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            _lines.AddRange(normalized.Split('\n'));
            if (_lines.Count == 0) _lines.Add(string.Empty);
        }

        private void PushUndo(string snapshot)
        {
            _undo.Push(snapshot);
            _redo.Clear();
            if (_undo.Count > MaxUndoDepth)
            {
                // Stack 无法删除底部元素，超限时保留最近的一半
                var keep = _undo.ToArray();
                _undo.Clear();
                for (int i = MaxUndoDepth / 2 - 1; i >= 0; i--) _undo.Push(keep[i]);
            }
        }

        private void OnChanged(bool markDirty)
        {
            Version++;
            LastModifiedUtc = DateTime.UtcNow;
            if (markDirty) SetDirty(true);
            Changed?.Invoke(Version);
        }
    }
}
