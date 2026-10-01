using System.Collections.Generic;
using UnityEngine;

namespace CncSim.Runtime
{
    /// <summary>
    /// 模型透明度调节（功能 41）。
    /// 对每个目标渲染器生成材质实例并设置透明渲染模式，可独立或统一调节 alpha。
    /// 支持的标准 Shader：Standard（透明模式 3）与 URP/Lit（_Surface=1, _Blend=0）。
    /// </summary>
    public class TransparencyController : MonoBehaviour
    {
        [Range(0f, 1f)]
        public float Opacity = 1f;

        [Tooltip("需要调节透明度的渲染器")]
        public Renderer[] Targets;

        [Tooltip("是否让所有目标共用同一透明度")]
        public bool Uniform = true;

        private readonly Dictionary<Renderer, Material[]> _runtime = new Dictionary<Renderer, Material[]>();

        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Start() => Apply();

        private void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }

        public void SetOpacity(float value)
        {
            Opacity = Mathf.Clamp01(value);
            Apply();
        }

        /// <summary>为单个渲染器设置独立透明度。</summary>
        public void SetOpacity(Renderer renderer, float value)
        {
            var mats = GetOrCreateMaterials(renderer);
            if (mats == null) return;
            foreach (var m in mats) SetMaterialAlpha(m, value);
        }

        public void Apply()
        {
            if (Targets == null) return;
            foreach (var r in Targets)
            {
                var mats = GetOrCreateMaterials(r);
                if (mats == null) continue;
                foreach (var m in mats) SetMaterialAlpha(m, Opacity);
            }
        }

        private Material[] GetOrCreateMaterials(Renderer r)
        {
            if (r == null) return null;
            if (_runtime.TryGetValue(r, out var existing))
            {
                // 透明度变化时仍要保持透明模式，直接返回实例
                return existing;
            }

            var source = r.sharedMaterials;
            var inst = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var m = source[i] != null ? new Material(source[i]) : null;
                if (m != null) MakeTransparentCapable(m);
                inst[i] = m;
            }
            r.materials = inst;
            _runtime[r] = inst;
            return inst;
        }

        private static void MakeTransparentCapable(Material m)
        {
            // Standard shader
            if (m.HasProperty(ModeId)) m.SetFloat(ModeId, 3f);
            if (m.HasProperty(SrcBlendId)) m.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty(DstBlendId)) m.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty(ZWriteId)) m.SetFloat(ZWriteId, 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.3f);
            // URP Lit
            if (m.HasProperty(SurfaceId)) m.SetFloat(SurfaceId, 1f);
            if (m.HasProperty(BlendId)) m.SetFloat(BlendId, 0f);

            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_ALPHABLEND_ON");
        }

        private static void SetMaterialAlpha(Material m, float alpha)
        {
            if (m == null) return;
            bool opaque = alpha >= 0.999f;
            if (m.HasProperty(ModeId)) m.SetFloat(ModeId, opaque ? 0f : 3f);
            if (m.HasProperty(SurfaceId)) m.SetFloat(SurfaceId, opaque ? 0f : 1f);
            if (m.HasProperty(ZWriteId)) m.SetFloat(ZWriteId, opaque ? 1f : 0f);
            m.renderQueue = opaque
                ? (int)UnityEngine.Rendering.RenderQueue.Geometry
                : (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (m.HasProperty(BaseColorId))
            {
                var c = m.GetColor(BaseColorId);
                c.a = alpha;
                m.SetColor(BaseColorId, c);
            }
            if (m.HasProperty(ColorId))
            {
                var c = m.GetColor(ColorId);
                c.a = alpha;
                m.SetColor(ColorId, c);
            }
        }

        /// <summary>释放运行时材质实例。</summary>
        public void Restore()
        {
            foreach (var kv in _runtime)
            {
                if (kv.Value == null) continue;
                foreach (var m in kv.Value) if (m != null) Destroy(m);
            }
            _runtime.Clear();
        }

        private void OnDestroy() => Restore();
    }
}
