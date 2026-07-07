using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RobustDownloader.Models;
using RobustDownloader.Services;

namespace RobustDownloader.Views;

public partial class AddTaskWindow : ShadUI.Window
{
    public AddTaskWindow()
    {
        InitializeComponent();
        TxtBatchTemplate.Text = "#.mp4";
        TxtBatchStart.Text = "1";
        TxtBatchStep.Text = "1";
        TxtBatchDigits.Text = "2";
        RdoBatchNamingAuto.IsChecked = true;
        UpdateUrlDependentFields();
    }

    public AddTaskWindow(AddTaskResult defaults) : this()
    {
        TxtSaveDir.Text = defaults.SaveDirectory;
        TxtThreads.Text = defaults.ThreadCount.ToString();
        TxtBlock.Text = defaults.BlockSize.ToString("0.##");
        TxtHeaders.Text = defaults.HeaderText;
        ChkCrcOnly.IsChecked = defaults.CrcOnly;
        ChkSkipCrc.IsChecked = defaults.SkipCrc;
        ChkUpdateFileTimestamp.IsChecked = defaults.UpdateFileTimestamp;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await TryFillUrlsFromClipboardAsync();
    }

    private async Task TryFillUrlsFromClipboardAsync()
    {
        if (!string.IsNullOrWhiteSpace(TxtUrls.Text) || Clipboard == null) return;

        string? clipboardText;
        try
        {
            clipboardText = await Clipboard.TryGetTextAsync();
        }
        catch
        {
            return;
        }

        var urls = GetHttpUrlsFromClipboard(clipboardText);
        if (urls.Length == 0) return;

        TxtUrls.Text = string.Join(Environment.NewLine, urls);
        ResetClipboardUrlTextBoxPosition();
    }

    private void ResetClipboardUrlTextBoxPosition()
    {
        TxtUrls.CaretIndex = 0;
        Dispatcher.UIThread.Post(() => TxtUrls.CaretIndex = 0, DispatcherPriority.Loaded);
    }

