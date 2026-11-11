using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using YRT.Core;
using YRT.Mvvm;

namespace YRT.ViewModels;

/// <summary>
/// 生成页视图模型（移植自 RustPackGen 页面1「材质包制作」）：
/// 目录 ↔ zip 互转，打包条目强制正斜杠。
/// </summary>
public class GenerateViewModel : ObservableObject
{
    private string _srcDir = "";
    private string _packName = "";
    private string _outDir = DefaultOutDir();
    private string _lastOut = "尚未生成";
    private string _overrideDescription = "";
    private string _iconPath = "";
    public ObservableCollection<string> Logs { get; } = new()
    {
        "欢迎使用生成页。把材质目录打包为 Minecraft 可识别的 zip（条目路径强制正斜杠）；也可解包现有 zip 进行编辑。",
    };

    public string SrcDir { get => _srcDir; set => Set(ref _srcDir, value); }
    public string PackName { get => _packName; set => Set(ref _packName, value); }
    public string OutDir { get => _outDir; set => Set(ref _outDir, value); }
    public string LastOut { get => _lastOut; private set => Set(ref _lastOut, value); }

    /// <summary>临时简介：仅写入本次生成的 zip 内 pack.mcmeta，不改动源目录。</summary>
    public string OverrideDescription { get => _overrideDescription; set => Set(ref _overrideDescription, value); }

    /// <summary>临时图标（pack.png）路径：仅写入本次生成的 zip，不改动源目录。</summary>
    public string IconPath { get => _iconPath; set => Set(ref _iconPath, value); }

    /// <summary>打包时目录缺 pack.mcmeta 则在 zip 内自动补齐（写入设置）</summary>
    public bool AutoMcmeta
    {
        get => AppSettings.Instance.AutoMcmeta;
        set { AppSettings.Instance.AutoMcmeta = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    public RelayCommand BrowseSrcCommand { get; }
    public RelayCommand BrowseOutCommand { get; }
    public RelayCommand BrowseIconCommand { get; }
    public RelayCommand PackCommand { get; }
    public RelayCommand UnpackCommand { get; }
    public RelayCommand OpenOutDirCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public GenerateViewModel()
    {
        BrowseSrcCommand = new RelayCommand(_ => BrowseSrc());
        BrowseOutCommand = new RelayCommand(_ => BrowseOut());
        BrowseIconCommand = new RelayCommand(_ => BrowseIcon());
        PackCommand = new RelayCommand(_ => DoPack());
        UnpackCommand = new RelayCommand(_ => DoUnpack());
        OpenOutDirCommand = new RelayCommand(_ => OpenOutDir());
        ClearLogCommand = new RelayCommand(_ => Logs.Clear());
    }

    private void BrowseIcon()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择包图标（建议 128×128 以上的 png）",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
        };
        if (dlg.ShowDialog() == true) IconPath = dlg.FileName;
    }

