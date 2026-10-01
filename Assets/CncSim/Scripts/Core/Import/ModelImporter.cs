using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CncSim.Core.Import
{
    /// <summary>支持的模型格式。</summary>
    public enum ModelFormat
    {
        Unknown,
        StlBinary,
        StlAscii,
        Obj,
        /// <summary>glTF 2.0 (.gltf / .glb)</summary>
        Gltf,
        /// <summary>FBX（需由 Unity 层导入后回传网格）</summary>
        Fbx,
        /// <summary>STEP（需外部转换器）</summary>
        Step
    }

    /// <summary>导入结果。</summary>
    public class ImportedModel
    {
        public bool Success;
        public string Error;
        public ModelFormat Format;
        public MeshData Mesh = new MeshData();
        public int TriangleCount => Mesh.TriangleCount;
        public Aabb Bounds => Mesh.ComputeBounds();
        /// <summary>原始文件名（用于显示）。</summary>
        public string Name;
    }

    /// <summary>
    /// 模型导入（功能 71-74）：STL（二进制/ASCII）、OBJ 由 Core 直接解析；
    /// glTF/FBX/STEP 交由 Unity 层（AssetDatabase / 第三方库）导入后，通过
    /// <see cref="FromUnityMesh"/> 回填。机床/夹具模型同样走本接口。
    /// </summary>
    public static class ModelImporter
    {
        public static ModelFormat DetectFormat(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            switch (ext)
            {
                case ".stl":
                    return IsBinaryStl(path) ? ModelFormat.StlBinary : ModelFormat.StlAscii;
                case ".obj":
                    return ModelFormat.Obj;
                case ".gltf":
                case ".glb":
                    return ModelFormat.Gltf;
                case ".fbx":
                    return ModelFormat.Fbx;
                case ".step":
                case ".stp":
                    return ModelFormat.Step;
                default:
                    return ModelFormat.Unknown;
            }
        }

        /// <summary>导入模型（自动识别 STL/OBJ）。glTF/FBX/STEP 返回 Unsupported，需 Unity 层处理。</summary>
        public static ImportedModel Load(string path)
        {
            var result = new ImportedModel { Name = Path.GetFileName(path) };
            try
            {
                if (!File.Exists(path))
                {
                    result.Error = "FileNotFound";
                    return result;
                }
                var format = DetectFormat(path);
                result.Format = format;
                switch (format)
                {
                    case ModelFormat.StlBinary:
                        result.Mesh = StlReader.ReadBinary(path);
                        result.Success = true;
                        break;
                    case ModelFormat.StlAscii:
                        result.Mesh = StlReader.ReadAscii(path);
                        result.Success = true;
                        break;
                    case ModelFormat.Obj:
                        result.Mesh = ObjReader.Read(path, out _);
                        result.Success = true;
                        break;
                    case ModelFormat.Gltf:
                    case ModelFormat.Fbx:
                    case ModelFormat.Step:
                        result.Error = "RequiresUnityImport";
                        break;
                    default:
                        result.Error = "UnsupportedFormat";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 由 Unity 层导入的顶点/法线/三角形数组回填为 Core 网格（用于 FBX/glTF/STEP）。
        /// </summary>
        public static MeshData FromUnityMesh(float[] vertices, float[] normals, int[] triangles,
            bool unityToCnc = true)
        {
            var mesh = new MeshData();
            if (vertices == null || triangles == null) return mesh;
            int count = vertices.Length / 3;
            for (int i = 0; i < count; i++)
            {
                float x = vertices[i * 3], y = vertices[i * 3 + 1], z = vertices[i * 3 + 2];
                // Unity (x,y,z) -> CNC (x,z,y)
                var p = unityToCnc ? new Vec3d(x, z, y) : new Vec3d(x, y, z);
                Vec3d n = Vec3d.UnitZ;
                if (normals != null && normals.Length >= vertices.Length)
                {
                    float nx = normals[i * 3], ny = normals[i * 3 + 1], nz = normals[i * 3 + 2];
                    n = unityToCnc ? new Vec3d(nx, nz, ny) : new Vec3d(nx, ny, nz);
                }
                mesh.AddVertex(p, n);
            }
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count) continue;
                // Unity 左手系 -> CNC 右手系：翻转绕序
                mesh.AddTriangle(a, c, b);
            }
            return mesh;
        }

        public static bool IsBinaryStl(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                if (fs.Length < 84) return false;
                var header = new byte[84];
                fs.Read(header, 0, 84);
                uint count = BitConverter.ToUInt32(header, 80);
                long expected = 84L + (long)count * 50L;
                if (expected == fs.Length) return true;
                // 头部以 "solid" 开头且非精确二进制长度 -> ASCII
                string prefix = Encoding.ASCII.GetString(header, 0, 5);
                return !prefix.StartsWith("solid", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>STL 读取（功能 71）。</summary>
    public static class StlReader
    {
        public static MeshData ReadBinary(string path)
        {
            var mesh = new MeshData();
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);
            reader.ReadBytes(80);
            uint triangleCount = reader.ReadUInt32();
            var map = new Dictionary<(float, float, float), int>();
            for (uint t = 0; t < triangleCount; t++)
            {
                var normal = new Vec3d(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                int[] idx = new int[3];
                for (int v = 0; v < 3; v++)
                {
                    float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                    idx[v] = GetOrAdd(mesh, map, x, y, z, normal);
                    reader.ReadUInt16(); // attribute byte count
                }
                mesh.AddTriangle(idx[0], idx[1], idx[2]);
            }
            mesh.RecalculateNormals();
            return mesh;
        }

        public static MeshData ReadAscii(string path)
        {
            var mesh = new MeshData();
            var map = new Dictionary<(float, float, float), int>();
            Vec3d pendingNormal = Vec3d.UnitZ;
            var triangle = new List<int>(3);
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                {
                    var p = Split(line);
                    if (p.Length >= 5)
                        pendingNormal = new Vec3d(P(p[2]), P(p[3]), P(p[4]));
                }
                else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    var p = Split(line);
                    if (p.Length >= 4)
                        triangle.Add(GetOrAdd(mesh, map, (float)P(p[1]), (float)P(p[2]), (float)P(p[3]), pendingNormal));
                }
                else if (line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
                {
                    if (triangle.Count == 3) mesh.AddTriangle(triangle[0], triangle[1], triangle[2]);
                    triangle.Clear();
                }
            }
            mesh.RecalculateNormals();
            return mesh;
        }

        private static int GetOrAdd(MeshData mesh, Dictionary<(float, float, float), int> map,
            float x, float y, float z, Vec3d normal)
        {
            var key = (x, y, z);
            if (map.TryGetValue(key, out int idx)) return idx;
            idx = mesh.AddVertex(new Vec3d(x, y, z), normal);
            map[key] = idx;
            return idx;
        }

        private static string[] Split(string line) =>
            line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        private static double P(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>OBJ 读取（功能 73 的通用网格格式支持）。</summary>
    public static class ObjReader
    {
        public static MeshData Read(string path, out Dictionary<int, string> groups)
        {
            groups = new Dictionary<int, string>();
            var positions = new List<Vec3d>();
            var normals = new List<Vec3d>();
            var mesh = new MeshData();
            var vertexMap = new Dictionary<(int, int), int>();
            int currentGroup = 0;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v":
                        if (parts.Length >= 4)
                            positions.Add(new Vec3d(P(parts[1]), P(parts[2]), P(parts[3])));
                        break;
                    case "vn":
                        if (parts.Length >= 4)
                            normals.Add(new Vec3d(P(parts[1]), P(parts[2]), P(parts[3])));
                        break;
                    case "g":
                    case "o":
                        groups[mesh.TriangleCount] = parts.Length > 1 ? parts[1] : string.Empty;
                        break;
                    case "f":
                        AddFace(parts, positions, normals, mesh, vertexMap);
                        currentGroup++;
                        break;
                }
            }
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void AddFace(string[] parts, List<Vec3d> positions, List<Vec3d> normals,
            MeshData mesh, Dictionary<(int, int), int> vertexMap)
        {
            var ring = new List<int>(parts.Length - 1);
            for (int i = 1; i < parts.Length; i++)
            {
                var tokens = parts[i].Split('/');
                if (!int.TryParse(tokens[0], out int vi)) continue;
                vi = vi < 0 ? positions.Count + vi : vi - 1;
                int ni = -1;
                if (tokens.Length >= 3 && int.TryParse(tokens[2], out int n)) ni = n < 0 ? normals.Count + n : n - 1;

                var key = (vi, ni);
                if (!vertexMap.TryGetValue(key, out int idx))
                {
                    Vec3d p = vi >= 0 && vi < positions.Count ? positions[vi] : Vec3d.Zero;
                    Vec3d vn = ni >= 0 && ni < normals.Count ? normals[ni] : Vec3d.UnitZ;
                    idx = mesh.AddVertex(p, vn);
                    vertexMap[key] = idx;
                }
                ring.Add(idx);
            }
            for (int i = 2; i < ring.Count; i++)
                mesh.AddTriangle(ring[0], ring[i - 1], ring[i]);
        }

        private static double P(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
