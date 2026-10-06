using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.UI.Maui;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.ObjectModel;
using RescuAR.App.Models;
using RescuAR.App.Services.Cloud;
using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Map;

public class CircleMember
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string StatusText { get; set; } = "Location unavailable";
    public string BatteryText { get; set; } = string.Empty;
    public string BatteryIcon { get; set; } = "🔋";
    public Microsoft.Maui.Graphics.Color BatteryColor { get; set; } = Microsoft.Maui.Graphics.Colors.Gray;
    public bool HasBattery => !string.IsNullOrWhiteSpace(BatteryText);
    public Microsoft.Maui.Graphics.Color ColorTheme { get; set; } = Microsoft.Maui.Graphics.Colors.Teal;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool HasLocation { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public string AvatarImageSource { get; set; } = string.Empty;
}

public partial class SafetyCircleViewModel : ObservableObject
{
    [ObservableProperty] private Mapsui.Map map = new();
    [ObservableProperty] private string selectedCircleName = "Select a Circle";
    [ObservableProperty] private bool isPeopleSheetOpen;
    [ObservableProperty] private bool isCircleDropdownOpen;
    [ObservableProperty] private bool isTutorialPopupVisible;
    [ObservableProperty] private string syncStatus = string.Empty;
    public ObservableCollection<CircleMember> CircleMembers { get; } = new();
    public ObservableCollection<SupabaseSafetyCircle> MyCircles { get; } = new();
    private Mapsui.Layers.MemoryLayer _pinsLayer = null!;
    private readonly SafetyCircleService _safetyCircleService;
    private readonly IDispatcherTimer _locationTimer;
    private string _currentCircleId = string.Empty;
    private string _accountId = string.Empty;
    private bool _hasCenteredOnUser;
    private bool _active;
    private bool _polling;
    private bool _locationPermission;
    private int _generation;
    private readonly System.Net.Http.HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, byte[]> _avatarRawBytesCache = new();

    public SafetyCircleViewModel(SafetyCircleService service)
    {
        _safetyCircleService = service;
        _locationTimer = Application.Current?.Dispatcher?.CreateTimer()
            ?? throw new InvalidOperationException("MAUI dispatcher is unavailable.");
        _locationTimer.Interval = TimeSpan.FromSeconds(5);
        _locationTimer.Tick += async (_, _) => await PollLocationsAsync();
    }

    public async Task StartAsync()
    {
        Stop();
        _active = true;
        try
        {
            var account = _safetyCircleService.GetCurrentUserId();
            if (_accountId != account)
            {
                CircleMembers.Clear(); MyCircles.Clear(); _avatarRawBytesCache.Clear();
                _currentCircleId = string.Empty;
                _hasCenteredOnUser = false;
            }
            _accountId = account;
            IsTutorialPopupVisible = !await _safetyCircleService.Sync.HasSeenTutorialAsync();
            _locationPermission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() == PermissionStatus.Granted
                || await Permissions.RequestAsync<Permissions.LocationWhenInUse>() == PermissionStatus.Granted;
            await LoadMyCirclesAsync();
            if (_active) _locationTimer.Start();
        }
        catch (Exception ex) { SyncStatus = ex.Message; }
    }

    public void Stop()
    {
        _active = false;
        _generation++;
        _locationTimer.Stop();
    }

    public async Task LoadMyCirclesAsync()
    {
        int generation = _generation;
        try
        {
            var snapshot = await _safetyCircleService.Sync.GetCirclesAsync();
            var selected = await _safetyCircleService.Sync.GetSelectedCircleIdAsync();
            if (!_active || generation != _generation) return;
            MyCircles.Clear();
            foreach (var circle in snapshot.Items) MyCircles.Add(circle);
            SyncStatus = snapshot.IsCached ? "Saved circles · cloud unavailable" : "Circles refreshed from cloud";
            var circleToSelect = MyCircles.FirstOrDefault(c => c.Id == selected);
            if (circleToSelect != null) await SelectCircleAsync(circleToSelect);
            else
            {
                _currentCircleId = string.Empty; SelectedCircleName = "No Circles Joined";
                CircleMembers.Clear(); UpdateMapMarkers(Map);
            }
        }
        catch (Exception ex) { SyncStatus = ex.Message; }
    }

