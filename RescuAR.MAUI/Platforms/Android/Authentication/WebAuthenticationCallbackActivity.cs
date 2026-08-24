using Android.App;
using Android.Content;
using Android.Content.PM;

namespace RescuAR.MAUI.Platforms.Android.Authentication;

[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    new[]
    {
        Intent.ActionView
    },
    Categories = new[]
    {
        Intent.CategoryDefault,
        Intent.CategoryBrowsable
    },
    DataScheme = CallbackScheme)]
public class WebAuthenticationCallbackActivity
    : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
    private const string CallbackScheme =
        "rescuar";
}
