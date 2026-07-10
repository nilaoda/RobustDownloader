using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using RobustDownloader.ViewModels;

namespace RobustDownloader.Views;

public partial class SpeedLimitDialog : UserControl
{
    public SpeedLimitDialog()
    {
        InitializeComponent();
        SliderSpeedLimit.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && DataContext is SpeedLimitDialogViewModel vm)
                vm.SetSpeedLimitFromSlider(SliderSpeedLimit.Value);
        };
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SpeedLimitDialogViewModel vm)
            vm.Save(TxtSpeedLimit.Text);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SpeedLimitDialogViewModel vm)
            vm.Cancel();
    }
}
