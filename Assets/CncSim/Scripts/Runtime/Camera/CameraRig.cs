using UnityEngine;
using CncSim.Core;

namespace CncSim.Runtime
{
    /// <summary>标准视角。</summary>
    public enum ViewPreset
    {
        Top,
        Front,
        Side,
        Isometric,
        Back,
        Bottom
    }

    /// <summary>
    /// 轨道相机控制器（功能 39 多视角切换、功能 40 缩放/旋转/平移）。
    /// 围绕一个焦点（通常是毛坯中心）旋转，支持鼠标与触控。
    /// 直接挂到场景的 Camera 上即可使用；相机坐标映射：CNC Z 向上 → Unity Y 向上。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        [Header("Focus")]
        public Vector3 FocusPoint = Vector3.zero;
        public float Distance = 400f;
        public float Yaw = 45f;
        public float Pitch = 30f;

        [Header("Limits")]
        public float MinDistance = 10f;
        public float MaxDistance = 4000f;
        public float MinPitch = -89f;
        public float MaxPitch = 89f;

        [Header("Sensitivity")]
        public float RotateSpeed = 4f;
        public float PanSpeed = 1f;
        public float ZoomSpeed = 10f;
        public float SmoothTime = 0.08f;

        [Header("Interaction")]
        public bool EnableMouse = true;
        public bool EnableTouch = true;

        private Camera _camera;
        private Vector3 _targetFocus;
        private float _targetDistance, _targetYaw, _targetPitch;
        private Vector3 _velocity;

        public Camera Camera => _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _targetFocus = FocusPoint;
            _targetDistance = Distance;
            _targetYaw = Yaw;
            _targetPitch = Pitch;
            ApplyTransform();
        }

        /// <summary>设置焦点与观察距离，用于适配包围盒（bounds 为 Unity 世界空间）。</summary>
        public void FocusOn(Bounds bounds, bool instant = false)
        {
            _targetFocus = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            float fov = _camera != null ? _camera.fieldOfView : 60f;
            _targetDistance = radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.4f;
            _targetDistance = Mathf.Clamp(_targetDistance, MinDistance, MaxDistance);
            if (instant) { FocusPoint = _targetFocus; Distance = _targetDistance; }
        }

        /// <summary>切换到标准视角。</summary>
        public void SetView(ViewPreset preset, bool instant = false)
        {
            switch (preset)
            {
                case ViewPreset.Top: SetAngles(0f, 89.9f); break;
                case ViewPreset.Bottom: SetAngles(0f, -89.9f); break;
                case ViewPreset.Front: SetAngles(0f, 0f); break;
                case ViewPreset.Back: SetAngles(180f, 0f); break;
                case ViewPreset.Side: SetAngles(90f, 0f); break;
                case ViewPreset.Isometric: SetAngles(45f, 30f); break;
            }
            if (instant) { Yaw = _targetYaw; Pitch = _targetPitch; ApplyTransform(); }
        }

        /// <summary>视图分辨率下按包围盒自动聚焦。</summary>
        public void FrameAll(Bounds bounds, ViewPreset? preset = null, bool instant = false)
        {
            if (preset.HasValue) SetView(preset.Value, instant);
            FocusOn(bounds, instant);
            if (instant) ApplyTransform();
        }

        private void SetAngles(float yaw, float pitch)
        {
            _targetYaw = yaw;
            _targetPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        /// <summary>程序化旋转（供 UI 滑杆/按钮调用），单位度。</summary>
        public void Orbit(float deltaYaw, float deltaPitch)
        {
            _targetYaw += deltaYaw;
            _targetPitch = Mathf.Clamp(_targetPitch + deltaPitch, MinPitch, MaxPitch);
        }

        /// <summary>程序化缩放（供 UI 调用），factor&lt;1 拉近。</summary>
        public void Zoom(float factor)
        {
            _targetDistance = Mathf.Clamp(_targetDistance * factor, MinDistance, MaxDistance);
        }

        /// <summary>程序化平移（屏幕空间像素）。</summary>
        public void Pan(Vector2 screenDelta)
        {
            _targetFocus -= transform.right * (screenDelta.x * PanSpeed) +
                            transform.up * (screenDelta.y * PanSpeed);
        }

        private void Update()
        {
            if (EnableMouse) HandleMouse();
            if (EnableTouch) HandleTouch();

            FocusPoint = Vector3.SmoothDamp(FocusPoint, _targetFocus, ref _velocity, SmoothTime);
            Distance = Mathf.Lerp(Distance, _targetDistance, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, SmoothTime)));
            Yaw = Mathf.Lerp(Yaw, _targetYaw, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, SmoothTime)));
            Pitch = Mathf.Lerp(Pitch, _targetPitch, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, SmoothTime)));
            ApplyTransform();
        }

        private void HandleMouse()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 1e-5f) Zoom(1f - scroll * ZoomSpeed * 0.1f);

            if (Input.GetMouseButton(0))
            {
                _targetYaw += Input.GetAxis("Mouse X") * RotateSpeed;
                _targetPitch = Mathf.Clamp(_targetPitch - Input.GetAxis("Mouse Y") * RotateSpeed, MinPitch, MaxPitch);
            }
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                Pan(new Vector2(-Input.GetAxis("Mouse X") * 10f, -Input.GetAxis("Mouse Y") * 10f));
            }
        }

        private void HandleTouch()
        {
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Moved)
                {
                    _targetYaw += t.deltaPosition.x * RotateSpeed * 0.1f;
                    _targetPitch = Mathf.Clamp(_targetPitch - t.deltaPosition.y * RotateSpeed * 0.1f, MinPitch, MaxPitch);
                }
            }
            else if (Input.touchCount == 2)
            {
                var a = Input.GetTouch(0);
                var b = Input.GetTouch(1);
                float prev = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
                float curr = (a.position - b.position).magnitude;
                if (prev > 1e-3f) Zoom(prev / Mathf.Max(1e-3f, curr));
                Pan((a.deltaPosition + b.deltaPosition) * 0.5f * PanSpeed);
            }
        }

        private void ApplyTransform()
        {
            if (_camera == null) return;
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.rotation = rot;
            transform.position = FocusPoint - rot * Vector3.forward * Distance;
        }
    }
}
