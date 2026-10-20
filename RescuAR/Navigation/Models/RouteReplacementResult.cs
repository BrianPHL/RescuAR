namespace RescuAR.Navigation.Models;

public enum RouteReplacementFailure { None, NoRoute, DestinationExcluded }

public readonly record struct RouteReplacementResult(RouteResult? Route, RouteReplacementFailure Failure);
