using System.Windows;
using System.Windows.Media.Animation;
using YRT.ViewModels;

namespace YRT.Views;

/// <summary>
/// 启动动画窗口：单文件自解压与主窗口初始化期间展示 Logo 呼吸动画，
/// 主窗口渲染完成后淡出关闭。
/// </summary>
public partial class SplashWindow : Window
{
    private bool _closing;

    public SplashWindow()
    {
        InitializeComponent();
        AppNameText.Text = MainViewModel.Instance.ApplicationName;
        VersionText.Text = Core.UpdateService.CurrentVersionText;
    }

    /// <summary>淡出并关闭（重复调用安全）。</summary>
    public void CloseWithFade()
    {
        if (_closing || !IsLoaded) { Close(); return; }
        _closing = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
