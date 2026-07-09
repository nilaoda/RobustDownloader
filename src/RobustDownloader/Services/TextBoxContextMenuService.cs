using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace RobustDownloader.Services;

public static class TextBoxContextMenuService
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        Control.LoadedEvent.AddClassHandler<TextBox>((textBox, _) => Attach(textBox));
    }

    private static void Attach(TextBox textBox)
    {
        if (textBox.ContextMenu != null) return;

        var shortcutModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var cut = CreateMenuItem(textBox, "TextBoxMenu.Cut", new KeyGesture(Key.X, shortcutModifier), () => textBox.Cut());
        var copy = CreateMenuItem(textBox, "TextBoxMenu.Copy", new KeyGesture(Key.C, shortcutModifier), () => textBox.Copy());
        var paste = CreateMenuItem(textBox, "TextBoxMenu.Paste", new KeyGesture(Key.V, shortcutModifier), () => textBox.Paste());
        var selectAll = CreateMenuItem(textBox, "TextBoxMenu.SelectAll", new KeyGesture(Key.A, shortcutModifier), () => textBox.SelectAll());

        var menu = new ContextMenu
        {
            Items =
            {
                cut,
                copy,
                paste,
                selectAll
            }
        };

        menu.Opening += async (_, _) =>
        {
            FocusTextBox(textBox);
            RefreshHeaders(cut, copy, paste, selectAll);

            var hasSelection = textBox.SelectionStart != textBox.SelectionEnd;
            var hasText = !string.IsNullOrEmpty(textBox.Text);
            var canEdit = textBox.IsEnabled && !textBox.IsReadOnly;

            cut.IsEnabled = canEdit && hasSelection;
            copy.IsEnabled = textBox.IsEnabled && hasSelection;
            paste.IsEnabled = false;
            selectAll.IsEnabled = textBox.IsEnabled && hasText;

            if (!canEdit) return;

            var clipboard = TopLevel.GetTopLevel(textBox)?.Clipboard;
            if (clipboard == null) return;

            try
            {
                var text = await clipboard.TryGetTextAsync();
                paste.IsEnabled = !string.IsNullOrEmpty(text);
            }
            catch
            {
                paste.IsEnabled = false;
            }
        };

        textBox.ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(TextBox textBox, string localizationKey, KeyGesture inputGesture, Action action)
    {
        var item = new MenuItem
        {
            Header = LocalizationService.Get(localizationKey),
            InputGesture = inputGesture
        };
        item.Click += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                FocusTextBox(textBox);
                action();
            });
        };
        return item;
    }

    private static void FocusTextBox(TextBox textBox)
    {
        if (textBox.IsEnabled)
            textBox.Focus(NavigationMethod.Pointer, KeyModifiers.None);
    }

    private static void RefreshHeaders(MenuItem cut, MenuItem copy, MenuItem paste, MenuItem selectAll)
    {
        cut.Header = LocalizationService.Get("TextBoxMenu.Cut");
        copy.Header = LocalizationService.Get("TextBoxMenu.Copy");
        paste.Header = LocalizationService.Get("TextBoxMenu.Paste");
        selectAll.Header = LocalizationService.Get("TextBoxMenu.SelectAll");
    }
}
