using Avalonia.Controls;
using RobustDownloader.ViewModels;

namespace RobustDownloader.Views;

public partial class CloseToTrayDialog : UserControl
{
    public CloseToTrayDialog()
    {
        InitializeComponent();
    }

    private void Minimize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is CloseToTrayDialogViewModel vm)
            vm.MinimizeToTray();
    }

    private void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is CloseToTrayDialogViewModel vm)
            vm.ExitApplication();
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is CloseToTrayDialogViewModel vm)
            vm.Cancel();
    }
}
