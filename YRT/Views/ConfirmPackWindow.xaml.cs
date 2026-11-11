using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace YRT.Views;

/// <summary>
/// 打包前确认窗口：再次展示并允许临时修改 包名称 / 简介 / 图标，
/// 仅作用于本次生成的 zip，不回写源目录。
/// </summary>
public partial class ConfirmPackWindow : Window, INotifyPropertyChanged
{
    private string _packName = "";
    private string _description = "";
    private string _iconPath = "";

    public ConfirmPackWindow(string packName, string description, string iconPath)
    {
        InitializeComponent();
        _packName = packName;
        _description = description;
        _iconPath = iconPath;
        DataContext = this;
    }

    public string PackName { get => _packName; set { _packName = value; OnP(); } }
    public string Description { get => _description; set { _description = value; OnP(); } }
    public string IconPath
    {
        get => _iconPath;
        set { _iconPath = value; OnP(); OnP(nameof(IconPreview)); }
    }

    /// <summary>图标路径对应的预览（无效路径时为空）。</summary>
    public BitmapImage? IconPreview => LoadPreview(_iconPath);

    private static BitmapImage? LoadPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp") return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择包图标（建议 128×128 以上的 png）",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
        };
        if (dlg.ShowDialog(this) == true) IconPath = dlg.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
