# RescuAR Navigation Routing Module

This directory contains the core routing implementations for RescuAR, adhering to the [`IRoutingService`](IRoutingService.cs) contract.

---

## 1. Routing Architecture Overview

Routing in RescuAR follows the **Strategy Pattern**. Higher-level modules (Map Rendering, AR Projection, ViewModels, Hazard Monitoring) depend exclusively on the `IRoutingService` interface rather than concrete pathfinding implementations.

```
                               ┌────────────────────────────────┐
                               │  Consumers (ViewModels, AR)   │
                               └───────────────┬────────────────┘
                                               │ Consumes
                                               ▼
                               ┌────────────────────────────────┐
                               │        IRoutingService         │
                               └───────────────┬────────────────┘
                                               │
               ┌───────────────────────────────┴───────────────────────────────┐
               ▼                                                               ▼
┌───────────────────────────────┐                               ┌───────────────────────────────┐
│      AStarRoutingService      │                               │      MLDRoutingService        │
│   (Offline Pedestrian A*)     │                               │    (Online OSRM MLD Server)   │
└──────────────┬────────────────┘                               └──────────────┬────────────────┘
               │ Loads                                                         │ HTTP REST
               ▼                                                               ▼
┌───────────────────────────────┐                               ┌───────────────────────────────┐
│     In-Memory RoadGraph       │                               │   Railway OSRM Instance       │
│     (ROADS.geojson)           │                               │   (Multi-Level Dijkstra)      │
└───────────────────────────────┘                               └───────────────────────────────┘
```

---

## 2. Deep Dive: A* vs MLD Algorithms

| Dimension | **Offline A\* Algorithm (`AStarRoutingService`)** | **Online MLD Algorithm (`MLDRoutingService`)** |
| :--- | :--- | :--- |
| **Execution Environment** | On-Device Local Memory (100% Offline) | Remote Cloud Server via HTTP REST API |
| **Data Source** | Embedded [`ROADS.geojson`](../Data/Resources/ROADS.geojson) dataset | Railway-hosted preprocessed OSRM binary graph |
| **Core Mechanism** | Min-Heap Priority Queue evaluated with Haversine heuristic (\(f = g + h\)) | Pre-partitioned Multi-Level Dijkstra (MLD) hierarchy |
| **Use Case** | Emergency scenarios without cellular/internet connectivity | Online navigation with real-time server-side map data |
| **Network Dependency** | Zero internet required | Requires active network connection |

---

## 3. Algorithm Specifications & Functions

### A. Offline A\* Engine (`AStarRoutingService.cs`)

* **Primary Function**: Calculates optimal pedestrian routes locally using the in-memory [`RoadGraph`](../Models/RoadGraph.cs).
* **Key Steps**:
  1. **Node Snapping**: Locates nearest `RoadNode` for origin and destination `GeoCoordinate`s.
  2. **\(f(n) = g(n) + h(n)\) Evaluation**:
     - \(g(n)\): Accumulated walking distance / edge cost.
     - \(h(n)\): Haversine straight-line distance heuristic (`node.Coordinate.DistanceTo(target.Coordinate)`).
  3. **Path Reconstruction**: Rebuilds ordered [`RoutePoint`](../Models/RoutePoint.cs) waypoints and calculates cumulative distance.

### B. Online MLD Engine (`MLDRoutingService.cs`)

* **Primary Function**: Interfaces with the Railway-hosted OSRM engine (`https://rescuar-production.up.railway.app`) preprocessed with Multi-Level Dijkstra.
* **Key Steps**:
  1. Formulates HTTP GET requests formatted for foot profiles (`/route/v1/foot/...`).
  2. Queries primary or fallback server endpoints (`PrimaryBaseUrl` / `FallbackBaseUrl`).
  3. Deserializes OSRM GeoJSON polyline geometry into canonical [`RouteResult`](../Models/RouteResult.cs).

---

## 4. Accessing & Implementing Routing in Other Modules

### Method A: Consuming Routing in ViewModels / Services

Inject `IRoutingService` into target classes (e.g., `MapViewModel`):

