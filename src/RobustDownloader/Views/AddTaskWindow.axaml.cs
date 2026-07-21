using System;
using System.Collections.Generic;
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
    private AddTaskResult _defaults = new();

    public AddTaskWindow()
    {
        InitializeComponent();
        TxtBatchTemplate.Text = "#.mp4";
        TxtBatchStart.Text = "1";
        TxtBatchStep.Text = "1";
        TxtBatchDigits.Text = "2";
        RdoBatchNamingAuto.IsChecked = true;
        UpdateInputMode();
        UpdateUrlDependentFields();
    }

    public AddTaskWindow(AddTaskResult defaults) : this()
    {
        _defaults = defaults;
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

    private void TxtCommands_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var count = GetCommandLines().Length;
        TxtCommandsLabel.Text = count == 0
            ? L.Add_Commands
            : L.Add_CommandsWithCount(count);
    }

    private void InputMode_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UpdateInputMode();
        Dispatcher.UIThread.Post(
            () => (RdoInputCommands.IsChecked == true ? TxtCommands : TxtUrls).Focus(),
            DispatcherPriority.Input);
    }

    private void UpdateInputMode()
    {
        var commandMode = RdoInputCommands.IsChecked == true;
        UrlInputPanel.IsVisible = !commandMode;
        CommandInputPanel.IsVisible = commandMode;
        TxtValidation.Text = "";
    }

    private void UpdateUrlDependentFields()
    {
        var lines = GetUrls();
        TxtUrlsLabel.Text = lines.Length == 0
            ? L.Add_Urls
            : L.Add_UrlsWithCount(lines.Length);

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
            TxtFileName.Text = L.Add_BatchFileName;
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
        var duplicateMessage = L.Validation_BatchDuplicateFileName;
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
                Title = L.Dialog_SelectSaveDirectory,
                AllowMultiple = false
            });

            var path = FolderPathHelper.GetLocalPath(folders.FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(path))
                TxtSaveDir.Text = path;
        }
        catch (Exception ex)
        {
            TxtValidation.Text = L.Validation_SelectDirectoryFailed(ex.Message);
        }
    }

    private void BtnAdd_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TxtValidation.Text = "";
        if (RdoInputCommands.IsChecked == true)
        {
            AddCommandLineTasks();
            return;
        }

        var urls = GetUrls();
        if (urls.Length == 0)
        {
            TxtValidation.Text = L.Validation_EnterUrl;
            return;
        }

        var saveDir = FolderPathHelper.Normalize(TxtSaveDir.Text ?? "");
        if (string.IsNullOrWhiteSpace(saveDir))
        {
            TxtValidation.Text = L.Validation_SelectDirectory;
            return;
        }

        if (!FolderPathHelper.DirectoryExists(saveDir))
        {
            TxtValidation.Text = L.Validation_DirectoryMissing;
            return;
        }

        if (!int.TryParse(TxtThreads.Text, out var threads) || threads < 1)
        {
            TxtValidation.Text = L.Validation_Threads;
            return;
        }

        if (!double.TryParse(TxtBlock.Text, out var block) || block <= 0)
        {
            TxtValidation.Text = L.Validation_BlockSize;
            return;
        }

        if (ChkCrcOnly.IsChecked == true && ChkSkipCrc.IsChecked == true)
        {
            TxtValidation.Text = L.Validation_CrcConflict;
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

        Close(new[]
        {
            new AddTaskResult
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
            }
        });
    }

    private void AddCommandLineTasks()
    {
        var commandLines = GetCommandLines();
        if (commandLines.Length == 0)
        {
            TxtValidation.Text = L.Validation_EnterCommand;
            return;
        }

        var results = new List<AddTaskResult>();
        foreach (var commandLine in commandLines)
        {
            var parseResult = CommandLineParser.ParseAddTaskText(commandLine.Text);
            if (!string.IsNullOrWhiteSpace(parseResult.Error))
            {
                TxtValidation.Text = L.Validation_CommandLineError(commandLine.LineNumber, parseResult.Error);
                return;
            }

            if (parseResult.Command.Kind != CommandLineCommandKind.AddTasks ||
                parseResult.Command.AddTasks == null)
                continue;

            results.Add(BuildCommandTaskResult(parseResult.Command.AddTasks));
        }

        if (results.Count == 0)
        {
            TxtValidation.Text = L.Validation_NoAddCommands;
            return;
        }

        Close(results.ToArray());
    }

    private AddTaskResult BuildCommandTaskResult(CommandLineAddTasksOptions options)
    {
        return new AddTaskResult
        {
            Urls = options.Urls,
            SaveDirectory = string.IsNullOrWhiteSpace(options.SaveDirectory)
                ? _defaults.SaveDirectory
                : options.SaveDirectory,
            SingleFileName = options.SingleFileName,
            ThreadCount = options.ThreadCount ?? _defaults.ThreadCount,
            BlockSize = options.BlockSizeMb ?? _defaults.BlockSize,
            CrcOnly = _defaults.CrcOnly,
            SkipCrc = _defaults.SkipCrc,
            UpdateFileTimestamp = _defaults.UpdateFileTimestamp,
            HeaderText = string.IsNullOrWhiteSpace(options.HeaderText)
                ? _defaults.HeaderText
                : options.HeaderText,
            StartImmediately = options.StartImmediately
        };
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

    private (int LineNumber, string Text)[] GetCommandLines()
    {
        return (TxtCommands.Text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select((line, index) => (LineNumber: index + 1, Text: line.Trim()))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
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
            error = L.Validation_BatchTemplate;
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
                error = L.Validation_BatchStart;
                return false;
            }

            if (!int.TryParse(TxtBatchStep.Text, out step) || step < 1)
            {
                error = L.Validation_BatchStep;
                return false;
            }

            if (!int.TryParse(TxtBatchDigits.Text, out digits) || digits < 1)
            {
                error = L.Validation_BatchDigits;
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
            error = L.Validation_BatchFileName;
            return false;
        }

        if (fileNames.Distinct(StringComparer.Ordinal).Count() != fileNames.Length)
        {
            error = L.Validation_BatchDuplicateFileName;
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
