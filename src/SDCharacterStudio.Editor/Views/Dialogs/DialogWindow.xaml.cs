using System.Windows;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.Views.Dialogs;

/// <summary>Hosts a dialog view model; its DataTemplate (DialogTemplates.xaml) supplies the content and buttons.</summary>
public partial class DialogWindow : FluentWindow
{
    public DialogWindow(object viewModel)
    {
        InitializeComponent();
        Infrastructure.WindowFit.Attach(this);
        DataContext = viewModel;
        (Title, Width) = viewModel switch
        {
            NewProjectViewModel => ("New project", 600.0),
            DependenciesViewModel => ("Dependencies", 780.0),
            PackSettingsViewModel => ("Pack settings", 560.0),
            SettingsViewModel => ("Settings", 640.0),
            VideoImportViewModel => ("Import video", 600.0),
            ExportViewModel => ("Export", 640.0),
            ItemPickerViewModel => ("Choose an item", 560.0),
            _ => ("SDCharacterStudio", 560.0)
        };
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        if (viewModel is ItemPickerViewModel) { SizeToContent = SizeToContent.Manual; Height = 640; }
        if (viewModel is DialogViewModel dialog) dialog.CloseRequested += accepted => { DialogResult = accepted; };
    }
}
