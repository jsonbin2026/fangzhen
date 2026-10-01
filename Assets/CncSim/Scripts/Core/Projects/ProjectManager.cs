using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;

namespace CncSim.Core.Projects
{
    /// <summary>单个工件/项目。</summary>
    [Serializable]
    public class CncProject
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Untitled";
        public string GCodePath;
        public string GCodeText = string.Empty;
        public string MachineType = "3Axis";
        public Vec3d BlankSize = new Vec3d(100, 100, 50);
        public Vec3d BlankCenter = Vec3d.Zero;
        public string BlankShape = "Box";
        public string MaterialId = "aluminum";
        public List<ToolDefinition> Tools = new List<ToolDefinition>();
        /// <summary>导入的模型（毛坯/夹具/机床）在项目中的相对路径或标识。</summary>
        public List<string> ImportedModels = new List<string>();
        public DateTime ModifiedUtc = DateTime.UtcNow;

        public string DisplayName => string.IsNullOrEmpty(Name) ? "Untitled" : Name;

        public CncProject Clone()
        {
            var c = (CncProject)MemberwiseClone();
            c.Tools = new List<ToolDefinition>();
            foreach (var t in Tools) c.Tools.Add(t.Clone());
            c.ImportedModels = new List<string>(ImportedModels);
            return c;
        }
    }

    /// <summary>
    /// 多工件/多项目管理器（功能 77）。
    /// 管理一组 <see cref="CncProject"/>，负责序列化到磁盘（每项目一个 .cncproj 文件），
    /// 支持新建、复制、删除、重命名、最近打开。
    /// </summary>
    public class ProjectManager
    {
        public string ProjectDirectory { get; private set; }
        public IReadOnlyList<CncProject> Projects => _projects;
        public CncProject Active { get; private set; }

        public event Action Changed;
        public event Action<CncProject> ActiveChanged;

        private readonly List<CncProject> _projects = new List<CncProject>();

        private const string Magic = "CNCPROJ1";

        public ProjectManager(string projectDirectory)
        {
            ProjectDirectory = projectDirectory;
            Directory.CreateDirectory(projectDirectory);
            LoadAll();
        }

        public CncProject Create(string name = null)
        {
            var project = new CncProject { Name = name ?? ("Project " + (_projects.Count + 1)) };
            project.Tools.AddRange(ToolLibrary.CreateDefault().Tools);
            _projects.Add(project);
            SetActive(project);
            Save(project);
            Changed?.Invoke();
            return project;
        }

        public CncProject Duplicate(CncProject source)
        {
            if (source == null) return null;
            var copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = source.Name + " Copy";
            _projects.Add(copy);
            Save(copy);
            Changed?.Invoke();
            return copy;
        }

        public bool Delete(CncProject project)
        {
            if (project == null) return false;
            string path = PathFor(project);
            if (File.Exists(path)) File.Delete(path);
            bool removed = _projects.Remove(project);
            if (removed && Active == project) SetActive(_projects.Count > 0 ? _projects[0] : null);
            if (removed) Changed?.Invoke();
            return removed;
        }

        public void Rename(CncProject project, string newName)
        {
            if (project == null) return;
            project.Name = newName;
            project.ModifiedUtc = DateTime.UtcNow;
            Save(project);
            Changed?.Invoke();
        }

        public void SetActive(CncProject project)
        {
            Active = project;
            ActiveChanged?.Invoke(project);
        }

        public string PathFor(CncProject project) =>
            Path.Combine(ProjectDirectory, project.Id + ".cncproj");

        public void Save(CncProject project)
        {
            if (project == null) return;
            project.ModifiedUtc = DateTime.UtcNow;
            var sb = new StringBuilder();
            sb.AppendLine(Magic);
            sb.AppendLine("name=" + project.Name);
            sb.AppendLine("id=" + project.Id);
            sb.AppendLine("machine=" + project.MachineType);
            sb.AppendLine("shape=" + project.BlankShape);
            sb.AppendLine("material=" + project.MaterialId);
            sb.AppendLine("blank=" + N(project.BlankSize.X) + "," + N(project.BlankSize.Y) + "," + N(project.BlankSize.Z));
            sb.AppendLine("center=" + N(project.BlankCenter.X) + "," + N(project.BlankCenter.Y) + "," + N(project.BlankCenter.Z));
            sb.AppendLine("gcode=" + Escape(project.GCodeText ?? string.Empty));
            sb.AppendLine("tools=" + project.Tools.Count);
            foreach (var t in project.Tools)
                sb.AppendLine("tool=" + t.Number + "," + N(t.Diameter) + "," + N(t.Length) + "," + t.Type + "," + Escape(t.Name));
            foreach (var m in project.ImportedModels)
                sb.AppendLine("model=" + Escape(m));
            File.WriteAllText(PathFor(project), sb.ToString());
        }

        public void SaveActive() => Save(Active);

        private void LoadAll()
        {
            _projects.Clear();
            if (!Directory.Exists(ProjectDirectory)) return;
            foreach (var path in Directory.GetFiles(ProjectDirectory, "*.cncproj"))
            {
                var project = LoadFile(path);
                if (project != null) _projects.Add(project);
            }
            _projects.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));
            if (_projects.Count > 0) SetActive(_projects[0]);
        }

        public CncProject LoadFile(string path)
        {
            try
            {
                var project = new CncProject();
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq);
                    string value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "id": project.Id = value; break;
                        case "name": project.Name = value; break;
                        case "machine": project.MachineType = value; break;
                        case "shape": project.BlankShape = value; break;
                        case "material": project.MaterialId = value; break;
                        case "blank": ApplyVec(value, v => project.BlankSize = v); break;
                        case "center": ApplyVec(value, v => project.BlankCenter = v); break;
                        case "gcode": project.GCodeText = Unescape(value); break;
                        case "tool":
                            var t = ParseTool(value);
                            if (t != null) project.Tools.Add(t);
                            break;
                        case "model": project.ImportedModels.Add(Unescape(value)); break;
                    }
                }
                project.ModifiedUtc = File.GetLastWriteTimeUtc(path);
                return project;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyVec(string value, Action<Vec3d> setter)
        {
            var parts = value.Split(',');
            if (parts.Length < 3) return;
            setter(new Vec3d(D(parts[0]), D(parts[1]), D(parts[2])));
        }

        private static ToolDefinition ParseTool(string value)
        {
            var parts = value.Split(',');
            if (parts.Length < 4) return null;
            var tool = new ToolDefinition
            {
                Number = int.TryParse(parts[0], out var n) ? n : 1,
                Diameter = D(parts[1]),
                Length = D(parts[2]),
                Name = parts.Length > 4 ? Unescape(parts[4]) : "Tool"
            };
            if (Enum.TryParse(parts[3], out ToolType type)) tool.Type = type;
            return tool;
        }

        private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static double D(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        private static string Unescape(string s) =>
            s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");
    }
}
