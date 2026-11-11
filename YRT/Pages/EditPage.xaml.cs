using System.Windows;
using System.Windows.Controls;
using YRT.ViewModels;

namespace YRT.Pages;

public partial class EditPage : UserControl
{
    public EditPage()
    {
        InitializeComponent();
        DataContext = new EditViewModel();
    }

    private void Page_Drop(object sender, DragEventArgs e)
    {
        DropHelper.HideOverlay(sender);
        var paths = DropHelper.GetPaths(e);
        if (paths.Count == 0) return;
        if (DataContext is EditViewModel vm) vm.HandleDrop(paths[0]);
        e.Handled = true;
    }

    private void Page_DragEnter(object sender, DragEventArgs e) => DropHelper.ShowOverlay(sender);
    private void Page_DragLeave(object sender, DragEventArgs e) => DropHelper.HideOverlay(sender);
}
