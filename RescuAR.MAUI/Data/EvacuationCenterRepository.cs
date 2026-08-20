using RescuAR.App.Models;

public static class EvacuationCenterRepository
{
    public static List<EvacuationCenter> GetEvacuationCenters()
    {
        return
        [
            new()
            {
                Name = "Marikina Sports Center",
                Latitude = 14.6358,
                Longitude = 121.0965
            },

            new()
            {
                Name = "Marikina City Hall",
                Latitude = 14.6507,
                Longitude = 121.1029
            },

            new()
            {
                Name = "Riverbanks Center",
                Latitude = 14.6350,
                Longitude = 121.0824
            }
        ];
    }
}
