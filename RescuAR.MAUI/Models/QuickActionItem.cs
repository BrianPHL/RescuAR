using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;
using Newtonsoft.Json;

namespace RescuAR.App.Models;

public class QuickActionItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _id = Guid.NewGuid().ToString();
    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private string _subtitle = string.Empty;
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; OnPropertyChanged(); }
    }

    private string _iconImage = "icon_hotlines.svg";
    public string IconImage
    {
        get => string.IsNullOrWhiteSpace(_iconImage) ? "icon_hotlines.svg" : _iconImage;
        set { _iconImage = value; OnPropertyChanged(); }
    }

    private string _iconData = "M9,2A1,1 0 0,0 8,3V8.5L10.5,11V21A1,1 0 0,0 11.5,22H12.5A1,1 0 0,0 13.5,21V11L16,8.5V3A1,1 0 0,0 15,2H9M10,4H14V6H10V4Z";
    public string IconData
    {
        get => _iconData;
        set
        {
            _iconData = value;
            _cachedGeometry = null;
            _lastParsedIconData = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconGeometry));
        }
    }

    [JsonIgnore]
    private Geometry? _cachedGeometry;
    [JsonIgnore]
    private string _lastParsedIconData = string.Empty;

    [JsonIgnore]
    public Geometry? IconGeometry
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconData))
                return null;

            if (_cachedGeometry != null && _lastParsedIconData == IconData)
                return _cachedGeometry;

            try
            {
                var converter = new PathGeometryConverter();
                _cachedGeometry = converter.ConvertFromInvariantString(IconData) as Geometry;
                _lastParsedIconData = IconData;
                return _cachedGeometry;
            }
            catch
            {
                return null;
            }
        }
    }

    private string _iconBg = "#F1F5F9";
    public string IconBg
    {
        get => _iconBg;
        set
        {
            _iconBg = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconBgBrush));
        }
    }

    [JsonIgnore]
    public Microsoft.Maui.Graphics.Color IconBgBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconBg))
                return Microsoft.Maui.Graphics.Color.FromArgb("#F1F5F9");
            try
            {
                return Microsoft.Maui.Graphics.Color.FromArgb(IconBg);
            }
            catch
            {
                return Microsoft.Maui.Graphics.Color.FromArgb("#F1F5F9");
            }
        }
    }

    private string _iconColor = "#0A8491";
    public string IconColor
    {
        get => _iconColor;
        set
        {
            _iconColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconColorBrush));
        }
    }

    [JsonIgnore]
    public Microsoft.Maui.Graphics.Color IconColorBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconColor))
                return Microsoft.Maui.Graphics.Color.FromArgb("#0A8491");
            try
            {
                return Microsoft.Maui.Graphics.Color.FromArgb(IconColor);
            }
            catch
            {
                return Microsoft.Maui.Graphics.Color.FromArgb("#0A8491");
            }
        }
    }

    private string _actionType = "Route";
    public string ActionType
    {
        get => _actionType ?? "Route";
        set { _actionType = value; OnPropertyChanged(); }
    }

    private string _targetRoute = string.Empty;
    public string TargetRoute
    {
        get => _targetRoute ?? string.Empty;
        set { _targetRoute = value; OnPropertyChanged(); }
    }

    private string _phoneNumber = string.Empty;
    public string PhoneNumber
    {
        get => _phoneNumber;
        set { _phoneNumber = value; OnPropertyChanged(); }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    private bool _isCustom;
    public bool IsCustom
    {
        get => _isCustom;
        set { _isCustom = value; OnPropertyChanged(); }
    }

    private bool _isActiveState;
    public bool IsActiveState
    {
        get => _isActiveState;
        set { _isActiveState = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string DisplayIconText => ActionType == "PhoneCall" || ActionType == "CustomContact" ? "📞" : "⚡";
}
