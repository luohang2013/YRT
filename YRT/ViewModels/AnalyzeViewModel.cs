using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using YRT.Core;
using YRT.Mvvm;

namespace YRT.ViewModels;

/// <summary>
/// 分析页视图模型（图六）：导入 zip/目录 → 解析 mcmeta、结构树、分类统计、诊断。
/// </summary>
public class AnalyzeViewModel : ObservableObject
{
    private string _srcPath = "";
    private PackAnalysis? _result;
    private bool _hasResult;
    private bool _busy;

    public ObservableCollection<string> Logs { get; } = new()
    {
        "欢迎使用分析页。导入资源包 zip 或目录后，将解析清单、目录结构、分类占用统计，并给出诊断结果。",
    };

    public string SrcPath { get => _srcPath; set => Set(ref _srcPath, value); }

    public PackAnalysis? Result { get => _result; private set { if (Set(ref _result, value)) HasResult = value is not null; } }

    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public RelayCommand BrowseZipCommand { get; }
    public RelayCommand BrowseDirCommand { get; }
    public RelayCommand AnalyzeCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    /// <summary>导出某分类内容为资源包 zip（参数：分类名）</summary>
    public RelayCommand ExportZipCommand { get; }
    /// <summary>导出某分类内容为 MC data 文件夹（参数：分类名）</summary>
    public RelayCommand ExportDirCommand { get; }
    /// <summary>可视化编辑 pack.mcmeta 并写回</summary>
    public RelayCommand EditMetaCommand { get; }
    /// <summary>把分析结果导出为 Markdown 报告</summary>
    public RelayCommand ExportReportCommand { get; }

    public AnalyzeViewModel()
    {
        BrowseZipCommand = new RelayCommand(_ => Browse(true));
        BrowseDirCommand = new RelayCommand(_ => Browse(false));
        AnalyzeCommand = new RelayCommand(_ => _ = DoAnalyzeAsync());
        OpenSourceCommand = new RelayCommand(_ => OpenSource());
        ClearLogCommand = new RelayCommand(_ => Logs.Clear());
        ExportZipCommand = new RelayCommand(p => ExportCategory(p as string, asZip: true));
        ExportDirCommand = new RelayCommand(p => ExportCategory(p as string, asZip: false));
        EditMetaCommand = new RelayCommand(_ => EditMeta());
        ExportReportCommand = new RelayCommand(_ => ExportReport());
    }

    private void Log(string s) => Logs.Add(s);

    private void Browse(bool wantZip)
    {
        if (wantZip)
        {
            var dlg = new OpenFileDialog { Title = "选择资源包压缩档", Filter = "资源包 (*.zip;*.mcpack)|*.zip;*.mcpack|所有文件|*.*" };
            if (dlg.ShowDialog() == true) SrcPath = dlg.FileName;
        }
        else
        {
            var dlg = new OpenFolderDialog { Title = "选择资源包目录" };
            if (dlg.ShowDialog() == true) SrcPath = dlg.FolderName;
        }
    }

