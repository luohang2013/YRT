using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using YRT.Core;
using YRT.Mvvm;

namespace YRT.ViewModels;

/// <summary>槽位勾选项（网易基岩编号目录 0/1/2…）</summary>
public class SlotItem : ObservableObject
{
    public string Name { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public SlotItem(string name) => Name = name;
}

/// <summary>
/// 安装页视图模型（移植自 RustPackGen 页面2「一键安装基岩版材质」+ 调研第1期启动器扫描规则）：
/// - Java 版：扫描主流启动器 resourcepacks 目录，一键部署 zip / 改 options.txt 启用关闭
/// - 网易基岩版：编号槽位整体替换基础包 + 材质包合并（跳过清单文件）
/// </summary>
public class InstallViewModel : ObservableObject
{
    private string _bedrockInfo = "未扫描（点击「扫描」定位网易安装点）";
    private string _baseDir = "";
    private bool _killMc = AppSettings.Instance.KillMc;
    private string _lastApply = "";
    private JavaPackDir? _selectedJavaDir;
    private string? _selectedZip;
    private bool _scanning;
    private bool _javaTabSelected = true;

    public ObservableCollection<string> Logs { get; } = new()
    {
        "欢迎使用安装页。Java 版：扫描启动器目录后可把 zip 部署进 resourcepacks，或直接改 options.txt 启用/关闭（需游戏已关闭）；网易基岩版：基础包整体替换编号槽位、材质包合并（保留游戏 manifest），基础包与材质包均支持 .mcpack / .zip 压缩档（自动解压）或目录，mcpack 可直接拖进本页。",
    };

    // ---------------- Java 版 ----------------

    public ObservableCollection<JavaPackDir> JavaDirs { get; } = new();

    public JavaPackDir? SelectedJavaDir
    {
        get => _selectedJavaDir;
        set
        {
            if (Set(ref _selectedJavaDir, value))
            {
                SelectedZip = null;
                OnPropertyChanged(nameof(Zips));
                OnPropertyChanged(nameof(OptionsVisible));
                LoadOptions();
            }
        }
    }

    // ---------------- options.txt 可视化编辑 ----------------

    private bool _optionsFound;
    private double _gammaPercent = 100;
    private int _guiScaleIndex;
    private bool _viewBobbing = true;
    private bool _smoothLighting = true;
    private bool _fullscreen;
    private string _renderDistance = "12";
    private int _cloudsIndex;
    private Dictionary<string, string>? _rawOptions;

    /// <summary>选中目录后显示 options 编辑卡</summary>
    public bool OptionsVisible => SelectedJavaDir is not null;

    /// <summary>该游戏根下是否找到 options.txt（找不到时提示）</summary>
    public bool OptionsFound { get => _optionsFound; private set => Set(ref _optionsFound, value); }

    /// <summary>gamma 亮度（0-100%，游戏内 0.0-1.0）</summary>
    public double GammaPercent { get => _gammaPercent; set => Set(ref _gammaPercent, value); }

    /// <summary>guiScale：0 自动 / 1 / 2 / 3 / 4</summary>
    public int GuiScaleIndex { get => _guiScaleIndex; set => Set(ref _guiScaleIndex, value); }

    public string[] GuiScaleOptions { get; } = { "自动", "1（小）", "2（中）", "3（大）", "4（特大）" };

    public bool ViewBobbing { get => _viewBobbing; set => Set(ref _viewBobbing, value); }

    public bool SmoothLighting { get => _smoothLighting; set => Set(ref _smoothLighting, value); }

    public bool Fullscreen { get => _fullscreen; set => Set(ref _fullscreen, value); }

    public string RenderDistance { get => _renderDistance; set => Set(ref _renderDistance, value); }

    /// <summary>renderClouds：true 渲染 / fast 快速 / false 关闭</summary>
    public int CloudsIndex { get => _cloudsIndex; set => Set(ref _cloudsIndex, value); }

    public string[] CloudsOptions { get; } = { "渲染", "快速", "关闭" };

    public ObservableCollection<string> Zips { get; } = new();

    public string? SelectedZip { get => _selectedZip; set => Set(ref _selectedZip, value); }

    // ---------------- 网易基岩版 ----------------

