using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RescuAR.App.Services.Navigation
{
    public interface IRoutingProvider
    {
        /// <summary>
        /// Calculates a route between a start and end location.
        /// (You can update the parameter types to your actual Location/Coordinate classes)
        /// </summary>
        Task<object> CalculateRouteAsync(object startCoordinate, object endCoordinate);
    }
}
