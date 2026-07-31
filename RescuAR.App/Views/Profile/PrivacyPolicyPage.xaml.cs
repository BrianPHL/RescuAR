using System.Globalization;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

/// <summary>
/// Code-behind for the Privacy Policy page.
/// </summary>
public partial class PrivacyPolicyPage : ContentPage
{
    public PrivacyPolicyPage(PrivacyPolicyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

/// <summary>
/// Converts a policy item's number to a boolean visibility for the divider.
/// Divider is hidden for item #1 (first in list) and visible for items #2+.
/// </summary>
public class PolicyNumberToDividerVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int number)
        {
            return number > 1;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
