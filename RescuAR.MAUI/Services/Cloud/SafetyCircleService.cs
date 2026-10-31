using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RescuAR.App.Models;
using Microsoft.Maui.Storage;
using RescuAR.Services;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;

namespace RescuAR.App.Services.Cloud
{
    public class SafetyCircleService
    {
        private Supabase.Client GetClient()
        {
            var client = SupabaseService.Instance.Client;
            if (client == null)
            {
                throw new InvalidOperationException("Supabase client is not initialized.");
            }
            return client;
        }

        public string GetCurrentUserId()
        {
            try
            {
                var client = SupabaseService.Instance.Client;
                var userId = client?.Auth.CurrentUser?.Id
                    ?? client?.Auth.CurrentSession?.User?.Id
                    ?? Preferences.Default.Get("current_user_id", string.Empty);

                if (!string.IsNullOrWhiteSpace(userId) && Guid.TryParse(userId, out _))
                {
                    Preferences.Default.Set("current_user_id", userId);
                    return userId;
                }
            }
            catch { }

            var email = Preferences.Default.Get("UserEmail", string.Empty);
            if (!string.IsNullOrWhiteSpace(email))
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
                var deterministicGuid = new Guid(hash).ToString();
                Preferences.Default.Set("current_user_id", deterministicGuid);
                return deterministicGuid;
            }

            var localId = Preferences.Default.Get("local_device_user_id", string.Empty);
            if (string.IsNullOrWhiteSpace(localId) || !Guid.TryParse(localId, out _))
            {
                localId = Guid.NewGuid().ToString();
                Preferences.Default.Set("local_device_user_id", localId);
            }
            Preferences.Default.Set("current_user_id", localId);
            return localId;
        }

        // --- Circle Management ---

        public class LocalCircleDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string InviteCode { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public string? CreatedBy { get; set; }
        }

        public async Task<SupabaseSafetyCircle> CreateCircleAsync(string name)
        {
            var userId = GetCurrentUserId();
            string inviteCode = GenerateInviteCode();

            var newCircle = new SupabaseSafetyCircle
            {
                Id = Guid.NewGuid().ToString(),
                Name = name.Trim(),
                InviteCode = inviteCode,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            // 1. Immediately cache locally so circles are never lost
            SaveLocalCircle(newCircle);
            Preferences.Default.Set("SelectedCircleId", newCircle.Id);

            try
            {
                var client = GetClient();
                var response = await client.From<SupabaseSafetyCircle>().Insert(newCircle);
                var circle = response.Models.FirstOrDefault() ?? newCircle;

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await JoinCircleInternalAsync(circle.Id, userId);
                }

                SaveLocalCircle(circle);
                Preferences.Default.Set("SelectedCircleId", circle.Id);
                return circle;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Supabase circle creation warning: {ex.Message}");
            }

            return newCircle;
        }

