using System.Collections.ObjectModel;

namespace RescuAR.App.Models;

public class QuickActionPage
{
    public ObservableCollection<QuickActionItem> Items { get; set; } = new();
}
