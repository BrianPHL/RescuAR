// Platform-independent model boundary for testing the actual flood presentation service.
// The service only reads these properties; production uses the Supabase-backed model.
namespace RescuAR.App.Models;
public sealed class DisasterAdvisory
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public double WaterLevel { get; set; }
    public string Id { get; set; } = "";
    public string DisplayAffectedArea { get; set; } = "Test area";
}
