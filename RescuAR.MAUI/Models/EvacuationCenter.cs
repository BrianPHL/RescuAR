namespace RescuAR.App.Models;

public sealed class EvacuationCenter
{
    public int Id { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int Capacity { get; set; }

    public int Occupancy { get; set; }

    public string Status { get; set; } =
        string.Empty;
}
