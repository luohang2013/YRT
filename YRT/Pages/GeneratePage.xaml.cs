using System.Windows.Controls;
using YRT.ViewModels;

namespace YRT.Pages;

public partial class GeneratePage : UserControl
{
    public GeneratePage()
    {
        InitializeComponent();
        DataContext = new GenerateViewModel();
    }

    private void Page_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropHelper.GeneratePage_Drop(sender, e);
        DropHelper.Page_DropDone(sender, e);
    }

    private void Page_DragEnter(object sender, System.Windows.DragEventArgs e) => DropHelper.Page_DragEnter(sender, e);
    private void Page_DragLeave(object sender, System.Windows.DragEventArgs e) => DropHelper.Page_DragLeave(sender, e);
}
