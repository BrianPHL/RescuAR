using Microsoft.Maui.Media;

namespace RescuAR.MAUI.Services.Navigation;

public sealed class MauiNavigationSpeech : INavigationSpeech
{
    public Task SpeakAsync(string text, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TextToSpeech.Default.SpeakAsync(text, cancelToken: cancellationToken);
        });
}
