using RescuAR.App.Models;
using RescuAR.App.Services.Evacuation;

public static class EvacuationCenterRepository
{
    public static List<EvacuationCenter> GetEvacuationCenters(EvacuationDirectorySnapshot? snapshot = null) =>
        (snapshot ?? EvacuationDirectoryRuntime.Current.Current).Centers.Select(center => new EvacuationCenter
        {
            Id = center.Id, Name = center.Name, Latitude = center.Latitude, Longitude = center.Longitude,
            Capacity = center.Capacity, Occupancy = center.CurrentEvacuees, Status = center.Status
        }).ToList();
}
