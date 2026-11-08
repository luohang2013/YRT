using YRT.Mvvm;

namespace YRT.ViewModels;

/// <summary>
/// 主页视图模型：快捷入口直接跳转对应页面；顶部公告卡来自更新源配置的公告。
/// </summary>
public class HomeViewModel : ObservableObject
{
    public RelayCommand NavCommand { get; }
    public RelayCommand PackNowCommand { get; }
    public RelayCommand OpenMixerCommand { get; }
    public RelayCommand OpenInstallCommand { get; }

    public HomeViewModel()
    {
        NavCommand = new RelayCommand(p => MainViewModel.Instance.NavigateTo(p as string ?? ""));
        PackNowCommand = new RelayCommand(_ => MainViewModel.Instance.NavigateTo("生成"));
        OpenMixerCommand = new RelayCommand(_ => MainViewModel.Instance.NavigateTo("资源包"));
        OpenInstallCommand = new RelayCommand(_ => MainViewModel.Instance.NavigateTo("安装"));

        // 公告可能由启动检查或设置页刷新拉取，订阅变化（后台线程 → 切回 UI 线程）
        Core.UpdateService.AnnouncementChanged += () =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshAnnouncement);
        RefreshAnnouncement();
    }

    // ---------------- 顶部公告卡 ----------------

    private string _announceTitle = "";
    public string AnnounceTitle { get => _announceTitle; private set => Set(ref _announceTitle, value); }

    private string _announceDate = "";
    public string AnnounceDate { get => _announceDate; private set => Set(ref _announceDate, value); }

    private string _announceContent = "";
    public string AnnounceContent { get => _announceContent; private set => Set(ref _announceContent, value); }

    /// <summary>有公告内容才显示卡片。</summary>
    public System.Windows.Visibility AnnounceVisibility =>
        string.IsNullOrWhiteSpace(AnnounceContent)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    private void RefreshAnnouncement()
    {
        var a = Core.UpdateService.Announcement;
        AnnounceTitle = string.IsNullOrWhiteSpace(a?.Title) ? "公告" : a.Title.Trim();
        AnnounceDate = a?.Date?.Trim() ?? "";
        AnnounceContent = a?.Content?.Trim() ?? "";
        OnPropertyChanged(nameof(AnnounceVisibility));
    }
}