    public ObservableCollection<BedrockInstall> BedrockInstalls { get; } = new();

    public string BedrockInfo { get => _bedrockInfo; private set => Set(ref _bedrockInfo, value); }

    public ObservableCollection<SlotItem> Slots { get; } = new();

    public string BaseDir { get => _baseDir; set => Set(ref _baseDir, value); }

    public ObservableCollection<string> Commons { get; } = new();

    /// <summary>安装前结束 MC 进程（写入设置）</summary>
    public bool KillMc
    {
        get => _killMc;
        set { if (Set(ref _killMc, value)) { AppSettings.Instance.KillMc = value; AppSettings.Save(); } }
    }

    public string LastApply { get => _lastApply; private set => Set(ref _lastApply, value); }

    // ---------------- 标签页（Java / 基岩，一次只显示一个） ----------------

    public bool JavaTabSelected
    {
        get => _javaTabSelected;
        private set { if (Set(ref _javaTabSelected, value)) OnPropertyChanged(nameof(BedrockTabSelected)); }
    }

    public bool BedrockTabSelected => !JavaTabSelected;

    public RelayCommand SelectJavaTabCommand { get; }
    public RelayCommand SelectBedrockTabCommand { get; }

    // ---------------- 命令 ----------------

    public RelayCommand ScanJavaCommand { get; }
    public RelayCommand DeployJavaCommand { get; }
    public RelayCommand EnablePackCommand { get; }
    public RelayCommand DisablePackCommand { get; }
    public RelayCommand SaveOptionsCommand { get; }
    public RelayCommand ReloadOptionsCommand { get; }
    public RelayCommand ScanBedrockCommand { get; }
    public RelayCommand BrowseBaseCommand { get; }
    public RelayCommand BrowseBaseFileCommand { get; }
    public RelayCommand AddCommonCommand { get; }
    public RelayCommand AddCommonFileCommand { get; }
    public RelayCommand RemoveCommonCommand { get; }
    public RelayCommand ClearCommonsCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand CheckMcCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public InstallViewModel()
    {
        SelectJavaTabCommand = new RelayCommand(_ => JavaTabSelected = true);
        SelectBedrockTabCommand = new RelayCommand(_ => JavaTabSelected = false);
        ScanJavaCommand = new RelayCommand(_ => _ = ScanJavaAsync());
        DeployJavaCommand = new RelayCommand(_ => DoDeployJava());
        EnablePackCommand = new RelayCommand(_ => TogglePack(enable: true));
        DisablePackCommand = new RelayCommand(_ => TogglePack(enable: false));
        SaveOptionsCommand = new RelayCommand(_ => SaveOptions());
        ReloadOptionsCommand = new RelayCommand(_ => LoadOptions());
        ScanBedrockCommand = new RelayCommand(_ => DoScanBedrock());
        BrowseBaseCommand = new RelayCommand(_ => BrowseBase());
        BrowseBaseFileCommand = new RelayCommand(_ => BrowseBase(fromFile: true));
        AddCommonCommand = new RelayCommand(_ => AddCommon());
        AddCommonFileCommand = new RelayCommand(_ => AddCommon(fromFile: true));
        RemoveCommonCommand = new RelayCommand(p => RemoveCommon(p as string));
        ClearCommonsCommand = new RelayCommand(_ => Commons.Clear());
        ApplyCommand = new RelayCommand(_ => DoApply());
        CheckMcCommand = new RelayCommand(_ => DoCheckMc());
        ClearLogCommand = new RelayCommand(_ => Logs.Clear());
    }

    private void Log(string s) => Logs.Add(s);

    // ---------------- Java 版实现 ----------------

