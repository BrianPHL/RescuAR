using System.Threading.Tasks;
using Microsoft.Maui.Networking;

namespace RescuAR.App.Services.Navigation
{
    public class RoutingService : IRoutingProvider
    {
        private readonly OSRMService _onlineProvider;
        private readonly OfflineRoutingService _offlineProvider;

        public RoutingService(OSRMService onlineProvider, OfflineRoutingService offlineProvider)
        {
            _onlineProvider = onlineProvider;
            _offlineProvider = offlineProvider;
        }

        public async Task<object> CalculateRouteAsync(object startCoordinate, object endCoordinate)
        {
            bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            if (isOnline)
            {
                return await _onlineProvider.CalculateRouteAsync(startCoordinate, endCoordinate);
            }
            else
            {
                return await _offlineProvider.CalculateRouteAsync(startCoordinate, endCoordinate);
            }
        }
    }
}
