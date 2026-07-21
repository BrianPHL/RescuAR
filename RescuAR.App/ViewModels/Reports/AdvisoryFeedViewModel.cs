using System.Collections.ObjectModel;
using System.Windows.Input;
using RescuAR.App.Models;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Reports;

public class AdvisoryFeedViewModel : BindableObject
{
    public ObservableCollection<CommunityReport> Reports { get; } = new();

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                FilterReports();
        }
    }

    private string _sortOrder = "Newest";
    public string SortOrder
    {
        get => _sortOrder;
        set
        {
            if (SetProperty(ref _sortOrder, value))
                SortReports();
        }
    }

    public ICommand ViewReportCommand { get; }
    public ICommand CreateReportCommand { get; }

    public AdvisoryFeedViewModel()
    {
        // Seed mock data
        Reports.Add(new CommunityReport
        {
            Id = 1,
            Title = "Road Blockage Near Main St",
            Description = "A fallen tree is blocking the road. Use alternate route.",
            HazardType = "Road Blockage",
            Address = "Main St & 2nd Ave",
            DistanceMeters = 850,
            PostedBy = "John Doe",
            PostedAt = DateTime.Now.AddHours(-2),
            ImageUrl = "https://via.placeholder.com/300x200.png?text=Road+Blockage"
        });
        Reports.Add(new CommunityReport
        {
            Id = 2,
            Title = "Flooding in Riverside Park",
            Description = "Water level rising rapidly, avoid the area.",
            HazardType = "Flooding",
            Address = "Riverside Park",
            DistanceMeters = 2500,
            PostedBy = "Jane Smith",
            PostedAt = DateTime.Now.AddHours(-5),
            ImageUrl = "https://via.placeholder.com/300x200.png?text=Flooding"
        });

        ViewReportCommand = new Command<CommunityReport>(OnViewReport);
        CreateReportCommand = new Command(OnCreateReport);

        SortReports();
    }

    private void OnViewReport(CommunityReport report)
    {
        // Navigate to detail page with route and report id
        Shell.Current.GoToAsync($"ReportDetailPage?reportId={report.Id}");
    }

    private void OnCreateReport()
    {
        Shell.Current.GoToAsync("CreateReportPage");
    }

    private void FilterReports()
    {
        // Simple filter: refresh collection based on SearchQuery (case‑insensitive)
        var filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? Reports
            : new ObservableCollection<CommunityReport>(Reports.Where(r => r.Title.Contains(SearchQuery, System.StringComparison.OrdinalIgnoreCase) || r.Description.Contains(SearchQuery, System.StringComparison.OrdinalIgnoreCase)));
        Reports.Clear();
        foreach (var r in filtered)
            Reports.Add(r);
    }

    private void SortReports()
    {
        var sorted = SortOrder == "Oldest"
            ? Reports.OrderBy(r => r.PostedAt)
            : Reports.OrderByDescending(r => r.PostedAt);
        Reports.Clear();
        foreach (var r in sorted)
            Reports.Add(r);
    }

    protected bool SetProperty<T>(ref T backingStore, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;
        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
