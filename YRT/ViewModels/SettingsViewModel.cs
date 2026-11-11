using YRT.Core;
using YRT.Mvvm;
using YRT.Theme;

namespace YRT.ViewModels;

/// <summary>
/// 设置页视图模型：四组设置（安装默认行为 / 生成页默认值 / 扫描设置 / 外观与重置），
/// 任何修改即时持久化到 settings.json。
/// </summary>
public class SettingsViewModel : ObservableObject
{
    public RelayCommand SetGreenThemeCommand { get; }
    public RelayCommand SetBlueThemeCommand { get; }
    public RelayCommand SetPurpleThemeCommand { get; }
    public RelayCommand SetAmberThemeCommand { get; }
    public RelayCommand SetCustomThemeCommand { get; }
    public RelayCommand SetLightModeCommand { get; }
    public RelayCommand SetDarkModeCommand { get; }
    public RelayCommand BrowseDefaultOutCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }
    public RelayCommand GetAnnouncementCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand OpenLanzouCommand { get; }
    public RelayCommand OpenDataDirCommand { get; }
    public RelayCommand ShowUpdateDialogCommand { get; }
    public RelayCommand ResetCommand { get; }

    // ---------------- 分类导航（图八：设置分类排放） ----------------

    public string[] Categories { get; } = { "外观", "安装", "生成", "扫描", "更新", "关于" };

    private int _selectedCategory;
    public int SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (Set(ref _selectedCategory, value))
            {
                OnPropertyChanged(nameof(ShowAppearance));
                OnPropertyChanged(nameof(ShowInstall));
                OnPropertyChanged(nameof(ShowGenerate));
                OnPropertyChanged(nameof(ShowScan));
                OnPropertyChanged(nameof(ShowUpdate));
                OnPropertyChanged(nameof(ShowAbout));
            }
        }
    }

    public bool ShowAppearance => _selectedCategory == 0;
    public bool ShowInstall => _selectedCategory == 1;
    public bool ShowGenerate => _selectedCategory == 2;
    public bool ShowScan => _selectedCategory == 3;
    public bool ShowUpdate => _selectedCategory == 4;
    public bool ShowAbout => _selectedCategory == 5;

    public SettingsViewModel()
    {
        SetGreenThemeCommand = new RelayCommand(_ => SetTheme("Green"));
        SetBlueThemeCommand = new RelayCommand(_ => SetTheme("Blue"));
        SetPurpleThemeCommand = new RelayCommand(_ => SetTheme("Purple"));
        SetAmberThemeCommand = new RelayCommand(_ => SetTheme("Amber"));
        SetCustomThemeCommand = new RelayCommand(_ => PickCustomColor());
        SetLightModeCommand = new RelayCommand(_ => SetMode("Light"));
        SetDarkModeCommand = new RelayCommand(_ => SetMode("Dark"));
        BrowseDefaultOutCommand = new RelayCommand(_ => BrowseDefaultOut());
        CheckUpdateCommand = new RelayCommand(_ => _ = CheckUpdateAsync());
        GetAnnouncementCommand = new RelayCommand(_ => _ = GetAnnouncementAsync());
        OpenReleaseCommand = new RelayCommand(_ => OpenReleasePage());
        OpenLanzouCommand = new RelayCommand(_ => OpenLanzou());
        OpenDataDirCommand = new RelayCommand(_ => OpenDataDir());
        ShowUpdateDialogCommand = new RelayCommand(_ => Views.UpdateDialogWindow.ShowIfPossible());
        ResetCommand = new RelayCommand(_ => DoReset());

        // 订阅更新/公告结果（可能在后台线程触发，切回 UI 线程刷新）
        Core.UpdateService.StateChanged += () =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshUpdateState);
        Core.UpdateService.AnnouncementChanged += () =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshAnnouncementState);
        RefreshUpdateState();
        RefreshAnnouncementState();
    }

    // ---------------- 安装默认行为 ----------------

    public bool KillMc
    {
        get => AppSettings.Instance.KillMc;
        set { AppSettings.Instance.KillMc = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    public bool OpenDirAfterInstall
    {
        get => AppSettings.Instance.OpenDirAfterInstall;
        set { AppSettings.Instance.OpenDirAfterInstall = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    /// <summary>基岩安装前自动备份目标槽位</summary>
    public bool BackupBeforeInstall
    {
        get => AppSettings.Instance.BackupBeforeInstall;
        set { AppSettings.Instance.BackupBeforeInstall = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    /// <summary>安装完成后弹出提示框</summary>
    public bool NotifyAfterInstall
    {
        get => AppSettings.Instance.NotifyAfterInstall;
        set { AppSettings.Instance.NotifyAfterInstall = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    public string[] DeployConflictOptions { get; } = { "覆盖同名包", "自动重命名" };

    /// <summary>Java 部署重名策略（0=覆盖 / 1=重命名）</summary>
    public int DeployConflictIndex
    {
        get => AppSettings.Instance.DeployConflict == "Rename" ? 1 : 0;
        set
        {
            AppSettings.Instance.DeployConflict = value == 1 ? "Rename" : "Overwrite";
            AppSettings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>当前数据目录（设置 → 安装 → 数据目录 展示）</summary>
    public string DataDir => YRT.FirstRun.ResourceDeployer.DataDir;

    private void OpenDataDir()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DataDir);
            System.Diagnostics.Process.Start("explorer.exe", DataDir);
        }
        catch { /* 打开失败忽略 */ }
    }

    // ---------------- 生成页默认值 ----------------

    public string DefaultOutDir
    {
        get => AppSettings.Instance.DefaultOutDir;
        set { AppSettings.Instance.DefaultOutDir = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    public string PackNameTemplate
    {
        get => AppSettings.Instance.PackNameTemplate;
        set { AppSettings.Instance.PackNameTemplate = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    /// <summary>打包时自动补齐 pack.mcmeta</summary>
    public bool AutoMcmeta
    {
        get => AppSettings.Instance.AutoMcmeta;
        set { AppSettings.Instance.AutoMcmeta = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    // ---------------- 扫描设置 ----------------

    /// <summary>扫描范围 = 程序所在目录</summary>
    public bool ScanProgramDir
    {
        get => AppSettings.Instance.ScanScope != "AllDrives";
        set { if (value) { AppSettings.Instance.ScanScope = "ProgramDir"; AppSettings.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(ScanAllDrives)); } }
    }

    /// <summary>扫描范围 = 全盘</summary>
    public bool ScanAllDrives
    {
        get => AppSettings.Instance.ScanScope == "AllDrives";
        set { if (value) { AppSettings.Instance.ScanScope = "AllDrives"; AppSettings.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(ScanProgramDir)); } }
    }

    /// <summary>扫描深度（1-8）</summary>
    public int ScanDepth
    {
        get => AppSettings.Instance.ScanDepth;
        set
        {
            var v = Math.Clamp(value, 1, 8);
            AppSettings.Instance.ScanDepth = v;
            AppSettings.Save();
            OnPropertyChanged();
        }
    }

    // ---------------- 外观 ----------------

    public string Theme => AppSettings.Instance.Theme;

    /// <summary>自定义主题色 #RRGGBB（用于色块展示）</summary>
    public string CustomThemeColor => AppSettings.Instance.CustomThemeColor;

    /// <summary>浅色模式选中态</summary>
    public bool IsLightMode => AppSettings.Instance.ThemeMode != "Dark";

    /// <summary>深色模式选中态</summary>
    public bool IsDarkMode => AppSettings.Instance.ThemeMode == "Dark";

    /// <summary>程序名称（外观-程序；输入即时生效并同步顶栏/标题/Hero）</summary>
    public string AppName
    {
        get => AppSettings.Instance.AppName;
        set
        {
            AppSettings.Instance.AppName = value;
            AppSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AppNameDisplay));
            MainViewModel.Instance.NotifyAppName();
        }
    }

    /// <summary>关于卡显示的程序名（空值回退默认名）</summary>
    public string AppNameDisplay => string.IsNullOrWhiteSpace(AppSettings.Instance.AppName)
        ? MainViewModel.DefaultAppName
        : AppSettings.Instance.AppName.Trim();

    private void SetTheme(string key)
    {
        ThemeManager.Apply(key);
        AppSettings.Instance.Theme = key;
        AppSettings.Save();
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(CustomThemeColor));
    }

    private void SetMode(string mode)
    {
        ThemeManager.ApplyMode(mode);
        AppSettings.Instance.ThemeMode = mode;
        AppSettings.Save();
        OnPropertyChanged(nameof(IsLightMode));
        OnPropertyChanged(nameof(IsDarkMode));
        if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            mw.ApplyTitleBarTheme();
    }

    private void PickCustomColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(0x3D, 0x8B, 0x5F),
        };
        if (ThemeManager.TryParseHex(AppSettings.Instance.CustomThemeColor, out var c))
            dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var color = System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
        ThemeManager.ApplyCustom(color);
        AppSettings.Instance.Theme = "Custom";
        AppSettings.Instance.CustomThemeColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        AppSettings.Save();
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(CustomThemeColor));
    }

    private void BrowseDefaultOut()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择生成页默认输出目录" };
        if (dlg.ShowDialog() == true)
        {
            DefaultOutDir = dlg.FolderName;
            OnPropertyChanged(nameof(DefaultOutDir));
        }
    }

    // ---------------- 更新与公告 ----------------

    // 更新源内置为 Core.UpdateService.OfficialRepo，不提供配置（防篡改注入）

    // 公告源内置为 Core.UpdateService.OfficialAnnouncementUrl，不公示、不可配置（防仿冒篡改）

    /// <summary>启动时自动检查更新</summary>
    public bool CheckUpdateOnStartup
    {
        get => AppSettings.Instance.CheckUpdateOnStartup;
        set { AppSettings.Instance.CheckUpdateOnStartup = value; AppSettings.Save(); OnPropertyChanged(); }
    }

    /// <summary>当前程序版本（程序集）</summary>
    public string CurrentVersion => Core.UpdateService.CurrentVersionText;

    private bool _updateBusy;
    public bool UpdateBusy
    {
        get => _updateBusy;
        private set { Set(ref _updateBusy, value); OnPropertyChanged(nameof(UpdateNotBusy)); }
    }
    public bool UpdateNotBusy => !_updateBusy;

    private bool _announceBusy;
    public bool AnnounceBusy
    {
        get => _announceBusy;
        private set { Set(ref _announceBusy, value); OnPropertyChanged(nameof(AnnounceNotBusy)); }
    }
    public bool AnnounceNotBusy => !_announceBusy;

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    private Core.GitHubRelease? _release;
    public bool ShowRelease => _release != null;
    public bool HasNewVersion => _release != null && Core.UpdateService.UpdateAvailable;
    public string ReleaseTag => _release?.TagName ?? "";
    public string ReleaseTitle => _release?.Name ?? "";
    public string ReleaseDate => _release?.PublishedAt?.ToLocalTime().ToString("yyyy 年 M 月 d 日") ?? "";
    public string ReleaseNotes => _release?.Body ?? "";
    public string ReleaseUrl => _release?.HtmlUrl ?? "";

    private string _announceStatus = "";
    public string AnnounceStatus { get => _announceStatus; private set => Set(ref _announceStatus, value); }

    private Core.Announcement? _announcement;
    public bool ShowAnnouncement => _announcement != null && !string.IsNullOrWhiteSpace(_announcement.Content);
    public string AnnounceTitle => string.IsNullOrWhiteSpace(_announcement?.Title) ? "公告" : _announcement!.Title.Trim();
    public string AnnounceDate => _announcement?.Date?.Trim() ?? "";
    public string AnnounceContent => _announcement?.Content?.Trim() ?? "";

    private async Task CheckUpdateAsync()
    {
        if (_updateBusy) return;
        UpdateBusy = true;
        UpdateStatus = "正在检查更新…";
        try
        {
            await Core.UpdateService.CheckForUpdateAsync(Core.UpdateService.OfficialRepo);
        }
        finally
        {
            UpdateBusy = false;
        }
        RefreshUpdateState();
    }

    private async Task GetAnnouncementAsync()
    {
        if (_announceBusy) return;
        AnnounceBusy = true;
        AnnounceStatus = "正在获取公告…";
        try
        {
            await Core.UpdateService.LoadAnnouncementAsync();
        }
        finally
        {
            AnnounceBusy = false;
        }
        RefreshAnnouncementState();
    }

    private void OpenReleasePage()
    {
        var url = Core.UpdateService.LatestRelease?.HtmlUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = $"https://github.com/{Core.UpdateService.OfficialRepo}/releases";
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 打开失败忽略 */ }
    }

    /// <summary>蓝奏云入口是否可用（未配置链接时隐藏按钮）</summary>
    public bool HasLanzou => !string.IsNullOrWhiteSpace(AppSettings.Instance.LanzouUrl);

    private void OpenLanzou()
    {
        var url = AppSettings.Instance.LanzouUrl.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        var pwd = AppSettings.Instance.LanzouPassword?.Trim() ?? "";
        try
        {
            if (pwd.Length > 0)
                System.Windows.Clipboard.SetText(pwd);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            UpdateStatus = pwd.Length > 0
                ? $"已在浏览器打开蓝奏云，提取码 {pwd} 已复制到剪贴板。"
                : "已在浏览器打开蓝奏云。";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"打开蓝奏云失败：{ex.Message}";
        }
    }

    private void RefreshUpdateState()
    {
        _release = Core.UpdateService.LatestRelease;
        OnPropertyChanged(nameof(ShowRelease));
        OnPropertyChanged(nameof(HasNewVersion));
        OnPropertyChanged(nameof(ReleaseTag));
        OnPropertyChanged(nameof(ReleaseTitle));
        OnPropertyChanged(nameof(ReleaseDate));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(ReleaseUrl));

        if (Core.UpdateService.LastError.Length > 0 && _release == null)
            UpdateStatus = Core.UpdateService.LastError + "。";
        else if (_release == null)
            UpdateStatus = "尚未检查更新。";
        else if (Core.UpdateService.UpdateAvailable)
            UpdateStatus = $"发现新版本 {_release.TagName}，建议尽快更新！";
        else
            UpdateStatus = "当前已是最新版本。";
    }

    private void RefreshAnnouncementState()
    {
        _announcement = Core.UpdateService.Announcement;
        OnPropertyChanged(nameof(ShowAnnouncement));
        OnPropertyChanged(nameof(AnnounceTitle));
        OnPropertyChanged(nameof(AnnounceDate));
        OnPropertyChanged(nameof(AnnounceContent));

        if (Core.UpdateService.AnnouncementError.Length > 0 && ReferenceEquals(_announcement, Core.UpdateService.FallbackAnnouncement))
            AnnounceStatus = Core.UpdateService.AnnouncementError + "，已显示内置公告。";
        else if (_announcement == null)
            AnnounceStatus = "尚未获取公告。";
        else
            AnnounceStatus = "公告已更新。";
    }

    private void DoReset()
    {
        AppSettings.Instance.ResetToDefaults();
        AppSettings.Save();
        ThemeManager.ApplySaved();
        MainViewModel.Instance.NotifyAppName();
        if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            mw.ApplyTitleBarTheme();
        // 通知全部属性刷新
        foreach (var p in new[]
                 {
                     nameof(KillMc), nameof(OpenDirAfterInstall), nameof(BackupBeforeInstall),
                     nameof(NotifyAfterInstall), nameof(DeployConflictIndex), nameof(DefaultOutDir),
                     nameof(PackNameTemplate), nameof(AutoMcmeta), nameof(ScanProgramDir),
                     nameof(ScanAllDrives), nameof(ScanDepth), nameof(Theme), nameof(CustomThemeColor),
                     nameof(IsLightMode), nameof(IsDarkMode), nameof(AppName), nameof(AppNameDisplay),
                     nameof(CheckUpdateOnStartup),
                     nameof(CurrentVersion), nameof(HasLanzou),
                 })
            OnPropertyChanged(p);
        RefreshUpdateState();
        RefreshAnnouncementState();
    }
}
