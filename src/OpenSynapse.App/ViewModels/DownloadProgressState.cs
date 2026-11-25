using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace OpenSynapse.App.ViewModels;

public sealed class DownloadProgressState : INotifyPropertyChanged
{
    private bool _isActive;
    private bool _isInstalling;
    private double _value;
    private bool _isIndeterminate;
    private string _detailText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Visibility Visibility => _isActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility =>
        string.IsNullOrEmpty(_detailText) ? Visibility.Collapsed : Visibility.Visible;

    public double Value
    {
        get => _value;
        private set
        {
            if (Math.Abs(_value - value) < 0.01) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (_isIndeterminate == value) return;
            _isIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public string DetailText
    {
        get => _detailText;
        private set
        {
            if (_detailText == value) return;
            _detailText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailVisibility));
        }
    }

    public void Report(long bytesReceived, long totalBytes, double bytesPerSecond)
    {
        if (_isInstalling) return;
        Activate();
        IsIndeterminate = totalBytes <= 0;
        Value = totalBytes > 0
            ? Math.Clamp(100d * bytesReceived / totalBytes, 0d, 100d)
            : 0d;
        DetailText = DownloadProgressText.Format(bytesReceived, totalBytes, bytesPerSecond);
    }

    public void ShowInstalling()
    {
        Activate();
        _isInstalling = true;
        IsIndeterminate = true;
        Value = 0d;
        DetailText = string.Empty;
    }

    public void Reset()
    {
        if (!_isActive && !_isInstalling && string.IsNullOrEmpty(_detailText) && !_isIndeterminate)
        {
            return;
        }

        _isActive = false;
        _isInstalling = false;
        IsIndeterminate = false;
        Value = 0d;
        DetailText = string.Empty;
        OnPropertyChanged(nameof(Visibility));
    }

    private void Activate()
    {
        if (_isActive) return;
        _isActive = true;
        OnPropertyChanged(nameof(Visibility));
    }

    private void OnPropertyChanged(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
