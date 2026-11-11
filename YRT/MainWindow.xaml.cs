using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using YRT.ViewModels;

namespace YRT;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = MainViewModel.Instance;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        Loaded += (_, _) => PlayPageEnterAnimation();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>深色模式下把系统标题栏也切为深色。</summary>
    public void ApplyTitleBarTheme()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = Theme.ThemeManager.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        _ = DwmSetWindowAttribute(hwnd, 19, ref dark, 4); // 旧版 Windows 回退
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.Navigate(NavList.SelectedIndex);
        PlayPageEnterAnimation();
    }

    /// <summary>页面切换动画：淡入 + 轻微上移回弹（每个分页面进入时播放）。</summary>
    private void PlayPageEnterAnimation()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var shift = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        };
        PageHost.BeginAnimation(UIElement.OpacityProperty, fade);
        PageShift.BeginAnimation(TranslateTransform.YProperty, shift);
    }
}
