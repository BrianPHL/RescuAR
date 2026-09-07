using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RescuAR.App.Models;
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
            var client = GetClient();
            return client.Auth.CurrentUser?.Id ?? throw new UnauthorizedAccessException("User is not logged in.");
        }

        // --- Circle Management ---

        public async Task<SupabaseSafetyCircle> CreateCircleAsync(string name)
        {
            var client = GetClient();
            var userId = GetCurrentUserId();

            // Generate a random 6-character invite code
            string inviteCode = GenerateInviteCode();

            var newCircle = new SupabaseSafetyCircle
            {
                Name = name,
                InviteCode = inviteCode,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            var response = await client.From<SupabaseSafetyCircle>().Insert(newCircle);
            var circle = response.Models.FirstOrDefault();

            if (circle != null)
            {
                // Auto-join the creator
                await JoinCircleInternalAsync(circle.Id, userId);
            }

            return circle ?? throw new Exception("Failed to create circle.");
        }

        public async Task<SupabaseSafetyCircle> JoinCircleWithCodeAsync(string inviteCode)
        {
            var client = GetClient();
            var userId = GetCurrentUserId();

            string cleanCode = (inviteCode ?? string.Empty).Trim().ToUpper();

            // Find circle by code
            var circleResponse = await client.From<SupabaseSafetyCircle>()
                .Where(x => x.InviteCode == cleanCode)
                .Get();

            var circle = circleResponse.Models.FirstOrDefault();
            if (circle == null)
            {
                throw new Exception("Invalid invite code. Please check the code and try again.");
            }

            await JoinCircleInternalAsync(circle.Id, userId);
            return circle;
        }

        private async Task JoinCircleInternalAsync(string circleId, string userId)
        {
            var client = GetClient();

            // Check if already a member
            var existing = await client.From<SupabaseCircleMember>()
                .Where(x => x.CircleId == circleId)
                .Where(x => x.UserId == userId)
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

        public async Task<List<SupabaseSafetyCircle>> GetMyCirclesAsync()
        {
            var client = GetClient();
            var userId = GetCurrentUserId();

            // Fetch circle memberships for current user
            var membershipsResponse = await client.From<SupabaseCircleMember>()
                .Select("circle_id")
                .Where(x => x.UserId == userId)
                .Get();

            var circleIds = membershipsResponse.Models.Select(m => m.CircleId).ToList();
            if (!circleIds.Any())
            {
                return new List<SupabaseSafetyCircle>();
            }

            // Fetch the actual circle details
            var circlesResponse = await client.From<SupabaseSafetyCircle>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.In, circleIds)
                .Get();

            return circlesResponse.Models;
        }

        public async Task<List<User>> GetCircleMembersAsync(string circleId)
        {
            var client = GetClient();

            // Fetch member user IDs
            var membershipsResponse = await client.From<SupabaseCircleMember>()
                .Select("user_id")
                .Where(x => x.CircleId == circleId)
                .Get();

            var userIds = membershipsResponse.Models.Select(m => m.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (!userIds.Any()) return new List<User>();

            var usersList = new List<User>();

            try
            {
                // Fetch from users table
                var usersResponse = await client.From<User>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.In, userIds)
                    .Get();

                if (usersResponse.Models != null)
                {
                    foreach (var u in usersResponse.Models)
                    {
                        if (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName))
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
            var missingIds = userIds.Where(uid => !usersList.Any(u => string.Equals(u.Id, uid, StringComparison.OrdinalIgnoreCase))).ToList();
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

            // Ensure every member has a real name representation
            foreach (var uid in userIds)
            {
                var existingUser = usersList.FirstOrDefault(u => string.Equals(u.Id, uid, StringComparison.OrdinalIgnoreCase));
                if (existingUser == null)
                {
                    string fn = string.Empty;
                    string ln = string.Empty;

                    // If this is the currently authenticated user, pull directly from active Auth session metadata
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

                        if (string.IsNullOrWhiteSpace(fn) && !string.IsNullOrWhiteSpace(client.Auth.CurrentUser.Email))
                        {
                            var emailPrefix = client.Auth.CurrentUser.Email.Split('@')[0];
                            fn = char.ToUpper(emailPrefix[0]) + (emailPrefix.Length > 1 ? emailPrefix.Substring(1) : "");
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
            var client = GetClient();

            // Get user ids in circle
            var membershipsResponse = await client.From<SupabaseCircleMember>()
                .Select("user_id")
                .Where(x => x.CircleId == circleId)
                .Get();

            var userIds = membershipsResponse.Models.Select(m => m.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (!userIds.Any()) return new List<SupabaseUserLocation>();

            // Fetch latest locations
            var locationsResponse = await client.From<SupabaseUserLocation>()
                .Filter("user_id", Supabase.Postgrest.Constants.Operator.In, userIds)
                .Get();

            return locationsResponse.Models ?? new List<SupabaseUserLocation>();
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
                var client = GetClient();
                userId = GetCurrentUserId();

                try
                {
                    var userResp = await client.From<User>().Where(u => u.Id == userId).Get();
                    var user = userResp.Models.FirstOrDefault();
                    if (user != null)
                    {
                        senderName = $"{user.FirstName} {user.LastName}".Trim();
                        if (string.IsNullOrWhiteSpace(senderName)) senderName = user.Username;
                        avatarUrl = user.AvatarUrl;
                    }
                }
                catch { }
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

            // 3. Save to Supabase Cloud
            try
            {
                var client = GetClient();
                var insertResp = await client.From<SupabaseCircleMessage>().Insert(msg);
                return insertResp.Models.FirstOrDefault() ?? msg;
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

            // Try fetching from Supabase Cloud and merge
            try
            {
                var client = GetClient();
                var resp = await client.From<SupabaseCircleMessage>()
                    .Where(m => m.CircleId == circleId)
                    .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending)
                    .Get();

                if (resp.Models != null && resp.Models.Count > 0)
                {
                    foreach (var remoteMsg in resp.Models)
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
