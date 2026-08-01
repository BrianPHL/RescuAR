# 🧭 RescuAR Navigation & Routing Module

**Location:** `RescuAR.App/Services/Navigation/`  
**Branch:** `Josh/Navigation`  
**Last updated:** 2026-07-21

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [File Map](#file-map)
4. [How MLD (Online Routing) Works](#how-mld-online-routing-works)
5. [How Offline Routing Works](#how-offline-routing-works)
6. [How to Consume This Module in Your Feature](#how-to-consume-this-module-in-your-feature)
7. [What Has Been Implemented](#what-has-been-implemented)
8. [What Still Needs to Be Done](#what-still-needs-to-be-done)
9. [Change Log](#change-log)

---

## Overview

The Navigation module gives any part of the RescuAR app the ability to calculate a route between two GPS coordinates. It automatically switches between two strategies depending on the device's internet connection:

| Connection | Algorithm | Technology |
|---|---|---|
| ✅ Online | **MLD** (Multi-Level Dijkstra) | OSRM HTTP API (Docker) |
| ❌ Offline | **A\*** (custom graph search) | OsmSharp + local `osm.pbf` |

The rest of the app never needs to know which strategy is active — just call `RoutingService` and let the facade handle it.

---

## Architecture

The module follows the **Strategy + Facade** design pattern.

```
┌──────────────────────────────────────────────┐
│            Other Modules / ViewModels         │
│     (MapViewModel, EmergencyViewModel, …)     │
└─────────────────────┬────────────────────────┘
                      │ injects & calls
                      ▼
          ┌───────────────────────┐
          │     RoutingService    │  ◄── The ONLY class you should call
          │       (Facade)        │
          └──────────┬────────────┘
                     │ checks Connectivity.Current
           ┌─────────┴───────────┐
           │                     │
  isOnline = true       isOnline = false
           │                     │
           ▼                     ▼
  ┌──────────────┐     ┌──────────────────────┐
  │  OSRMService │     │ OfflineRoutingService │
  │  (Online)    │     │  (Offline / A*)       │
  └──────┬───────┘     └──────────┬────────────┘
         │                        │
         ▼                        ▼
  OSRM Docker server        osm.pbf (local file)
  (MLD algorithm)           (OsmSharp graph)
```

Both `OSRMService` and `OfflineRoutingService` implement the **`IRoutingProvider`** interface, which guarantees they both have exactly one method:

```csharp
Task<object> CalculateRouteAsync(object startCoordinate, object endCoordinate);
```

---

## File Map

```
Services/Navigation/
│
├── IRoutingProvider.cs          ← The shared contract (interface)
├── OSRMService.cs               ← Online routing via OSRM HTTP API (MLD)
├── OfflineRoutingService.cs     ← Offline routing via OsmSharp + A*
├── RoutingService.cs            ← Facade — the only class modules should use
├── HazardReroutingService.cs    ← (Planned) Reroute around marked hazard zones
├── Navigation_Implementation_Guide.md  ← Original dev notes & checklist
└── README.md                    ← This file
```

---

## How MLD (Online Routing) Works

**MLD = Multi-Level Dijkstra** — a hierarchical shortest-path algorithm used by OSRM. It pre-processes a road network into multiple levels (like a highway vs. a side street) to allow extremely fast query times on large maps.

### Infrastructure

The MLD server runs as a **Docker container** on `http://127.0.0.1:5000` (localhost). It was set up separately using the `osrm-backend` Docker image with a pre-processed `.osm.pbf` file.

> **To start the OSRM Docker server before testing:**
> ```bash
> docker start <your-osrm-container-name>
> ```

### What `OSRMService` Does

1. Receives `startCoordinate` and `endCoordinate` as `Microsoft.Maui.Devices.Sensors.Location` objects.
2. Builds an OSRM HTTP request URL:
   ```
   GET http://127.0.0.1:5000/route/v1/driving/{lon1},{lat1};{lon2},{lat2}?overview=full&geometries=geojson
   ```
   > ⚠️ OSRM uses **longitude, latitude** order — not the usual lat/lon.
3. Sends the request with `HttpClient`.
4. Returns a `JsonDocument` of the raw OSRM response. A future DTO will be added.

### OSRM Response Shape (for reference)

```json
{
  "routes": [{
    "distance": 1234.5,
    "duration": 234.1,
    "geometry": {
      "type": "LineString",
      "coordinates": [[lon, lat], [lon, lat], ...]
    }
  }],
  "waypoints": [...]
}
```

---

## How Offline Routing Works

When there is no internet, the app falls back to a local map file.

### Step 1 — Initialization (done at app startup)

```csharp
await offlineRoutingService.InitializeMapDataAsync();
```

This copies `osm.pbf` from the app bundle to `FileSystem.CacheDirectory` on the device. It only runs the copy once; subsequent launches skip it if the file already exists.

### Step 2 — Graph Loading

```csharp
offlineRoutingService.LoadRoutingGraph();
```

*(Not yet implemented — see [What Still Needs to Be Done](#what-still-needs-to-be-done))*  
Uses `OsmSharp` to parse the PBF file and build a graph of traversable road nodes and edges.

### Step 3 — A* Route Calculation

When `CalculateRouteAsync` is called and the device is offline, the service runs an A* search over the pre-loaded graph, returning an ordered list of coordinates forming the route.

---

## How to Consume This Module in Your Feature

> **Rule:** Never inject `OSRMService` or `OfflineRoutingService` directly. Always use `RoutingService`.

### Step 1 — Inject `RoutingService` into your ViewModel

```csharp
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Services.Navigation;

public class YourViewModel
{
    private readonly RoutingService _routing;

    public YourViewModel(RoutingService routing)
    {
        _routing = routing;
    }

    public async Task GetDirectionsAsync(Location start, Location end)
    {
        // Automatically uses MLD (online) or A* (offline)
        var route = await _routing.CalculateRouteAsync(start, end);

        // Cast the result to JsonDocument to read OSRM fields
        if (route is System.Text.Json.JsonDocument doc)
        {
            var coordinates = doc.RootElement
                .GetProperty("routes")[0]
                .GetProperty("geometry")
                .GetProperty("coordinates");

            // Draw route on map, animate, etc.
        }
    }
}
```

### Step 2 — Register your ViewModel in `MauiProgram.cs`

```csharp
// The three navigation services are already registered:
builder.Services.AddSingleton<OSRMService>();
builder.Services.AddSingleton<OfflineRoutingService>();
builder.Services.AddSingleton<RoutingService>();

// Add YOUR new ViewModel here:
builder.Services.AddTransient<YourViewModel>();
```

### Step 3 — Inject in your Page (XAML code-behind)

```csharp
public partial class YourPage : ContentPage
{
    private readonly YourViewModel _vm;

    public YourPage(YourViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }
}
```

### Step 4 — Get the user's current location

Use MAUI's `Geolocation` API to get a `Location` object for the start coordinate:

```csharp
var userLocation = await Geolocation.Default.GetLastKnownLocationAsync()
                ?? await Geolocation.Default.GetLocationAsync(
                       new GeolocationRequest(GeolocationAccuracy.Medium));
```

---

## What Has Been Implemented

| # | Task | File | Status |
|---|---|---|---|
| 1 | `IRoutingProvider` interface | `IRoutingProvider.cs` | ✅ Done |
| 2 | `OSRMService` — HTTP request to OSRM Docker | `OSRMService.cs` | ✅ Done |
| 3 | `OfflineRoutingService` — map init & file copy | `OfflineRoutingService.cs` | ✅ Done (init only) |
| 4 | `RoutingService` — facade + real connectivity check | `RoutingService.cs` | ✅ Done |
| 5 | DI registration of all three services | `MauiProgram.cs` | ✅ Done |
| 6 | `MapViewModel` — consumes `RoutingService` | `ViewModels/Map/MapViewModel.cs` | ✅ Done (scaffold) |

---

## What Still Needs to Be Done

| # | Task | File | Notes |
|---|---|---|---|
| 1 | **Bundle `osm.pbf` as a MauiAsset** | `RescuAR.App.csproj` | Add `<MauiAsset Include="Resources/Raw/osm.pbf" />` |
| 2 | **Call `InitializeMapDataAsync` at startup** | `App.xaml.cs` | Inject `OfflineRoutingService` and `await` the init before the main page loads |
| 3 | **Implement A\* graph search** | `OfflineRoutingService.cs` | Use OsmSharp to parse PBF into a road graph, run A\* between closest nodes to start/end coords |
| 4 | **Create a typed `RouteDto`** | New: `Models/RouteDto.cs` | Deserialize OSRM JSON into a proper C# model instead of raw `JsonDocument` |
| 5 | **Wire `MapViewModel` to the UI** | `Views/Map/MapPage.xaml` | Call `ShowRouteAsync` and draw the returned coordinates on the map control |
| 6 | **Error handling & fallback UI** | `MapViewModel.cs` | Wrap `CalculateRouteAsync` in try/catch; show user-facing error if both providers fail |
| 7 | **Hazard rerouting** | `HazardReroutingService.cs` | Reroute around disaster zones marked by responders |

---

## Change Log

### 2026-07-21 — `Josh/Navigation` branch

| File | What Changed |
|---|---|
| `OSRMService.cs` | Implemented `CalculateRouteAsync`: real `HttpClient` call to `http://127.0.0.1:5000`, correct `lon,lat` coordinate ordering, returns `JsonDocument`. Fixed broken brace structure and added missing `using` directives. |
| `OfflineRoutingService.cs` | **Restored** after accidental overwrite. Class name is `OfflineRoutingService` (not `OSRMService`). Original `InitializeMapDataAsync` and `LoadRoutingGraph` logic preserved. |
| `RoutingService.cs` | Replaced hardcoded `bool isOnline = true;` stub with `Connectivity.Current.NetworkAccess == NetworkAccess.Internet`. Added `using Microsoft.Maui.Networking;`. |
| `MapViewModel.cs` | Scaffold implemented: injects `RoutingService`, exposes `ShowRouteAsync(Location, Location)`. Fixed brace indentation. Added `using Microsoft.Maui.Devices.Sensors` and `using RescuAR.App.Services.Navigation`. |
| `MauiProgram.cs` | All three navigation services registered as singletons: `OSRMService`, `OfflineRoutingService`, `RoutingService`. |
| `Navigation_Implementation_Guide.md` | Original dev guide retained as-is for historical reference. |
| `README.md` | **Created** as the primary developer reference for this module. |
