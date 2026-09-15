using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;

namespace RescuAR.App.Views.Prepare;

public partial class PdfViewerPage : ContentPage, IQueryAttributable
{
    public PdfViewerPage()
    {
        InitializeComponent();
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("pdfPath", out var encodedPathObj) && encodedPathObj is string encodedPath)
        {
            try
            {
                var path = Uri.UnescapeDataString(encodedPath);
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    Title = "Disaster Safety Guide PDF",
                    File = new ReadOnlyFile(path)
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load PDF in viewer: {ex.Message}");
                _ = Shell.Current?.DisplayAlert("Error", "Unable to display PDF.", "OK");
            }
        }
    }
}
