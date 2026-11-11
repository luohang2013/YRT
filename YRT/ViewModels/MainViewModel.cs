using System.Collections.ObjectModel;
using YRT.Mvvm;
using YRT.Pages;

namespace YRT.ViewModels;

public class MainViewModel : ObservableObject
{
    /// <summary>全局单例：主页等功能卡片需要跨页导航时直接访问。</summary>
    public static MainViewModel Instance { get; } = new();

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem("主页", "\uE80F", new HomePage()),
        new NavItem("生成", "\uE710", new GeneratePage()),
        new NavItem("资源包", "\uE8B7", new PackManagerPage()),
        new NavItem("编辑", "\uE70F", new EditPage()),
        new NavItem("分析", "\uE9D9", new AnalyzePage()),
        new NavItem("安装", "\uE896", new InstallPage()),
        new NavItem("设置", "\uE713", new SettingsPage()),
    };

    private NavItem _current = null!;
    public NavItem Current { get => _current; private set => Set(ref _current, value); }

    /// <summary>顶栏导航选中索引（与 NavList 双向同步，主页卡片跳转时联动高亮）。</summary>
    private int _selectedNavIndex;
    public int SelectedNavIndex { get => _selectedNavIndex; set => Set(ref _selectedNavIndex, value); }

    private MainViewModel()
    {
        _current = NavItems[0];
        _selectedNavIndex = 0;
        // 启动静默检查更新 + 拉取公告（结果缓存在 UpdateService，设置页/主页订阅刷新）
        _ = RunStartupChecksAsync();
    }

    /// <summary>本次会话是否已由启动检查触发过更新弹窗（手动检查不重复弹）。</summary>
    public static bool StartupUpdatePrompted;

    private static async Task RunStartupChecksAsync()
    {
        var s = Core.AppSettings.Instance;
        // 更新与公告并行拉取，互不阻塞（公告卡能更快显示出来）
        var tasks = new List<Task>();
        if (s.CheckUpdateOnStartup)
        {
            tasks.Add(Core.UpdateService.CheckForUpdateAsync(Core.UpdateService.OfficialRepo).ContinueWith(_ =>
            {
                // 启动检查发现新版本 → 弹出更新对话框（仅一次；手动检查走设置页卡片）
                if (Core.UpdateService.UpdateAvailable && !StartupUpdatePrompted)
                {
                    StartupUpdatePrompted = true;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        Views.UpdateDialogWindow.ShowIfPossible());
                }
            }));
        }
        if (!string.IsNullOrWhiteSpace(s.AnnouncementUrl))
            tasks.Add(Core.UpdateService.LoadAnnouncementAsync(s.AnnouncementUrl));
        await Task.WhenAll(tasks);
    }

    public void Navigate(int index)
    {
        if (index < 0 || index >= NavItems.Count) return;
        SelectedNavIndex = index; // 同步顶栏高亮（重复设置同值不会触发事件，无死循环）
        Current = NavItems[index];
    }

    /// <summary>按 Title 跳转（主页快捷入口用），找不到则不动。</summary>
    public void NavigateTo(string title)
    {
        var idx = -1;
        for (var i = 0; i < NavItems.Count; i++)
        {
            if (NavItems[i].Title != title) continue;
            idx = i;
            break;
        }
        if (idx >= 0) Navigate(idx);
    }

    /// <summary>程序名称（外观-程序 里自定义；空 = 默认名）。窗口标题/顶栏/主页 Hero 绑定此属性。</summary>
    public const string DefaultAppName = "YHCT 的资源包工具箱";

    public string ApplicationName =>
        string.IsNullOrWhiteSpace(Core.AppSettings.Instance.AppName) ? DefaultAppName : Core.AppSettings.Instance.AppName.Trim();

    /// <summary>程序名被设置页修改后调用，通知绑定刷新。</summary>
    public void NotifyAppName() => OnPropertyChanged(nameof(ApplicationName));
}

public class NavItem
{
    public string Title { get; }
    /// <summary>Segoe MDL2 Assets 图标字形</summary>
    public string Glyph { get; }
    public object Page { get; }

    public NavItem(string title, string glyph, object page)
    {
        Title = title;
        Glyph = glyph;
        Page = page;
    }

    public override string ToString() => Title;
}
