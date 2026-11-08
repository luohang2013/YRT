using System.Windows;

namespace YRT.Pages;

/// <summary>
/// 各页面共用的拖拽辅助：从 DragEventArgs 提取文件/目录路径，
/// 调用对应 ViewModel 的 HandleDrop。
/// </summary>
public static class DropHelper
{
    /// <summary>提取拖入的本地路径列表（非文件拖拽返回空）。</summary>
    public static List<string> GetPaths(DragEventArgs e)
    {
        var result = new List<string>();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                result.AddRange(paths.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
        return result;
    }

    public static void GeneratePage_Drop(object sender, DragEventArgs e)
    {
        var paths = GetPaths(e);
        if (paths.Count == 0) return;
        if (sender is System.Windows.Controls.UserControl { DataContext: ViewModels.GenerateViewModel vm })
            vm.HandleDrop(paths[0]);
        e.Handled = true;
    }

    public static void PackManagerPage_Drop(object sender, DragEventArgs e)
    {
        var paths = GetPaths(e);
        if (paths.Count == 0) return;
        if (sender is System.Windows.Controls.UserControl { DataContext: ViewModels.PackManagerViewModel vm })
            vm.HandleDrop(paths);
        e.Handled = true;
    }

    public static void AnalyzePage_Drop(object sender, DragEventArgs e)
    {
        var paths = GetPaths(e);
        if (paths.Count == 0) return;
        if (sender is System.Windows.Controls.UserControl { DataContext: ViewModels.AnalyzeViewModel vm })
            vm.HandleDrop(paths[0]);
        e.Handled = true;
    }

    public static void InstallPage_Drop(object sender, DragEventArgs e)
    {
        var paths = GetPaths(e);
        if (paths.Count == 0) return;
        if (sender is System.Windows.Controls.UserControl { DataContext: ViewModels.InstallViewModel vm })
            vm.HandleDrop(paths[0]);
        e.Handled = true;
    }

    // ---------------- 拖拽视觉反馈（DropOverlay） ----------------

    /// <summary>DragEnter：带文件拖入时显示虚线主题色覆盖层。</summary>
    public static void Page_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) ShowOverlay(sender);
    }

    /// <summary>DragLeave：拖出页面时隐藏覆盖层。</summary>
    public static void Page_DragLeave(object sender, DragEventArgs e) => HideOverlay(sender);

    /// <summary>Drop 结束后隐藏覆盖层（在各页 Drop 处理器内统一调用）。</summary>
    public static void Page_DropDone(object sender, DragEventArgs e) => HideOverlay(sender);

    public static void ShowOverlay(object sender)
    {
        if (sender is System.Windows.Controls.UserControl uc &&
            uc.FindName("DropOverlay") is System.Windows.Controls.Border b)
            b.Visibility = Visibility.Visible;
    }

    public static void HideOverlay(object sender)
    {
        if (sender is System.Windows.Controls.UserControl uc &&
            uc.FindName("DropOverlay") is System.Windows.Controls.Border b)
            b.Visibility = Visibility.Collapsed;
    }
}

/// <summary>集合计数 → 可见性：Count==0 时 Visible（空状态提示用）。</summary>
public class EmptyToVisConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            _ => 1,
        };
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>false → Visible（空状态提示用）。</summary>
public class InverseBoolToVisConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 控制台日志着色：按 [√] / [!] / [i] 前缀返回成功红绿/警示红/提示蓝灰笔刷
/// （从 Application 资源取 DynamicResource 笔刷，随主题与深浅色联动）。
/// </summary>
public class LogBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var key = value is string s ? s switch
        {
            var t when t.StartsWith("[√]") => "SuccessBrush",
            var t when t.StartsWith("[!]") => "DangerBrush",
            var t when t.StartsWith("[i]") => "InfoBrush",
            _ => "SubTextBrush",
        } : "SubTextBrush";
        return System.Windows.Application.Current?.TryFindResource(key)
               ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
