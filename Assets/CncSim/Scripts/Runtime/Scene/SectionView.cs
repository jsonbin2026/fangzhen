using UnityEngine;

namespace CncSim.Runtime
{
    /// <summary>
    /// 截面视图（功能 42）。
    /// 通过独立截面材质 + 裁剪平面实现"剖开查看工件内部"。
    /// 工作原理：启用时把目标渲染器替换为持有 clipping plane 的材质，
    /// 平面方程在 Unity 世界空间；用户可用三个轴向与偏移量控制剖面位置。
    /// 注意：需要支持裁剪平面的 Shader（内置 Standard 支持 _GlobalClippingPlane 需自行扩展；
    /// 本实现使用 Material.SetVector("_ClipPlane")，请在你的 Shader 中读取该属性）。
    /// </summary>
    public class SectionView : MonoBehaviour
    {
        public enum SectionAxis { X, Y, Z }

        [Header("State")]
        public bool Enabled;

        [Header("Plane")]
        public SectionAxis Axis = SectionAxis.X;
        [Tooltip("沿轴的世界偏移（Unity 单位）")]
        public float Offset;

        [Tooltip("剖面朝向是否反向")]
        public bool Flip;

        [Header("Rendering")]
        [Tooltip("用于截面的材质；留空则使用 Standard 并仅做显示切换")]
        public Material SectionMaterial;
        [Tooltip("需要被截面的渲染器集合")]
        public Renderer[] Targets;

        private Material _runtimeMaterial;
        private readonly System.Collections.Generic.Dictionary<Renderer, Material[]> _originalMaterials
            = new System.Collections.Generic.Dictionary<Renderer, Material[]>();
        private static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane");
        private static readonly int ClipEnabledId = Shader.PropertyToID("_ClipEnabled");

        /// <summary>当前裁剪平面（Unity 世界空间，法线指向保留的一侧）。</summary>
        public Plane CurrentPlane
        {
            get
            {
                Vector3 normal = Axis switch
                {
                    SectionAxis.X => Vector3.right,
                    SectionAxis.Y => Vector3.forward,
                    SectionAxis.Z => Vector3.up,
                    _ => Vector3.right
                };
                if (Flip) normal = -normal;
                Vector3 point = normal * Offset;
                return new Plane(normal, point);
            }
        }

        private void Awake()
        {
            EnsureMaterial();
        }

        private void OnEnable() => Apply();
        private void OnDisable() => Disable();

        private void Update()
        {
            if (Enabled) Apply();
        }

        /// <summary>设置剖面位置。value 单位与场景一致（Unity 单位）。</summary>
        public void SetOffset(float value)
        {
            Offset = value;
            Apply();
        }

        public void SetAxis(SectionAxis axis)
        {
            Axis = axis;
            Apply();
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (enabled) Apply(); else Disable();
        }

        private void Apply()
        {
            if (Targets == null) return;
            EnsureMaterial();
            if (_runtimeMaterial == null) return;

            var plane = CurrentPlane;
            var planeVector = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            _runtimeMaterial.SetVector(ClipPlaneId, planeVector);
            _runtimeMaterial.SetFloat(ClipEnabledId, Enabled ? 1f : 0f);

            var mats = new Material[Targets.Length];
            for (int i = 0; i < Targets.Length; i++) mats[i] = _runtimeMaterial;
            for (int i = 0; i < Targets.Length; i++)
            {
                var r = Targets[i];
                if (r == null) continue;
                if (!_originalMaterials.ContainsKey(r))
                    _originalMaterials[r] = r.sharedMaterials;
                int count = Mathf.Max(1, r.sharedMaterials.Length);
                var assigned = new Material[count];
                for (int m = 0; m < count; m++) assigned[m] = _runtimeMaterial;
                r.sharedMaterials = assigned;
            }
        }

        private void EnsureMaterial()
        {
            if (_runtimeMaterial == null)
            {
                _runtimeMaterial = SectionMaterial != null
                    ? new Material(SectionMaterial)
                    : new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
                _runtimeMaterial.name = "CncSimSectionMat";
            }
        }

        private void Disable()
        {
            if (_runtimeMaterial != null) _runtimeMaterial.SetFloat(ClipEnabledId, 0f);
            foreach (var kv in _originalMaterials)
            {
                if (kv.Key == null) continue;
                kv.Key.sharedMaterials = kv.Value;
            }
            _originalMaterials.Clear();
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(_runtimeMaterial);
                else DestroyImmediate(_runtimeMaterial);
            }
        }
    }
}
