using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using YRT.Core;
using YRT.Mvvm;

namespace YRT.ViewModels;

/// <summary>游戏目录里扫描到的资源包（编辑页一键导入用）。</summary>
public sealed class GamePackItem
{
    public string FullPath { get; init; } = "";
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";

    public override string ToString() => Name;
}

/// <summary>
/// 编辑页视图模型：导入资源包（zip/目录/游戏扫描）→ 工作副本，
/// 浏览与编辑包内文件（重命名/删除/替换/添加），编辑 pack.mcmeta，整包导出。
/// </summary>
public class EditViewModel : ObservableObject
{
    // ---------------- 包信息与列表 ----------------

    private string _sourcePath = "";
    public string SourcePath { get => _sourcePath; private set => Set(ref _sourcePath, value); }

    private string _packName = "";
    public string PackName { get => _packName; private set => Set(ref _packName, value); }

    private string _description = "";
    public string Description { get => _description; private set => Set(ref _description, value); }

    private string _formatText = "";
    public string FormatText { get => _formatText; private set => Set(ref _formatText, value); }

    private string _stats = "";
    public string Stats { get => _stats; private set => Set(ref _stats, value); }

    public bool HasPack => PackName.Length > 0;

    public ObservableCollection<EditFileInfo> Items { get; } = new();

