using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RobustDownloader.ViewModels;
using RobustDownloader.Views;

namespace RobustDownloader;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        if (param is MainWindowViewModel)
        {
            return new MainWindow();
        }

        return new TextBlock { Text = "Not Found: " + param.GetType().Name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