    private void TxtUrls_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateUrlDependentFields();
    }

    private void UpdateUrlDependentFields()
    {
        var lines = GetUrls();
        TxtUrlsLabel.Text = lines.Length == 0
            ? LocalizationService.Get("Add.Urls")
            : LocalizationService.Format("Add.UrlsWithCount", lines.Length);

        if (lines.Length == 1)
        {
            SingleFileNamePanel.IsVisible = true;
            BatchFileNamingPanel.IsVisible = false;
            TxtFileName.IsEnabled = true;
            TxtFileName.Text = TaskFileNameHelper.GetFileName(lines[0]);
        }
        else if (lines.Length > 1)
        {
            SingleFileNamePanel.IsVisible = false;
            BatchFileNamingPanel.IsVisible = true;
            TxtFileName.IsEnabled = false;
            TxtFileName.Text = LocalizationService.Get("Add.BatchFileName");
        }
        else
        {
            SingleFileNamePanel.IsVisible = true;
            BatchFileNamingPanel.IsVisible = false;
            TxtFileName.Text = "";
            TxtFileName.IsEnabled = true;
        }

        UpdateBatchNamingFields();
    }

    private void BatchNaming_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UpdateBatchNamingFields();
    }

    private void BatchNaming_Changed(object? sender, TextChangedEventArgs e)
    {
        UpdateBatchNamingFields();
    }

    private void UpdateBatchNamingFields()
    {
        var isTemplateMode = RdoBatchNamingTemplate.IsChecked == true;
        BatchTemplateSettingsPanel.IsVisible = isTemplateMode;
        BatchNumberSettingsPanel.IsVisible = isTemplateMode && TemplateHasNumberPlaceholder();
        var duplicateMessage = LocalizationService.Get("Validation.BatchDuplicateFileName");
        if (!isTemplateMode)
        {
            TxtBatchPreview.Text = "";
            if (TxtValidation.Text == duplicateMessage)
                TxtValidation.Text = "";
            return;
        }

        if (TryBuildBatchFileNames(GetUrls(), out var fileNames, out var error))
        {
            TxtBatchPreview.Text = string.Join(Environment.NewLine, fileNames);
            if (TxtValidation.Text == duplicateMessage)
                TxtValidation.Text = "";
            return;
        }

        TxtBatchPreview.Text = "";
        if (error == duplicateMessage)
            TxtValidation.Text = error;
    }

    private async void BtnBrowse_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocalizationService.Get("Dialog.SelectSaveDirectory"),
                AllowMultiple = false
            });

            var path = FolderPathHelper.GetLocalPath(folders.FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(path))
                TxtSaveDir.Text = path;
        }
        catch (Exception ex)
        {
            TxtValidation.Text = LocalizationService.Format("Validation.SelectDirectoryFailed", ex.Message);
        }
    }

    private void BtnAdd_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TxtValidation.Text = "";
        var urls = GetUrls();
        if (urls.Length == 0)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.EnterUrl");
            return;
        }

        var saveDir = FolderPathHelper.Normalize(TxtSaveDir.Text ?? "");
        if (string.IsNullOrWhiteSpace(saveDir))
        {
            TxtValidation.Text = LocalizationService.Get("Validation.SelectDirectory");
            return;
        }

        if (!FolderPathHelper.DirectoryExists(saveDir))
        {
            TxtValidation.Text = LocalizationService.Get("Validation.DirectoryMissing");
            return;
        }

        if (!int.TryParse(TxtThreads.Text, out var threads) || threads < 1)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.Threads");
            return;
        }

        if (!double.TryParse(TxtBlock.Text, out var block) || block <= 0)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.BlockSize");
            return;
        }

        if (ChkCrcOnly.IsChecked == true && ChkSkipCrc.IsChecked == true)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.CrcConflict");
            return;
        }

        var fileNames = Array.Empty<string>();
        if (urls.Length > 1 && RdoBatchNamingTemplate.IsChecked == true)
        {
            if (!TryBuildBatchFileNames(urls, out fileNames, out var fileNameError))
            {
                TxtValidation.Text = fileNameError;
                return;
            }
        }

        Close(new AddTaskResult
        {
            Urls = urls,
            FileNames = fileNames,
            SaveDirectory = saveDir,
            SingleFileName = TxtFileName.IsEnabled ? TxtFileName.Text?.Trim() ?? "" : "",
            ThreadCount = threads,
            BlockSize = block,
            CrcOnly = ChkCrcOnly.IsChecked == true,
            SkipCrc = ChkSkipCrc.IsChecked == true,
            UpdateFileTimestamp = ChkUpdateFileTimestamp.IsChecked == true,
            HeaderText = TxtHeaders.Text ?? ""
        });
    }

    private void BtnCancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private string[] GetUrls()
    {
        return (TxtUrls.Text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToArray();
    }

    private bool TemplateHasNumberPlaceholder()
    {
        return (TxtBatchTemplate.Text ?? "").Contains('#', StringComparison.Ordinal);
    }

    private bool TryBuildBatchFileNames(string[] urls, out string[] fileNames, out string error)
    {
        fileNames = [];
        error = "";

        var template = TxtBatchTemplate.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(template))
        {
            error = LocalizationService.Get("Validation.BatchTemplate");
            return false;
        }

        var hasNumberPlaceholder = template.Contains('#', StringComparison.Ordinal);
        var start = 0;
        var step = 1;
        var digits = 1;

        if (hasNumberPlaceholder)
        {
            if (!int.TryParse(TxtBatchStart.Text, out start) || start < 0)
            {
                error = LocalizationService.Get("Validation.BatchStart");
                return false;
            }

            if (!int.TryParse(TxtBatchStep.Text, out step) || step < 1)
            {
                error = LocalizationService.Get("Validation.BatchStep");
                return false;
            }

            if (!int.TryParse(TxtBatchDigits.Text, out digits) || digits < 1)
            {
                error = LocalizationService.Get("Validation.BatchDigits");
                return false;
            }
        }

        fileNames = urls
            .Select((url, index) => new
            {
                Url = url,
                Number = start + index * step
            })
            .Select(item =>
                template
                    .Replace("#", item.Number.ToString($"D{digits}"), StringComparison.Ordinal)
                    .Replace("*", TaskFileNameHelper.GetFileName(item.Url), StringComparison.Ordinal))
            .ToArray();

        if (fileNames.Any(name => string.IsNullOrWhiteSpace(name) || HasInvalidFileNameChars(name)))
        {
            error = LocalizationService.Get("Validation.BatchFileName");
            return false;
        }

        if (fileNames.Distinct(StringComparer.Ordinal).Count() != fileNames.Length)
        {
            error = LocalizationService.Get("Validation.BatchDuplicateFileName");
            return false;
        }

        return true;
    }

    private static bool HasInvalidFileNameChars(string fileName)
    {
        return fileName.IndexOfAny(InvalidFileNameChars) >= 0;
    }

    private static readonly char[] InvalidFileNameChars =
    [
        ..Path.GetInvalidFileNameChars(),
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    private static string[] GetHttpUrlsFromClipboard(string? text)
    {
        var candidates = (text ?? "")
            .Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeClipboardUrlToken)
            .Where(token => token.Length > 0)
            .ToArray();

        if (candidates.Length == 0)
            return [];

        return candidates.All(IsHttpUrl) ? candidates : [];
    }

    private static string NormalizeClipboardUrlToken(string token)
    {
        return token.Trim().Trim('"', '\'', '<', '>');
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https" &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }
}