    private async Task ScanJavaAsync()
    {
        if (_scanning) return;
        _scanning = true;
        var opts = new JavaScanOptions
        {
            AllDrives = AppSettings.Instance.ScanScope == "AllDrives",
            MaxDepth = Math.Clamp(AppSettings.Instance.ScanDepth, 1, 8),
        };
        Log(opts.AllDrives
            ? $"[i] 正在全盘扫描 Java 版启动器目录（深度 ≤ {opts.MaxDepth}，可能需要几秒到几十秒）…"
            : $"[i] 正在扫描程序所在目录（深度 ≤ {opts.MaxDepth}）…");
        try
        {
            var dirs = await Task.Run(() => LauncherScanner.ScanJava(opts));
            JavaDirs.Clear();
            Zips.Clear();
            foreach (var d in dirs) JavaDirs.Add(d);
            Log(dirs.Count > 0
                ? $"[√] 扫描完成，找到 {dirs.Count} 个 resourcepacks 目录"
                : "[!] 未找到任何 resourcepacks 目录（若启动器装在别处，可在设置中把扫描范围切为「全盘」）");
        }
        catch (Exception ex)
        {
            Log($"[!] 扫描失败: {ex.Message}");
        }
        finally
        {
            _scanning = false;
        }
    }

    private void DoDeployJava() => DeployZip(null);

