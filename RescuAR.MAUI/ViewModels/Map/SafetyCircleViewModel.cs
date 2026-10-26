using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.UI.Maui;
using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Map;

public partial class CircleMember : ObservableObject
{
    public required CirclePresence Presence { get; init; }
    public string UserId => Presence.Person.UserId;
    public string Name { get; init; } = "";
    public string PhoneNumber => Presence.Person.Phone;
    public bool HasPhoneNumber => !string.IsNullOrWhiteSpace(PhoneNumber);
    public string Initials { get; init; } = "";
    [ObservableProperty] private string locationSubtitle = "";
    public string SinceTimeText { get; init; } = "";
    public string BatteryText => Presence.BatteryPercent is { } percent ? $"{percent}%{(Presence.IsCharging ? " ⚡" : "")}" : "Unknown";
    public Color BatteryColor => Presence.BatteryPercent is null || !Presence.IsRecent ? Colors.Gray : Presence.IsCharging ? Colors.RoyalBlue : Presence.BatteryPercent <= 20 ? Colors.Crimson : Colors.Teal;
    public Color ColorTheme { get; init; } = Colors.Gray;
    public bool HasPosition => Presence.Position is not null;
    public double Latitude => Presence.Position?.Latitude ?? double.NaN;
    public double Longitude => Presence.Position?.Longitude ?? double.NaN;
    public string PinImage { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAvatarUrl), nameof(HasNoAvatarUrl))] private ImageSource? avatarImage;
    public bool HasAvatarUrl => AvatarImage is not null;
    public bool HasNoAvatarUrl => !HasAvatarUrl;
}

public partial class SafetyCircleViewModel : ObservableObject
{
    private readonly CircleMembershipService memberships = CircleMembershipRuntime.Current;
    private readonly CircleMapCoordinator coordinator;
    private readonly IDispatcherTimer timer;
    private readonly HttpClient photos = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, byte[]> avatarBytes = new();
    private readonly Dictionary<string, string> placeNames = new();
    private readonly SemaphoreSlim avatarRequests = new(3, 3);
    private CircleMembershipScope? owner;
    private CancellationTokenSource? page;
    private long version;
    private bool polling;
    private bool settingSharing;
    private bool enablingSharing;
    private long sharingRequest;
    private bool centered;
    private string selectedId = "";
    public ObservableCollection<CircleMembership> MyCircles { get; } = new();
    public ObservableCollection<CircleMember> CircleMembers { get; } = new();
    [ObservableProperty] private Mapsui.Map map = new();
    [ObservableProperty] private string selectedCircleName = "Select a Circle";
    [ObservableProperty] private bool isCircleDropdownOpen;
    [ObservableProperty] private bool isSharingEnabled;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string sharingStatus = "Sharing Off.";

