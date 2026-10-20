using System;
using System.Collections.Generic;
using RescuAR.App.Models;
using RescuAR.App.Services.Storage;

public static class EvacuationCenterRepository
{
    private static readonly List<EvacuationCenter> DefaultMarikinaCenters =
    [
        new() { Id = 1, Name = "Malanday Elementary School", Latitude = 14.650283, Longitude = 121.094409, Capacity = 1200, Status = "Standby" },
        new() { Id = 2, Name = "H. Bautista Elementary School", Latitude = 14.657914, Longitude = 121.104240, Capacity = 1000, Status = "Standby" },
        new() { Id = 3, Name = "Nangka Elementary School", Latitude = 14.672991, Longitude = 121.108440, Capacity = 1500, Status = "Standby" },
        new() { Id = 4, Name = "Concepcion Elementary School", Latitude = 14.647648, Longitude = 121.103974, Capacity = 1100, Status = "Standby" },
        new() { Id = 5, Name = "Sto. Niño Elementary School", Latitude = 14.638324, Longitude = 121.098368, Capacity = 1300, Status = "Standby" },
        new() { Id = 6, Name = "St. Mary Elem. School", Latitude = 14.668643, Longitude = 121.113418, Capacity = 600, Status = "Standby" },
        new() { Id = 7, Name = "Concepcion Integrated School ES", Latitude = 14.649954, Longitude = 121.101893, Capacity = 1800, Status = "Standby" },
        new() { Id = 8, Name = "Fortune Elem. School Fields", Latitude = 14.655000, Longitude = 121.115000, Capacity = 700, Status = "Standby" },
        new() { Id = 9, Name = "San Roque High School", Latitude = 14.622798, Longitude = 121.097105, Capacity = 500, Status = "Standby" },
        new() { Id = 10, Name = "Marikina Sports Center", Latitude = 14.635800, Longitude = 121.096500, Capacity = 2000, Status = "Open" },
        new() { Id = 11, Name = "Marikina City Hall", Latitude = 14.650700, Longitude = 121.102900, Capacity = 800, Status = "Standby" },
        new() { Id = 12, Name = "Riverbanks Center", Latitude = 14.635000, Longitude = 121.082400, Capacity = 1500, Status = "Standby" },
        new() { Id = 13, Name = "Bulelak Gym", Latitude = 14.652100, Longitude = 121.097800, Capacity = 600, Status = "Standby" },
        new() { Id = 14, Name = "Barangka Elementary School", Latitude = 14.631800, Longitude = 121.087200, Capacity = 900, Status = "Standby" },
        new() { Id = 15, Name = "Parang High School", Latitude = 14.664000, Longitude = 121.111800, Capacity = 1000, Status = "Standby" },
        new() { Id = 16, Name = "Tumana Evacuation Center", Latitude = 14.658200, Longitude = 121.091500, Capacity = 1400, Status = "Standby" }
    ];

    public static List<EvacuationCenter> GetEvacuationCenters()
    {
        try
        {
            var cached = CacheService.LoadEvacuationCenters();
            if (cached?.Centers != null && cached.Centers.Count > 0)
            {
                var list = new List<EvacuationCenter>();
                int idCounter = 1;
                foreach (var c in cached.Centers)
                {
                    if (c.Latitude != 0 || c.Longitude != 0)
                    {
                        list.Add(new EvacuationCenter
                        {
                            Id = int.TryParse(c.Id, out var id) ? id : idCounter++,
                            Name = c.Name,
                            Latitude = c.Latitude,
                            Longitude = c.Longitude,
                            Capacity = c.Capacity,
                            Occupancy = c.CurrentEvacuees,
                            Status = string.IsNullOrWhiteSpace(c.Status) ? "Standby" : c.Status
                        });
                    }
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        return new List<EvacuationCenter>(DefaultMarikinaCenters);
    }
}
