using System;

namespace RescuAR.App.Models;

public class DashboardCarouselItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public string ImageSource { get; set; } = string.Empty;
    public string IconData { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty; // "Checklist", "Camera", "LearnMore"
}