        public void SaveLocalCircle(SupabaseSafetyCircle circle)
        {
            if (circle == null) return;
            try
            {
                var json = Preferences.Default.Get("LocalSafetyCircles", "[]");
                var list = System.Text.Json.JsonSerializer.Deserialize<List<LocalCircleDto>>(json) ?? new();
                var existing = list.FirstOrDefault(c => (!string.IsNullOrEmpty(c.Id) && c.Id == circle.Id) || (!string.IsNullOrEmpty(c.InviteCode) && c.InviteCode.Equals(circle.InviteCode, StringComparison.OrdinalIgnoreCase)));
                if (existing != null)
                {
                    existing.Name = circle.Name;
                    existing.InviteCode = circle.InviteCode;
                    if (!string.IsNullOrEmpty(circle.CreatedBy)) existing.CreatedBy = circle.CreatedBy;
                }
                else
                {
                    list.Add(new LocalCircleDto
                    {
                        Id = circle.Id,
                        Name = circle.Name,
                        InviteCode = circle.InviteCode,
                        CreatedAt = circle.CreatedAt,
                        CreatedBy = circle.CreatedBy
                    });
                }
                Preferences.Default.Set("LocalSafetyCircles", System.Text.Json.JsonSerializer.Serialize(list));

                // Also maintain saved list of joined circle IDs
                var joinedIdsJson = Preferences.Default.Get("UserJoinedCircleIds", "[]");
                var joinedIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(joinedIdsJson) ?? new();
                if (!string.IsNullOrEmpty(circle.Id) && !joinedIds.Contains(circle.Id))
                {
                    joinedIds.Add(circle.Id);
                    Preferences.Default.Set("UserJoinedCircleIds", System.Text.Json.JsonSerializer.Serialize(joinedIds));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveLocalCircle error: {ex.Message}");
            }
        }

        public async Task<SupabaseSafetyCircle> JoinCircleWithCodeAsync(string inviteCode)
        {
            var client = GetClient();
            var userId = GetCurrentUserId();

            string cleanCode = (inviteCode ?? string.Empty).Trim().ToUpper();

            SupabaseSafetyCircle? circle = null;

            try
            {
                // 1. Find circle by code in Supabase
                var circleResponse = await client.From<SupabaseSafetyCircle>()
                    .Where(x => x.InviteCode == cleanCode)
                    .Get();

                circle = circleResponse.Models.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding circle by code: {ex.Message}");
            }

            // 2. If not found by query, fetch all circles and match case-insensitively
            if (circle == null)
            {
                try
                {
                    var allCircles = await client.From<SupabaseSafetyCircle>().Get();
                    circle = allCircles.Models.FirstOrDefault(c => string.Equals(c.InviteCode?.Trim(), cleanCode, StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }

            // 3. Fallback to local circles
            if (circle == null)
            {
                var json = Preferences.Default.Get("LocalSafetyCircles", "[]");
                var list = System.Text.Json.JsonSerializer.Deserialize<List<SupabaseSafetyCircle>>(json) ?? new();
                circle = list.FirstOrDefault(c => string.Equals(c.InviteCode?.Trim(), cleanCode, StringComparison.OrdinalIgnoreCase));
            }

            if (circle == null)
            {
                throw new Exception($"Invalid invite code '{cleanCode}'. Please verify the 6-character code with your friend.");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await JoinCircleInternalAsync(circle.Id, userId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Join membership error: {ex.Message}");
            }

            SaveLocalCircle(circle);
            Preferences.Default.Set("SelectedCircleId", circle.Id);
            return circle;
        }

        private async Task JoinCircleInternalAsync(string circleId, string userId)
        {
            if (string.IsNullOrWhiteSpace(circleId) || string.IsNullOrWhiteSpace(userId)) return;

            try
            {
                var client = GetClient();

                // Check if already a member in cloud
                var existing = await client.From<SupabaseCircleMember>()
                    .Where(x => x.CircleId == circleId && x.UserId == userId)
                    .Get();

                if (existing.Models.Any()) return; // Already joined

                var member = new SupabaseCircleMember
                {
                    CircleId = circleId,
                    UserId = userId,
                    JoinedAt = DateTime.UtcNow
                };

                await client.From<SupabaseCircleMember>().Insert(member);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JoinCircleInternalAsync error: {ex.Message}");
            }
        }

        public async Task<List<SupabaseSafetyCircle>> GetMyCirclesAsync()
        {
            var result = new Dictionary<string, SupabaseSafetyCircle>(StringComparer.OrdinalIgnoreCase);

            // Deleted circle blacklist
            var deletedJson = Preferences.Default.Get("UserDeletedCircleIds", "[]");
            var deletedIds = System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(deletedJson) ?? new(StringComparer.OrdinalIgnoreCase);

            // 1. Load local circles first to guarantee instant availability
            try
            {
                var json = Preferences.Default.Get("LocalSafetyCircles", "[]");
                var localList = System.Text.Json.JsonSerializer.Deserialize<List<LocalCircleDto>>(json) ?? new();
                foreach (var c in localList)
                {
                    if (string.IsNullOrWhiteSpace(c.Id) || deletedIds.Contains(c.Id))
                        continue;

                    var model = new SupabaseSafetyCircle
                    {
                        Id = c.Id,
                        Name = c.Name,
                        InviteCode = c.InviteCode,
                        CreatedAt = c.CreatedAt,
                        CreatedBy = c.CreatedBy
                    };

                    result[c.Id] = model;
                }
            }
            catch { }

            // 2. Load UserJoinedCircleIds
            var joinedIdsJson = Preferences.Default.Get("UserJoinedCircleIds", "[]");
            var joinedIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(joinedIdsJson) ?? new();

            // 3. Load cloud circles (both created by user and joined memberships)
            try
            {
                var client = GetClient();
                var userId = GetCurrentUserId();

                // Fetch all circles from Supabase cloud
                var allCirclesResp = await client.From<SupabaseSafetyCircle>().Get();
                var cloudCircles = allCirclesResp.Models ?? new List<SupabaseSafetyCircle>();

                // Also fetch memberships
                var memberCircleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    try
                    {
                        var membershipsResponse = await client.From<SupabaseCircleMember>()
                            .Where(x => x.UserId == userId)
                            .Get();
                        foreach (var m in membershipsResponse.Models)
                        {
                            if (!string.IsNullOrWhiteSpace(m.CircleId)) memberCircleIds.Add(m.CircleId);
                        }
                    }
                    catch { }
                }

                foreach (var sc in cloudCircles)
                {
                    if (string.IsNullOrWhiteSpace(sc.Id) || deletedIds.Contains(sc.Id))
                        continue;

                    bool isMyCircle = false;

                    // A. Created by user
                    if (!string.IsNullOrWhiteSpace(userId) && string.Equals(sc.CreatedBy, userId, StringComparison.OrdinalIgnoreCase))
                        isMyCircle = true;

                    // B. User is member in safety_circle_members
                    if (memberCircleIds.Contains(sc.Id))
                        isMyCircle = true;

                    // C. User has joined it locally via invite code
                    if (joinedIds.Contains(sc.Id))
                        isMyCircle = true;

                    // D. Circle is in result from local cache
                    if (result.ContainsKey(sc.Id) || (!string.IsNullOrWhiteSpace(sc.InviteCode) && result.ContainsKey(sc.InviteCode)))
                        isMyCircle = true;

                    if (isMyCircle)
                    {
                        result[sc.Id] = sc;
                        SaveLocalCircle(sc);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching remote circles: {ex.Message}");
            }

            return result.Values.ToList();
        }

        public async Task DeleteCircleAsync(string circleId)
        {
            if (string.IsNullOrWhiteSpace(circleId)) return;
            var userId = GetCurrentUserId();

            // 1. Record in persistent deleted blacklist
            try
            {
                var deletedJson = Preferences.Default.Get("UserDeletedCircleIds", "[]");
                var deletedIds = System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(deletedJson) ?? new(StringComparer.OrdinalIgnoreCase);
                deletedIds.Add(circleId.Trim());
                Preferences.Default.Set("UserDeletedCircleIds", System.Text.Json.JsonSerializer.Serialize(deletedIds));
            }
            catch { }

            // 2. Remove from local preferences cache
            try
            {
                var json = Preferences.Default.Get("LocalSafetyCircles", "[]");
                var list = System.Text.Json.JsonSerializer.Deserialize<List<LocalCircleDto>>(json) ?? new();
                list.RemoveAll(c => string.Equals(c.Id, circleId, StringComparison.OrdinalIgnoreCase));
                Preferences.Default.Set("LocalSafetyCircles", System.Text.Json.JsonSerializer.Serialize(list));

                var joinedIdsJson = Preferences.Default.Get("UserJoinedCircleIds", "[]");
                var joinedIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(joinedIdsJson) ?? new();
                joinedIds.RemoveAll(id => string.Equals(id, circleId, StringComparison.OrdinalIgnoreCase));
                Preferences.Default.Set("UserJoinedCircleIds", System.Text.Json.JsonSerializer.Serialize(joinedIds));

                if (string.Equals(Preferences.Default.Get("SelectedCircleId", string.Empty), circleId, StringComparison.OrdinalIgnoreCase))
                {
                    Preferences.Default.Remove("SelectedCircleId");
                }
            }
            catch { }

            // 3. Remove membership / circle record in Supabase cloud
            try
            {
                var client = GetClient();

                // Delete membership for current user
                await client.From<SupabaseCircleMember>()
                    .Where(x => x.CircleId == circleId && x.UserId == userId)
                    .Delete();

                // Also delete circle row if current user was the creator
                await client.From<SupabaseSafetyCircle>()
                    .Where(x => x.Id == circleId && x.CreatedBy == userId)
                    .Delete();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting circle in cloud: {ex.Message}");
            }
        }

        public async Task<List<User>> GetCircleMembersAsync(string circleId)
        {
            if (string.IsNullOrWhiteSpace(circleId)) return new List<User>();

            var client = GetClient();
            var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. Fetch member user IDs from memberships table
                var membershipsResponse = await client.From<SupabaseCircleMember>()
                    .Where(x => x.CircleId == circleId)
                    .Get();

                foreach (var m in membershipsResponse.Models)
                {
                    if (!string.IsNullOrWhiteSpace(m.UserId))
                    {
                        userIds.Add(m.UserId);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCircleMembers memberships error: {ex.Message}");
            }

            try
            {
                // 2. Also fetch circle creator to ensure creator is always included
                var circleResp = await client.From<SupabaseSafetyCircle>()
                    .Where(x => x.Id == circleId)
                    .Get();
                var circleObj = circleResp.Models.FirstOrDefault();
                if (circleObj != null && !string.IsNullOrWhiteSpace(circleObj.CreatedBy))
                {
                    userIds.Add(circleObj.CreatedBy);
                }
            }
            catch { }

            // Current user ID
            var currentUserId = GetCurrentUserId();
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                userIds.Add(currentUserId);
            }

            if (!userIds.Any()) return new List<User>();

            var usersList = new List<User>();
            var targetIds = userIds.ToList();

            try
            {
                // Fetch from users table
                var usersResponse = await client.From<User>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.In, targetIds)
                    .Get();

                if (usersResponse.Models != null)
                {
                    foreach (var u in usersResponse.Models)
                    {
                        if (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName) || !string.IsNullOrWhiteSpace(u.Username))
                        {
                            usersList.Add(u);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch users from database: {ex.Message}");
            }

            // Try profiles table for any members missing from users table
            var missingIds = targetIds.Where(uid => !usersList.Any(u => string.Equals(u.Id, uid, StringComparison.OrdinalIgnoreCase))).ToList();
            if (missingIds.Any())
            {
                try
                {
                    var profilesResponse = await client.From<SupabaseProfile>()
                        .Filter("id", Supabase.Postgrest.Constants.Operator.In, missingIds)
                        .Get();

                    if (profilesResponse.Models != null)
                    {
                        foreach (var p in profilesResponse.Models)
                        {
                            if (!string.IsNullOrWhiteSpace(p.FirstName) || !string.IsNullOrWhiteSpace(p.LastName))
                            {
                                usersList.Add(new User
                                {
                                    Id = p.Id,
                                    FirstName = p.FirstName,
                                    LastName = p.LastName
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            // Ensure every member ID has a friendly representation
            foreach (var uid in targetIds)
            {
                var existingUser = usersList.FirstOrDefault(u => string.Equals(u.Id, uid, StringComparison.OrdinalIgnoreCase));
                if (existingUser == null)
                {
                    string fn = string.Empty;
                    string ln = string.Empty;

                    // If this is current user, pull from active Auth session or local preferences
                    if (client.Auth.CurrentUser != null && string.Equals(client.Auth.CurrentUser.Id, uid, StringComparison.OrdinalIgnoreCase))
                    {
                        if (client.Auth.CurrentUser.UserMetadata != null)
                        {
                            if (client.Auth.CurrentUser.UserMetadata.TryGetValue("first_name", out var f) && f != null)
                                fn = f.ToString()?.Trim() ?? string.Empty;
                            if (client.Auth.CurrentUser.UserMetadata.TryGetValue("last_name", out var l) && l != null)
                                ln = l.ToString()?.Trim() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(fn) && client.Auth.CurrentUser.UserMetadata.TryGetValue("full_name", out var full) && full != null)
                            {
                                var parts = full.ToString()?.Trim().Split(' ');
                                if (parts?.Length > 0) fn = parts[0];
                                if (parts?.Length > 1) ln = string.Join(" ", parts.Skip(1));
                            }
                        }

                        if (string.IsNullOrWhiteSpace(fn))
                        {
                            fn = Preferences.Default.Get("UserFirstName", string.Empty);
                            ln = Preferences.Default.Get("UserLastName", string.Empty);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(fn))
                    {
                        fn = "Family";
                        ln = "Member";
                    }

                    usersList.Add(new User
                    {
                        Id = uid,
                        FirstName = fn,
                        LastName = ln
                    });
                }
                else if (string.IsNullOrWhiteSpace(existingUser.FirstName))
                {
                    if (!string.IsNullOrWhiteSpace(existingUser.Username))
                    {
                        existingUser.FirstName = existingUser.Username;
                    }
                    else if (!string.IsNullOrWhiteSpace(existingUser.Email))
                    {
                        var emailPrefix = existingUser.Email.Split('@')[0];
                        existingUser.FirstName = char.ToUpper(emailPrefix[0]) + (emailPrefix.Length > 1 ? emailPrefix.Substring(1) : "");
                    }
                }
            }

            return usersList;
        }

        // --- Location Tracking ---

        public async Task PushLocationAsync(double lat, double lon, string status = "In Transit")
        {
            try
            {
                var client = GetClient();
                var userId = GetCurrentUserId();

                var location = new SupabaseUserLocation
                {
                    UserId = userId,
                    Latitude = lat,
                    Longitude = lon,
                    StatusText = status,
                    LastUpdated = DateTime.UtcNow
                };

                try
                {
                    await client.From<SupabaseUserLocation>().Upsert(location);
                }
                catch
                {
                    try
                    {
                        await client.From<SupabaseUserLocation>()
                            .Where(x => x.UserId == userId)
                            .Set(x => x.Latitude, lat)
                            .Set(x => x.Longitude, lon)
                            .Set(x => x.StatusText, status)
                            .Set(x => x.LastUpdated, DateTime.UtcNow)
                            .Update();
                    }
                    catch
                    {
                        await client.From<SupabaseUserLocation>().Insert(location);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to push location: {ex.Message}");
            }
        }

        public async Task<List<SupabaseUserLocation>> GetCircleLocationsAsync(string circleId)
        {
            if (string.IsNullOrWhiteSpace(circleId)) return new List<SupabaseUserLocation>();

            var client = GetClient();
            var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var membershipsResponse = await client.From<SupabaseCircleMember>()
                    .Where(x => x.CircleId == circleId)
                    .Get();

                foreach (var m in membershipsResponse.Models)
                {
                    if (!string.IsNullOrWhiteSpace(m.UserId)) userIds.Add(m.UserId);
                }
            }
            catch { }

            try
            {
                var circleResp = await client.From<SupabaseSafetyCircle>()
                    .Where(x => x.Id == circleId)
                    .Get();
                var circleObj = circleResp.Models.FirstOrDefault();
                if (circleObj != null && !string.IsNullOrWhiteSpace(circleObj.CreatedBy))
                {
                    userIds.Add(circleObj.CreatedBy);
                }
            }
            catch { }

            if (!userIds.Any()) return new List<SupabaseUserLocation>();

            try
            {
                var locationsResponse = await client.From<SupabaseUserLocation>()
                    .Filter("user_id", Supabase.Postgrest.Constants.Operator.In, userIds.ToList())
                    .Get();

                return locationsResponse.Models ?? new List<SupabaseUserLocation>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCircleLocations error: {ex.Message}");
                return new List<SupabaseUserLocation>();
            }
        }

        // --- Chat Messaging with Permanent Local & Cloud Persistence ---
        private static readonly Dictionary<string, List<SupabaseCircleMessage>> _inMemoryMessages = new();

        private string GetLocalChatFilePath(string circleId)
        {
            return Path.Combine(FileSystem.AppDataDirectory, $"chat_cache_{circleId}.json");
        }

        private List<SupabaseCircleMessage> LoadLocalMessages(string circleId)
        {
            try
            {
                var path = GetLocalChatFilePath(circleId);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SupabaseCircleMessage>>(json);
                        if (list != null) return list;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadLocalMessages error: {ex.Message}");
            }
            return new List<SupabaseCircleMessage>();
        }

        private void SaveLocalMessages(string circleId, List<SupabaseCircleMessage> messages)
        {
            try
            {
                var path = GetLocalChatFilePath(circleId);
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(messages);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveLocalMessages error: {ex.Message}");
            }
        }

        public async Task<SupabaseCircleMessage?> SendMessageAsync(string circleId, string messageText, string? mediaUrl = null, string? mediaType = "Text")
        {
            string senderName = "Family Member";
            string avatarUrl = string.Empty;
            string userId = string.Empty;

            try
            {
                userId = GetCurrentUserId();

                var cachedFn = Preferences.Default.Get("UserFirstName", string.Empty);
                var cachedLn = Preferences.Default.Get("UserLastName", string.Empty);
                if (!string.IsNullOrWhiteSpace(cachedFn))
                {
                    senderName = $"{cachedFn} {cachedLn}".Trim();
                }
                if (string.IsNullOrWhiteSpace(senderName) || senderName == "Family Member")
                {
                    senderName = Preferences.Default.Get("UserName", Preferences.Default.Get("UserEmail", "Family Member"));
                }
                avatarUrl = Preferences.Default.Get("UserAvatarUrl", Preferences.Default.Get("UserProfilePicPath", string.Empty));
            }
            catch { }

            var msg = new SupabaseCircleMessage
            {
                Id = Guid.NewGuid().ToString(),
                CircleId = circleId,
                UserId = userId,
                SenderName = senderName,
                SenderAvatarUrl = avatarUrl,
                MessageText = messageText ?? string.Empty,
                MediaUrl = mediaUrl ?? string.Empty,
                MediaType = string.IsNullOrWhiteSpace(mediaUrl) ? "Text" : (mediaType ?? "Image"),
                CreatedAt = DateTime.UtcNow
            };

            // 1. Save to local disk cache permanently (persists across logouts)
            var localList = LoadLocalMessages(circleId);
            localList.Add(msg);
            SaveLocalMessages(circleId, localList);

            // 2. Save in memory
            lock (_inMemoryMessages)
            {
                _inMemoryMessages[circleId] = localList;
            }

            // 3. Save to Supabase Cloud (try safety_circle_messages first, then circle_messages fallback)
            try
            {
                var client = GetClient();
                try
                {
                    var insertResp = await client.From<SupabaseCircleMessage>().Insert(msg);
                    return insertResp.Models.FirstOrDefault() ?? msg;
                }
                catch
                {
                    var altMsg = new SupabaseCircleMessageAlt
                    {
                        Id = msg.Id,
                        CircleId = msg.CircleId,
                        UserId = msg.UserId,
                        SenderName = msg.SenderName,
                        SenderAvatarUrl = msg.SenderAvatarUrl,
                        MessageText = msg.MessageText,
                        MediaUrl = msg.MediaUrl,
                        MediaType = msg.MediaType,
                        CreatedAt = msg.CreatedAt
                    };
                    await client.From<SupabaseCircleMessageAlt>().Insert(altMsg);
                    return msg;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Supabase send message error (persisted locally): {ex.Message}");
                return msg;
            }
        }

        public async Task<List<SupabaseCircleMessage>> GetCircleMessagesAsync(string circleId)
        {
            // Load local persistent messages first
            var merged = LoadLocalMessages(circleId);

            // Try fetching from Supabase Cloud (safety_circle_messages or circle_messages) and merge
            try
            {
                var client = GetClient();
                List<SupabaseCircleMessage>? remoteList = null;

                try
                {
                    var resp = await client.From<SupabaseCircleMessage>()
                        .Where(m => m.CircleId == circleId)
                        .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending)
                        .Get();
                    remoteList = resp.Models;
                }
                catch
                {
                    try
                    {
                        var altResp = await client.From<SupabaseCircleMessageAlt>()
                            .Where(m => m.CircleId == circleId)
                            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending)
                            .Get();
                        if (altResp.Models != null)
                        {
                            remoteList = altResp.Models.Select(a => new SupabaseCircleMessage
                            {
                                Id = a.Id,
                                CircleId = a.CircleId,
                                UserId = a.UserId,
                                SenderName = a.SenderName,
                                SenderAvatarUrl = a.SenderAvatarUrl,
                                MessageText = a.MessageText,
                                MediaUrl = a.MediaUrl,
                                MediaType = a.MediaType,
                                CreatedAt = a.CreatedAt
                            }).ToList();
                        }
                    }
                    catch { }
                }

                if (remoteList != null && remoteList.Count > 0)
                {
                    foreach (var remoteMsg in remoteList)
                    {
                        if (!merged.Any(m => m.Id == remoteMsg.Id))
                        {
                            merged.Add(remoteMsg);
                        }
                    }
                    // Save back merged results to disk cache
                    SaveLocalMessages(circleId, merged);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCircleMessages remote sync error: {ex.Message}");
            }

            lock (_inMemoryMessages)
            {
                _inMemoryMessages[circleId] = merged;
            }

            return merged.OrderBy(m => m.CreatedAt).ToList();
        }

        private string GenerateInviteCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
