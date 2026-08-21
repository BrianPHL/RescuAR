using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Map;

public partial class MapViewModel : ObservableObject
{
    [ObservableProperty]
    public partial HtmlWebViewSource MapHtmlSource { get; set; } = null!;

    public MapViewModel()
    {
        InitializeMapHtml();
    }

    private void InitializeMapHtml()
    {
        var html = """
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
    <title>RescuAR Evacuation Map</title>
    <!-- Tailwind CSS -->
    <link href="https://cdn.jsdelivr.net/npm/tailwindcss@2.2.19/dist/tailwind.min.css" rel="stylesheet" />
    <!-- Leaflet CSS -->
    <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
    <style>
        html, body, #map {
            width: 100%;
            height: 100%;
            margin: 0;
            padding: 0;
        }
        /* Custom scrollbar hiding */
        .no-scrollbar::-webkit-scrollbar {
            display: none;
        }
        .no-scrollbar {
            -ms-overflow-style: none;
            scrollbar-width: none;
        }
        /* Style leaflet popups to look native and clean */
        .leaflet-popup-content-wrapper {
            border-radius: 12px;
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
            padding: 4px;
        }
        .leaflet-popup-tip {
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
        }
    </style>
</head>
<body class="relative bg-gray-100">

    <!-- Floating Search & Filtering Panel -->
    <div class="absolute top-4 left-4 right-4 flex flex-col gap-2" style="z-index: 9999;">
        <!-- Search Input -->
        <div class="relative bg-white shadow-md rounded-xl flex items-center p-1.5 border border-gray-200/50">
            <svg class="w-5 h-5 text-gray-400 ml-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"></path>
            </svg>
            <input id="searchInput" type="text" placeholder="Search evacuation centers..." class="w-full px-2.5 py-1 text-sm text-gray-700 bg-transparent outline-none" />
        </div>
        
        <!-- Filter Tabs Horizontal List -->
        <div class="flex gap-1.5 overflow-x-auto pb-1 no-scrollbar">
            <button onclick="filterType('All')" id="btn-All" class="px-3.5 py-1.5 bg-gray-900 text-white rounded-full text-xs font-semibold shadow-sm shrink-0 transition-colors">All</button>
            <button onclick="filterType('Flood-safe')" id="btn-Flood" class="px-3.5 py-1.5 bg-white text-gray-700 rounded-full text-xs font-semibold shadow-sm shrink-0 border border-gray-200/40 transition-colors">Flood-safe</button>
            <button onclick="filterType('Dual-Purpose')" id="btn-Dual" class="px-3.5 py-1.5 bg-white text-gray-700 rounded-full text-xs font-semibold shadow-sm shrink-0 border border-gray-200/40 transition-colors">Dual-Purpose</button>
            <button onclick="filterType('Earthquake-safe')" id="btn-Earthquake" class="px-3.5 py-1.5 bg-white text-gray-700 rounded-full text-xs font-semibold shadow-sm shrink-0 border border-gray-200/40 transition-colors">Earthquake-safe</button>
        </div>
    </div>

    <!-- Map container -->
    <div id="map"></div>

    <!-- Leaflet JS -->
    <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
    <script>
        // List of 50 Marikina Evacuation Centers with coordinates
        var evacuationCenters = [
            // Flood-safe Camp (Major & Minor)
            { name: "Malanday Elementary School", lat: 14.6612, lon: 121.0963, type: "Flood-safe Camp" },
            { name: "H. Bautista Elementary School", lat: 14.6468, lon: 121.0961, type: "Flood-safe Camp" },
            { name: "Nangka Elementary School", lat: 14.6698, lon: 121.1025, type: "Flood-safe Camp" },
            { name: "Concepcion Elementary School", lat: 14.6521, lon: 121.1084, type: "Flood-safe Camp" },
            { name: "Sto. Niño Elementary School", lat: 14.6438, lon: 121.0995, type: "Flood-safe Camp" },
            { name: "Sto. Niño National High School", lat: 14.6445, lon: 121.1005, type: "Flood-safe Camp" },
            { name: "Leodegario Victorino Elementary", lat: 14.6315, lon: 121.0945, type: "Flood-safe Camp" },
            { name: "Bulelak Gym", lat: 14.6598, lon: 121.0925, type: "Flood-safe Camp" },
            { name: "Sampaguita Gym", lat: 14.6335, lon: 121.1188, type: "Flood-safe Camp" },
            { name: "Marikina Elementary School", lat: 14.6291, lon: 121.0978, type: "Flood-safe Camp" },
            { name: "Sta. Elena High School", lat: 14.6278, lon: 121.0991, type: "Flood-safe Camp" },
            { name: "Nangka Gym", lat: 14.6710, lon: 121.1030, type: "Flood-safe Camp" },
            { name: "Kalumpang Elementary School", lat: 14.6212, lon: 121.0905, type: "Flood-safe Camp" },
            { name: "Kalumpang NHS", lat: 14.6218, lon: 121.0912, type: "Flood-safe Camp" },
            { name: "San Roque Elementary School", lat: 14.6258, lon: 121.1042, type: "Flood-safe Camp" },
            { name: "San Roque High School", lat: 14.6262, lon: 121.1048, type: "Flood-safe Camp" },
            { name: "Barangka Elementary School", lat: 14.6348, lon: 121.0825, type: "Flood-safe Camp" },
            { name: "Tañong High School", lat: 14.6368, lon: 121.0935, type: "Flood-safe Camp" },
            { name: "IVS Covered Court", lat: 14.6548, lon: 121.0930, type: "Flood-safe Camp" },
            { name: "Jesus Dela Peña NHS", lat: 14.6375, lon: 121.0915, type: "Flood-safe Camp" },
            { name: "St. Mary Elem. School", lat: 14.6288, lon: 121.1025, type: "Flood-safe Camp" },
            { name: "SSS Village Elem. School", lat: 14.6395, lon: 121.1165, type: "Flood-safe Camp" },
            { name: "SSS National High School", lat: 14.6402, lon: 121.1172, type: "Flood-safe Camp" },
            { name: "Kap. Moy Elementary School", lat: 14.6302, lon: 121.0962, type: "Flood-safe Camp" },
            { name: "Marikina Science High School", lat: 14.6285, lon: 121.0982, type: "Flood-safe Camp" },
            { name: "Champaca I Gym", lat: 14.6455, lon: 121.1215, type: "Flood-safe Camp" },
            { name: "Manotoc Gym", lat: 14.6345, lon: 121.0910, type: "Flood-safe Camp" },
            { name: "Sunny Square Gym", lat: 14.6502, lon: 121.0970, type: "Flood-safe Camp" },
            { name: "Sta. Teresita Gym", lat: 14.6385, lon: 121.1085, type: "Flood-safe Camp" },
            { name: "Amang Rodriguez Gym", lat: 14.6338, lon: 121.0988, type: "Flood-safe Camp" },
            { name: "St. Mary Gym", lat: 14.6282, lon: 121.1020, type: "Flood-safe Camp" },
            { name: "Fairlane Covered Court", lat: 14.6482, lon: 121.1020, type: "Flood-safe Camp" },
            { name: "St. Benedick Gym, Nangka", lat: 14.6675, lon: 121.1045, type: "Flood-safe Camp" },
            { name: "Greenland Gym", lat: 14.6655, lon: 121.1012, type: "Flood-safe Camp" },
            { name: "Jesus Dela Peña Gym", lat: 14.6372, lon: 121.0908, type: "Flood-safe Camp" },

            // Dual-Purpose Camp (Major & Minor)
            { name: "Concepcion Integrated School ES", lat: 14.6575, lon: 121.1112, type: "Dual-Purpose Camp" },
            { name: "Concepcion Integrated School SL", lat: 14.6582, lon: 121.1118, type: "Dual-Purpose Camp" },
            { name: "PLMAR (GH) Campus Grounds", lat: 14.6452, lon: 121.1158, type: "Dual-Purpose Camp" },
            { name: "Marikina High School Wide Fields", lat: 14.6322, lon: 121.1018, type: "Dual-Purpose Camp" },
            { name: "Parang Elem. School Open Grounds", lat: 14.6602, lon: 121.1158, type: "Dual-Purpose Camp" },
            { name: "Parang High School Open Grounds", lat: 14.6610, lon: 121.1165, type: "Dual-Purpose Camp" },
            { name: "Fortune Elem. School Fields", lat: 14.6625, lon: 121.1278, type: "Dual-Purpose Camp" },
            { name: "Fortune High School Fields", lat: 14.6632, lon: 121.1285, type: "Dual-Purpose Camp" },
            { name: "PLMAR San Roque Plaza", lat: 14.6245, lon: 121.1035, type: "Dual-Purpose Camp" },
            { name: "Marikina Hotel Parking Grounds", lat: 14.6442, lon: 121.1232, type: "Dual-Purpose Camp" },

            // Earthquake-safe Camp (Minor)
            { name: "Sta. Elena Chapel Plaza", lat: 14.6268, lon: 121.0988, type: "Earthquake-safe Camp" },
            { name: "Aglipay Church Courtyard, Malanday", lat: 14.6585, lon: 121.0955, type: "Earthquake-safe Camp" },
            { name: "Aglipay Church Courtyard, Sto. Niño", lat: 14.6448, lon: 121.0988, type: "Earthquake-safe Camp" },
            { name: "OLA Church Open Plaza & Courtyard", lat: 14.6308, lon: 121.0965, type: "Earthquake-safe Camp" }
        ];

        // Initialize Map
        var map = L.map('map', {
            zoomControl: false // Hide default controls for custom modern look
        }).setView([14.6507, 121.1029], 13); // Centered in Marikina City

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: ''
        }).addTo(map);

        // Helper to generate elegant custom SVG map pins
        function createSvgIcon(color) {
            return L.divIcon({
                className: 'custom-icon',
                html: `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                         <path d="M12 2C8.13 2 5 5.13 5 9C5 14.25 12 22 12 22C12 22 19 14.25 19 9C19 5.13 15.87 2 12 2Z" fill="${color}" stroke="#FFFFFF" stroke-width="1.5"/>
                         <circle cx="12" cy="9" r="3" fill="#FFFFFF"/>
                       </svg>`,
                iconSize: [28, 28],
                iconAnchor: [14, 22],
                popupAnchor: [0, -20]
            });
        }

        // Color coding markers by category
        var icons = {
            "Flood-safe Camp": createSvgIcon('#D32F2F'), // Red
            "Dual-Purpose Camp": createSvgIcon('#F2A104'), // Orange/Gold
            "Earthquake-safe Camp": createSvgIcon('#2E7D32') // Green
        };

        var allMarkers = [];

        // Add markers to map
        evacuationCenters.forEach(function(center) {
            var marker = L.marker([center.lat, center.lon], { icon: icons[center.type] });
            
            var popupContent = `
                <div class="p-1.5 font-sans">
                    <h3 class="font-bold text-sm text-gray-900 mb-0.5">${center.name}</h3>
                    <p class="text-xs text-gray-500 mb-1.5">Category: <span class="font-semibold text-gray-700">${center.type}</span></p>
                    <div class="flex justify-between items-center border-t border-gray-100 pt-1.5 mt-1.5">
                        <span class="text-[10px] bg-green-50 text-green-700 px-1.5 py-0.5 rounded font-bold">ACTIVE</span>
                        <a href="#" class="text-[11px] font-bold text-teal-600 hover:text-teal-800" onclick="alert('Navigating to ${center.name}...')">Route Guidance &rarr;</a>
                    </div>
                </div>
            `;

            marker.bindPopup(popupContent);
            marker.centerData = center;
            marker.addTo(map);
            allMarkers.push(marker);
        });

        // Filter and Search logic
        var activeType = 'All';

        window.filterType = function(type) {
            activeType = type;
            updateMapMarkers();
        };

        function updateMapMarkers() {
            var searchVal = document.getElementById('searchInput').value.toLowerCase().trim();

            // Highlight chosen filter tab
            var types = ['All', 'Flood-safe', 'Dual-Purpose', 'Earthquake-safe'];
            types.forEach(function(t) {
                var btnId = 'btn-' + (t === 'All' ? 'All' : t.split('-')[0]);
                var btn = document.getElementById(btnId);
                if (!btn) return;
                
                if (t === activeType) {
                    btn.classList.remove('bg-white', 'text-gray-700', 'border-gray-200/40');
                    btn.classList.add('bg-gray-900', 'text-white');
                } else {
                    btn.classList.remove('bg-gray-900', 'text-white');
                    btn.classList.add('bg-white', 'text-gray-700', 'border-gray-200/40');
                }
            });

            // Filter markers
            allMarkers.forEach(function(marker) {
                var center = marker.centerData;
                var matchesSearch = center.name.toLowerCase().includes(searchVal);
                
                // Matches type filter
                var matchesType = activeType === 'All' || center.type.indexOf(activeType) === 0;

                if (matchesSearch && matchesType) {
                    if (!map.hasLayer(marker)) {
                        marker.addTo(map);
                    }
                } else {
                    if (map.hasLayer(marker)) {
                        map.removeLayer(marker);
                    }
                }
            });
        }

        // Hook input search
        document.getElementById('searchInput').addEventListener('input', updateMapMarkers);
    </script>
</body>
</html>
""";

        MapHtmlSource = new HtmlWebViewSource
        {
            Html = html
        };
    }
}
