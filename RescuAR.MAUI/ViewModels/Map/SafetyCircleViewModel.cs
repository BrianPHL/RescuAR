using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Mapsui;
using Mapsui.UI.Maui;
using Mapsui.Nts;
using System.Linq;
using NetTopologySuite.Geometries;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.ObjectModel;
using RescuAR.App.Models;

namespace RescuAR.App.ViewModels.Map;

public class CircleMember
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string StatusText { get; set; } = "Online";
    public string LocationSubtitle { get; set; } = "Near Current Location";
    public string SinceTimeText { get; set; } = "Since Just Now";
    public string BatteryText { get; set; } = "100%";
    public string BatteryIcon { get; set; } = "🔋";
    public Microsoft.Maui.Graphics.Color BatteryColor { get; set; } = Microsoft.Maui.Graphics.Color.FromArgb("#16A34A");
    public bool HasBattery => true;
    public Microsoft.Maui.Graphics.Color ColorTheme { get; set; } = Microsoft.Maui.Graphics.Colors.Teal;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public bool HasAvatarUrl => !string.IsNullOrWhiteSpace(AvatarUrl);
    public bool HasNoAvatarUrl => !HasAvatarUrl;
    public string AvatarImageSource { get; set; } = string.Empty;
    public bool IsMe { get; set; } = false;
    public bool IsSafe { get; set; } = true;
    public string MemberStatusLine => $"{SinceTimeText} • {BatteryIcon} {BatteryText}";
}

public partial class SafetyCircleViewModel : ObservableObject
{
    [ObservableProperty]
    private Mapsui.Map _map = new();

    [ObservableProperty]
    private string _selectedCircleName = "Select a Circle";

    [ObservableProperty]
    private bool _isPeopleSheetOpen = false;

    [ObservableProperty]
    private bool _isCircleDropdownOpen = false;

    [ObservableProperty]
    private bool _isCreatedCirclePopupOpen = false;

    [ObservableProperty]
    private string _newlyCreatedCircleName = string.Empty;

    [ObservableProperty]
    private string _newlyCreatedInviteCode = string.Empty;

    [ObservableProperty]
    private bool _isCopiedSuccess = false;

    // --- First-time Guide Wizard & Custom Delete Dialog ---
    [ObservableProperty]
    private bool _isTutorialPopupVisible;

    [ObservableProperty]
    private bool _isDeleteConfirmPopupVisible;

    [ObservableProperty]
    private SupabaseSafetyCircle? _circleToDelete;

    public ObservableCollection<CircleMember> CircleMembers { get; } = new();
    public ObservableCollection<SupabaseSafetyCircle> MyCircles { get; } = new();

    private Mapsui.Layers.MemoryLayer _pinsLayer = null!;
    private readonly RescuAR.App.Services.Cloud.SafetyCircleService _safetyCircleService;
    private readonly IDispatcherTimer _locationTimer;
    private string _currentCircleId = "";
    private bool _hasCenteredOnUser = false;
    
    private readonly System.Net.Http.HttpClient _httpClient = new();

    public SafetyCircleViewModel(RescuAR.App.Services.Cloud.SafetyCircleService safetyCircleService)
    {
        _safetyCircleService = safetyCircleService;

        _isTutorialPopupVisible = !Preferences.Default.Get("HasSeenSafetyCircleTutorial", false);

        _locationTimer = Application.Current?.Dispatcher?.CreateTimer()
            ?? throw new InvalidOperationException("MAUI dispatcher is not available.");
        _locationTimer.Interval = TimeSpan.FromSeconds(5);
        _locationTimer.Tick += async (s, e) => await PollLocationsAsync();
    }

