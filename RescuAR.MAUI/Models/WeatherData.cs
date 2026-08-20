using System;
using System.Collections.Generic;
using System.Text;

namespace RescuAR.App.Models
{
    public class WeatherData
    {
        public string LocationName { get; set; } = string.Empty;
        public double TemperatureCelsius { get; set; }
        public int WeatherCode { get; set; }
        public string ConditionDescription { get; set; } = string.Empty;
        public string ConditionSummary { get; set; } = string.Empty;
        public string IconPathData { get; set; } = string.Empty;
    }
}