    private async Task DoAnalyzeAsync()
    {
        var path = SrcPath.Trim();
        if (path.Length == 0) { Log("[!] 请先选择 zip 或目录"); return; }
        if (!File.Exists(path) && !Directory.Exists(path)) { Log("[!] 路径不存在：" + path); return; }

        Busy = true;
        Log("[i] 正在分析（大包可能需要几秒）…");
        try
        {
            var sw = Stopwatch.StartNew();
            var result = await Task.Run(() => PackAnalyzer.Analyze(path));
            Result = result;
            Log($"[√] 分析完成：{result.FileCount} 个文件，共 {PackAnalyzer.FmtBytes(result.TotalBytes)}，耗时 {sw.ElapsedMilliseconds} ms");
            foreach (var s in result.Stats)
                Log($"    · {s.Name}: {s.Count} 个 / {PackAnalyzer.FmtBytes(s.Bytes)} ({s.Ratio:P0})");
            Log($"[i] 诊断 {result.Diagnostics.Count(d => d.Level != "info")} 条警告/错误，详情见下方列表");
        }
        catch (Exception ex)
        {
            Result = null;
            Log("[!] 分析失败：" + ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private void OpenSource()
    {
        if (Result is null) return;
        try
        {
            if (Result.IsZip) System.Diagnostics.Process.Start("explorer.exe", "/select," + Result.Source);
            else System.Diagnostics.Process.Start("explorer.exe", Result.Source);
        }
        catch { /* 打开失败不影响 */ }
    }

    /// <summary>可视化编辑 pack.mcmeta：缺清单时也能用（保存即创建）。</summary>
    private void EditMeta()
    {
        var path = SrcPath.Trim();
        if (path.Length == 0) { Log("[!] 请先选择 zip 或目录"); return; }
        if (!File.Exists(path) && !Directory.Exists(path)) { Log("[!] 路径不存在：" + path); return; }

        var win = new McmetaEditorWindow(path) { Owner = System.Windows.Application.Current.MainWindow };
        Log(win.ShowDialog() == true
            ? $"[√] pack.mcmeta 已写回：{path}"
            : "[i] 已取消清单编辑");
        // 清单可能刚被创建/修复，刷新诊断提示
        if (win.DialogResult == true && HasResult) _ = DoAnalyzeAsync();
    }

    /// <summary>把当前分析结果导出为 Markdown 报告。</summary>
    private void ExportReport()
    {
        if (Result is null) { Log("[!] 请先分析一个资源包"); return; }
        var dlg = new SaveFileDialog
        {
            Title = "导出分析报告",
            FileName = Path.GetFileNameWithoutExtension(Result.Source) + "-分析报告.md",
            Filter = "Markdown 报告|*.md|文本文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, PackMeta.BuildReport(Result));
            Log($"[√] 报告已导出：{dlg.FileName}");
        }
        catch (Exception ex)
        {
            Log("[!] 导出失败：" + ex.Message);
        }
    }

    /// <summary>拖拽入口：zip/mcpack 或目录填入路径并立即分析。</summary>
    public void HandleDrop(string path)
    {
        if (!Directory.Exists(path) && !(File.Exists(path) && PackPacker.IsPackArchive(path)))
        {
            Log("[!] 拖入的既不是目录也不是 zip/mcpack：" + path);
            return;
        }
        SrcPath = path;
        Log("[√] 已拖入：" + path);
        _ = DoAnalyzeAsync();
    }

    /// <summary>
    /// 导出某分类的内容（图4扩展：把音效包等分类从资源包里抽出来）。
    /// asZip=true 保存为可直接安装的资源包 zip；
    /// asZip=false 输出为 MC data 文件夹（保持包内相对路径，可直接并入网易基岩 MC data / 资源包目录结构）。
    /// </summary>
    private async void ExportCategory(string? category, bool asZip)
    {
        if (Result is null) { Log("[!] 请先分析一个资源包"); return; }
        if (string.IsNullOrEmpty(category)) return;

        Busy = true;
        try
        {
            // 重新加载来源包以拿到分类条目（zip/目录都支持）
            var pack = await Task.Run(() => SourcePack.Load(SrcPath.Trim()));
            if (!pack.Categories.TryGetValue(category, out var entries) || entries.Count == 0)
            {
                Log($"[!] 该包中不存在分类「{category}」的内容");
                return;
            }

            var safeCat = string.Join("", category.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            if (asZip)
            {
                var dlg = new SaveFileDialog
                {
                    Title = $"导出「{category}」为资源包 zip",
                    FileName = $"{pack.Name}-{safeCat}.zip",
                    Filter = "资源包 zip|*.zip",
                };
                if (dlg.ShowDialog() != true) return;
                var outZip = dlg.FileName;
                await Task.Run(() =>
                {
                    using var fs = File.Create(outZip);
                    using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
                    var meta = zip.CreateEntry("pack.mcmeta", System.IO.Compression.CompressionLevel.Optimal);
                    using (var w = new StreamWriter(meta.Open()))
                        w.Write("{\"pack\":{\"pack_format\":34,\"description\":\"" + category + " 导出（YHCT 的资源包工具箱）\"}}");
                    foreach (var (rel, source) in entries)
                        SourcePack.CopyEntryPublic(zip, pack, rel, source);
                });
                Log($"[√] 已导出 {entries.Count} 个文件为 zip：{outZip}");
            }
            else
            {
                var dlg = new OpenFolderDialog { Title = $"导出「{category}」为 MC data 文件夹" };
                if (dlg.ShowDialog() != true) return;
                var outDir = Path.Combine(dlg.FolderName, $"{pack.Name}-{safeCat}");
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(outDir);
                    foreach (var (rel, source) in entries)
                    {
                        var dst = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        if (pack.IsZip)
                        {
                            using var z = System.IO.Compression.ZipFile.OpenRead(pack.Path);
                            var e = z.GetEntry(source) ?? throw new FileNotFoundException("zip 条目丢失：" + source);
                            using var s = e.Open();
                            using var d = File.Create(dst);
                            s.CopyTo(d);
                        }
                        else
                        {
                            File.Copy(source, dst, overwrite: true);
                        }
                    }
                });
                Log($"[√] 已导出 {entries.Count} 个文件为 MC data 文件夹（保持包内相对路径）：{outDir}");
                Log("    可直接把该文件夹内容并入网易基岩 MC data / 资源包目录结构使用。");
            }
        }
        catch (Exception ex)
        {
            Log("[!] 导出失败：" + ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}

