using System.Threading.Tasks;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Services.Navigation;

namespace RescuAR.App.ViewModels.Map
{
    public class MapViewModel
    {
        private readonly RoutingService _routing;

        public MapViewModel(RoutingService routing)
        {
            _routing = routing;
        }

        public async Task ShowRouteAsync(Location start, Location end)
        {
            var route = await _routing.CalculateRouteAsync(start, end);
            // TODO: render route on map control
        }
    }
}
