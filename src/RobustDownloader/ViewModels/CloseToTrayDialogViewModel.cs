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
    public string Title => L.CloseDialog_Title;
    public string Message => IsMenuBar ? L.CloseDialog_Message_MenuBar : L.CloseDialog_Message_Tray;
    public string MinimizeText => IsMenuBar ? L.CloseDialog_MinimizeToMenuBar : L.CloseDialog_MinimizeToTray;
    public string ExitText => L.CloseDialog_Exit;
    public string CancelText => L.CloseDialog_Cancel;
    public string DoNotAskAgainText => L.CloseDialog_DoNotAskAgain;

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
