using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CncSim.Core.CodeEditor
{
    /// <summary>
    /// 文件服务：新建/打开/保存/另存为、导入、导出。
    /// 文件对话框由 UI 层负责（如 StandaloneFileBrowser / NativeFilePicker），本类只处理路径。
    /// </summary>
    public class GCodeFileService
    {
        /// <summary>可导入的扩展名（小写，含点）。</summary>
        public static readonly string[] SupportedExtensions = { ".nc", ".gcode", ".txt", ".ngc", ".tap", ".cnc", ".gc", ".mpf", ".ptp", ".iso" };

        /// <summary>单文件大小上限，防止误打开超大文件卡死（默认 64MB）。</summary>
        public long MaxFileSizeBytes = 64L * 1024 * 1024;

        public RecentFilesList RecentFiles { get; }

        public GCodeFileService(RecentFilesList recentFiles)
        {
            RecentFiles = recentFiles ?? throw new ArgumentNullException(nameof(recentFiles));
        }

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        /// <summary>文件对话框过滤器字符串，如 "nc,gcode,txt"。</summary>
        public static string ExtensionFilter => string.Join(",", SupportedExtensions.Select(e => e.TrimStart('.')));

        public GCodeDocument New(string template = null)
        {
            var doc = new GCodeDocument();
            doc.SetText(template ?? DefaultTemplate, recordUndo: false, markDirty: false);
            return doc;
        }

        public const string DefaultTemplate =
            "%\nO0001 (NEW PROGRAM)\nG21 G17 G90 G94 G40 G49 G80\nG54\nT1 M06\nS3000 M03\nG00 X0 Y0 Z10\n\nM05\nG28 G91 Z0\nM30\n%";

        /// <summary>打开文件（自动识别 UTF-8/UTF-16 BOM，其它按 UTF-8 回退 Latin1）。</summary>
        public FileResult<GCodeDocument> Open(string path)
        {
            try
            {
                if (!File.Exists(path)) return FileResult<GCodeDocument>.Fail("FileNotFound", path);
                var info = new FileInfo(path);
                if (info.Length > MaxFileSizeBytes) return FileResult<GCodeDocument>.Fail("FileTooLarge", path);
                string text = ReadTextAutoEncoding(path);
                var doc = new GCodeDocument();
                doc.SetText(text, recordUndo: false, markDirty: false);
                doc.MarkSaved(path);
                RecentFiles.Add(path);
                return FileResult<GCodeDocument>.Ok(doc);
            }
            catch (Exception ex)
            {
                return FileResult<GCodeDocument>.Fail(ex.Message, path);
            }
        }

        /// <summary>导入：与打开的区别是导入后作为未命名新文档（不覆盖源文件）。</summary>
        public FileResult<GCodeDocument> Import(string path)
        {
            if (!IsSupported(path)) return FileResult<GCodeDocument>.Fail("UnsupportedExtension", path);
            var result = Open(path);
            if (!result.Success) return result;
            result.Value.FilePath = null;
            result.Value.SetDirty(true);
            return result;
        }

        public FileResult<bool> Save(GCodeDocument doc)
        {
            if (string.IsNullOrEmpty(doc.FilePath)) return FileResult<bool>.Fail("NoPath", null);
            return SaveAs(doc, doc.FilePath);
        }

        public FileResult<bool> SaveAs(GCodeDocument doc, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(Path.GetExtension(path))) path += ".nc";
                WriteAtomic(path, doc.Text);
                doc.MarkSaved(path);
                RecentFiles.Add(path);
                return FileResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return FileResult<bool>.Fail(ex.Message, path);
            }
        }

        /// <summary>
        /// 导出到目标路径（不改变文档自身路径）。lineEnding 可选 "\r\n"（Fanuc/Windows 控制器常用）。
        /// </summary>
        public FileResult<string> Export(GCodeDocument doc, string path, string lineEnding = "\r\n", bool ensurePercent = false)
        {
            try
            {
                string text = doc.Text.Replace("\n", lineEnding);
                if (ensurePercent)
                {
                    string trimmed = text.Trim();
                    if (!trimmed.StartsWith("%")) text = "%" + lineEnding + text;
                    if (!trimmed.EndsWith("%")) text = text.TrimEnd() + lineEnding + "%" + lineEnding;
                }
                WriteAtomic(path, text);
                return FileResult<string>.Ok(path);
            }
            catch (Exception ex)
            {
                return FileResult<string>.Fail(ex.Message, path);
            }
        }

        /// <summary>导出到临时目录以供系统分享（移动端分享面板需要一个真实文件路径）。</summary>
        public FileResult<string> ExportForShare(GCodeDocument doc, string cacheDirectory)
        {
            Directory.CreateDirectory(cacheDirectory);
            string name = Path.GetFileNameWithoutExtension(doc.DisplayName);
            string path = Path.Combine(cacheDirectory, SanitizeFileName(name) + ".nc");
            return Export(doc, path);
        }

        public static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "program" : name;
        }

        public static string ReadTextAutoEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // 老控制器导出的文件常为 ANSI/Latin1
                return Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
            }
        }

        /// <summary>先写临时文件再替换，防止写入中断导致文件损坏。</summary>
        public static void WriteAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    public struct FileResult<T>
    {
        public bool Success;
        public T Value;
        public string Error;
        public string Path;

        public static FileResult<T> Ok(T value) => new FileResult<T> { Success = true, Value = value };
        public static FileResult<T> Fail(string error, string path) => new FileResult<T> { Success = false, Error = error, Path = path };
    }

    /// <summary>最近文件列表（持久化为纯文本，每行一个路径）。</summary>
    public class RecentFilesList
    {
        private readonly List<string> _items = new List<string>();
        private readonly string _storagePath;

        public int Capacity { get; set; }
        public IReadOnlyList<string> Items => _items;
        public event Action Changed;

        public RecentFilesList(string storagePath, int capacity = 10)
        {
            _storagePath = storagePath;
            Capacity = capacity;
            Load();
        }

        public void Add(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string full = Path.GetFullPath(path);
            _items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            _items.Insert(0, full);
            while (_items.Count > Capacity) _items.RemoveAt(_items.Count - 1);
            Save();
        }

        public void Remove(string path)
        {
            if (_items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }

        public void Clear()
        {
            _items.Clear();
            Save();
        }

        /// <summary>移除已不存在的文件。</summary>
        public void PruneMissing()
        {
            if (_items.RemoveAll(p => !File.Exists(p)) > 0) Save();
        }

        private void Load()
        {
            _items.Clear();
            if (string.IsNullOrEmpty(_storagePath) || !File.Exists(_storagePath)) return;
            foreach (var line in File.ReadAllLines(_storagePath))
                if (!string.IsNullOrWhiteSpace(line)) _items.Add(line.Trim());
        }

        private void Save()
        {
            if (!string.IsNullOrEmpty(_storagePath))
            {
                string dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_storagePath, _items);
            }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 自动保存与崩溃恢复。
    /// 调用方每帧/定时调用 Tick(now)；文档变脏且距上次修改超过 IdleDelay 或距上次备份超过 Interval 时写入恢复文件。
    /// 程序启动时调用 GetRecoverableSessions() 检查是否有未正常关闭的会话。
    /// </summary>
    public class AutoSaveService
    {
        private readonly string _recoveryDir;
        private GCodeDocument _doc;
        private int _savedVersion = -1;
        private DateTime _lastBackupUtc = DateTime.MinValue;

        public TimeSpan Interval = TimeSpan.FromSeconds(60);
        public TimeSpan IdleDelay = TimeSpan.FromSeconds(3);
        public bool Enabled = true;

        /// <summary>当前会话恢复文件 ID。</summary>
        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public event Action<string> BackupWritten;

        public AutoSaveService(string recoveryDirectory)
        {
            _recoveryDir = recoveryDirectory;
            Directory.CreateDirectory(_recoveryDir);
        }

        public void Attach(GCodeDocument doc)
        {
            _doc = doc;
            _savedVersion = doc?.Version ?? -1;
        }

        public void Tick(DateTime utcNow)
        {
            if (!Enabled || _doc == null || !_doc.IsDirty || _doc.Version == _savedVersion) return;
            bool idle = utcNow - _doc.LastModifiedUtc >= IdleDelay;
            bool overdue = utcNow - _lastBackupUtc >= Interval;
            if (idle || overdue) WriteBackup(utcNow);
        }

        public void WriteBackup(DateTime utcNow)
        {
            if (_doc == null) return;
            string dataPath = Path.Combine(_recoveryDir, SessionId + ".nc");
            string metaPath = Path.Combine(_recoveryDir, SessionId + ".meta");
            GCodeFileService.WriteAtomic(dataPath, _doc.Text);
            File.WriteAllLines(metaPath, new[]
            {
                _doc.FilePath ?? string.Empty,
                utcNow.ToString("o"),
                _doc.DisplayName
            });
            _savedVersion = _doc.Version;
            _lastBackupUtc = utcNow;
            BackupWritten?.Invoke(dataPath);
        }

        /// <summary>正常退出时调用，清除本会话恢复文件。</summary>
        public void EndSessionCleanly() => DiscardSession(SessionId);

        public List<RecoverySession> GetRecoverableSessions()
        {
            var list = new List<RecoverySession>();
            foreach (var meta in Directory.GetFiles(_recoveryDir, "*.meta"))
            {
                string id = Path.GetFileNameWithoutExtension(meta);
                if (id == SessionId) continue;
                string data = Path.Combine(_recoveryDir, id + ".nc");
                if (!File.Exists(data)) continue;
                var lines = File.ReadAllLines(meta);
                DateTime.TryParse(lines.Length > 1 ? lines[1] : null, null, System.Globalization.DateTimeStyles.RoundtripKind, out var time);
                list.Add(new RecoverySession
                {
                    Id = id,
                    OriginalPath = lines.Length > 0 && lines[0].Length > 0 ? lines[0] : null,
                    SavedAtUtc = time,
                    DisplayName = lines.Length > 2 ? lines[2] : id,
                    DataPath = data
                });
            }
            list.Sort((a, b) => b.SavedAtUtc.CompareTo(a.SavedAtUtc));
            return list;
        }

        /// <summary>恢复会话为文档（保留原始路径，标记为脏），并移除恢复文件。</summary>
        public GCodeDocument Recover(RecoverySession session)
        {
            var doc = new GCodeDocument();
            doc.SetText(File.ReadAllText(session.DataPath), recordUndo: false, markDirty: true);
            doc.FilePath = session.OriginalPath;
            DiscardSession(session.Id);
            return doc;
        }

        /// <summary>丢弃恢复文件：移动到 discarded 子目录（保留备份以防误操作）。</summary>
        public void DiscardSession(string id)
        {
            string discarded = Path.Combine(_recoveryDir, "discarded");
            foreach (var ext in new[] { ".nc", ".meta" })
            {
                string src = Path.Combine(_recoveryDir, id + ext);
                if (!File.Exists(src)) continue;
                Directory.CreateDirectory(discarded);
                string dst = Path.Combine(discarded, id + ext);
                if (File.Exists(dst)) File.Replace(src, dst, null);
                else File.Move(src, dst);
            }
        }
    }

    public class RecoverySession
    {
        public string Id;
        public string OriginalPath;
        public string DisplayName;
        public DateTime SavedAtUtc;
        public string DataPath;
    }
}
