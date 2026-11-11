using System.Windows.Media;

namespace YRT.Theme;

/// <summary>
/// 主题管理：运行时替换资源色，所有 DynamicResource 引用处自动联动。
/// - 主题色：Green/Blue/Purple/Amber 预设 + Custom 自定义（hover/pressed/soft 自动派生）
/// - 深浅色模式：Light/Dark 两套中性色板
/// - 对比度：按主色亮度自动计算主色上的前景色（浅色→黑字，深色→白字）
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, (string Primary, string Hover, string Pressed)> Themes = new()
    {
        ["Green"] = ("#3D8B5F", "#4FA371", "#347750"),
        ["Blue"] = ("#3D7EA8", "#4F96C2", "#356A8D"),
        ["Purple"] = ("#7B5EA7", "#9174BF", "#695091"),
        ["Amber"] = ("#B07D3A", "#C49350", "#976C31"),
    };

    // 深浅色两套中性色板：背景/卡片/输入框/轨道/文字/次文字/分割线/控制台/悬停/按下/滚动条滑块
    private static readonly (string Bg, string Card, string Field, string Track, string Text, string SubText, string Divider, string Console, string Hover, string Pressed, string Thumb, string ThumbHover) LightPalette =
        ("#F4F4F1", "#FFFFFF", "#F7F7F4", "#ECECE6", "#2B2B28", "#8A8A84", "#E3E3DD", "#FBFBF9", "#12000000", "#1E000000", "#28000000", "#48000000");

    private static readonly (string Bg, string Card, string Field, string Track, string Text, string SubText, string Divider, string Console, string Hover, string Pressed, string Thumb, string ThumbHover) DarkPalette =
        ("#1F1F23", "#2A2A30", "#232327", "#17171A", "#EAEAE6", "#9A9AA2", "#3A3A42", "#26262B", "#14FFFFFF", "#24FFFFFF", "#30FFFFFF", "#55FFFFFF");

    private static bool _isDark;
    private static Color _primary = Color.FromRgb(0x3D, 0x8B, 0x5F);
    private static Color _pressed = Color.FromRgb(0x34, 0x77, 0x50);

    /// <summary>当前是否深色模式。</summary>
    public static bool IsDark => _isDark;

    /// <summary>应用指定主题（键不存在时保持现状；Custom 走自定义色）。</summary>
    public static void Apply(string key)
    {
        if (key == "Custom")
        {
            var hex = Core.AppSettings.Instance.CustomThemeColor;
            if (TryParseHex(hex, out var c)) { ApplyCustom(c); return; }
            Apply("Green"); // 自定义色无效时回退
            return;
        }
        if (!Themes.TryGetValue(key, out var t)) return;
        ApplyPrimary(Parse(t.Primary), Parse(t.Hover), Parse(t.Pressed));
    }

    /// <summary>应用自定义主题色（自动派生 hover/pressed/soft）。</summary>
    public static void ApplyCustom(Color primary)
    {
        var hover = Lighten(primary, 0.12);
        var pressed = Darken(primary, 0.12);
        ApplyPrimary(primary, hover, pressed);
    }

    /// <summary>切换深浅色模式（"Light" / "Dark"）：直接替换中性笔刷实例。</summary>
    public static void ApplyMode(string mode)
    {
        _isDark = mode == "Dark";
        var p = _isDark ? DarkPalette : LightPalette;
        SetBrush("BackgroundBrush", p.Bg);
        SetBrush("CardBrush", p.Card);
        SetBrush("FieldBrush", p.Field);
        SetBrush("TrackBrush", p.Track);
        SetBrush("TextBrush", p.Text);
        SetBrush("SubTextBrush", p.SubText);
        SetBrush("DividerBrush", p.Divider);
        SetBrush("ConsoleBrush", p.Console);
        SetBrush("HoverBrush", p.Hover);
        SetBrush("PressedBrush", p.Pressed);
        SetBrush("ThumbBrush", p.Thumb);
        SetBrush("ThumbHoverBrush", p.ThumbHover);
        // 浅底色跟随模式重新派生（深色下与深色卡片混合）
        DeriveSoftAndContrast();
    }

    private static void SetBrush(string key, string hex)
    {
        SetBrush(key, Parse(hex));
    }

    private static void SetBrush(string key, Color c)
    {
        var res = System.Windows.Application.Current.Resources;
        res[key] = new SolidColorBrush(c);
        res[key.Replace("Brush", "Color")] = c;
    }

    /// <summary>解析 #RGB / #RRGGBB。</summary>
    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return false;
            color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            return true;
        }
        catch { return false; }
    }

    private static Color Parse(string hex) { TryParseHex(hex, out var c); return c; }

    public static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    public static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    /// <summary>应用启动时恢复上次选择的主题与深浅色模式。</summary>
    public static void ApplySaved()
    {
        ApplyMode(Core.AppSettings.Instance.ThemeMode);
        Apply(Core.AppSettings.Instance.Theme);
    }

    private static void ApplyPrimary(Color primary, Color hover, Color pressed)
    {
        _primary = primary;
        _pressed = pressed;
        // 直接替换笔刷实例（DynamicResource 引用处自动刷新）
        SetBrush("PrimaryBrush", primary);
        SetBrush("PrimaryHoverBrush", hover);
        SetBrush("PrimaryPressedBrush", pressed);
        DeriveSoftAndContrast();
    }

    /// <summary>按当前主色与深浅模式派生浅底色 + 主色上的前景/覆盖层（保证对比度）。</summary>
    private static void DeriveSoftAndContrast()
    {
        // 浅底色：浅色模式向白混合，深色模式向卡片色混合
        var mixTo = _isDark ? Parse("#2A2A30") : Colors.White;
        const double mix = 0.82;
        var soft = Color.FromRgb(
            (byte)(_primary.R + (mixTo.R - _primary.R) * mix),
            (byte)(_primary.G + (mixTo.G - _primary.G) * mix),
            (byte)(_primary.B + (mixTo.B - _primary.B) * mix));
        SetBrush("PrimarySoftBrush", soft);

        // 主色上的文字：亮 → 黑字，暗 → 白字
        bool darkText = Luminance(_primary) > 0.62;
        var on = darkText ? Colors.Black : Colors.White;
        SetBrush("OnPrimaryBrush", on);
        SetBrush("OnPrimarySubBrush", Color.FromArgb(0xD8, on.R, on.G, on.B));

        // Hero 按钮覆盖层：白字场景用白叠加，黑字场景用黑叠加
        if (darkText)
        {
            SetBrush("OverlaySoftBrush", Color.FromArgb(0x1E, 0, 0, 0));
            SetBrush("OverlayHoverBrush", Color.FromArgb(0x30, 0, 0, 0));
            SetBrush("OverlayPressedBrush", Color.FromArgb(0x10, 0, 0, 0));
        }
        else
        {
            SetBrush("OverlaySoftBrush", Color.FromArgb(0x22, 255, 255, 255));
            SetBrush("OverlayHoverBrush", Color.FromArgb(0x38, 255, 255, 255));
            SetBrush("OverlayPressedBrush", Color.FromArgb(0x12, 255, 255, 255));
        }
    }

    /// <summary>相对亮度（0-1，Rec.601 加权）。</summary>
    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>给定底色上应使用的可读前景色（黑或白）。</summary>
    public static Color OnColor(Color background) => Luminance(background) > 0.62 ? Colors.Black : Colors.White;
}
