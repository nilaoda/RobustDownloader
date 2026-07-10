using System;
using RobustDownloader.Models;
using RobustDownloader.Services;
using ShadUI;

namespace RobustDownloader.ViewModels;

public sealed class CloseToTrayDialogViewModel(DialogManager dialogManager)
{
    public bool DoNotAskAgain { get; set; }
    public WindowCloseBehavior Choice { get; private set; } = WindowCloseBehavior.MinimizeToTray;
    public bool IsMenuBar => OperatingSystem.IsMacOS();
    public string Title => LocalizationService.Get("CloseDialog.Title");
    public string Message => LocalizationService.Get(IsMenuBar ? "CloseDialog.Message.MenuBar" : "CloseDialog.Message.Tray");
    public string MinimizeText => LocalizationService.Get(IsMenuBar ? "CloseDialog.MinimizeToMenuBar" : "CloseDialog.MinimizeToTray");
    public string ExitText => LocalizationService.Get("CloseDialog.Exit");
    public string CancelText => LocalizationService.Get("CloseDialog.Cancel");
    public string DoNotAskAgainText => LocalizationService.Get("CloseDialog.DoNotAskAgain");

    public void MinimizeToTray()
    {
        Choice = WindowCloseBehavior.MinimizeToTray;
        dialogManager.Close(this, new CloseDialogOptions { Success = true });
    }

    public void ExitApplication()
    {
        Choice = WindowCloseBehavior.ExitApplication;
        dialogManager.Close(this, new CloseDialogOptions { Success = true });
    }

    public void Cancel()
    {
        dialogManager.Close(this, new CloseDialogOptions { Success = false });
    }
}
