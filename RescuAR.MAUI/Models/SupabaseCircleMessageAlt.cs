using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Models;

[Table("circle_messages")]
public class SupabaseCircleMessageAlt : BaseModel
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
    public string MediaType { get; set; } = "Text";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
