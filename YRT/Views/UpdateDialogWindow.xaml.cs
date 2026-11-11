using System.Diagnostics;
using System.IO;
using System.Windows;
using YRT.Core;
using YRT.Mvvm;

namespace YRT.Views;

/// <summary>
/// 更新对话框：发现新版本时展示更新日志，
/// 提供「手动下载更新」（蓝奏云发布页 + 自动复制提取码）与
/// 「自动下载更新」（GitHub 直连 + 进度条 + 关闭程序自替换安装）。
/// </summary>
public partial class UpdateDialogWindow : Window
{
    public UpdateDialogViewModel VM { get; }

    private UpdateDialogWindow(GitHubRelease release)
    {
        InitializeComponent();
        VM = new UpdateDialogViewModel(release);
        DataContext = VM;
    }

    /// <summary>有新版本时弹出（已打开则忽略）；返回是否弹出。</summary>
    public static bool ShowIfPossible()
    {
        var release = UpdateService.LatestRelease;
        if (release is null || !UpdateService.UpdateAvailable) return false;
        if (Application.Current.Windows.OfType<UpdateDialogWindow>().Any()) return false;
        var win = new UpdateDialogWindow(release);
        var owner = Application.Current.MainWindow;
        if (owner is { IsLoaded: true }) win.Owner = owner;
        win.Show();
        win.Activate();
        return true;
    }
}

public class UpdateDialogViewModel : ObservableObject
{
    private readonly GitHubRelease _release;

    public UpdateDialogViewModel(GitHubRelease release)
    {
        _release = release;
        ManualCommand = new RelayCommand(_ => DoManual());
        AutoCommand = new RelayCommand(_ => _ = DoAutoAsync(), _ => AutoReady);
        Tag = string.IsNullOrWhiteSpace(release.TagName) ? "" : release.TagName;
        var name = string.IsNullOrWhiteSpace(release.Name) ? "" : release.Name + "  ";
        var date = release.PublishedAt?.ToLocalTime().ToString("yyyy 年 M 月 d 日") ?? "";
        Headline = $"{name}{date}".Trim();
    }

    // ---------------- 展示 ----------------

    public string Tag { get; }
    public string Headline { get; }
    public string Notes => string.IsNullOrWhiteSpace(_release.Body) ? "（本次更新没有填写更新日志）" : _release.Body.Trim();

    private string _progressText = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    private int _progress;
    public int Progress { get => _progress; private set => Set(ref _progress, value); }

    private bool _downloading;
    public bool Downloading
    {
        get => _downloading;
        private set { if (Set(ref _downloading, value)) OnPropertyChanged(nameof(AutoReady)); }
    }

    /// <summary>自动下载按钮可用条件：没有附件不可用；下载中不可用。</summary>
    public bool AutoReady => !_downloading && UpdateService.PickAsset(_release) is not null;

    private string _errorText = "";
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    public bool HasError => _errorText.Length > 0;

    public RelayCommand ManualCommand { get; }
    public RelayCommand AutoCommand { get; }

    // ---------------- 手动下载：蓝奏云 + 自动复制提取码 ----------------

    private void DoManual()
    {
        var url = AppSettings.Instance.LanzouUrl.Trim();
        var pwd = AppSettings.Instance.LanzouPassword?.Trim() ?? "";
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = $"https://github.com/{UpdateService.OfficialRepo}/releases";
        try
        {
            if (pwd.Length > 0) System.Windows.Clipboard.SetText(pwd);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            ProgressText = "";
            ErrorText = "";
            CloseDialog();
        }
        catch (Exception ex)
        {
            ErrorText = "打开下载页失败：" + ex.Message;
        }
    }

    // ---------------- 自动下载：进度条 → 询问 → 自替换安装 ----------------

    private async Task DoAutoAsync()
    {
        var asset = UpdateService.PickAsset(_release);
        if (asset is null) return;

        Downloading = true;
        ErrorText = "";
        Progress = 0;
        ProgressText = "正在连接下载源…";
        try
        {
            var progress = new Progress<(int Percent, string Text)>(p =>
            {
                Progress = p.Percent;
                ProgressText = p.Text;
            });
            var zip = await UpdateService.DownloadReleaseAsync(
                asset, SelfUpdater.UpdateDir, progress);

            ProgressText = "下载完成，准备安装…";
            var ok = MessageBox.Show(
                $"安装包已下载完成：\n{zip}\n\n是否立即关闭程序并安装新版本？",
                "自动更新", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK)
            {
                Downloading = false;
                ProgressText = $"已取消自动安装，安装包保留在：{zip}";
                return;
            }

            if (!SelfUpdater.PrepareInstall(zip, out var err))
            {
                ErrorText = "准备安装失败：" + err;
                Downloading = false;
                return;
            }
            // 已启动替换脚本，直接退出主程序
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            Downloading = false;
            ErrorText = "自动下载失败：" + ex.Message + "（可改用「手动下载更新」）";
        }
    }

    private void CloseDialog()
    {
        foreach (var w in System.Windows.Application.Current.Windows.OfType<UpdateDialogWindow>())
            w.Close();
    }
}
