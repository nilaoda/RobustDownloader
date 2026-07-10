using System;
using System.Globalization;
using RobustDownloader.Services;
using ShadUI;

namespace RobustDownloader.ViewModels;

public sealed class SpeedLimitDialogViewModel : ViewModelBase
{
    private const double MinimumSpeedLimitMbps = 0.1;
    private const double MaximumSpeedLimitMbps = 100;
    private readonly DialogManager _dialogManager;
    private bool _updatingText;
    private bool _isEnabled;
    private double _speedLimitMbps = 10;
    private string _speedLimitText = "";
    private string _validationMessage = "";

    public SpeedLimitDialogViewModel(DialogManager dialogManager, bool isEnabled, double speedLimitMbps)
    {
        _dialogManager = dialogManager;
        IsEnabled = isEnabled;
        SpeedLimitMbps = CoerceSpeedLimitMbps(speedLimitMbps);
        SpeedLimitText = FormatSpeedLimit(SpeedLimitMbps);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public double SpeedLimitMbps
    {
        get => _speedLimitMbps;
        set => SetProperty(ref _speedLimitMbps, CoerceSpeedLimitMbps(value));
    }

    public string SpeedLimitText
    {
        get => _speedLimitText;
        set => SetProperty(ref _speedLimitText, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }
    public string Title => LocalizationService.Get("Settings.SpeedLimit");
    public string EnableText => LocalizationService.Get("Settings.SpeedLimitEnable");
    public string UnitText => LocalizationService.Get("Settings.SpeedLimitUnit");
    public string HintText => LocalizationService.Get("Settings.SpeedLimitHint");
    public string CancelText => LocalizationService.Get("Settings.Cancel");
    public string SaveText => LocalizationService.Get("Settings.Save");

    public void SetSpeedLimitFromSlider(double value)
    {
        SpeedLimitMbps = CoerceSpeedLimitMbps(value);
        SyncTextFromValue();
    }

    public void Save(string? text)
    {
        ValidationMessage = "";

        if (!TryParseSpeedLimit(text, out var parsed))
        {
            ValidationMessage = LocalizationService.Get("Validation.SpeedLimit");
            return;
        }

        SpeedLimitMbps = CoerceSpeedLimitMbps(parsed);
        SpeedLimitText = FormatSpeedLimit(SpeedLimitMbps);
        _dialogManager.Close(this, new CloseDialogOptions { Success = true });
    }

    public void Cancel()
    {
        _dialogManager.Close(this, new CloseDialogOptions { Success = false });
    }

    private void SyncTextFromValue()
    {
        if (_updatingText) return;

        _updatingText = true;
        try
        {
            SpeedLimitText = FormatSpeedLimit(SpeedLimitMbps);
            OnPropertyChanged(nameof(SpeedLimitText));
        }
        finally
        {
            _updatingText = false;
        }
    }

    private static bool TryParseSpeedLimit(string? text, out double value)
    {
        text = (text ?? "").Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

        value = 0;
        return false;
    }

    private static double CoerceSpeedLimitMbps(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 10;
        return Math.Clamp(value, MinimumSpeedLimitMbps, MaximumSpeedLimitMbps);
    }

    private static string FormatSpeedLimit(double value)
    {
        return CoerceSpeedLimitMbps(value).ToString("0.##", CultureInfo.CurrentCulture);
    }
}
