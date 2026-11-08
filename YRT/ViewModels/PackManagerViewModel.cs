using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using YRT.Core;
using YRT.Mvvm;

namespace YRT.ViewModels;

/// <summary>
/// 一个分类的内容来源选择行（图四：从不同资源包选取不同内容）。
/// </summary>
public class CategoryChoice : ObservableObject
{
    public string Category { get; }

    /// <summary>拥有该分类的来源包</summary>
    public List<SourcePack> Packs { get; }

    /// <summary>下拉选项展示名</summary>
    public List<string> Options { get; }

    private int _selectedIndex;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { if (Set(ref _selectedIndex, value)) OnPropertyChanged(nameof(SelectedPack)); }
    }

    public SourcePack? SelectedPack =>
        _selectedIndex >= 0 && _selectedIndex < Packs.Count ? Packs[_selectedIndex] : null;

    public string Summary =>
        SelectedPack is null ? "不包含" : $"{SelectedPack.Name} · {CountOf(SelectedPack)} 个文件";

    public CategoryChoice(string category, List<SourcePack> packs)
    {
        Category = category;
        Packs = packs;
        Options = packs.Select(p => $"{p.Name}（{CountOf(p)} 个）").ToList();
    }

    private int CountOf(SourcePack p) =>
        p.Categories.TryGetValue(Category, out var l) ? l.Count : 0;
}

/// <summary>
/// 资源包管理页视图模型（图四）：多来源包入库 → 按分类选来源 → 聚合导出。
/// </summary>
public class PackManagerViewModel : ObservableObject
{
    private string _outDir = DefaultOutDir();
    private bool _busy;

    public ObservableCollection<SourcePack> Packs { get; } = new();

    public ObservableCollection<CategoryChoice> Choices { get; } = new();

    public ObservableCollection<string> Logs { get; } = new()
    {
        "欢迎使用资源包管理。添加多个资源包（zip 或目录）后，可按分类混搭：例如方块贴图取 A 包、音效取 B 包，一键聚合导出完整 zip。",
    };

    public string OutDir { get => _outDir; set => Set(ref _outDir, value); }

    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public RelayCommand AddZipCommand { get; }
    public RelayCommand AddDirCommand { get; }
    public RelayCommand RemovePackCommand { get; }
    public RelayCommand ClearPacksCommand { get; }
    public RelayCommand BrowseOutCommand { get; }
    public RelayCommand MergeCommand { get; }
    public RelayCommand SavePlanCommand { get; }
    public RelayCommand LoadPlanCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public PackManagerViewModel()
    {
        AddZipCommand = new RelayCommand(_ => Add(true));
        AddDirCommand = new RelayCommand(_ => Add(false));
        RemovePackCommand = new RelayCommand(p => { if (p is SourcePack s) RemovePack(s); });
        ClearPacksCommand = new RelayCommand(_ => ClearPacks());
        BrowseOutCommand = new RelayCommand(_ => BrowseOut());
        MergeCommand = new RelayCommand(_ => _ = DoMergeAsync());
        SavePlanCommand = new RelayCommand(_ => SavePlan());
        LoadPlanCommand = new RelayCommand(_ => LoadPlan());
        ClearLogCommand = new RelayCommand(_ => Logs.Clear());
        RestoreSavedPacks();
    }

    /// <summary>启动时后台恢复上次入库的来源包（缺失/失效路径跳过）。</summary>
    private async void RestoreSavedPacks()
    {
        var saved = AppSettings.Instance.SavedPackPaths;
        if (saved.Count == 0) return;
        var paths = saved.ToList();
        await Task.Run(() => { /* 预热：无操作，仅让 UI 先渲染 */ });
        var ok = 0;
        foreach (var p in paths)
            if (AddPathSilent(p)) ok++;
        if (ok > 0)
            Log($"[i] 已自动恢复上次入库的 {ok} 个来源包。");
        foreach (var p in paths.Where(p => !Packs.Any(x => x.Path.Equals(p, StringComparison.OrdinalIgnoreCase))))
            Log($"[!] 上次的来源包已不存在，已跳过：{p}");
    }

