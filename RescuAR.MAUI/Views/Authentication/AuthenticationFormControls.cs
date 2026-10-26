namespace RescuAR.App.Views.Authentication;

// Scope Athena's border styling to imported authentication fields.
public sealed class AuthenticationEntry : Entry
{
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is global::Android.Views.View native)
        {
            native.Background = null;
            native.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        }
#endif
    }
}

public sealed class AuthenticationPicker : Picker
{
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is global::Android.Views.View native)
        {
            native.Background = null;
            native.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        }
#endif
    }
}

public sealed class AuthenticationDatePicker : DatePicker
{
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is global::Android.Views.View native)
        {
            native.Background = null;
            native.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        }
#endif
    }
}