    private async Task SelectCircleAsync(SupabaseSafetyCircle circle)
    {
        await _safetyCircleService.Sync.SelectCircleAsync(circle.Id);
        _generation++;
        _currentCircleId = circle.Id;
        SelectedCircleName = circle.Name;
        IsCircleDropdownOpen = false;
        CircleMembers.Clear(); UpdateMapMarkers(Map);
        await PollLocationsAsync();
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
        if (!_active || _polling || string.IsNullOrEmpty(_currentCircleId)) return;
        _polling = true;
        int generation = _generation;
        string id = _currentCircleId;
        bool StillCurrent() => _active && generation == _generation &&
            _accountId == RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id;
        try
        {
            Location? local = null;
            bool locationShared = false;
            string ownBattery = string.Empty;
            try
            {
                var level = Battery.Default.ChargeLevel;
                if (level >= 0 && level <= 1) ownBattery = $"{(int)Math.Round(level * 100)}%";
            }
            catch { }
            if (_locationPermission)
            {
                try { local = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(4))); }
                catch { }
                if (!StillCurrent()) return;
                if (local != null && (DateTimeOffset.UtcNow - local.Timestamp).Duration() <= TimeSpan.FromMinutes(2)
                    && CircleLocationStatus.ValidCoordinates(local.Latitude, local.Longitude))
                {
                    try
                    {
                        await _safetyCircleService.PushLocationAsync(local.Latitude, local.Longitude, $"Location shared|{ownBattery}");
                        locationShared = true;
                    }
                    catch { }
                }
                else local = null;
            }
            var members = await _safetyCircleService.Sync.GetMembersAsync(id);
            var locations = await _safetyCircleService.Sync.GetLocationsAsync(id);
            if (!StillCurrent()) return;
            var updated = new List<CircleMember>();
            foreach (var member in members.Items)
            {
                bool isMe = member.Id == _accountId;
                var location = locations.Items.FirstOrDefault(l => l.UserId == member.Id);
                bool cachedLocation = locations.IsCached || (isMe && local == null);
                if (isMe && local != null)
                {
                    cachedLocation = false;
                    location = new SupabaseUserLocation { UserId = _accountId, Latitude = local.Latitude,
                        Longitude = local.Longitude, LastUpdated = local.Timestamp.UtcDateTime, StatusText = $"Location shared|{ownBattery}" };
                }
                string name = $"{member.FirstName} {member.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = "Circle member";
                string avatar = member.AvatarUrl;
                if (isMe)
                {
                    var localAvatar = RescuAR.App.Services.Profile.UserProfileService.Instance.GetLocal("AvatarPath");
                    if (File.Exists(localAvatar)) avatar = localAvatar;
                }
                var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string initials = words.Length == 0 ? "?" : $"{words[0][0]}{(words.Length > 1 ? words[^1][0] : ' ')}".Trim().ToUpperInvariant();
                string status = CircleLocationStatus.Describe(location, DateTime.UtcNow, cachedLocation);
                if (isMe && local != null && !locationShared) status = "On this device · location not shared";
                if (isMe && !_locationPermission) status = "Sharing unavailable · " + CircleLocationStatus.Describe(location, DateTime.UtcNow, true);
                var cm = new CircleMember
                {
                    Id = member.Id, Name = isMe ? $"{name} (You)" : name, Initials = initials,
                    StatusText = status, HasLocation = CircleLocationStatus.HasLocation(location),
                    Latitude = location?.Latitude ?? double.NaN, Longitude = location?.Longitude ?? double.NaN,
                    BatteryText = isMe ? ownBattery : CircleLocationStatus.BatteryText(location, DateTime.UtcNow, cachedLocation),
                    ColorTheme = !cachedLocation && CircleLocationStatus.IsFresh(location, DateTime.UtcNow)
                        ? GetColorForUser(member.Id) : Microsoft.Maui.Graphics.Colors.Gray,
                    AvatarUrl = avatar
                };
                byte[]? bytes = null;
                if (!string.IsNullOrWhiteSpace(avatar))
                {
                    if (!_avatarRawBytesCache.TryGetValue(avatar, out bytes))
                    {
                        try
                        {
                            bytes = File.Exists(avatar) ? await File.ReadAllBytesAsync(avatar) : await _httpClient.GetByteArrayAsync(avatar);
                            _avatarRawBytesCache[avatar] = bytes;
                        }
                        catch { }
                    }
                }
                if (!StillCurrent()) return;
                cm.AvatarImageSource = GenerateLife360PinImageSource(bytes, cm.Name, cm.ColorTheme.ToHex(), isMe, initials);
                updated.Add(cm);
            }
            CircleMembers.Clear();
            foreach (var member in updated) CircleMembers.Add(member);
            SyncStatus = members.IsCached || locations.IsCached ? "Saved data · locations may be out of date" : "Member locations refreshed";
            if (local != null && !_hasCenteredOnUser)
            {
                _hasCenteredOnUser = true;
                var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(local.Longitude, local.Latitude);
                Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 25);
            }
            UpdateMapMarkers(Map);
        }
        catch (Exception ex)
        {
            if (StillCurrent()) SyncStatus = $"Locations unavailable: {ex.Message}";
            if (RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id != _accountId)
            {
                CircleMembers.Clear(); MyCircles.Clear(); UpdateMapMarkers(Map); Stop();
            }
        }
        finally
        {
            _polling = false;
            if (_active && generation != _generation) _ = PollLocationsAsync();
        }
    }

    private Microsoft.Maui.Graphics.Color GetColorForUser(string id)
    {
        var colors = new[] { "#0A8491", "#EAB308", "#931492", "#E11D48", "#2563EB", "#16A34A" };
        return Microsoft.Maui.Graphics.Color.FromArgb(colors[(uint)id.GetHashCode() % (uint)colors.Length]);
    }

    public async Task InitializeMapAsync(MapControl mapControl)
    {
        try
        {
            var map = new Mapsui.Map
            {
                CRS = "EPSG:3857"
            };

            // Load Online OpenStreetMap Base Layer
            map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());

            // Center and Zoom to Map Data (Default Marikina or user location)
            var (homeX, homeY) = Mapsui.Projections.SphericalMercator.FromLonLat(121.1029, 14.6507);
            map.Navigator.CenterOnAndZoomTo(new MPoint(homeX, homeY), 38.2);

            Map = map;
            mapControl.Map = map;

            // Draw initial pins
            UpdateMapMarkers(map);
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

        // Safety Circle Member Pins
        foreach (var member in CircleMembers.Where(m => m.HasLocation))
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
            
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

    [RelayCommand]
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
        var name = await Shell.Current.DisplayPromptAsync("Create circle", "Circle name:", "Create", "Cancel", maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var circle = await _safetyCircleService.CreateCircleAsync(name);
            await Shell.Current.DisplayAlert("Circle created", $"Invite code: {circle.InviteCode}", "OK");
            await LoadMyCirclesAsync();
        }
        catch (Exception ex) { await Shell.Current.DisplayAlert("Creation not confirmed", ex.Message, "OK"); }
    }

    [RelayCommand]
    private async Task JoinCircleAsync()
    {
        var code = await Shell.Current.DisplayPromptAsync("Join circle", "6-character invite code:", "Join", "Cancel", maxLength: 6);
        if (string.IsNullOrWhiteSpace(code)) return;
        try { await _safetyCircleService.JoinCircleWithCodeAsync(code); await LoadMyCirclesAsync(); }
        catch (Exception ex) { await Shell.Current.DisplayAlert("Join not confirmed", ex.Message, "OK"); }
    }

    [RelayCommand]
    private async Task SelectCircleFromListAsync(SupabaseSafetyCircle circle)
    {
        try { await SelectCircleAsync(circle); }
        catch (Exception ex) { SyncStatus = ex.Message; }
    }

    [RelayCommand]
    private void CenterOnMember(CircleMember member)
    {
        if (!member.HasLocation) { SyncStatus = $"{member.Name}: location unavailable"; return; }
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
        Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 25);
        IsPeopleSheetOpen = false;
    }

    [RelayCommand]
    private async Task CopyInviteCodeAsync()
    {
        var circle = MyCircles.FirstOrDefault(c => c.Id == _currentCircleId);
        if (circle == null) return;
        await Clipboard.Default.SetTextAsync(circle.InviteCode);
        await Shell.Current.DisplayAlert("Invite code", $"{circle.InviteCode} · copied", "OK");
    }

    [RelayCommand]
    private async Task CloseTutorialAsync()
    {
        await _safetyCircleService.Sync.MarkTutorialSeenAsync();
        IsTutorialPopupVisible = false;
    }

    [RelayCommand]
    private async Task ManageCirclesAsync() => await Shell.Current.GoToAsync("SafetyCircleSettingsPage");

    [RelayCommand]
    private async Task OpenChatAsync()
    {
        if (string.IsNullOrEmpty(_currentCircleId))
        {
            await Shell.Current.DisplayAlert("Select circle", "Create or join a circle first.", "OK");
            return;
        }
        await Shell.Current.GoToAsync($"CircleChatPage?circleId={Uri.EscapeDataString(_currentCircleId)}&circleName={Uri.EscapeDataString(SelectedCircleName)}");
    }
}
