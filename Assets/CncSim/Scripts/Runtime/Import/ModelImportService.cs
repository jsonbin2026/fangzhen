using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CncSim.Core;
using CncSim.Core.Import;
using CncSim.Core.Stock;

namespace CncSim.Runtime
{
    /// <summary>导入用途。</summary>
    public enum ModelRole
    {
        Workpiece,
        Fixture,
        Machine,
        Tool
    }

    /// <summary>
    /// 模型导入服务（功能 71-74）。
    /// - STL / OBJ：Core 直接解析（二进制/ASCII STL、OBJ）。
    /// - glTF / FBX / STEP：需要 Unity 侧解析。Unity 内置 FBX/glTF(通过包) 导入后，
    ///   用 <see cref="ImportFromGameObject"/> 递归提取网格并回填为 Core MeshData；
    ///   STEP 需外部转换器（如 OpenCascade/商用库），Unity 无法直接读取。
    /// 导入后的网格可设为毛坯（自定义形状）、夹具或机床部件。
    /// </summary>
    public class ModelImportService : MonoBehaviour
    {
        [Header("Fallback material for imported renderers")]
        public Material ImportedMaterial;

        public event Action<ModelRole, ImportedModel> ModelImported;
        public event Action<ModelRole, string> ImportFailed;

        /// <summary>
        /// 从文件路径导入。STL/OBJ 直接解析；glTF/FBX/STEP 返回 RequiresUnityImport，
        /// 需要先通过 Unity 的资源管线加载为 GameObject，再用 ImportFromGameObject。
        /// </summary>
        public ImportedModel ImportFile(string path, ModelRole role)
        {
            var model = Core.Import.ModelImporter.Load(path);
            if (!model.Success)
            {
                ImportFailed?.Invoke(role, model.Error);
                return model;
            }
            ModelImported?.Invoke(role, model);
            return model;
        }

        /// <summary>从已加载的 Unity GameObject（FBX/glTF 实例）提取网格。</summary>
        public ImportedModel ImportFromGameObject(GameObject root, ModelRole role, string name = null)
        {
            var model = new ImportedModel { Name = name ?? root.name, Format = ModelFormat.Fbx, Success = false };
            if (root == null)
            {
                model.Error = "NullGameObject";
                ImportFailed?.Invoke(role, model.Error);
                return model;
            }

            var filters = root.GetComponentsInChildren<MeshFilter>();
            var combined = new Core.MeshData();
            foreach (var f in filters)
            {
                if (f == null || f.sharedMesh == null) continue;
                var src = f.sharedMesh;
                var verts = src.vertices;
                var norms = src.normals;
                var tris = src.triangles;
                if (verts == null || tris == null || verts.Length == 0) continue;

                var vf = new float[verts.Length * 3];
                for (int i = 0; i < verts.Length; i++)
                {
                    vf[i * 3] = verts[i].x;
                    vf[i * 3 + 1] = verts[i].y;
                    vf[i * 3 + 2] = verts[i].z;
                }
                float[] nf = null;
                if (norms != null && norms.Length == verts.Length)
                {
                    nf = new float[norms.Length * 3];
                    for (int i = 0; i < norms.Length; i++)
                    {
                        nf[i * 3] = norms[i].x;
                        nf[i * 3 + 1] = norms[i].y;
                        nf[i * 3 + 2] = norms[i].z;
                    }
                }
                var part = Core.Import.ModelImporter.FromUnityMesh(vf, nf, tris, unityToCnc: true);
                combined.Append(part);
            }

            if (combined.VertexCount == 0)
            {
                model.Error = "NoMeshFound";
                ImportFailed?.Invoke(role, model.Error);
                return model;
            }

            model.Mesh = combined;
            model.Success = true;
            ModelImported?.Invoke(role, model);
            return model;
        }

        /// <summary>把导入的模型应用为自定义毛坯（功能 26 自定义形状）。</summary>
        public void ApplyAsBlank(CncSimBehaviour simulator, ImportedModel model)
        {
            if (simulator?.Sim == null || model == null || !model.Success) return;
            var bounds = model.Bounds;
            simulator.Sim.Blank.Shape = BlankShape.Custom;
            simulator.Sim.Blank.CustomMesh = model.Mesh;
            simulator.Sim.Blank.Size = bounds.Size;
            simulator.Sim.Blank.Center = bounds.Center;
            simulator.Entities?.RebuildAll();
        }

        /// <summary>把导入的模型应用为夹具外观（功能 29、74）。</summary>
        public void ApplyAsFixture(CncSimBehaviour simulator, ImportedModel model, string colorHex = "#3C6E9F")
        {
            if (simulator?.Sim == null || model == null || !model.Success) return;
            var bounds = model.Bounds;
            simulator.Sim.Fixture.Enabled = true;
            simulator.Sim.Fixture.Bounds = bounds;
            simulator.Entities?.RebuildAll();
        }
    }
}
