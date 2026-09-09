using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Models;

[Table("monitoring_stations")]
public class MonitoringStation : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("station_name")]
    public string StationName { get; set; } = string.Empty;

    [Column("level")]
    public double Level { get; set; }

    [Column("status")]
    public string Status { get; set; } = string.Empty;

    [Column("is_main_node")]
    public bool IsMainNode { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