    private static string DefaultOutDir()
    {
        // 设置里配置了默认输出目录则优先；否则用 exe 同级 output\
        var configured = AppSettings.Instance.DefaultOutDir.Trim();
        return configured.Length > 0
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "output");
    }

    /// <summary>按设置里的包名模板生成包名：{name} 替换为源目录名；模板为空时直接用源目录名。</summary>
    private string ApplyNameTemplate(string dirName)
    {
        var tpl = AppSettings.Instance.PackNameTemplate.Trim();
        return tpl.Length == 0 ? dirName : tpl.Replace("{name}", dirName);
    }

    private void Log(string s) => Logs.Add(s);

    private void BrowseSrc()
    {
        var dlg = new OpenFolderDialog { Title = "选择源目录" };
        if (dlg.ShowDialog() != true) return;
        SrcDir = dlg.FolderName;
        // 与 Rust 版一致：选目录后自动带出包名（套用设置中的包名模板）
        var dirName = Path.GetFileName(SrcDir.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        PackName = ApplyNameTemplate(dirName);
    }

    private void BrowseOut()
    {
        var dlg = new OpenFolderDialog { Title = "选择输出目录" };
        if (dlg.ShowDialog() == true) OutDir = dlg.FolderName;
    }

    private void DoPack()
    {
        if (string.IsNullOrWhiteSpace(SrcDir))
        {
            Log("[!] 请先选择源目录");
            return;
        }
        if (!Directory.Exists(SrcDir))
        {
            Log($"[!] 源目录不存在: {SrcDir}");
            return;
        }
        var name = string.IsNullOrWhiteSpace(PackName)
            ? ApplyNameTemplate(Path.GetFileName(SrcDir.TrimEnd(Path.DirectorySeparatorChar)) ?? "")
            : PackName.Trim();

        // 生成前确认：再次展示 名称 / 简介 / 图标，可临时修改（只影响本次 zip）
        var confirm = new YRT.Views.ConfirmPackWindow(name, OverrideDescription, IconPath)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        if (confirm.ShowDialog() != true)
        {
            Log("[i] 已取消打包");
            return;
        }
        name = string.IsNullOrWhiteSpace(confirm.PackName) ? name : confirm.PackName.Trim();
        var desc = confirm.Description?.Trim() ?? "";
        var icon = confirm.IconPath?.Trim() ?? "";
        if (icon.Length > 0 && !File.Exists(icon))
        {
            Log("[!] 图标文件不存在: " + icon);
            return;
        }

        var outZip = Path.Combine(OutDir.Trim(), name + ".zip");
        try
        {
            var auto = AppSettings.Instance.AutoMcmeta;
            var n = PackPacker.PackDir(SrcDir, outZip, autoMcmeta: auto, packName: name,
                description: desc.Length > 0 ? desc : null,
                iconPath: icon.Length > 0 ? icon : null);
            OverrideDescription = desc;
            IconPath = icon;
            LastOut = outZip;
            var notes = new List<string>();
            var note = auto && !File.Exists(Path.Combine(SrcDir, "pack.mcmeta"))
                ? "，已自动补齐 pack.mcmeta（可在设置里关闭）" : "";
            if (note.Length > 0) notes.Add(note.TrimStart('，'));
            if (desc.Length > 0) notes.Add("已写入临时简介");
            if (icon.Length > 0) notes.Add("已写入临时图标");
            Log($"[√] 打包完成: {outZip}（{n} 个文件, {DirSizeMb(SrcDir):F1} MB{string.Join("；", notes)}）");
        }
        catch (Exception ex)
        {
            Log($"[!] 打包失败: {ex.Message}");
        }
    }

    private void DoUnpack() => UnpackZip(null);

    /// <summary>拖拽 / 按钮共用入口：zipPath 为空时弹文件选择框。</summary>
    private void UnpackZip(string? zipPath)
    {
        if (string.IsNullOrEmpty(zipPath))
        {
            var pick = new OpenFileDialog
            {
                Title = "选择要解包的资源包压缩档",
                Filter = "资源包 (*.zip;*.mcpack)|*.zip;*.mcpack|所有文件|*.*",
            };
            if (pick.ShowDialog() != true) return;
            zipPath = pick.FileName;
        }
        else if (!File.Exists(zipPath))
        {
            Log("[!] 拖入的文件不存在: " + zipPath);
            return;
        }

        var pickDest = new OpenFolderDialog { Title = "选择解包目标目录（会新建以 zip 命名的子目录）" };
        if (pickDest.ShowDialog() != true) return;

        var name = Path.GetFileNameWithoutExtension(zipPath);
        var dest = Path.Combine(pickDest.FolderName, name);
        try
        {
            var n = PackPacker.Unzip(zipPath, dest);
            Log($"[√] 解包完成: {dest}（{n} 个文件）");
        }
        catch (Exception ex)
        {
            Log($"[!] 解包失败: {ex.Message}");
        }
    }

    /// <summary>拖拽入口：目录 → 填入材质目录并带出包名；zip/mcpack → 直接进入解包流程。</summary>
    public void HandleDrop(string path)
    {
        if (Directory.Exists(path))
        {
            SrcDir = path;
            var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
            PackName = ApplyNameTemplate(dirName);
            Log($"[√] 已拖入材质目录：{path}");
        }
        else if (PackPacker.IsPackArchive(path))
        {
            Log("[i] 检测到拖入压缩档，继续选择解包目标目录…");
            UnpackZip(path);
        }
        else
        {
            Log("[!] 拖入的既不是目录也不是 zip/mcpack：" + path);
        }
    }

    private void OpenOutDir()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", OutDir.Trim());
        }
        catch (Exception ex)
        {
            Log($"[!] 打开目录失败: {ex.Message}");
        }
    }

    private static double DirSizeMb(string dir)
    {
        long size = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { size += new FileInfo(f).Length; } catch { /* 跳过被占用的文件 */ }
        }
        return size / 1048576.0;
    }
}
