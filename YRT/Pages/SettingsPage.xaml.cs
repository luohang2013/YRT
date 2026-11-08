using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using YRT.ViewModels;

namespace YRT.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }

    /// <summary>分类切换动画：右侧内容淡入 + 轻微上移。</summary>
    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
        };
        var shift = new System.Windows.Media.Animation.DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
        };
        ContentScroll.BeginAnimation(UIElement.OpacityProperty, fade);
        ContentShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, shift);
    }

    /// <summary>友情链接：用系统默认浏览器打开。</summary>
    private void FriendLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink h && h.CommandParameter is string url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* 打开失败忽略 */ }
        }
    }
}

/// <summary>#RRGGBB → SolidColorBrush（非法值返回透明）。</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        try
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Trim()));
        }
        catch { /* fall through */ }
        return new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)); // 未设置自定义色时的中性兜底
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>#RRGGBB → 黑/白前景（按底色亮度保证可读）。</summary>
public class HexToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        try
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s) &&
                Theme.ThemeManager.TryParseHex(s.Trim(), out var c))
                return new SolidColorBrush(Theme.ThemeManager.OnColor(c));
        }
        catch { /* fall through */ }
        return Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>设置分类名 → Segoe MDL2 图标字符（外观/安装/生成/扫描/关于）。</summary>
public class CategoryIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            "外观" => "\uE790",
            "安装" => "\uE896",
            "生成" => "\uE8B7",
            "扫描" => "\uE721",
            "更新" => "\uE777",
            "关于" => "\uE946",
            _ => "\uE7C3",
        };

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
