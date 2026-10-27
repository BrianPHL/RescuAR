namespace RescuAR.App.Services.SafetyCircle;

public readonly record struct CircleChatScope(CircleMembershipScope Account, string CircleId);
public enum ChatDelivery { Pending, Failed, Sent }
public sealed class CircleChatInputException(string message) : IOException(message);
public sealed record ChatFile(string Path, string Name, string Kind, long Bytes);
public sealed record CircleChatMessage(string Id, string CircleId, string UserId, string SenderName, string AvatarUrl,
    string Text, string MediaUrl, string MediaType, DateTimeOffset CreatedAt, ChatDelivery Delivery, ChatFile? LocalMedia = null, string Error = "");
public sealed record CircleChatState(string Server, string UserId, string CircleId, List<CircleChatMessage> Messages,
    ChatFile? Attachment = null, ChatFile? LocalPhoto = null, int? MemberCount = null, DateTimeOffset? MemberCountCheckedAt = null);
public sealed record CircleChatSnapshot(CircleChatState State, bool IsCached, bool CacheSaved, string Notice, bool MemberCountConfirmed = false);
public interface ICircleChatStore
{
    CircleChatState? Read(CircleChatScope scope);
    void Write(CircleChatScope scope, CircleChatState state);
    bool Owns(CircleChatScope scope, ChatFile file);
}
public interface ICircleChatRemote
{
    Task<int?> ReadMemberCountAsync(CircleChatScope scope, CancellationToken token) => Task.FromResult<int?>(null);
    Task<List<CircleChatMessage>> ReadAsync(CircleChatScope scope, CancellationToken token);
    Task<CircleChatMessage?> FindAsync(CircleChatScope scope, string id, CancellationToken token);
    Task<CircleChatMessage> SendAsync(CircleChatScope scope, CircleChatMessage message, CancellationToken token);
}
public interface ICircleChatUploader
{
    Task<string> UploadAsync(CircleChatScope scope, ChatFile file, CancellationToken token);
}