    private EditFileInfo? _selected;
    public EditFileInfo? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) LoadPreview(); }
    }

    private string _filter = "";
    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) RefreshItems(); }
    }

    /// <summary>文件分类筛选（全部/贴图/音效/模型/动画/字体/语言/信息/其他）。</summary>
    public string[] Categories { get; } = { "全部", "贴图", "音效", "模型", "动画", "字体", "语言", "信息", "其他" };

    private int _selectedCategory;
    public int SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) RefreshItems(); }
    }

    /// <summary>选中文件是图片时显示预览（BitmapImage，冻结跨线程安全）。</summary>
    private System.Windows.Media.Imaging.BitmapImage? _previewImage;
    public System.Windows.Media.Imaging.BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set
        {
            Set(ref _previewImage, value);
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public bool HasPreview => PreviewImage is not null;

    private string _workDir = "";
    private string _packRoot = "";

    // ---------------- 游戏扫描 ----------------

    public ObservableCollection<GamePackItem> GamePacks { get; } = new();

    private GamePackItem? _selectedGamePack;
    public GamePackItem? SelectedGamePack { get => _selectedGamePack; set => Set(ref _selectedGamePack, value); }

    private bool _scanning;
    public bool Scanning { get => _scanning; private set => Set(ref _scanning, value); }

    // ---------------- 日志 ----------------

    public ObservableCollection<string> Logs { get; } = new()
    {
        "欢迎使用编辑页。把资源包 zip / mcpack / 目录拖进本页，或点「扫描游戏」导入已安装的材质包；左侧可浏览文件、右侧编辑包详情，改完整包导出。",
    };

    private void Log(string s) => Logs.Add(s);

    // ---------------- 命令 ----------------

    public RelayCommand BrowseCommand { get; }
    public RelayCommand ScanGameCommand { get; }
    public RelayCommand ImportGamePackCommand { get; }
    public RelayCommand ClosePackCommand { get; }
    public RelayCommand EditMetaCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand AddFileCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand OpenDirCommand { get; }
    public RelayCommand OpenFileCommand { get; }

    public EditViewModel()
    {
        BrowseCommand = new RelayCommand(_ => BrowseImport());
        ScanGameCommand = new RelayCommand(_ => _ = ScanGameAsync());
        ImportGamePackCommand = new RelayCommand(_ => ImportGamePack());
        ClosePackCommand = new RelayCommand(_ => ClosePack());
        EditMetaCommand = new RelayCommand(_ => EditMeta());
        RenameCommand = new RelayCommand(_ => DoRename(), _ => Selected is not null);
        DeleteCommand = new RelayCommand(_ => DoDelete(), _ => Selected is not null);
        ReplaceCommand = new RelayCommand(_ => DoReplace(), _ => Selected is not null);
        AddFileCommand = new RelayCommand(_ => DoAddFile(), _ => HasPack);
        ExportCommand = new RelayCommand(_ => DoExport(), _ => HasPack);
        OpenDirCommand = new RelayCommand(_ => OpenWorkDir(), _ => HasPack);
        OpenFileCommand = new RelayCommand(_ => DoOpenFile(), _ => Selected is not null);

        PackEditor.CleanStale();
    }

    // ---------------- 导入 ----------------

    private void BrowseImport()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择资源包（zip / mcpack）",
            Filter = "资源包 (*.zip;*.mcpack)|*.zip;*.mcpack|所有文件|*.*",
        };
        if (dlg.ShowDialog() == true) Import(dlg.FileName);
    }

    private async Task ScanGameAsync()
    {
        if (Scanning) return;
        Scanning = true;
        Log("[i] 正在扫描游戏目录里的资源包…");
        try
        {
            var opts = new JavaScanOptions
            {
                AllDrives = AppSettings.Instance.ScanScope == "AllDrives",
                MaxDepth = Math.Clamp(AppSettings.Instance.ScanDepth, 1, 8),
            };
            var dirs = await Task.Run(() => LauncherScanner.ScanJava(opts));
            GamePacks.Clear();
            foreach (var d in dirs)
                foreach (var z in d.Zips)
                    GamePacks.Add(new GamePackItem
                    {
                        FullPath = Path.Combine(d.Dir, z),
                        Name = z,
                        Source = d.Source,
                    });
            Log(GamePacks.Count > 0
                ? $"[√] 找到 {GamePacks.Count} 个资源包，选中后点「导入」开始编辑"
                : "[!] 没有找到资源包 zip（可手动浏览选择，或把 zip 拖进本页）");
        }
        catch (Exception ex)
        {
            Log("[!] 扫描失败：" + ex.Message);
        }
        finally
        {
            Scanning = false;
        }
    }

    private void ImportGamePack()
    {
        var p = SelectedGamePack?.FullPath;
        if (p is null) { Log("[!] 请先在游戏资源包列表中选中一个 zip"); return; }
        Import(p);
    }

    /// <summary>导入入口（浏览 / 扫描 / 拖拽共用）。</summary>
    public void HandleDrop(string path)
    {
        if (Directory.Exists(path) || PackPacker.IsPackArchive(path))
            Import(path);
        else
            Log("[!] 请拖入资源包 zip / mcpack / 目录：" + path);
    }

    private void Import(string path)
    {
        try
        {
            Log("[i] 正在导入：" + path);
            (_workDir, _packRoot) = PackEditor.Import(path);
            SourcePath = path;
            PackName = Path.GetFileName(_packRoot);
            RefreshItems();
            LoadMeta();
            Log($"[√] 导入完成（共 {Items.Count} 个文件），可开始编辑");
        }
        catch (Exception ex)
        {
            Log("[!] 导入失败：" + ex.Message);
        }
    }

    private void ClosePack()
    {
        _workDir = _packRoot = "";
        SourcePath = PackName = Description = FormatText = Stats = "";
        Items.Clear();
        PreviewImage = null;
        OnPropertyChanged(nameof(HasPack));
    }

    private void RefreshItems()
    {
        Items.Clear();
        if (_packRoot.Length == 0) { PreviewImage = null; OnPropertyChanged(nameof(HasPack)); return; }
        IEnumerable<EditFileInfo> files = PackEditor.ListFiles(_packRoot);
        if (SelectedCategory > 0 && SelectedCategory - 1 < Categories.Length - 1)
        {
            var cat = Categories[SelectedCategory];
            files = files.Where(x => x.Category == cat);
        }
        if (Filter.Trim().Length > 0)
        {
            var f = Filter.Trim();
            files = files.Where(x => x.RelPath.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        var list = files.ToList();
        foreach (var x in list) Items.Add(x);
        Stats = $"{list.Count} 个文件 · {list.Sum(x => x.Size) / 1024.0 / 1024.0:0.00} MB";
        OnPropertyChanged(nameof(HasPack));
    }

    /// <summary>选中文件是图片时加载预览（从磁盘解码，冻结后供 UI 使用）。</summary>
    private void LoadPreview()
    {
        PreviewImage = null;
        var item = _selected;
        if (item is null || _packRoot.Length == 0) return;
        var ext = Path.GetExtension(item.RelPath).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".gif") return;
        try
        {
            var full = Path.Combine(_packRoot, item.RelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(full, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            PreviewImage = bmp;
        }
        catch
        {
            PreviewImage = null; // 图片损坏不阻塞编辑
        }
    }

    /// <summary>用系统默认程序打开选中文件（工作副本内）。</summary>
    private void DoOpenFile()
    {
        var item = Selected;
        if (item is null || _packRoot.Length == 0) return;
        try
        {
            var full = Path.Combine(_packRoot, item.RelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) { Log("[!] 文件不存在：" + item.RelPath); return; }
            Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
        }
        catch (Exception ex) { Log("[!] 打开失败：" + ex.Message); }
    }

    private void LoadMeta()
    {
        if (_packRoot.Length == 0) return;
        var meta = PackMeta.Read(_packRoot);
        Description = meta.Exists ? meta.Description : "（无 pack.mcmeta）";
        FormatText = meta.Exists && meta.FormatValid ? meta.PackFormat.ToString() : "-";
    }

    // ---------------- 文件操作 ----------------

    private void DoRename()
    {
        var item = Selected;
        if (item is null) return;
        var newName = Views.InputWindow.Show("重命名文件", $"原路径：{item.RelPath}", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        try
        {
            PackEditor.RenameFile(_packRoot, item.RelPath, newName);
            Log($"[√] 已重命名：{item.Name} → {newName.Trim()}");
            RefreshItems();
        }
        catch (Exception ex) { Log("[!] 重命名失败：" + ex.Message); }
    }

    private void DoDelete()
    {
        var item = Selected;
        if (item is null) return;
        var ok = MessageBox.Show($"确定删除 {item.RelPath} 吗？（工作副本内删除，导出后生效）",
            "删除文件", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;
        try
        {
            PackEditor.DeleteFile(_packRoot, item.RelPath);
            Log($"[√] 已删除：{item.RelPath}");
            RefreshItems();
        }
        catch (Exception ex) { Log("[!] 删除失败：" + ex.Message); }
    }

    private void DoReplace()
    {
        var item = Selected;
        if (item is null) return;
        var dlg = new OpenFileDialog { Title = "选择替换用文件" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            PackEditor.ReplaceFile(_packRoot, item.RelPath, dlg.FileName);
            Log($"[√] 已替换：{item.RelPath}");
            RefreshItems();
        }
        catch (Exception ex) { Log("[!] 替换失败：" + ex.Message); }
    }

    private void DoAddFile()
    {
        var dlg = new OpenFileDialog
        { Title = "选择要添加的文件", Filter = "所有文件|*.*|图片 (*.png)|*.png|声音 (*.ogg)|*.ogg" };
        if (dlg.ShowDialog() != true) return;
        var sub = Views.InputWindow.Show("添加到子目录",
            "输入包内目标子目录（留空 = 包根，例如 assets/minecraft/textures/item）", "");
        if (sub is null) return;
        try
        {
            PackEditor.AddFile(_packRoot, dlg.FileName, sub.Trim());
            Log($"[√] 已添加：{dlg.FileName}");
            RefreshItems();
        }
        catch (Exception ex) { Log("[!] 添加失败：" + ex.Message); }
    }

    private void EditMeta()
    {
        if (!HasPack) return;
        var win = new McmetaEditorWindow(_packRoot) { Owner = System.Windows.Application.Current.MainWindow };
        win.ShowDialog();
        if (win.DialogResult == true) LoadMeta();
    }

    private void DoExport()
    {
        if (!HasPack) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出资源包",
            Filter = "资源包 zip|*.zip",
            FileName = (string.IsNullOrWhiteSpace(PackName) ? "resourcepack" : PackName) + ".zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            PackEditor.Export(_packRoot, dlg.FileName, autoMcmeta: false, packName: PackName);
            Log($"[√] 已导出：{dlg.FileName}");
            if (MessageBox.Show("导出完成！要打开所在文件夹吗？", "导出资源包",
                    MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK)
                Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
        }
        catch (Exception ex) { Log("[!] 导出失败：" + ex.Message); }
    }

    private void OpenWorkDir()
    {
        if (_workDir.Length == 0) return;
        try { Process.Start("explorer.exe", _workDir); }
        catch { /* 打开失败忽略 */ }
    }
}