    public SafetyCircleViewModel()
    {
        coordinator = new(memberships, new SupabaseCirclePresenceRemote(CircleMembershipRuntime.Context), new MauiCircleDeviceReader(), () => DateTimeOffset.UtcNow);
        timer = Application.Current?.Dispatcher?.CreateTimer() ?? throw new InvalidOperationException("A page dispatcher is required.");
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += async (_, _) => await PollAsync();
    }
    private void Require(CircleMembershipScope scope, long request)
    {
        if (page is null || page.IsCancellationRequested || version != request || owner != scope) throw new OperationCanceledException();
        memberships.Require(scope);
    }
    public async Task StartAsync(MapControl control)
    {
        Stop();
        long request = version;
        try
        {
            owner = memberships.Capture(); page = new(); coordinator.Start();
            if (!Map.Layers.Any())
            {
                Map = new Mapsui.Map { CRS = "EPSG:3857" };
                Map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());
                ZoomReset();
            }
            foreach (var layer in Map.Layers) layer.Enabled = true;
            control.Map = Map;
            await PollAsync();
            if (request == version && page is not null) timer.Start();
        }
        catch (OperationCanceledException) { if (request == version) { Stop(); StatusMessage = "Your account changed. Reopen the Map tab."; } }
        catch { if (request == version) StatusMessage = "Circle data could not be loaded. Check your sign-in and connection, then Refresh."; }
    }
    public void Stop()
    {
        Interlocked.Increment(ref version); timer.Stop(); coordinator.Stop();
        page?.Cancel(); page?.Dispose(); page = null; owner = null;
        settingSharing = true; IsSharingEnabled = false; settingSharing = false;
        sharingRequest++; enablingSharing = false;
        IsBusy = false; polling = false; selectedId = ""; centered = false; IsCircleDropdownOpen = false;
        SelectedCircleName = "Select a Circle"; StatusMessage = ""; SharingStatus = "Sharing Off. No new positions are uploaded.";
        MyCircles.Clear(); CircleMembers.Clear(); avatarBytes.Clear(); placeNames.Clear();
        foreach (var layer in Map.Layers) layer.Enabled = false;
        var oldPins = Map.Layers.FirstOrDefault(layer => layer.Name == "MapPins");
        if (oldPins is not null) Map.Layers.Remove(oldPins);
    }
    partial void OnIsSharingEnabledChanged(bool value)
    {
        if (settingSharing) return;
        if (!value) { sharingRequest++; enablingSharing = false; coordinator.SetSharing(false); SharingStatus = "Sharing Off. Previous last-known records remain."; return; }
        _ = EnableSharingAsync();
    }
    private async Task EnableSharingAsync()
    {
        if (owner is not { } scope || page is null) { ResetSharing(); return; }
        long request = version, toggle = ++sharingRequest;
        enablingSharing = true;
        try
        {
            Require(scope, request);
            var permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (permission != PermissionStatus.Granted) permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            Require(scope, request);
            if (!IsSharingEnabled || toggle != sharingRequest) return;
            if (permission != PermissionStatus.Granted) { ResetSharing(); SharingStatus = "Sharing Off — location permission was not granted."; return; }
            coordinator.SetSharing(true); SharingStatus = "Sharing On — waiting for a valid location and confirmed membership.";
            await PollAsync();
        }
        catch { if (request == version && toggle == sharingRequest) { ResetSharing(); SharingStatus = "Sharing Off — permission or account could not be confirmed."; } }
        finally { if (toggle == sharingRequest) enablingSharing = false; }
    }
    private void ResetSharing() { sharingRequest++; enablingSharing = false; coordinator.SetSharing(false); settingSharing = true; IsSharingEnabled = false; settingSharing = false; }

    private async Task PollAsync()
    {
        if (polling || owner is not { } scope || page is null) return;
        long request = version; var token = page.Token;
        polling = true; IsBusy = true;
        try
        {
            var snapshot = await coordinator.PollAsync();
            Require(scope, request);
            if (snapshot is null) return;
            MyCircles.Clear(); foreach (var circle in snapshot.Memberships.Circles) MyCircles.Add(circle);
            selectedId = snapshot.Memberships.SelectedId;
            SelectedCircleName = MyCircles.FirstOrDefault(circle => circle.Id == selectedId)?.Name ?? "No Circles joined";
            StatusMessage = snapshot.Memberships.CacheSaved ? snapshot.Notice : snapshot.Memberships.Notice + " " + snapshot.Notice;
            if (snapshot.Memberships.IsCached) StatusMessage += " Circle list is cached / offline.";
            SharingStatus = !IsSharingEnabled ? "Sharing Off. No new positions are uploaded; last-known records remain." : enablingSharing ? "Confirming location permission…" : snapshot.SharingNotice;
            if (!coordinator.SharingEnabled && IsSharingEnabled && !enablingSharing) ResetSharing();
            var rows = snapshot.People.Select(person => Row(person, scope.UserId, snapshot.PeopleCached)).ToList();
            CircleMembers.Clear(); foreach (var row in rows) CircleMembers.Add(row);
            UpdateMapMarkers();
            if (!centered)
            {
                var self = rows.FirstOrDefault(row => row.UserId == scope.UserId && row.Presence.IsRecent && row.HasPosition);
                if (self is not null) { CenterOnMember(self); centered = true; }
            }
            await Task.WhenAll(rows.SelectMany(row => new[] { LoadAvatarAsync(row, scope, request, token), LoadPlaceAsync(row, scope, request, token) }));
            Require(scope, request); UpdateMapMarkers();
        }
        catch (OperationCanceledException) { if (request == version) { Stop(); StatusMessage = "The account or Circle screen changed. Reopen the Map tab."; } }
        catch
        {
            if (request == version)
            {
                ResetSharing(); MyCircles.Clear(); CircleMembers.Clear(); selectedId = ""; SelectedCircleName = "Membership not confirmed"; UpdateMapMarkers();
                StatusMessage = "Circle access or member readings could not be confirmed. Refresh before continuing.";
                SharingStatus = "Sharing Off. No new positions are uploaded.";
            }
        }
        finally { if (request == version) { polling = false; IsBusy = false; } }
    }
    internal static CircleMember Row(CirclePresence presence, string self, bool cached)
    {
        string name = presence.Person.Name + (presence.Person.UserId == self ? " (You)" : "");
        string initials = string.Concat(presence.Person.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
        return new() { Presence = presence, Name = name, Initials = initials.Length > 0 ? initials : "?",
            LocationSubtitle = presence.Position is { } position ? $"Last shared: {position.Latitude:F5}, {position.Longitude:F5}" : "Location unavailable",
            SinceTimeText = presence.Position is { } at ? at.RecordedAt.ToLocalTime().ToString("MMM d, HH:mm") + (cached ? " · cached" : presence.IsRecent ? " · recent" : " · last known") : "No dated position",
            ColorTheme = cached || !presence.IsRecent ? Colors.Gray : Color.FromArgb(new[] { "#0A8491", "#931492", "#2563EB", "#16A34A" }[(uint)presence.Person.UserId.GetHashCode() % 4]) };
    }
    private async Task LoadAvatarAsync(CircleMember row, CircleMembershipScope scope, long request, CancellationToken token)
    {
        string url = CirclePresencePolicy.SafeAvatar(row.Presence.Person.AvatarUrl);
        if (url.Length == 0) return;
        await avatarRequests.WaitAsync(token);
        try
        {
            Require(scope, request);
            if (!avatarBytes.TryGetValue(url, out var bytes))
            {
                using var response = await photos.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 4_194_304) return;
                using var input = await response.Content.ReadAsStreamAsync(token);
                using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                { if (output.Length + count > 4_194_304) return; await output.WriteAsync(buffer.AsMemory(0, count), token); }
                bytes = output.ToArray(); Require(scope, request); avatarBytes[url] = bytes;
            }
            Require(scope, request);
            row.AvatarImage = ImageSource.FromStream(() => new MemoryStream(bytes, false));
            row.PinImage = GenerateLife360PinImageSource(bytes, row.Name, row.ColorTheme.ToHex(), row.UserId == scope.UserId, row.Initials);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Keep initials when the optional image is unavailable. */ }
        finally { avatarRequests.Release(); }
    }
    private async Task LoadPlaceAsync(CircleMember row, CircleMembershipScope scope, long request, CancellationToken token)
    {
        if (!row.HasPosition || !CircleMembershipRuntime.Context.IsOnline) return;
        string key = row.Latitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + row.Longitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        await avatarRequests.WaitAsync(token);
        try
        {
            Require(scope, request);
            if (!placeNames.TryGetValue(key, out string? label))
            {
                label = row.LocationSubtitle;
                try
                {
                    var places = await Geocoding.Default.GetPlacemarksAsync(new Microsoft.Maui.Devices.Sensors.Location(row.Latitude, row.Longitude)).WaitAsync(TimeSpan.FromSeconds(4), token);
                    Require(scope, request);
                    string? street = places?.FirstOrDefault()?.Thoroughfare;
                    if (!string.IsNullOrWhiteSpace(street)) label = "Last shared near " + street.Trim() + " (approx.)";
                }
                catch (OperationCanceledException) { throw; }
                catch { /* Keep recorded coordinates when the optional street lookup is unavailable. */ }
                Require(scope, request);
                if (placeNames.Count >= 256) placeNames.Clear();
                placeNames[key] = label;
            }
            Require(scope, request); row.LocationSubtitle = label;
        }
        finally { avatarRequests.Release(); }
    }
    private void UpdateMapMarkers()
    {
        var features = new List<Mapsui.Nts.GeometryFeature>();
        foreach (var member in CircleMembers.Where(member => member.HasPosition))
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
            var feature = new Mapsui.Nts.GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
            string picture = member.PinImage.Length > 0 ? member.PinImage : GenerateLife360PinImageSource(null, member.Name, member.ColorTheme.ToHex(), member.UserId == owner?.UserId, member.Initials);
            feature.Styles.Add(new Mapsui.Styles.ImageStyle { Image = picture, SymbolScale = 0.5, Offset = new Mapsui.Styles.Offset(0, 35) });
            features.Add(feature);
        }
        var old = Map.Layers.FirstOrDefault(layer => layer.Name == "MapPins");
        if (old is not null) Map.Layers.Remove(old);
        Map.Layers.Add(new Mapsui.Layers.MemoryLayer { Name = "MapPins", Features = features }); Map.Refresh();
    }

    [RelayCommand] private void ToggleCircleDropdown() => IsCircleDropdownOpen = !IsCircleDropdownOpen;
    [RelayCommand] private void ZoomIn() => Map.Navigator.ZoomIn();
    [RelayCommand] private void ZoomOut() => Map.Navigator.ZoomOut();
    [RelayCommand] private void ZoomReset()
    {
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(121.1029, 14.6507);
        Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 38.2);
    }
    [RelayCommand] private void CenterOnMember(CircleMember member)
    {
        if (member is null || !member.HasPosition || !CircleMembers.Contains(member)) return;
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
        Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 22);
    }
    [RelayCommand] private async Task SelectCircleFromList(CircleMembership circle)
    {
        if (circle is null || owner is not { } scope || IsBusy) return;
        long request = version;
        try
        {
            Require(scope, request); IsBusy = true; ResetSharing(); CircleMembers.Clear(); UpdateMapMarkers();
            await coordinator.SelectAsync(circle.Id); Require(scope, request);
            IsCircleDropdownOpen = false; centered = false; IsBusy = false; await PollAsync();
        }
        catch { if (request == version) { IsBusy = false; StatusMessage = "Circle selection could not be confirmed. Refresh and try again."; } }
    }
    [RelayCommand] private async Task Refresh() { coordinator.RefreshMemberships(); await PollAsync(); }
    [RelayCommand] private Task CreateCircle() => Navigate("SafetyCircleSettingsPage");
    [RelayCommand] private Task JoinCircle() => Navigate("SafetyCircleSettingsPage");
    [RelayCommand] private Task ManageCircles() => Navigate("SafetyCircleSettingsPage");
    [RelayCommand] private Task OpenGeneralMap() => Navigate("GeneralMap");
    [RelayCommand] private Task OpenNotifications() => Navigate("NotificationsPage");
    private async Task Navigate(string route)
    {
        if (Shell.Current is null) return;
        try { await Shell.Current.GoToAsync(route); }
        catch { StatusMessage = "This screen could not be opened. Try again."; }
    }
    [RelayCommand] private async Task OpenChat()
    {
        if (owner is not { } scope || IsBusy || selectedId.Length == 0) return;
        long request = version;
        try
        {
            string id = selectedId;
            var fresh = await memberships.LoadAsync(scope); Require(scope, request);
            var circle = fresh.Circles.FirstOrDefault(circle => circle.Id == id);
            if (fresh.IsCached || circle is null) { StatusMessage = "Connect and confirm this Circle before opening chat."; return; }
            await Navigate($"CircleChatPage?circleId={Uri.EscapeDataString(id)}&circleName={Uri.EscapeDataString(circle.Name)}");
        }
        catch { if (request == version) StatusMessage = "Chat membership could not be confirmed. Refresh your Circles."; }
    }
    [RelayCommand] private void CallMember(CircleMember member)
    {
        if (owner is not { } scope || member is null || !CircleMembers.Contains(member) || !member.HasPhoneNumber) return;
        try { Require(scope, version); if (PhoneDialer.Default.IsSupported) PhoneDialer.Default.Open(member.PhoneNumber); else StatusMessage = "Phone dialing is unavailable on this device."; }
        catch { StatusMessage = "The phone app could not be opened."; }
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
}