    /// <summary>入库但安静处理失败（恢复用），返回是否成功。</summary>
    private bool AddPathSilent(string path)
    {
        if (Packs.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return false;
        try
        {
            var pack = SourcePack.Load(path);
            Packs.Add(pack);
            Rebuild();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把当前包库路径同步进设置并落盘。</summary>
    private void PersistPackPaths()
    {
        AppSettings.Instance.SavedPackPaths = Packs.Select(p => p.Path).ToList();
        AppSettings.Save();
    }

    private void RemovePack(SourcePack s)
    {
        Packs.Remove(s);
        PersistPackPaths();
        Log($"[i] 已移除来源包：{s.Name}");
        Rebuild();
    }

    private void ClearPacks()
    {
        if (Packs.Count == 0) return;
        Packs.Clear();
        PersistPackPaths();
        Log("[i] 已清空来源包库。");
        Rebuild();
    }

    // ---------------- 混搭方案保存 / 载入 ----------------

    /// <summary>方案文件结构：包库路径 + 每个分类的选中索引。</summary>
    private sealed class MixPlan
    {
        public List<string>? Packs { get; set; }
        public List<MixPlanChoice>? Choices { get; set; }
    }

    private sealed class MixPlanChoice
    {
        public string? Category { get; set; }
        public int Index { get; set; }
    }

    private void SavePlan()
    {
        if (Choices.Count == 0) { Log("[!] 当前没有可选分类，先入库资源包再保存方案"); return; }
        var dlg = new SaveFileDialog
        {
            Title = "保存混搭方案",
            Filter = "混搭方案 (*.yrtplan)|*.yrtplan|所有文件|*.*",
            FileName = $"mixplan-{DateTime.Now:yyyyMMdd}.yrtplan",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var plan = new MixPlan
            {
                Packs = Packs.Select(p => p.Path).ToList(),
                Choices = Choices.Select(c => new MixPlanChoice { Category = c.Category, Index = c.SelectedIndex }).ToList(),
            };
            File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log($"[√] 方案已保存：{dlg.FileName}（{Packs.Count} 个来源包，{Choices.Count} 个分类选择）");
        }
        catch (Exception ex)
        {
            Log("[!] 保存方案失败：" + ex.Message);
        }
    }

    private void LoadPlan()
    {
        var dlg = new OpenFileDialog
        {
            Title = "载入混搭方案",
            Filter = "混搭方案 (*.yrtplan)|*.yrtplan|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var plan = System.Text.Json.JsonSerializer.Deserialize<MixPlan>(File.ReadAllText(dlg.FileName));
            if (plan?.Packs is null) { Log("[!] 方案文件格式不正确"); return; }

            // 先按方案恢复包库（去重、跳过失效路径）
            Packs.Clear();
            var restored = 0;
            foreach (var p in plan.Packs)
            {
                if (!File.Exists(p) && !Directory.Exists(p))
                {
                    Log($"[!] 方案中的来源包不存在，已跳过：{p}");
                    continue;
                }
                if (AddPathSilent(p)) restored++;
            }
            PersistPackPaths();
            Log($"[√] 方案包库已载入：{restored} 个来源包");
            Log("[i] 正在按方案恢复各分类的来源选择…");

            // 再应用各分类的选中索引（按分类名匹配，包集不同则尽量对齐）
            if (plan.Choices is not null)
            {
                var applied = 0;
                foreach (var c in Choices)
                {
                    var match = plan.Choices.FirstOrDefault(x => x.Category == c.Category);
                    if (match is null) continue;
                    c.SelectedIndex = Math.Clamp(match.Index, -1, c.Packs.Count - 1);
                    applied++;
                }
                Log($"[√] 已恢复 {applied} 个分类的来源选择，可直接「聚合导出 zip」。");
            }
        }
        catch (Exception ex)
        {
            Log("[!] 载入方案失败：" + ex.Message);
        }
    }

    private static string DefaultOutDir()
    {
        var configured = AppSettings.Instance.DefaultOutDir.Trim();
        return configured.Length > 0
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "output");
    }

    private void Log(string s) => Logs.Add(s);

    private void Add(bool wantZip)
    {
        try
        {
            string path;
            if (wantZip)
            {
                var dlg = new OpenFileDialog { Title = "添加资源包压缩档", Filter = "资源包 (*.zip;*.mcpack)|*.zip;*.mcpack|所有文件|*.*" };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }
            else
            {
                var dlg = new OpenFolderDialog { Title = "添加资源包目录" };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FolderName;
            }
            AddPath(path);
        }
        catch (Exception ex)
        {
            Log("[!] 添加失败：" + ex.Message);
        }
    }

    /// <summary>按路径入库（按钮与拖拽共用）；返回是否成功。</summary>
    public bool AddPath(string path)
    {
        try
        {
            if (Packs.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                Log("[!] 该来源包已在库中");
                return false;
            }
            var pack = SourcePack.Load(path);
            Packs.Add(pack);
            Log($"[√] 已入库：{pack.Name}（{pack.FileCount} 个文件，{PackAnalyzer.FmtBytes(pack.TotalBytes)}）");
            PersistPackPaths();
            Rebuild();
            return true;
        }
        catch (Exception ex)
        {
            Log("[!] 添加失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>拖拽入口：zip/mcpack 或目录直接入库（多个取有效项）。</summary>
    public void HandleDrop(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p) || (File.Exists(p) && PackPacker.IsPackArchive(p)))
                AddPath(p);
            else
                Log("[!] 跳过不支持的拖入项：" + p);
        }
    }

    private void Rebuild()
    {
        Choices.Clear();
        var cats = new SortedDictionary<string, List<SourcePack>>();
        foreach (var pack in Packs)
            foreach (var (cat, list) in pack.Categories)
            {
                if (!cats.TryGetValue(cat, out var l))
                    cats[cat] = l = new List<SourcePack>();
                l.Add(pack);
            }
        foreach (var (cat, packs) in cats)
            Choices.Add(new CategoryChoice(cat, packs));
        Log($"[i] 已按 {Choices.Count} 个分类生成来源选择，默认各分类不包含内容。");
    }

    private void BrowseOut()
    {
        var dlg = new OpenFolderDialog { Title = "选择聚合输出目录" };
        if (dlg.ShowDialog() == true) OutDir = dlg.FolderName;
    }

    private async Task DoMergeAsync()
    {
        if (Choices.Count == 0) { Log("[!] 请先添加至少一个来源包"); return; }
        var picks = Choices.ToDictionary(c => c.Category, c => c.SelectedPack);
        var usedCats = picks.Where(kv => kv.Value is not null).ToList();
        if (usedCats.Count == 0) { Log("[!] 请至少为一个分类选择来源包"); return; }

        var name = $"mixed-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var outZip = Path.Combine(OutDir.Trim(), name);

        Busy = true;
        Log("[i] 正在聚合导出（复制文件可能需要几秒）…");
        try
        {
            var n = await Task.Run(() => SourcePack.ExportMerged(picks, outZip));
            Log($"[√] 聚合完成：{outZip}");
            Log($"    · 内容 {n} 个文件，来自 {usedCats.Count} 个分类：");
            foreach (var (cat, pack) in usedCats)
                Log($"      - {cat} ← {pack!.Name}");
            Log("[i] 清单 pack.mcmeta 自动生成（取来源包最小 pack_format），可直接安装。");
        }
        catch (Exception ex)
        {
            Log("[!] 聚合失败：" + ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}