    public async Task LoadMyCirclesAsync()
    {
        try
        {
            var circles = await _safetyCircleService.GetMyCirclesAsync();
            MyCircles.Clear();
            foreach (var circle in circles)
            {
                MyCircles.Add(circle);
            }

            if (MyCircles.Any())
            {
                var savedSelectedId = Preferences.Default.Get("SelectedCircleId", string.Empty);
                var target = MyCircles.FirstOrDefault(c => c.Id == savedSelectedId) 
                    ?? (!string.IsNullOrEmpty(_currentCircleId) ? MyCircles.FirstOrDefault(c => c.Id == _currentCircleId) : null)
                    ?? MyCircles.First();
                
                SelectCircle(target);
            }
            else
            {
                SelectedCircleName = "No Circles Joined";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading circles: {ex.Message}");
        }
    }

    public void SelectCircle(SupabaseSafetyCircle circle)
    {
        if (circle == null) return;
        _currentCircleId = circle.Id;
        SelectedCircleName = circle.Name;
        Preferences.Default.Set("SelectedCircleId", circle.Id);
        IsCircleDropdownOpen = false;
        
        // Load members instantly, then timer will keep updating
        _ = PollLocationsAsync();
        _locationTimer.Start();
    }

    private async Task<bool> CheckAndRequestLocationPermission()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status == PermissionStatus.Granted)
            return true;
        
        status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    private readonly System.Collections.Generic.Dictionary<string, byte[]> _avatarRawBytesCache = new();

    private (int percent, bool isCharging) GetRealtimeBatteryLevel()
    {
        int batteryLevel = 0;
        bool isCharging = false;

#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var filter = new Android.Content.IntentFilter(Android.Content.Intent.ActionBatteryChanged);
            var batteryStatus = context.RegisterReceiver(null, filter);
            if (batteryStatus != null)
            {
                int level = batteryStatus.GetIntExtra(Android.OS.BatteryManager.ExtraLevel, -1);
                int scale = batteryStatus.GetIntExtra(Android.OS.BatteryManager.ExtraScale, -1);
                if (level >= 0 && scale > 0)
                {
                    batteryLevel = (int)Math.Round((level / (float)scale) * 100);
                }

                int status = batteryStatus.GetIntExtra(Android.OS.BatteryManager.ExtraStatus, -1);
                isCharging = status == (int)Android.OS.BatteryStatus.Charging || status == (int)Android.OS.BatteryStatus.Full;
            }
        }
        catch { }
#endif

        if (batteryLevel <= 0)
        {
            try
            {
                var charge = Battery.Default.ChargeLevel;
                if (charge >= 0)
                {
                    batteryLevel = (int)Math.Round(charge * 100);
                }
                isCharging = Battery.Default.State == BatteryState.Charging;
            }
            catch { }
        }

        if (batteryLevel <= 0) batteryLevel = 50;

