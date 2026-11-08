using System.Windows.Controls;
using YRT.ViewModels;

namespace YRT.Pages;

public partial class InstallPage : UserControl
{
    public InstallPage()
    {
        InitializeComponent();
        DataContext = new InstallViewModel();
    }

    private void Page_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropHelper.InstallPage_Drop(sender, e);
        DropHelper.Page_DropDone(sender, e);
    }

    private void Page_DragEnter(object sender, System.Windows.DragEventArgs e) => DropHelper.Page_DragEnter(sender, e);
    private void Page_DragLeave(object sender, System.Windows.DragEventArgs e) => DropHelper.Page_DragLeave(sender, e);
}
