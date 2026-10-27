namespace RescuAR.App.Services.SafetyCircle;

public sealed class CircleChatService(CircleMembershipService memberships, ICircleChatStore store, ICircleChatRemote remote,
    ICircleChatUploader uploader, Func<DateTimeOffset> clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public CircleChatScope Capture(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Select a confirmed Circle.");
        return new(memberships.Capture(), id);
    }
    public void Require(CircleChatScope scope, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); memberships.Require(scope.Account);
        if (string.IsNullOrWhiteSpace(scope.CircleId)) throw new ArgumentException("Select a confirmed Circle.");
    }
    private async Task<bool> Member(CircleChatScope scope, CancellationToken token, bool onlineOnly)
    {
        Require(scope, token);
        var snapshot = await memberships.LoadAsync(scope.Account).WaitAsync(token);
        Require(scope, token);
        if (!snapshot.Circles.Any(circle => circle.Id == scope.CircleId)) throw new UnauthorizedAccessException("This Circle is no longer available to your account.");
        if (onlineOnly && snapshot.IsCached) throw new CircleMembershipOfflineException("Connect and confirm membership before delivery or retry.");
        return snapshot.IsCached;
    }
    private async Task<CircleChatSnapshot> WithState(CircleChatScope scope, CancellationToken token,
        Func<CircleChatState, bool, Task<CircleChatSnapshot>> action)
    {
        Require(scope, token); await gate.WaitAsync(token);
        try
        {
            bool cached = await Member(scope, token, false);
            var state = store.Read(scope) ?? new(scope.Account.Server, scope.Account.UserId, scope.CircleId, new());
            ValidateState(scope, state); Require(scope, token);
            return await action(state with { Messages = state.Messages.ToList() }, cached);
        }
        finally { gate.Release(); }
    }
    private void ValidateState(CircleChatScope scope, CircleChatState state)
    {
        if (state.Server != scope.Account.Server || state.UserId != scope.Account.UserId || state.CircleId != scope.CircleId || state.Messages is null)
            throw new IOException("The chat file does not belong to this account, server and Circle.");
        foreach (var item in state.Messages)
        {
            ValidateMessage(scope, item);
            if (item.Delivery != ChatDelivery.Sent && item.UserId != scope.Account.UserId) throw new IOException("A queued message has a different owner.");
            if (item.LocalMedia is { } media && !store.Owns(scope, media)) throw new IOException("A message attachment has a different owner.");
        }
        if (state.Attachment is { } attachment && !store.Owns(scope, attachment)) throw new IOException("The attachment is outside this Circle's local storage.");
        if (state.LocalPhoto is { } photo && !store.Owns(scope, photo)) throw new IOException("The local Circle photo has a different owner.");
    }
    private static void ValidateMessage(CircleChatScope scope, CircleChatMessage item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.CircleId != scope.CircleId || string.IsNullOrWhiteSpace(item.UserId)
            || item.MediaType is not ("Text" or "Image" or "Video") || item.CreatedAt <= DateTimeOffset.UnixEpoch)
            throw new InvalidDataException("The service returned an incomplete or mismatched chat message.");
        if (item.MediaUrl.Length > 0 && CirclePresencePolicy.SafeAvatar(item.MediaUrl).Length == 0)
            throw new InvalidDataException("A media message did not contain a secure confirmed URL.");
        if (item.Delivery == ChatDelivery.Sent && item.MediaType != "Text" && item.MediaUrl.Length == 0)
            throw new InvalidDataException("A media message was not confirmed with its uploaded attachment.");
    }
    private CircleChatSnapshot Save(CircleChatScope scope, CircleChatState state, bool cached, string notice, bool confirmed)
    {
        Require(scope);
        bool saved = true;
        try { store.Write(scope, state); }
        catch (Exception error) when (confirmed && (error is IOException or UnauthorizedAccessException))
        { saved = false; notice = "The cloud result is confirmed, but its local copy could not be saved. " + notice; }
        Require(scope);
        return new(state, cached, saved, notice);
    }
    public Task<CircleChatSnapshot> LoadAsync(CircleChatScope scope, CancellationToken token = default) => WithState(scope, token, async (state, cached) =>
    {
        if (cached) return new(state, true, true, "Cached / offline chat. Delivery labels reflect the last confirmation; pending messages require Retry.");
        List<CircleChatMessage> messages;
        try { messages = await remote.ReadAsync(scope, token); }
        catch (Exception error) when (Offline(error)) { Require(scope, token); return new(state, true, true, "Connection lost. This is the saved chat; pending messages have not been retried."); }
        Require(scope, token);
        foreach (var message in messages) ValidateMessage(scope, message);
        var merged = messages.DistinctBy(message => message.Id).Select(message => message with
        {
            Delivery = ChatDelivery.Sent, Error = "",
            LocalMedia = state.Messages.FirstOrDefault(old => old.Id == message.Id && old.UserId == message.UserId)?.LocalMedia
        }).ToList();
        merged.AddRange(state.Messages.Where(old => old.Delivery != ChatDelivery.Sent && !merged.Any(item => item.Id == old.Id)));
        merged = merged.OrderBy(message => message.CreatedAt).ToList();
        int? memberCount = state.MemberCount; DateTimeOffset? checkedAt = state.MemberCountCheckedAt;
        bool countConfirmed = false;
        try
        {
            if (await remote.ReadMemberCountAsync(scope, token) is > 0 and var count)
            { Require(scope, token); memberCount = count; checkedAt = clock(); countConfirmed = true; }
        }
        catch (OperationCanceledException) { throw; }
        catch { Require(scope, token); /* Keep a labelled saved count without preventing message delivery/history. */ }
        Require(scope, token);
        return Save(scope, state with { Messages = merged, MemberCount = memberCount, MemberCountCheckedAt = checkedAt }, false,
            "Messages checked online. Sent confirms server acceptance, not that another member has read the message.", true)
            with { MemberCountConfirmed = countConfirmed };
    });
    private static bool Offline(Exception error) => error is CircleMembershipOfflineException or TimeoutException || error is HttpRequestException { StatusCode: null };

    public Task<CircleChatSnapshot> LoadLocalAsync(CircleChatScope scope, CancellationToken token = default) => WithState(scope, token,
        (state, cached) => Task.FromResult(new CircleChatSnapshot(state, cached, true, "Circle photo and attachment drafts are local to this account, server and Circle.")));

    public Task<CircleChatSnapshot> SetAttachmentAsync(CircleChatScope scope, ChatFile? file, CancellationToken token = default) => WithState(scope, token, (state, cached) =>
    {
        if (file is not null && (!store.Owns(scope, file) || file.Kind is not ("Image" or "Video"))) throw new IOException("Select a photo or video saved for this account and Circle.");
        Require(scope, token);
        return Task.FromResult(Save(scope, state with { Attachment = file }, cached, file is null ? "Attachment cancelled. Nothing was uploaded." : "Attachment saved locally for preview. Send uploads it; Cancel removes the draft selection.", false));
    });
    public Task<CircleChatSnapshot> SetLocalPhotoAsync(CircleChatScope scope, ChatFile file, CancellationToken token = default) => WithState(scope, token, (state, cached) =>
    {
        if (!store.Owns(scope, file) || file.Kind != "LocalPhoto") throw new IOException("The local Circle photo has a different owner.");
        Require(scope, token);
        return Task.FromResult(Save(scope, state with { LocalPhoto = file }, cached, "Circle photo saved on this device for this account and Circle only. Other members do not receive this change.", false));
    });
    public Task<CircleChatSnapshot> SendAsync(CircleChatScope scope, string text, string? expectedAttachmentPath, string sender, string avatar,
        Action<CircleChatSnapshot>? queued = null, CancellationToken token = default) => WithState(scope, token, async (state, cached) =>
    {
        text = (text ?? "").Trim();
        if (state.Attachment?.Path != expectedAttachmentPath) throw new InvalidOperationException("The attachment changed. Review it before Send.");
        if (text.Length == 0 && state.Attachment is null) throw new ArgumentException("Enter a message or choose an attachment.");
        if (state.Attachment is { } file && !store.Owns(scope, file)) throw new IOException("The attachment has a different owner.");
        var item = new CircleChatMessage(Guid.NewGuid().ToString(), scope.CircleId, scope.Account.UserId, string.IsNullOrWhiteSpace(sender) ? "Member" : sender,
            CirclePresencePolicy.SafeAvatar(avatar), text, "", state.Attachment?.Kind ?? "Text", clock(), ChatDelivery.Pending, state.Attachment);
        var pending = state with { Messages = state.Messages.Append(item).ToList(), Attachment = null };
        var saved = Save(scope, pending, cached, "Pending — saved locally. No delivery is confirmed yet.", false);
        Require(scope, token); queued?.Invoke(saved);
        if (cached) return saved;
        return await Deliver(scope, pending, item, token);
    });
    public Task<CircleChatSnapshot> RetryAsync(CircleChatScope scope, string id, CancellationToken token = default) => WithState(scope, token, async (state, cached) =>
    {
        var item = state.Messages.FirstOrDefault(message => message.Id == id) ?? throw new ArgumentException("The queued message is unavailable.");
        if (item.UserId != scope.Account.UserId || item.Delivery == ChatDelivery.Sent) throw new InvalidOperationException("Only your unconfirmed messages can be retried.");
        await Member(scope, token, true);
        return await Deliver(scope, state, item, token);
    });
    private static void Confirm(CircleChatScope scope, CircleChatMessage original, CircleChatMessage actual)
    {
        ValidateMessage(scope, actual);
        if (actual.Id != original.Id || actual.UserId != scope.Account.UserId || actual.Text != original.Text || actual.MediaType != original.MediaType
            || (original.MediaUrl.Length > 0 && actual.MediaUrl != original.MediaUrl) || (actual.MediaType != "Text" && actual.MediaUrl.Length == 0))
            throw new InvalidDataException("The cloud did not confirm this message's identity and content.");
    }
    private async Task<CircleChatSnapshot> Deliver(CircleChatScope scope, CircleChatState state, CircleChatMessage item, CancellationToken token)
    {
        try
        {
            await Member(scope, token, true);
            var existing = await remote.FindAsync(scope, item.Id, token); Require(scope, token);
            if (existing is not null)
            {
                Confirm(scope, item, existing);
                item = existing with { Delivery = ChatDelivery.Sent, LocalMedia = item.LocalMedia, Error = "" };
            }
            else
            {
                if (item.MediaType != "Text" && item.MediaUrl.Length == 0)
                {
                    if (item.LocalMedia is not { } file || !store.Owns(scope, file)) throw new IOException("The saved attachment is unavailable. Nothing new was sent.");
                    string url = await uploader.UploadAsync(scope, file, token); Require(scope, token);
                    if (CirclePresencePolicy.SafeAvatar(url).Length == 0) throw new InvalidDataException("The upload did not confirm a secure media URL.");
                    item = item with { MediaUrl = url, Delivery = ChatDelivery.Pending, Error = "" };
                    state = Replace(state, item);
                    Save(scope, state, false, "Upload confirmed; message delivery is still pending.", false);
                }
                var actual = await remote.SendAsync(scope, item, token); Require(scope, token);
                Confirm(scope, item, actual);
                item = actual with { Delivery = ChatDelivery.Sent, LocalMedia = item.LocalMedia, Error = "" };
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Require(scope, token);
            item = item with { Delivery = ChatDelivery.Failed, Error = "Delivery was not confirmed. Connect, check membership and tap Retry." };
            return Save(scope, Replace(state, item), false, "Failed — the message/draft is retained for explicit Retry. Nothing is labelled Sent without confirmation.", false);
        }
        Require(scope, token);
        return Save(scope, Replace(state, item), false, "Sent — server acceptance confirmed. This is not a read or rescue-response receipt.", true);
    }
    private static CircleChatState Replace(CircleChatState state, CircleChatMessage message) => state with
    { Messages = state.Messages.Where(item => item.Id != message.Id).Append(message).OrderBy(item => item.CreatedAt).ToList() };
}
