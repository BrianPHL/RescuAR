# Navigation & Routing Module Implementation Guide

This document outlines how the dual online/offline routing architecture works in the `RescuAR` app and what steps remain to fully implement it.

## 🏗️ Architecture Overview

The Navigation module uses a **Strategy/Facade Pattern** to ensure the app can seamlessly switch between Online (OSRM) and Offline (Local Map Data + A*) routing without the rest of the application needing to care about the device's internet connection.

### 1. `IRoutingProvider.cs`
The shared contract for any routing logic. It guarantees that any service implementing it will provide a `CalculateRouteAsync` method.

### 2. `OSRMService.cs` (Online)
Handles routing when the device has an active internet connection.
- **Technology**: OSRM (Open Source Routing Machine).
- **Algorithm**: MLD (Multi-Level Dijkstra) executed on a remote OSRM server.
- **To-Do**: Implement the `HttpClient` logic inside `CalculateRouteAsync` to ping your OSRM backend API.

### 3. `OfflineRoutingService.cs` (Offline)
Handles routing when the device loses connection.
- **Technology**: `OsmSharp` library parsing a local `osm.pbf` file.
- **Algorithm**: Custom A* implementation traversing the nodes and ways from the PBF map data.
- **To-Do**: Implement the A* algorithm in `CalculateRouteAsync` using the extracted map graph.

### 4. `RoutingService.cs` (The Facade)
The **ONLY** service that other modules (like ViewModels) should interact with. It takes both providers and automatically delegates the routing request based on network connectivity.
- **To-Do**: Hook up the MAUI Connectivity API (`Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess`) to determine the `isOnline` boolean.

---

## 🚀 How to use this in other Modules

If you are building a new Page (e.g., `MapPage`) or a ViewModel, **DO NOT** inject `OSRMService` or `OfflineRoutingService`. Instead, always inject the `RoutingService`.

**Example:**
```csharp
public class MapViewModel
{
    private readonly RoutingService _routingService;

    public MapViewModel(RoutingService routingService)
    {
        _routingService = routingService;
    }

    public async Task GetDirections(Location start, Location end)
    {
        // The RoutingService automatically decides if it should use OSRM or A*!
        var route = await _routingService.CalculateRouteAsync(start, end);
        DrawRouteOnMap(route);
    }
}
```

---

## 🛠️ What needs to be implemented next?

To make sure you don't get lost, follow this checklist of remaining tasks:

- [ ] **1. Setup Dependency Injection**
  Open `MauiProgram.cs` and add the services to the DI container:
  ```csharp
  builder.Services.AddSingleton<OSRMService>();
  builder.Services.AddSingleton<OfflineRoutingService>();
  builder.Services.AddSingleton<RoutingService>();
  ```

- [ ] **2. Initialize Map Data at Startup**
  The `osm.pbf` file is bundled inside the app but needs to be extracted to local storage before `OsmSharp` can read it.
  In `App.xaml.cs` (or your Splash/Loading Screen), inject `OfflineRoutingService` and call:
  ```csharp
  await offlineRoutingService.InitializeMapDataAsync();
  ```

- [ ] **3. Implement the Offline A* Algorithm**
  Inside `OfflineRoutingService.cs`, use `OsmSharp` to parse `_localMapPath`, build a graph of traversable nodes, and write the A* search logic.

- [ ] **4. Implement the Online OSRM Call**
  Inside `OSRMService.cs`, use an `HttpClient` to format coordinates and send a request to your OSRM backend (e.g., `http://your-osrm-server/route/v1/driving/long,lat;long,lat`). Deserialize the JSON response into your route object.

- [ ] **5. Implement the Network Check**
  Inside `RoutingService.cs`, update the hardcoded `bool isOnline = true;` to use MAUI's actual network status:
  ```csharp
  bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
  ```
