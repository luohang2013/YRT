using System.Windows.Controls;
using System.Windows.Data;

namespace YRT.Pages;

public partial class AnalyzePage : UserControl
{
    public AnalyzePage()
    {
        InitializeComponent();
        DataContext = new ViewModels.AnalyzeViewModel();
    }

    private void Page_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropHelper.AnalyzePage_Drop(sender, e);
        DropHelper.Page_DropDone(sender, e);
    }

    private void Page_DragEnter(object sender, System.Windows.DragEventArgs e) => DropHelper.Page_DragEnter(sender, e);
    private void Page_DragLeave(object sender, System.Windows.DragEventArgs e) => DropHelper.Page_DragLeave(sender, e);
}

/// <summary>占比 → 条形宽度（px）。ConverterParameter 为满条宽度。</summary>
public class RatioToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var ratio = value is double d ? Math.Clamp(d, 0, 1) : 0;
        var max = 260.0;
        if (parameter is string s && double.TryParse(s, out var p)) max = p;
        return Math.Max(2, ratio * max);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
