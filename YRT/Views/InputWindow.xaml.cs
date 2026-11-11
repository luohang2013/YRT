using System.Windows;
using YRT.Mvvm;

namespace YRT.Views;

/// <summary>通用单行输入对话框（重命名等场景复用）。Show 返回 null = 取消。</summary>
public partial class InputWindow : Window
{
    public InputViewModel VM { get; }

    private InputWindow(string title, string message, string initial)
    {
        InitializeComponent();
        VM = new InputViewModel(title, message, initial);
        DataContext = VM;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>弹出输入框；返回输入文本（取消返回 null）。</summary>
    public static string? Show(string title, string message, string initial = "")
    {
        var win = new InputWindow(title, message, initial);
        var owner = Application.Current.MainWindow;
        if (owner is { IsLoaded: true }) win.Owner = owner;
        return win.ShowDialog() == true ? win.VM.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

public class InputViewModel : ObservableObject
{
    public string Title { get; }
    public string Message { get; }

    private string _value;
    public string Value { get => _value; set => Set(ref _value, value); }

    public InputViewModel(string title, string message, string initial)
    {
        Title = title;
        Message = message;
        _value = initial;
    }
}
