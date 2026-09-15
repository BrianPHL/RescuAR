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

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("pdfPath", out var encodedPathObj) && encodedPathObj is string encodedPath)
        {
            try
            {
                var path = Uri.UnescapeDataString(encodedPath);
                var uri = new Uri(path);
                PdfWebView.Source = uri;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load PDF in viewer: {ex.Message}");
                _ = Shell.Current?.DisplayAlert("Error", "Unable to display PDF.", "OK");
            }
        }
    }
}
