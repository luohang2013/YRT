using System.Windows.Controls;
using YRT.ViewModels;

namespace YRT.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = new HomeViewModel();
    }
}
