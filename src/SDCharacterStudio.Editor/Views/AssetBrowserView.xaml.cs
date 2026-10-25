using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views;

public partial class AssetBrowserView : UserControl
{
    public const string AssetFormat = "SDCS.Asset";
    private Point dragStart;
    private AssetItemViewModel? dragItem;

    public AssetBrowserView() => InitializeComponent();

    private void Thumbnails_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(null);
        dragItem = (e.OriginalSource as FrameworkElement)?.DataContext as AssetItemViewModel;
    }

    private void Thumbnails_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragItem == null) return;
        var delta = e.GetPosition(null) - dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = dragItem;
        dragItem = null;
        DragDrop.DoDragDrop(Thumbnails, new DataObject(AssetFormat, item), DragDropEffects.Copy);
    }

    private void Thumbnails_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is AssetItemViewModel item) item.UseCommand.Execute(null);
    }
}
