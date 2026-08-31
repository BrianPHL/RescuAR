# Railway Deployment & Navigation API Access Guide

This guide explains how team members can access, monitor, manage, and consume the RescuAR Navigation Service deployment on the Railway platform.

---

## Table of Contents
1. [Getting Access to the Project](#1-getting-access-to-the-project)
2. [Exposing the Service (Getting a Public URL)](#2-exposing-the-service-getting-a-public-url)
3. [Monitoring and Troubleshooting](#3-monitoring-and-troubleshooting)
4. [Managing Deployments](#4-managing-deployments)
5. [Environment Variables Setup](#5-environment-variables-setup)
   - [5.1 Web Application (.env in rescuar-web)](#51-web-application-env-in-rescuar-web)
   - [5.2 Mobile Application (.NET MAUI in RescuAR.App)](#52-mobile-application-net-maui-in-rescuarapp)
   - [5.3 Railway Backend Environment Variables](#53-railway-backend-environment-variables)
6. [API Endpoints & Integration Guide](#6-api-endpoints--integration-guide)
   - [6.1 Base Endpoints](#61-base-endpoints)
   - [6.2 Core OSRM API Endpoints](#62-core-osrm-api-endpoints)
   - [6.3 Module-Specific Implementation Guide](#63-module-specific-implementation-guide)

---

## 1. Getting Access to the Project

To allow team members to view and manage the deployment, they must be invited to the Railway project.

**As the Project Owner:**
1. Log in to the [Railway Dashboard](https://railway.app/dashboard).
2. Open the **RescuAR** (or `chic-abundance`) project.
3. Click on the **Settings** gear icon in the top navigation bar.
4. Go to the **Members** tab.
5. Enter your team member's email address or Railway username and click **Invite**.

**As a Team Member:**
1. Check your email for the Railway invitation link.
2. Click the link to accept (you will be prompted to sign in via GitHub, Discord, or Email).
3. Once logged in, the project will appear on your Railway dashboard.

---

## 2. Exposing the Service (Getting a Public URL)

To make the navigation service accessible over the internet:

1. Click on the **perceptive-spontaneity** (Navigation) service block on the project canvas.
2. Go to the **Settings** tab on the right sidebar.
3. Scroll down to the **Networking** section.
4. Click **Generate Domain**. Railway will automatically generate a public `*.up.railway.app` URL for the service.
5. *(Optional)* Click **Custom Domain** if you want to configure a custom domain (e.g., `nav.rescuar.com`).

---

## 3. Monitoring and Troubleshooting

### Viewing Logs
1. Click on the navigation service block on the project canvas.
2. Select the **Deployments** tab.
3. Click **View logs** on the active (green) deployment.
4. Available logs:
   - **Deploy Logs:** Shows runtime logs (e.g., OSRM startup messages, incoming HTTP API requests, or OOM crash errors).
   - **Build Logs:** Shows the Docker build process (`osrm-extract`, `osrm-partition`, `osrm-customize`).

### Viewing Metrics
1. Click on the service block.
2. Go to the **Metrics** tab to inspect **CPU**, **Memory**, and **Network** usage.
   > [!NOTE]
   > Railway's starter tier includes 500MB RAM. If memory usage flatlines near 500MB before a restart, it indicates an Out-Of-Memory (OOM) error.

---

## 4. Managing Deployments

Railway automatically deploys the service whenever a new commit is pushed to the target branch. You can also manage deployments manually:

- **Restart Service:** Click the three dots (`⋮`) next to the active deployment and select **Restart**.
- **Rollback:** Locate a previous successful deployment in the History list, click (`⋮`), and select **Redeploy**.
- **Manual Trigger:** Click **Deploy** in the sidebar to re-trigger a build from the latest commit.

---

## 5. Environment Variables Setup

### 5.1 Web Application (`.env` in `rescuar-web`)
Vite requires client-side environment variables to be prefixed with **`VITE_`**.

#### Step 1: Add to `.env` file (`rescuar-web/.env`)
```env
VITE_SUPABASE_URL=https://itxjqcnvxlgzeqkivhhc.supabase.co
VITE_SUPABASE_ANON_KEY=sb_publishable_vD7hDjeuohyysFweHnJPPQ_xId2pJ4F
VITE_OSRM_API_URL=https://rescuar-production.up.railway.app
```

#### Step 2: Access in React JavaScript / JSX code
```javascript
const OSRM_API_URL = import.meta.env.VITE_OSRM_API_URL;

export const fetchRoute = async (startLng, startLat, endLng, endLat) => {
  const url = `${OSRM_API_URL}/route/v1/driving/${startLng},${startLat};${endLng},${endLat}?overview=full&geometries=geojson`;
  const response = await fetch(url);
  return await response.json();
};
```

---

### 5.2 Mobile Application (.NET MAUI in `RescuAR.App`)

#### Step 1: Add to Constants or Config (`AppConstants.cs`)
```csharp
namespace RescuAR.App
{
    public static class AppConstants
    {
        public const string OsrmBaseUrl = "https://rescuar-production.up.railway.app";
    }
}
```

#### Step 2: Access in Service (`OSRMService.cs`)
```csharp
string url = $"{AppConstants.OsrmBaseUrl}/route/v1/driving/" +
             $"{start.Longitude},{start.Latitude};" +
             $"{end.Longitude},{end.Latitude}" +
             $"?overview=full&geometries=geojson";
```

---

### 5.3 Railway Backend Environment Variables

If configuration updates are needed on the server side (e.g., `PORT` overrides or dataset parameters):
1. Click the navigation service block on the Railway canvas.
2. Select the **Variables** tab.
3. Add a new variable (e.g., `PORT=5000`). Railway will automatically restart the container with the updated environment variables.

---

## 6. API Endpoints & Integration Guide

The Navigation Service runs on Railway using the **OSRM (Open Source Routing Machine)** engine with **MLD (Multi-Level Dijkstra)**. All application modules can consume the service via standard HTTP `GET` requests.

### 6.1 Base Endpoints
- **Primary Domain:** `https://rescuar-production.up.railway.app`
- **Fallback Domain:** `https://rescuar-production-2c22.up.railway.app`
- **Internal Port:** `5000` *(Mapped automatically by Railway to standard HTTPS port 443)*

---

### 6.2 Core OSRM API Endpoints

#### 1. Route Calculation (`/route/v1`)
Calculates the shortest/fastest route between coordinates.
```http
GET /route/v1/driving/{longitude1},{latitude1};{longitude2},{latitude2}?overview=full&geometries=geojson&steps=true
```

#### 2. Distance & Duration Matrix (`/table/v1`)
Calculates travel times and distances between multiple coordinates.
```http
GET /table/v1/driving/{lon1},{lat1};{lon2},{lat2};{lon3},{lat3}?annotations=duration,distance
```

#### 3. Nearest Road Snapping (`/nearest/v1`)
Snaps raw GPS coordinates to the nearest road network node.
```http
GET /nearest/v1/driving/{longitude},{latitude}?number=1
```

---

### 6.3 Module-Specific Implementation Guide

#### 🎥 AR Camera Navigation Overlay
- **Goal:** Project live turn-by-turn navigation paths into the 3D AR Camera view.
- **Endpoint Parameters:** `overview=full&geometries=geojson&steps=true`
- **Workflow:**
  1. Capture device location (`start`) and target destination (`end`).
  2. Request route JSON from the `/route/v1/driving/...` endpoint.
  3. Extract coordinate points array from `routes[0].geometry.coordinates`.
  4. Convert GeoJSON Lat/Lon coordinates into local AR World Space Cartesian vectors $(X, Y, Z)$ relative to camera origin.
  5. Render 3D waypoints/breadcrumbs in the AR viewport.

#### 🗺️ 2D Interactive Map / Dispatch View
- **Goal:** Display route polylines, travel distance, and ETA on 2D map views.
- **Endpoint Parameters:** `overview=simplified&geometries=geojson`
- **Workflow:**
  1. Pass origin and destination coordinates to `/route/v1/driving/...`.
  2. Extract `routes[0].distance` (meters) and `routes[0].duration` (seconds) for UI display.
  3. Feed `routes[0].geometry` into Leaflet / Mapbox / MAUI Map polyline overlays.

#### 🛡️ Safety Circle & Hazard Area Avoidance (MLD Engine)
- **Goal:** Route around danger zones, hazard boundaries, or flooded areas.
- **MLD Capabilities:**
  - **Snapping Radii (`radii`):** Restrict snapping outside safety zones (`radii=50;200`).
  - **Exclusion Classes (`exclude`):** Exclude hazardous road segments (`exclude=hazard_zone`).
  - **Isochrone Distance Matrix (`/table/v1`):** Verify if rescue units or targets fall within a safety radius circle.