        return (batteryLevel, isCharging);
    }

    private string GenerateLife360PinImageSource(byte[]? avatarBytes, string name, string colorHex, bool isMe, string initials)
    {
        const int width = 140;
        const int height = 175;
        const float circleRadius = 40f;
        const float circleCenterX = width / 2f;
        const float circleCenterY = 48f;

        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        var pinColor = SkiaSharp.SKColor.Parse(colorHex);

        // 1. Draw Pointer Triangle at bottom of circle pointing down
        using (var trianglePath = new SkiaSharp.SKPath())
        {
            trianglePath.MoveTo(circleCenterX - 14, circleCenterY + circleRadius - 4);
            trianglePath.LineTo(circleCenterX + 14, circleCenterY + circleRadius - 4);
            trianglePath.LineTo(circleCenterX, circleCenterY + circleRadius + 18);
            trianglePath.Close();

            using var trianglePaint = new SkiaSharp.SKPaint
            {
                Color = pinColor,
                IsAntialias = true,
                Style = SkiaSharp.SKPaintStyle.Fill
            };
            canvas.DrawPath(trianglePath, trianglePaint);
        }

        // 2. Draw Outer Border Circle
        using (var borderPaint = new SkiaSharp.SKPaint
        {
            Color = pinColor,
            IsAntialias = true,
            Style = SkiaSharp.SKPaintStyle.Fill
        })
        {
            canvas.DrawCircle(circleCenterX, circleCenterY, circleRadius, borderPaint);
        }

        // 3. Draw Inner White Ring
        using (var whiteRingPaint = new SkiaSharp.SKPaint
        {
            Color = SkiaSharp.SKColors.White,
            IsAntialias = true,
            Style = SkiaSharp.SKPaintStyle.Fill
        })
        {
            canvas.DrawCircle(circleCenterX, circleCenterY, circleRadius - 4, whiteRingPaint);
        }

        // 4. Draw Avatar Image or Initials
        float innerRadius = circleRadius - 6;
        bool drewAvatar = false;
        if (avatarBytes != null && avatarBytes.Length > 0)
        {
            try
            {
                using var origBitmap = SkiaSharp.SKBitmap.Decode(avatarBytes);
                if (origBitmap != null)
                {
                    using var shader = SkiaSharp.SKShader.CreateBitmap(
                        origBitmap,
                        SkiaSharp.SKShaderTileMode.Clamp,
                        SkiaSharp.SKShaderTileMode.Clamp,
                        SkiaSharp.SKMatrix.CreateScale(
                            (innerRadius * 2f) / origBitmap.Width,
                            (innerRadius * 2f) / origBitmap.Height
                        ).PostConcat(SkiaSharp.SKMatrix.CreateTranslation(circleCenterX - innerRadius, circleCenterY - innerRadius))
                    );

                    using var avatarPaint = new SkiaSharp.SKPaint
                    {
                        Shader = shader,
                        IsAntialias = true
                    };
                    canvas.DrawCircle(circleCenterX, circleCenterY, innerRadius, avatarPaint);
                    drewAvatar = true;
                }
            }
            catch { }
        }

        if (!drewAvatar)
        {
            // Draw Initials with colored background
            using var initBgPaint = new SkiaSharp.SKPaint
            {
                Color = pinColor,
                IsAntialias = true,
                Style = SkiaSharp.SKPaintStyle.Fill
            };
            canvas.DrawCircle(circleCenterX, circleCenterY, innerRadius, initBgPaint);

            using var textPaint = new SkiaSharp.SKPaint
            {
                Color = SkiaSharp.SKColors.White,
                IsAntialias = true
            };
            using var textTypeface = SkiaSharp.SKTypeface.FromFamilyName("sans-serif", SkiaSharp.SKFontStyle.Bold);
            using var textFont = new SkiaSharp.SKFont(textTypeface, 24);
            canvas.DrawText(initials, circleCenterX, circleCenterY + 9, SkiaSharp.SKTextAlign.Center, textFont, textPaint);
        }

        // 5. Draw Name Pill Tag at the bottom
        string rawFirstName = (name ?? string.Empty).Replace("(You)", "").Trim().Split(' ')[0].Trim();
        if (string.IsNullOrWhiteSpace(rawFirstName) || rawFirstName.Equals("Member", StringComparison.OrdinalIgnoreCase))
        {
            rawFirstName = isMe ? "You" : (name ?? "Member");
        }

        string displayName = isMe ? $"{rawFirstName} (You)" : rawFirstName;
        float pillY = circleCenterY + circleRadius + 22;
        float pillHeight = 26;
        
        using var pillTextPaint = new SkiaSharp.SKPaint
        {
            Color = SkiaSharp.SKColors.White,
            IsAntialias = true
        };
        using var pillTypeface = SkiaSharp.SKTypeface.FromFamilyName("sans-serif", SkiaSharp.SKFontStyle.Bold);
        using var pillFont = new SkiaSharp.SKFont(pillTypeface, 16);

        float textWidth = pillFont.MeasureText(displayName);
        float pillWidth = Math.Max(70, textWidth + 24);
        float pillX = circleCenterX - (pillWidth / 2f);

        using (var pillPaint = new SkiaSharp.SKPaint
        {
            Color = SkiaSharp.SKColor.Parse("#1E293B"),
            IsAntialias = true,
            Style = SkiaSharp.SKPaintStyle.Fill
        })
        {
            var roundRect = new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(pillX, pillY, pillX + pillWidth, pillY + pillHeight), 13, 13);
            canvas.DrawRoundRect(roundRect, pillPaint);
        }

        canvas.DrawText(displayName, circleCenterX, pillY + 19, SkiaSharp.SKTextAlign.Center, pillFont, pillTextPaint);

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        return $"base64-content://{Convert.ToBase64String(ms.ToArray())}";
    }

    private async Task PollLocationsAsync()
    {
        try
        {
            // 1. Get real-time battery
            var (myBatteryPercent, myIsCharging) = GetRealtimeBatteryLevel();
            string myBatteryStatus = $"{myBatteryPercent}%{(myIsCharging ? "⚡" : "")}";

            Microsoft.Maui.Devices.Sensors.Location? loc = null;
            var hasPermission = await CheckAndRequestLocationPermission();
            if (hasPermission)
            {
                try
                {
                    loc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(4)));
                }
                catch { }

                if (loc == null)
                {
                    try
                    {
                        loc = await Geolocation.Default.GetLastKnownLocationAsync();
                    }
                    catch { }
                }

                if (loc != null)
                {
                    string pushStatus = $"Online|{myBatteryStatus}";
                    await _safetyCircleService.PushLocationAsync(loc.Latitude, loc.Longitude, pushStatus);
                    
                    // Auto-center map on user's current GPS location on first fix
                    if (!_hasCenteredOnUser && Map != null)
                    {
                        _hasCenteredOnUser = true;
                        var (userX, userY) = Mapsui.Projections.SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
                        Map.Navigator?.CenterOnAndZoomTo(new MPoint(userX, userY), 25);
                    }
                }
            }

            // 2. Build current user's profile pin so it ALWAYS appears at their live GPS position
            double myLat = loc?.Latitude ?? 14.6507;
            double myLon = loc?.Longitude ?? 121.1029;

            string myName = string.Empty;
            try
            {
                var currentUser = RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser;
                if (currentUser?.UserMetadata != null)
                {
                    string fn = currentUser.UserMetadata.TryGetValue("first_name", out var f) && f != null ? f.ToString()?.Trim() ?? "" : "";
                    string ln = currentUser.UserMetadata.TryGetValue("last_name", out var l) && l != null ? l.ToString()?.Trim() ?? "" : "";
                    myName = $"{fn} {ln}".Trim();
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(myName))
            {
                var fn = Preferences.Default.Get("UserFirstName", string.Empty);
                var ln = Preferences.Default.Get("UserLastName", string.Empty);
                myName = $"{fn} {ln}".Trim();
            }

            if (string.IsNullOrWhiteSpace(myName))
            {
                myName = "You";
            }

            string myInitials = "ME";
            var myWords = myName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (myWords.Length >= 2)
            {
                myInitials = $"{myWords[0][0]}{myWords[1][0]}".ToUpper();
            }
            else if (myWords.Length == 1 && myWords[0].Length >= 2)
            {
                myInitials = myWords[0].Substring(0, 2).ToUpper();
            }

            var meMember = new CircleMember
            {
                Id = _safetyCircleService.GetCurrentUserId(),
                Name = $"{myName} (You)",
                Initials = myInitials,
                StatusText = "Online",
                LocationSubtitle = await GetEstimatedLocationSubtitleAsync(myLat, myLon),
                SinceTimeText = $"Since {DateTime.Now:h:mm tt}",
                BatteryText = $"{myBatteryPercent}%",
                BatteryIcon = myIsCharging ? "⚡" : "🔋",
                BatteryColor = myIsCharging ? Microsoft.Maui.Graphics.Color.FromArgb("#2563EB") : (myBatteryPercent <= 20 ? Microsoft.Maui.Graphics.Color.FromArgb("#EF4444") : Microsoft.Maui.Graphics.Color.FromArgb("#16A34A")),
                Latitude = myLat,
                Longitude = myLon,
                ColorTheme = Microsoft.Maui.Graphics.Color.FromArgb("#10B981"),
                IsMe = true,
                IsSafe = true
            };

            byte[]? myAvatarBytes = null;
            var localAvatarPath = Preferences.Default.Get("UserAvatarUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(localAvatarPath))
            {
                localAvatarPath = Preferences.Default.Get("UserProfilePicPath", string.Empty);
            }
            if (!string.IsNullOrWhiteSpace(localAvatarPath) && File.Exists(localAvatarPath))
            {
                try { myAvatarBytes = File.ReadAllBytes(localAvatarPath); } catch { }
            }

            meMember.AvatarImageSource = GenerateLife360PinImageSource(myAvatarBytes, myName, "#10B981", true, myInitials);

            CircleMembers.Clear();
            CircleMembers.Add(meMember);

            // 3. If in a circle, load other circle members & cloud locations
            if (!string.IsNullOrEmpty(_currentCircleId))
            {
                var members = await _safetyCircleService.GetCircleMembersAsync(_currentCircleId);
                var locations = await _safetyCircleService.GetCircleLocationsAsync(_currentCircleId);
                string currentUserId = string.Empty;
                try { currentUserId = _safetyCircleService.GetCurrentUserId(); } catch { }

                foreach (var member in members)
                {
                    if (string.Equals(member.Id, currentUserId, StringComparison.OrdinalIgnoreCase))
                        continue; // Already added current user above

                    var userLoc = locations.FirstOrDefault(l => string.Equals(l.UserId, member.Id, StringComparison.OrdinalIgnoreCase));
                    string rawStatus = userLoc?.StatusText ?? "Online";
                    string displayStatus = "Online";
                    string batteryText = "100%";
                    string batteryIcon = "🔋";
                    var batteryColor = Microsoft.Maui.Graphics.Color.FromArgb("#16A34A");

                    if (rawStatus.Contains("|"))
                    {
                        var parts = rawStatus.Split('|');
                        displayStatus = parts[0];
                        batteryText = parts[1];
                        if (batteryText.Contains("⚡"))
                        {
                            batteryIcon = "⚡";
                            batteryColor = Microsoft.Maui.Graphics.Color.FromArgb("#2563EB");
                        }
                        else
                        {
                            var cleanVal = batteryText.Replace("%", "").Trim();
                            if (int.TryParse(cleanVal, out int bVal))
                            {
                                if (bVal <= 20) batteryColor = Microsoft.Maui.Graphics.Color.FromArgb("#EF4444");
                                else if (bVal <= 50) batteryColor = Microsoft.Maui.Graphics.Color.FromArgb("#F59E0B");
                                else batteryColor = Microsoft.Maui.Graphics.Color.FromArgb("#16A34A");
                            }
                        }
                    }

                    double memberLat = userLoc?.Latitude ?? 0;
                    double memberLon = userLoc?.Longitude ?? 0;

                    // Fallback to slight offset around user if member coordinates are zero so pins are always visible
                    if (memberLat == 0 && memberLon == 0 && myLat != 0 && myLon != 0)
                    {
                        memberLat = myLat;
                        memberLon = myLon;
                    }

                    string memberName = $"{member.FirstName} {member.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(memberName))
                    {
                        memberName = !string.IsNullOrWhiteSpace(member.Username) ? member.Username : "Family Member";
                    }

                    string initials = "FM";
                    var words = memberName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length >= 2) initials = $"{words[0][0]}{words[1][0]}".ToUpper();
                    else if (words.Length == 1 && words[0].Length >= 2) initials = words[0].Substring(0, 2).ToUpper();

                    string sinceTime = userLoc != null && userLoc.LastUpdated.HasValue && userLoc.LastUpdated.Value != default
                        ? $"Since {userLoc.LastUpdated.Value.ToLocalTime():h:mm tt}"
                        : $"Since {DateTime.Now:h:mm tt}";

                    string locationSubtitle = await GetEstimatedLocationSubtitleAsync(memberLat, memberLon);

                    var cm = new CircleMember
                    {
                        Id = member.Id,
                        Name = memberName,
                        Initials = initials,
                        StatusText = displayStatus,
                        LocationSubtitle = locationSubtitle,
                        SinceTimeText = sinceTime,
                        BatteryText = batteryText,
                        BatteryIcon = batteryIcon,
                        BatteryColor = batteryColor,
                        Latitude = memberLat,
                        Longitude = memberLon,
                        ColorTheme = GetColorForUser(member.Id),
                        AvatarUrl = member.AvatarUrl,
                        IsMe = false,
                        IsSafe = true
                    };

                    byte[]? avatarBytes = null;
                    if (!string.IsNullOrEmpty(cm.AvatarUrl))
                    {
                        if (File.Exists(cm.AvatarUrl))
                        {
                            try { avatarBytes = File.ReadAllBytes(cm.AvatarUrl); } catch { }
                        }
                        else if (!_avatarRawBytesCache.TryGetValue(member.Id, out avatarBytes))
                        {
                            try
                            {
                                avatarBytes = await _httpClient.GetByteArrayAsync(cm.AvatarUrl);
                                _avatarRawBytesCache[member.Id] = avatarBytes;
                            }
                            catch { }
                        }
                    }

                    cm.AvatarImageSource = GenerateLife360PinImageSource(avatarBytes, cm.Name, cm.ColorTheme.ToHex(), false, cm.Initials);
                    CircleMembers.Add(cm);
                }
            }

            // 4. Render all pins onto Map
            if (Map != null)
            {
                UpdateMapMarkers(Map);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Polling Error: {ex.Message}");
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _streetNameCache = new();

    private async Task<string> GetEstimatedLocationSubtitleAsync(double lat, double lon)
    {
        if (lat == 0 || lon == 0) return "Near Current Location";

        string cacheKey = $"{Math.Round(lat, 4)}_{Math.Round(lon, 4)}";
        if (_streetNameCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var placemarks = await Geocoding.Default.GetPlacemarksAsync(new Microsoft.Maui.Devices.Sensors.Location(lat, lon));
            var placemark = placemarks?.FirstOrDefault();
            if (placemark != null)
            {
                string street = placemark.Thoroughfare ?? placemark.SubLocality ?? placemark.Locality ?? "";
                if (!string.IsNullOrWhiteSpace(street))
                {
                    string result = street.StartsWith("Near ", StringComparison.OrdinalIgnoreCase) ? street : $"Near {street}";
                    _streetNameCache[cacheKey] = result;
                    return result;
                }
            }
        }
        catch { }

        string fallback = "Near Current Location";
        _streetNameCache[cacheKey] = fallback;
        return fallback;
    }

    private Microsoft.Maui.Graphics.Color GetColorForUser(string userId)
    {
        int hash = userId.GetHashCode();
        var colors = new[] { "#0A8491", "#EAB308", "#931492", "#E11D48", "#2563EB", "#16A34A" };
        return Microsoft.Maui.Graphics.Color.FromArgb(colors[Math.Abs(hash) % colors.Length]);
    }

    public async Task InitializeMapAsync(MapControl mapControl)
    {
        try
        {
            var map = new Mapsui.Map
            {
                CRS = "EPSG:3857"
            };
            // Clear default debug overlays (INFO, FPS, Scale bar)
            map.Widgets.Clear();

            // Load Online OpenStreetMap Base Layer with compliant User-Agent
            map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer("RescuAR-EvacuationApp/1.0 (contact@rescuar.app)"));

            // Center and Zoom to Map Data (Default Marikina or user location)
            var (homeX, homeY) = Mapsui.Projections.SphericalMercator.FromLonLat(121.1029, 14.6507);
            map.Navigator.CenterOnAndZoomTo(new MPoint(homeX, homeY), 38.2);

            Map = map;
            mapControl.Map = map;

            // Draw initial pins
            _ = PollLocationsAsync();
        }
        catch (Exception ex)
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Map Error", $"Base Map failed: {ex.Message}", "OK");
            Console.WriteLine($"Error loading map: {ex.Message}");
        }
    }

    private void UpdateMapMarkers(Mapsui.Map map)
    {
        var features = new System.Collections.Generic.List<Mapsui.Nts.GeometryFeature>();
        var activeMembers = CircleMembers.Where(m => m.Latitude != 0 && m.Longitude != 0).ToList();

        // 1. Group active members into clusters based on proximity (< 45 meters)
        var clusters = new List<List<CircleMember>>();
        foreach (var member in activeMembers)
        {
            bool added = false;
            foreach (var cluster in clusters)
            {
                var refMember = cluster[0];
                var (refX, refY) = Mapsui.Projections.SphericalMercator.FromLonLat(refMember.Longitude, refMember.Latitude);
                var (memX, memY) = Mapsui.Projections.SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
                double distance = Math.Sqrt(Math.Pow(memX - refX, 2) + Math.Pow(memY - refY, 2));

                if (distance < 45.0) // Within 45 meters in Mercator meters
                {
                    cluster.Add(member);
                    added = true;
                    break;
                }
            }
            if (!added)
            {
                clusters.Add(new List<CircleMember> { member });
            }
        }

        // 2. Compute dispersed / fanned-out positions for each cluster
        foreach (var cluster in clusters)
        {
            if (cluster.Count == 1)
            {
                var single = cluster[0];
                var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(single.Longitude, single.Latitude);
                AddMemberPinFeatures(features, single, x, y);
            }
            else
            {
                // Multi-member co-located cluster: Calculate centroid
                double avgLon = cluster.Average(m => m.Longitude);
                double avgLat = cluster.Average(m => m.Latitude);
                var (centerX, centerY) = Mapsui.Projections.SphericalMercator.FromLonLat(avgLon, avgLat);

                // Fan-out radius (32m base + scaling per member)
                double radius = 32.0 + (cluster.Count - 2) * 8.0;

                for (int i = 0; i < cluster.Count; i++)
                {
                    var member = cluster[i];
                    double angle = (2.0 * Math.PI * i / cluster.Count) - (Math.PI / 2.0);
                    double px = centerX + radius * Math.Cos(angle);
                    double py = centerY + radius * Math.Sin(angle);

                    AddMemberPinFeatures(features, member, px, py);
                }
            }
        }

        var oldLayer = map.Layers.FirstOrDefault(l => l.Name == "MapPins");
        if (oldLayer != null)
        {
            map.Layers.Remove(oldLayer);
        }

        _pinsLayer = new Mapsui.Layers.MemoryLayer
        {
            Name = "MapPins",
            Features = features
        };

        map.Layers.Add(_pinsLayer);
        map.Refresh();
    }

    private void AddMemberPinFeatures(System.Collections.Generic.List<Mapsui.Nts.GeometryFeature> features, CircleMember member, double x, double y)
    {
        // Halo (Translucent Outer Ring)
        var haloFeature = new Mapsui.Nts.GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
        var colorTheme = member.ColorTheme;
        var translucentColor = new Mapsui.Styles.Color((int)(colorTheme.Red * 255), (int)(colorTheme.Green * 255), (int)(colorTheme.Blue * 255), 40);
        haloFeature.Styles.Add(new Mapsui.Styles.SymbolStyle
        {
            SymbolType = Mapsui.Styles.SymbolType.Ellipse,
            SymbolScale = 1.2,
            Fill = new Mapsui.Styles.Brush(translucentColor),
            Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.Transparent)
        });
        features.Add(haloFeature);

        // Inner Pin (Life360 Avatar + Pointer + Name Pill)
        var feature = new Mapsui.Nts.GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
        if (!string.IsNullOrWhiteSpace(member.AvatarImageSource))
        {
            feature.Styles.Add(new Mapsui.Styles.ImageStyle
            {
                Image = member.AvatarImageSource,
                SymbolScale = 0.5,
                Offset = new Mapsui.Styles.Offset(0, 35)
            });
        }
        features.Add(feature);
    }

    [RelayCommand]
    private void CenterOnMember(CircleMember member)
    {
        if (member == null || member.Latitude == 0 || member.Longitude == 0 || Map == null) return;
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
        Map.Navigator?.CenterOnAndZoomTo(new MPoint(x, y), 22);
    }
    private void TogglePeopleSheet()
    {
        IsPeopleSheetOpen = !IsPeopleSheetOpen;
        if (IsPeopleSheetOpen) IsCircleDropdownOpen = false;
    }

    [RelayCommand]
    private void ToggleCircleDropdown()
    {
        IsCircleDropdownOpen = !IsCircleDropdownOpen;
        if (IsCircleDropdownOpen) IsPeopleSheetOpen = false;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        Map?.Navigator?.ZoomIn();
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Map?.Navigator?.ZoomOut();
    }

    [RelayCommand]
    private void ZoomReset()
    {
        var (homeX, homeY) = Mapsui.Projections.SphericalMercator.FromLonLat(121.1029, 14.6507);
        Map?.Navigator?.CenterOnAndZoomTo(new MPoint(homeX, homeY), 38.2);
    }

    [RelayCommand]
    private async Task CreateCircleAsync()
    {
        if (Shell.Current == null) return;
        string result = await Shell.Current.DisplayPromptAsync("Create Safety Circle", "Enter a name for your new Safety Circle:", "Create", "Cancel");
        if (!string.IsNullOrWhiteSpace(result))
        {
            try
            {
                var circle = await _safetyCircleService.CreateCircleAsync(result);
                NewlyCreatedCircleName = circle.Name;
                NewlyCreatedInviteCode = circle.InviteCode;
                IsCopiedSuccess = false;
                IsCreatedCirclePopupOpen = true;

                await LoadMyCirclesAsync();
                SelectCircle(circle);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    [RelayCommand]
    private async Task CopyInviteCodeAsync()
    {
        if (!string.IsNullOrWhiteSpace(NewlyCreatedInviteCode))
        {
            await Clipboard.Default.SetTextAsync(NewlyCreatedInviteCode);
            IsCopiedSuccess = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                MainThread.BeginInvokeOnMainThread(() => IsCopiedSuccess = false);
            });
        }
    }

    [RelayCommand]
    private void CloseCreatedCirclePopup()
    {
        IsCreatedCirclePopupOpen = false;
    }

    [RelayCommand]
    private async Task JoinCircleAsync()
    {
        if (Shell.Current == null) return;
        string result = await Shell.Current.DisplayPromptAsync("Join Circle", "Enter the 6-character Invite Code:", "Join", "Cancel");
        if (!string.IsNullOrWhiteSpace(result))
        {
            try
            {
                var circle = await _safetyCircleService.JoinCircleWithCodeAsync(result);
                await Shell.Current.DisplayAlert("Success", $"You've joined {circle.Name}!", "OK");
                await LoadMyCirclesAsync();
                SelectCircle(circle);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    [RelayCommand]
    private void CloseTutorial()
    {
        IsTutorialPopupVisible = false;
        Preferences.Default.Set("HasSeenSafetyCircleTutorial", true);
    }

    [RelayCommand]
    private void SelectCircleFromList(RescuAR.App.Models.SupabaseSafetyCircle circle)
    {
        if (circle != null)
        {
            SelectCircle(circle);
        }
    }

    [RelayCommand]
    private void DeleteCircle(RescuAR.App.Models.SupabaseSafetyCircle circle)
    {
        if (circle == null) return;
        CircleToDelete = circle;
        IsDeleteConfirmPopupVisible = true;
    }

    [RelayCommand]
    private void CancelDeleteCircle()
    {
        IsDeleteConfirmPopupVisible = false;
        CircleToDelete = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteCircleAsync()
    {
        if (CircleToDelete == null) return;
        var circle = CircleToDelete;
        IsDeleteConfirmPopupVisible = false;
        CircleToDelete = null;

        try
        {
            await _safetyCircleService.DeleteCircleAsync(circle.Id);
            MyCircles.Remove(circle);
            if (_currentCircleId == circle.Id)
            {
                _currentCircleId = string.Empty;
                SelectedCircleName = "No Circles Joined";
                Preferences.Default.Remove("SelectedCircleId");
                CircleMembers.Clear();
            }
            await LoadMyCirclesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting circle: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenChatAsync()
    {
        if (string.IsNullOrEmpty(_currentCircleId))
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Select Circle", "Please create or select a Safety Circle first to chat with family members.", "OK");
            return;
        }

        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync($"CircleChatPage?circleId={_currentCircleId}&circleName={Uri.EscapeDataString(SelectedCircleName)}");
        }
    }

    [RelayCommand]
    private async Task OpenNotificationsAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("NotificationsPage");
        }
    }
}
