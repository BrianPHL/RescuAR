using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Models
{
    [Table("safety_circles")]
    public class SupabaseSafetyCircle : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("invite_code")]
        public string InviteCode { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("created_by")]
        public string CreatedBy { get; set; } = string.Empty;
    }

    [Table("safety_circle_members")]
    public class SupabaseCircleMember : BaseModel
    {
        [PrimaryKey("circle_id", true)]
        public string CircleId { get; set; } = string.Empty;

        [PrimaryKey("user_id", true)]
        public string UserId { get; set; } = string.Empty;

        [Column("joined_at")]
        public DateTime JoinedAt { get; set; }
    }

    [Table("user_locations")]
    public class SupabaseUserLocation : BaseModel
    {
        [PrimaryKey("user_id", true)]
        public string UserId { get; set; } = string.Empty;

        [Column("latitude")]
        public double Latitude { get; set; }

        [Column("longitude")]
        public double Longitude { get; set; }

        [Column("status_text")]
        public string StatusText { get; set; } = string.Empty;

        [Column("last_updated")]
        public DateTime LastUpdated { get; set; }
    }

    [Table("profiles")]
    public class SupabaseProfile : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [Column("last_name")]
        public string LastName { get; set; } = string.Empty;

        [Column("initials")]
        public string Initials { get; set; } = string.Empty;
    }

    [Table("safety_circle_messages")]
    public class SupabaseCircleMessage : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("circle_id")]
        public string CircleId { get; set; } = string.Empty;

        [Column("user_id")]
        public string UserId { get; set; } = string.Empty;

        [Column("sender_name")]
        public string SenderName { get; set; } = string.Empty;

        [Column("sender_avatar_url")]
        public string SenderAvatarUrl { get; set; } = string.Empty;

        [Column("message_text")]
        public string MessageText { get; set; } = string.Empty;

        [Column("media_url")]
        public string MediaUrl { get; set; } = string.Empty;

        [Column("media_type")]
        public string MediaType { get; set; } = "Text"; // Text, Image, Video

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("emergency_hotlines")]
    public class SupabaseEmergencyHotline : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("agency")]
        public string Agency { get; set; } = string.Empty;

        [Column("primary_number")]
        public string PrimaryNumber { get; set; } = string.Empty;

        [Column("category")]
        public string Category { get; set; } = string.Empty;

        [Column("availability")]
        public string Availability { get; set; } = "24/7";

        [Column("alternative_number")]
        public string AlternativeNumber { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("coverage")]
        public string Coverage { get; set; } = "Citywide";
    }
}
