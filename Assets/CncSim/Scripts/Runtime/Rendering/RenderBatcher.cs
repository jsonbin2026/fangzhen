using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CncSim.Runtime
{
    /// <summary>
    /// 渲染批量合并与 GPU 实例化工具（功能 87 批量合并、功能 88 GPU 实例化）。
    /// - StaticBatcher：把共享材质的静态子网格合并为单个 Mesh，减少 DrawCall。
    /// - InstancedRenderer：对相同几何 + 材质的重复对象（如刀库中的刀具、多个夹具）启用
    ///   MaterialPropertyBlock + Graphics.DrawMeshInstanced，实现一次绘制多份。
    /// 这些是辅助工具，由场景搭建代码按需调用。
    /// </summary>
    public static class StaticBatcher
    {
        /// <summary>
        /// 合并一批 MeshFilter（要求同材质）为一个网格，返回合并后的 GameObject。
        /// 结果顶点数受 Unity 16 位索引限制时自动切换到 32 位索引。
        /// </summary>
        public static GameObject Combine(Transform parent, string name, IList<MeshFilter> filters, Material material)
        {
            if (filters == null || filters.Count == 0) return null;

            var combines = new List<CombineInstance>(filters.Count);
            foreach (var f in filters)
            {
                if (f == null || f.sharedMesh == null) continue;
                combines.Add(new CombineInstance
                {
                    mesh = f.sharedMesh,
                    transform = parent != null
                        ? parent.worldToLocalMatrix * f.transform.localToWorldMatrix
                        : f.transform.localToWorldMatrix
                });
            }
            if (combines.Count == 0) return null;

            var mesh = new Mesh { name = name + "_Combined", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combines.ToArray(), true, true);

            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return go;
        }
    }

    /// <summary>GPU 实例化绘制器，适用于大量重复的小零件。</summary>
    public class InstancedRenderer : MonoBehaviour
    {
        public Mesh Mesh;
        public Material Material;
        [Tooltip("每批最大实例数（受 Graphics.DrawMeshInstanced 上限 1023 限制）")]
        public int BatchSize = 1023;
        public ShadowCastingMode CastShadows = ShadowCastingMode.Off;
        public bool ReceiveShadows;

        private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
        private MaterialPropertyBlock _block;

        /// <summary>设置实例矩阵列表（世界空间）。</summary>
        public void SetInstances(IList<Matrix4x4> matrices)
        {
            _matrices.Clear();
            if (matrices != null) _matrices.AddRange(matrices);
        }

        public void AddInstance(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            _matrices.Add(Matrix4x4.TRS(position, rotation, scale));
        }

        public void ClearInstances() => _matrices.Clear();

        private void Update()
        {
            if (Mesh == null || Material == null || _matrices.Count == 0) return;
            _block ??= new MaterialPropertyBlock();
            int batch = Mathf.Clamp(BatchSize, 1, 1023);
            for (int i = 0; i < _matrices.Count; i += batch)
            {
                int count = Mathf.Min(batch, _matrices.Count - i);
                var slice = _matrices.GetRange(i, count);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, slice, count, _block,
                    CastShadows, ReceiveShadows, gameObject.layer);
            }
        }
    }
}