```csharp
using System.Threading.Tasks;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Routing;

public class MapViewModel
{
    private readonly IRoutingService _routingService;

    public MapViewModel(IRoutingService routingService)
    {
        _routingService = routingService;
    }

    public async Task CalculateRouteToEvacuationCenterAsync(
        GeoCoordinate userPosition, 
        GeoCoordinate centerPosition)
    {
        // Compute route regardless of underlying algorithm
        RouteResult? route = await _routingService.FindRouteAsync(userPosition, centerPosition);

        if (route == null)
        {
            // Handle unreachable destination
            return;
        }

        System.Console.WriteLine($"Algorithm: {route.Algorithm}");
        System.Console.WriteLine($"Total Distance: {route.TotalDistanceMeters:F1} m");

        // Pass waypoints to Map display or AR Overlay
        foreach (RoutePoint point in route.Points)
        {
            RenderWaypointOnMap(point.Coordinate.Latitude, point.Coordinate.Longitude);
        }
    }
}
```

---

### Method B: Hybrid Online/Offline Fallback Manager

For disaster management, implement a resilience wrapper that attempts online MLD routing first, automatically falling back to offline A* pathfinding when network connection fails:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

public sealed class HybridRoutingManager : IRoutingService
{
    private readonly MLDRoutingService _mldOnline;
    private readonly AStarRoutingService _aStarOffline;
    private readonly Func<bool> _isNetworkAvailable;

    public string AlgorithmName => _isNetworkAvailable() ? _mldOnline.AlgorithmName : _aStarOffline.AlgorithmName;

    public HybridRoutingManager(
        MLDRoutingService mldOnline,
        AStarRoutingService aStarOffline,
        Func<bool> isNetworkAvailable)
    {
        _mldOnline = mldOnline;
        _aStarOffline = aStarOffline;
        _isNetworkAvailable = isNetworkAvailable;
    }

    public async Task<RouteResult?> FindRouteAsync(
        GeoCoordinate origin, 
        GeoCoordinate destination, 
        CancellationToken cancellationToken = default)
    {
        if (_isNetworkAvailable())
        {
            try
            {
                RouteResult? onlineRoute = await _mldOnline.FindRouteAsync(origin, destination, cancellationToken);
                if (onlineRoute != null)
                {
                    return onlineRoute;
                }
            }
            catch
            {
                // Fall through to offline search on network timeout or exception
            }
        }

        // Offline A* routing execution
        return await _aStarOffline.FindRouteAsync(origin, destination, cancellationToken);
    }
}
```

---

### Method C: Converting Routes to AR World Space (`MLDARIntegrationService.cs`)

To render calculated routes in Augmented Reality (3D world space):

```csharp
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Routing;

public class ARNavigationPresenter
{
    private readonly MLDARIntegrationService _arIntegrationService;

    public ARNavigationPresenter(MLDARIntegrationService arIntegrationService)
    {
        _arIntegrationService = arIntegrationService;
    }

    public void ProjectRouteToAR(RouteResult route, GeoCoordinate userCurrentLocation)
    {
        // Transforms GeoCoordinates into AR 3D World Vector points (Evergine / Unity)
        var arWaypoints = _arIntegrationService.TransformRouteToARPoints(route, userCurrentLocation);
        
        // Render 3D arrows/path in camera view
    }
}
```

---

### Method D: Dependency Injection Setup (`MauiProgram.cs`)

Register services during application startup:

```csharp
public static MauiProgram CreateMauiProgram()
{
    var builder = MauiProgram.CreateBuilder();

    // 1. Build and register in-memory RoadGraph singleton for offline A*
    builder.Services.AddSingleton<RoadGraph>(sp => 
    {
        string geoJson = File.ReadAllText("Path/To/ROADS.geojson");
        var loader = new GeoJsonRoadLoader();
        var features = loader.LoadRoadFeatures(geoJson);
        return new RoadGraphBuilder().BuildGraph(features);
    });

    // 2. Register Routing Implementations
    builder.Services.AddSingleton<AStarRoutingService>();
    builder.Services.AddSingleton<MLDRoutingService>();

    // 3. Register primary IRoutingService boundary
    builder.Services.AddSingleton<IRoutingService, AStarRoutingService>();

    return builder.Build();
}
```

---

## 5. File Manifest

| File | Type | Description |
| :--- | :--- | :--- |
| [`IRoutingService.cs`](IRoutingService.cs) | Interface | Core contract implemented by all routing engines. |
| [`AStarRoutingService.cs`](AStarRoutingService.cs) | Class | Offline A\* search algorithm engine. |
| [`MLDRoutingService.cs`](MLDRoutingService.cs) | Class | Online adapter for Railway OSRM MLD REST API. |
| [`MLDARIntegrationService.cs`](MLDARIntegrationService.cs) | Class | Transforms GPS route points to 3D AR coordinates. |
| [`MLDRoutingDiagnostic.cs`](MLDRoutingDiagnostic.cs) | Class | Health and latency diagnostics for online routing. |
