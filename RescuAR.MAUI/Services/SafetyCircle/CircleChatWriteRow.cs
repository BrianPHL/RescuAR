using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Services.SafetyCircle;

// Unlike the legacy read model, this insert explicitly includes the stable client message ID.
[Table("safety_circle_messages")]
public sealed class CircleChatWriteRow : BaseModel
{
    [PrimaryKey("id", true)] public string Id { get; set; } = "";
    [Column("circle_id")] public string CircleId { get; set; } = "";
    [Column("user_id")] public string UserId { get; set; } = "";
    [Column("sender_name")] public string SenderName { get; set; } = "";
    [Column("sender_avatar_url")] public string AvatarUrl { get; set; } = "";
    [Column("message_text")] public string Text { get; set; } = "";
    [Column("media_url")] public string MediaUrl { get; set; } = "";
    [Column("media_type")] public string MediaType { get; set; } = "Text";
    [Column("created_at")] public DateTime CreatedAt { get; set; }

    public static CircleChatWriteRow From(CircleChatMessage message) => new()
    {
        Id = message.Id, CircleId = message.CircleId, UserId = message.UserId, SenderName = message.SenderName,
        AvatarUrl = message.AvatarUrl, Text = message.Text, MediaUrl = message.MediaUrl, MediaType = message.MediaType,
        CreatedAt = message.CreatedAt.UtcDateTime
    };
}
