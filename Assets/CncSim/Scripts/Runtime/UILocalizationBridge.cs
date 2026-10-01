using UnityEngine;
using CncSim.Core;
using CncSim.Core.Localization;
using CncSim.Core.Theming;

namespace CncSim.Runtime
{
    /// <summary>
    /// 多语言与主题运行时辅助（功能 92 多语言、功能 93 主题）。
    /// 监听 Loc.LanguageChanged 与 ThemeManager.ThemeChanged，把变化广播给 UI，
    /// 并提供把主题色应用到相机背景/环境光的便捷方法。
    /// </summary>
    public class UILocalizationBridge : MonoBehaviour
    {
        [Header("References")]
        public CncSimBehaviour Simulator;
        public Camera TargetCamera;

        /// <summary>语言变化（参数为语言代码，如 zh-CN / en-US）。</summary>
        public event System.Action<string> LanguageChanged;
        /// <summary>主题变化。</summary>
        public event System.Action<ThemeManager> ThemeChanged;

        public string CurrentLanguage => Loc.Language;
        public ThemeManager Theme => Simulator?.Sim?.Theme;

        private void Awake()
        {
            if (TargetCamera == null) TargetCamera = Camera.main;
            Loc.LanguageChanged += OnLanguageChanged;
            if (Theme != null) Theme.ThemeChanged += OnThemeChanged;
        }

        private void Start()
        {
            OnThemeChanged(Theme);
        }

        private void OnLanguageChanged(string lang) => LanguageChanged?.Invoke(lang);

        private void OnThemeChanged(ThemeManager theme)
        {
            if (theme == null || TargetCamera == null) return;
            var bg = ColorRgba.FromHex(theme.Palette.Background);
            TargetCamera.clearFlags = CameraClearFlags.SolidColor;
            TargetCamera.backgroundColor = MeshBuilder.ToUnity(bg);
            ThemeChanged?.Invoke(theme);
        }

        /// <summary>切换语言（如 "en-US"）。</summary>
        public void SetLanguage(string language)
        {
            Loc.Language = language;
        }

        /// <summary>切换暗/亮主题。</summary>
        public void ToggleTheme()
        {
            Theme?.Toggle();
        }

        /// <summary>取本地化文本。</summary>
        public string T(string key) => Loc.Get(key);
        public string T(string key, params object[] args) => Loc.Format(key, args);

        private void OnDestroy()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            if (Theme != null) Theme.ThemeChanged -= OnThemeChanged;
        }
    }
}
