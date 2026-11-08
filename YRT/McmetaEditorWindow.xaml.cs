using System.Windows;
using YRT.Core;
using YRT.Mvvm;

namespace YRT;

/// <summary>
/// pack.mcmeta 可视化编辑对话框：编辑描述与格式版本，实时预览 JSON，
/// 保存时直接写回 zip（替换条目）或目录（覆盖文件）。
/// </summary>
public partial class McmetaEditorWindow : Window
{
    private readonly string _packPath;

    public McmetaEditorViewModel VM { get; }

    public McmetaEditorWindow(string packPath)
    {
        InitializeComponent();
        _packPath = packPath;
        VM = new McmetaEditorViewModel(PackMeta.Read(packPath));
        DataContext = VM;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!VM.TryGetFormat(out var fmt))
        {
            MessageBox.Show("pack_format 需要是非负整数", "无法保存",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var target = PackMeta.Write(_packPath, VM.Description.Trim(), fmt);
            MessageBox.Show("已写回：" + target, "保存成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("写回失败：" + ex.Message, "保存失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

/// <summary>编辑器视图模型：描述 / 格式版本 / 实时 JSON 预览。</summary>
public class McmetaEditorViewModel : ObservableObject
{
    private string _description;
    private string _packFormatText;
    private readonly string _raw;

    public McmetaEditorViewModel(McmetaInfo info)
    {
        _description = info.Description;
        _packFormatText = info.FormatValid ? info.PackFormat.ToString() : "34";
        _raw = info.Raw;
    }

    public string Description { get => _description; set { _description = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); } }
    public string PackFormatText { get => _packFormatText; set { _packFormatText = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); } }

    /// <summary>格式版本输入是否有效。</summary>
    public bool TryGetFormat(out int fmt) => int.TryParse(PackFormatText.Trim(), out fmt) && fmt >= 0;

    public string Preview
    {
        get
        {
            if (!TryGetFormat(out var fmt)) return "（pack_format 无效，需为非负整数）";
            return PackMeta.BuildJson(Description, fmt);
        }
    }

    /// <summary>打开前包里没有 mcmeta 时的提示。</summary>
    public string FormatHint => _raw.Length == 0
        ? "该包原本没有 pack.mcmeta，保存后将自动创建。"
        : "";
}