    /// <summary>按钮 / 拖拽共用部署入口（zipPath 为空时弹选择框）。</summary>
    private void DeployZip(string? zipPath)
    {
        if (SelectedJavaDir is null) { Log("[!] 请先扫描并选中一个 resourcepacks 目录"); return; }
        if (string.IsNullOrEmpty(zipPath))
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要安装的资源包 zip",
                Filter = "资源包 zip|*.zip|所有文件|*.*",
            };
            if (dlg.ShowDialog() != true) return;
            zipPath = dlg.FileName;
        }
        else if (!File.Exists(zipPath))
        {
            Log("[!] 拖入的文件不存在: " + zipPath);
            return;
        }
        try
        {
            var dst = PackDeployer.DeployJava(zipPath, SelectedJavaDir.Dir);
            Log($"[√] 已部署: {dst}");
            RefreshZips();
        }
        catch (Exception ex)
        {
            Log($"[!] 部署失败: {ex.Message}");
        }
    }

    /// <summary>拖拽入口：按当前页签分流 —— Java 页签拖 zip 部署；基岩页签拖 mcpack/zip/目录加入材质包列表。</summary>
    public void HandleDrop(string path)
    {
        if (JavaTabSelected)
        {
            if (!File.Exists(path) || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Log("[!] Java 页签请拖入资源包 zip（mcpack 请切到「网易基岩版」页签使用）：" + path);
                return;
            }
            Log("[√] 检测到拖入 zip：" + path);
            DeployZip(path);
        }
        else
        {
            AddCommonPath(path);
        }
    }

    private void TogglePack(bool enable)
    {
        if (SelectedJavaDir is null) { Log("[!] 请先扫描并选中一个 resourcepacks 目录"); return; }
        if (SelectedZip is null) { Log("[!] 请先在列表中选中一个资源包 zip"); return; }
        try
        {
            if (PackDeployer.McRunning())
            {
                Log("[!] 检测到 Minecraft 正在运行，改写 options.txt 会被游戏覆盖，请先关闭游戏");
                return;
            }
            var msg = PackDeployer.ToggleResourcePack(SelectedJavaDir.GameRoot, SelectedZip, enable);
            Log($"[√] {SelectedZip} {msg}（游戏根: {SelectedJavaDir.GameRoot}）");
        }
        catch (Exception ex)
        {
            Log($"[!] 操作失败: {ex.Message}");
        }
    }

    private void RefreshZips()
    {
        Zips.Clear();
        if (SelectedJavaDir is null) return;
        foreach (var z in LauncherScanner.ListZips(SelectedJavaDir.Dir)) Zips.Add(z);
    }

    // ---------------- options.txt 可视化编辑实现 ----------------

    /// <summary>读取选中目录对应游戏根的 options.txt，填充可视化编辑项。</summary>
    private void LoadOptions()
    {
        OptionsFound = false;
        if (SelectedJavaDir is null) return;
        var path = Path.Combine(SelectedJavaDir.GameRoot, "options.txt");
        if (!File.Exists(path))
        {
            Log($"[!] 未找到 options.txt（游戏根：{SelectedJavaDir.GameRoot}），无法可视化编辑");
            return;
        }
        try
        {
            _rawOptions = PackDeployer.ReadOptions(SelectedJavaDir.GameRoot);
            var o = _rawOptions;
            if (o.TryGetValue("gamma", out var g) &&
                double.TryParse(g, System.Globalization.CultureInfo.InvariantCulture, out var gv))
                GammaPercent = Math.Clamp(gv, 0, 1) * 100;
            if (o.TryGetValue("guiScale", out var gs) && int.TryParse(gs, out var gsv))
                GuiScaleIndex = Math.Clamp(gsv, 0, 4);
            ViewBobbing = !o.TryGetValue("viewBobbing", out var vb) || vb != "false";
            SmoothLighting = !o.TryGetValue("smoothLighting", out var sl) || sl != "false";
            Fullscreen = o.TryGetValue("fullscreen", out var fs) && fs == "true";
            if (o.TryGetValue("renderDistance", out var rd) && int.TryParse(rd, out _))
                RenderDistance = rd;
            CloudsIndex = o.TryGetValue("renderClouds", out var cl)
                ? cl switch { "false" => 2, "fast" => 1, _ => 0 }
                : 0;
            OptionsFound = true;
            Log($"[√] 已读取 options.txt（{o.Count} 项，游戏根：{SelectedJavaDir.GameRoot}）");
        }
        catch (Exception ex)
        {
            Log("[!] 读取 options.txt 失败：" + ex.Message);
        }
    }

    /// <summary>把可视化编辑项写回 options.txt（先备份 .bak，需游戏已关闭）。</summary>
    private void SaveOptions()
    {
        if (SelectedJavaDir is null) { Log("[!] 请先扫描并选中一个 resourcepacks 目录"); return; }
        if (!File.Exists(Path.Combine(SelectedJavaDir.GameRoot, "options.txt")))
        {
            Log("[!] 该游戏根目录下未找到 options.txt，无法保存");
            return;
        }
        if (PackDeployer.McRunning())
        {
            Log("[!] 检测到 Minecraft 正在运行，改写 options.txt 会被游戏覆盖，请先关闭游戏");
            return;
        }
        if (!int.TryParse(RenderDistance.Trim(), out var rdVal) || rdVal < 2 || rdVal > 64)
        {
            Log("[!] 渲染距离应为 2-64 的整数");
            return;
        }
        try
        {
            var gamma = Math.Clamp(GammaPercent, 0, 100) / 100.0;
            var values = new Dictionary<string, string>
            {
                ["gamma"] = gamma.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture),
                ["guiScale"] = GuiScaleIndex.ToString(),
                ["viewBobbing"] = ViewBobbing ? "true" : "false",
                ["smoothLighting"] = SmoothLighting ? "true" : "false",
                ["fullscreen"] = Fullscreen ? "true" : "false",
                ["renderDistance"] = rdVal.ToString(),
                ["renderClouds"] = CloudsIndex switch { 1 => "fast", 2 => "false", _ => "true" },
            };
            var n = PackDeployer.WriteOptions(SelectedJavaDir.GameRoot, values);
            Log($"[√] options.txt 已保存（更新 {n} 项，备份为 options.txt.bak），下次进游戏生效。");
        }
        catch (Exception ex)
        {
            Log("[!] 保存 options.txt 失败：" + ex.Message);
        }
    }

    // ---------------- 网易基岩版实现（对齐 RustPackGen 页面2） ----------------

    private void DoScanBedrock()
    {
        var list = LauncherScanner.ScanBedrock();
        BedrockInstalls.Clear();
        foreach (var b in list) BedrockInstalls.Add(b);
        if (list.Count == 0)
        {
            BedrockInfo = "未找到网易基岩版安装点（需 %APPDATA% 下 MinecraftPC_Netease_PB / MinecraftPE_Netease）";
            Log("[!] " + BedrockInfo);
            return;
        }
        var first = list[0];
        BedrockInfo = $"安装点: {first.PacksRoot}（槽位 {(first.Slots.Count == 0 ? "无" : string.Join(", ", first.Slots))}）";
        Log($"[√] {BedrockInfo}");
        Slots.Clear();
        foreach (var s in first.Slots) Slots.Add(new SlotItem(s));
    }

    private void BrowseBase(bool fromFile = false)
    {
        if (fromFile)
        {
            var dlgFile = new OpenFileDialog
            {
                Title = "选择基础包压缩档（mcpack/zip，安装时自动解压）",
                Filter = "基岩材质包 (*.mcpack)|*.mcpack|zip 压缩档|*.zip|所有文件|*.*",
            };
            if (dlgFile.ShowDialog() == true) BaseDir = dlgFile.FileName;
        }
        else
        {
            var dlg = new OpenFolderDialog { Title = "选择基础包目录（整体替换槽位）" };
            if (dlg.ShowDialog() == true) BaseDir = dlg.FolderName;
        }
    }

    private void AddCommon(bool fromFile = false)
    {
        if (fromFile)
        {
            var dlgFile = new OpenFileDialog
            {
                Title = "添加材质包压缩档（mcpack/zip，安装时自动解压）",
                Filter = "基岩材质包 (*.mcpack)|*.mcpack|zip 压缩档|*.zip|所有文件|*.*",
            };
            if (dlgFile.ShowDialog() != true) return;
            AddCommonPath(dlgFile.FileName);
            return;
        }
        var dlg = new OpenFolderDialog { Title = "添加材质包目录（合并进槽位）" };
        if (dlg.ShowDialog() != true) return;
        AddCommonPath(dlg.FolderName);
    }

    /// <summary>把目录或 mcpack/zip 压缩档加入材质包列表（去重）。</summary>
    private void AddCommonPath(string path)
    {
        if (Commons.Contains(path))
        {
            Log("[!] 该材质包已在列表中");
            return;
        }
        Commons.Add(path);
        Log(Directory.Exists(path)
            ? $"[√] 已添加材质包目录：{path}"
            : $"[√] 已添加材质包压缩档：{path}（安装时自动解压合并）");
    }

    private void RemoveCommon(string? path)
    {
        if (path is not null) Commons.Remove(path);
    }

    private void DoCheckMc()
    {
        var running = PackDeployer.McRunning();
        Log($"[i] Minecraft 进程: {(running ? "运行中" : "未运行")}");
    }

    private void DoApply()
    {
        var b = BedrockInstalls.FirstOrDefault();
        if (b is null) { Log("[!] 请先点击「扫描」定位安装点"); return; }
        var slots = Slots.Where(s => s.IsSelected).Select(s => int.Parse(s.Name)).ToList();
        if (slots.Count == 0) { Log("[!] 请至少勾选一个槽位（0/1/2）"); return; }
        if (BaseDir.Trim().Length == 0 && Commons.Count == 0)
        {
            Log("[!] 请先选择基础包目录或添加材质包目录");
            return;
        }
        if (KillMc)
        {
            var killed = PackDeployer.KillMinecraftProcesses();
            Log(killed.Count > 0 ? $"[i] 已结束进程: {string.Join(", ", killed)}" : "[i] 未发现运行中的 Minecraft 进程");
        }
        try
        {
            // mcpack/zip 压缩档先物化为目录（带缓存复用，不改原文件）
            var baseDirs = new List<string>();
            if (BaseDir.Trim().Length > 0)
            {
                var d = PackDeployer.MaterializeArchive(BaseDir.Trim());
                if (!d.Equals(BaseDir.Trim(), StringComparison.OrdinalIgnoreCase))
                    Log($"[i] 基础包压缩档已解压 → {d}");
                baseDirs.Add(d);
            }
            var commonDirs = new List<string>();
            foreach (var c in Commons)
            {
                var d = PackDeployer.MaterializeArchive(c);
                if (!d.Equals(c, StringComparison.OrdinalIgnoreCase))
                    Log($"[i] 材质包压缩档已解压 → {d}");
                commonDirs.Add(d);
            }

            var (total, used) = PackDeployer.ApplyBedrock(b.PacksRoot, slots, baseDirs, commonDirs, new HashSet<string>());
            LastApply = $"安装完成！共写入 {total} 个文件 → 槽位 {string.Join(", ", used)}";
            Log($"[√] {LastApply}（重启游戏生效）");
            if (AppSettings.Instance.OpenDirAfterInstall)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", b.PacksRoot); }
                catch { /* 打开目录失败不影响安装结果 */ }
            }
        }
        catch (Exception ex)
        {
            Log($"[!] 安装失败: {ex.Message}");
        }
    }
}
