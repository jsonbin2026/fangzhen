using UnityEngine;
using CncSim.Core;
using CncSim.Core.Theming;

namespace CncSim.Runtime.UI
{
    /// <summary>
    /// UI 主题色板：把 Core 的 ThemeManager.Palette（十六进制字符串）转换为 Unity 颜色，
    /// 并派生 UI 专用色（面板底、按钮态、强调色、文本色、分隔线），供所有控件统一取色。
    /// 监听主题变化即可整体换肤。
    /// </summary>
    public class CncUiTheme
    {
        public Color Panel;
        public Color PanelAlt;
        public Color TopBar;
        public Color BottomBar;
        public Color Button;
        public Color ButtonHover;
        public Color ButtonActive;
        public Color Accent;
        public Color AccentText;
        public Color Text;
        public Color TextMuted;
        public Color Divider;
        public Color Success;
        public Color Warning;
        public Color Error;

        private ThemeManager _source;

        public static CncUiTheme From(ThemeManager theme)
        {
            var t = new CncUiTheme();
            var p = theme?.Palette;
            if (p != null)
            {
                t.Panel = ToColor(p.PanelBackground, new Color(0.16f, 0.17f, 0.20f));
                t.TopBar = ToColor(p.ToolbarBackground, new Color(0.12f, 0.13f, 0.15f));
                t.Text = ToColor(p.Foreground, Color.white);
                t.Accent = ToColor(p.Accent, new Color(0.26f, 0.60f, 0.95f));
                t.AccentText = ToColor(p.AccentText, Color.white);
                t.Divider = ToColor(p.Border, new Color(1f, 1f, 1f, 0.14f));
                t.Success = ToColor(p.Success, new Color(0.32f, 0.74f, 0.42f));
                t.Warning = ToColor(p.Warning, new Color(0.94f, 0.72f, 0.24f));
                t.Error = ToColor(p.Error, new Color(0.92f, 0.36f, 0.36f));
            }
            else
            {
                t.Panel = new Color(0.16f, 0.17f, 0.20f);
                t.TopBar = new Color(0.12f, 0.13f, 0.15f);
                t.Text = Color.white;
                t.Accent = new Color(0.26f, 0.60f, 0.95f);
                t.AccentText = Color.white;
                t.Divider = new Color(1f, 1f, 1f, 0.14f);
                t.Success = new Color(0.32f, 0.74f, 0.42f);
                t.Warning = new Color(0.94f, 0.72f, 0.24f);
                t.Error = new Color(0.92f, 0.36f, 0.36f);
            }

            bool dark = t.Panel.grayscale < 0.5f;
            t.PanelAlt = Shade(t.Panel, dark ? 1.18f : 0.94f);
            t.BottomBar = t.TopBar;
            t.Button = Shade(t.Panel, dark ? 1.28f : 0.90f);
            t.ButtonHover = Shade(t.Button, dark ? 1.18f : 1.10f);
            t.ButtonActive = t.Accent;
            t.TextMuted = new Color(t.Text.r, t.Text.g, t.Text.b, 0.62f);
            t._source = theme;
            return t;
        }

        public bool Matches(ThemeManager theme) => ReferenceEquals(_source, theme);

        private static Color ToColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return MeshBuilder.ToUnity(ColorRgba.FromHex(hex));
        }

        private static Color Shade(Color c, float f)
        {
            return new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);
        }
    }
}
