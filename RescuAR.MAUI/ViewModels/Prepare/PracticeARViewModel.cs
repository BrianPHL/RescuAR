using CommunityToolkit.Mvvm.ComponentModel;

namespace RescuAR.App.ViewModels.Prepare;

public partial class PracticeARViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "AR Practice Drill";
}
