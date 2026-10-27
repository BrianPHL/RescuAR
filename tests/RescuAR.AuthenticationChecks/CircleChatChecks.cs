using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RescuAR.App.Services.SafetyCircle;

internal static class CircleChatChecks
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly ChatFile Image = new("owned/image.jpg", "image.jpg", "Image", 4);
    private static readonly ChatFile Video = new("owned/clip.mp4", "clip.mp4", "Video", 8);

    public static async Task RunAsync(Action<bool, string> check)
    {
        var counts = new Rig(); var countScope = counts.Chat.Capture("circle-a");
        var countSnapshot = await counts.Chat.LoadAsync(countScope);
        check(countSnapshot.State.MemberCount == 4 && countSnapshot.MemberCountConfirmed && countSnapshot.State.MemberCountCheckedAt == Now,
            "online chat restores the confirmed numeric membership count with its check time");
        counts.Context.Online = false; int countReads = counts.Remote.CountReads;
        countSnapshot = await counts.Chat.LoadAsync(countScope);
        check(countSnapshot.State.MemberCount == 4 && !countSnapshot.MemberCountConfirmed && countSnapshot.IsCached && counts.Remote.CountReads == countReads,
            "offline chat retains a labelled saved count without claiming live membership or making a count request");
        counts.Context.Online = true; counts.Remote.FailCount = true;
        countSnapshot = await counts.Chat.LoadAsync(countScope);
        check(countSnapshot.State.MemberCount == 4 && !countSnapshot.MemberCountConfirmed && !countSnapshot.IsCached,
            "count failure retains the saved count while message history remains independently available");
        var unknownCount = new Rig(); unknownCount.Remote.Count = null;
        check((await unknownCount.Chat.LoadAsync(unknownCount.Chat.Capture("circle-a"))).State.MemberCount is null,
            "an unavailable count is not replaced with one, zero or a fabricated Online label");

        var rig = new Rig(); var scope = rig.Chat.Capture("circle-a");
        CircleChatSnapshot? queued = null;
        var result = await rig.Chat.SendAsync(scope, " Hello ", null, "Resident", "", snapshot => queued = snapshot);
        var sent = result.State.Messages.Single();
        check(queued?.State.Messages.Single().Delivery == ChatDelivery.Pending && queued.CacheSaved, "chat queues a durable Pending message before attempting delivery");
        check(sent.Delivery == ChatDelivery.Sent && sent.Text == "Hello" && queued?.State.Messages.Single().Id == sent.Id, "one message ID connects Pending display and confirmed Sent result");
        check(rig.Remote.Sends == 1 && rig.Upload.Count == 0 && rig.Remote.Rows.Single().Id == sent.Id, "a text send submits exactly that stable ID without media upload");
        result = await rig.Chat.LoadAsync(scope);
        check(result.State.Messages.Count == 1 && result.State.Messages.Single().Id == sent.Id, "refresh cannot duplicate a message that was already shown optimistically");
        await Reject<InvalidOperationException>(() => rig.Chat.RetryAsync(scope, sent.Id), check, "confirmed messages cannot be manually resent through Retry");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); rig.Remote.FailSend = true;
        result = await rig.Chat.SendAsync(scope, "Need help", null, "Resident", ""); var failed = result.State.Messages.Single();
        check(failed.Delivery == ChatDelivery.Failed && failed.Error.Contains("not confirmed") && rig.Store.State?.Messages.Single().Id == failed.Id, "a failed cloud send is visibly Failed and remains available locally");
        rig.Remote.FailSend = false;
        result = await rig.Chat.RetryAsync(scope, failed.Id);
        check(result.State.Messages.Count == 1 && result.State.Messages.Single().Id == failed.Id && result.State.Messages.Single().Delivery == ChatDelivery.Sent, "explicit retry retains the original ID instead of creating another message");
        check(rig.Remote.Rows.Count == 1, "a successful retry produces one cloud message");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); rig.Remote.LoseResponse = true;
        result = await rig.Chat.SendAsync(scope, "Arrived", null, "Resident", ""); failed = result.State.Messages.Single();
        check(failed.Delivery == ChatDelivery.Failed && rig.Remote.Rows.Count == 1, "a lost response does not falsely label an unconfirmed result Sent");
        rig.Remote.LoseResponse = false;
        result = await rig.Chat.RetryAsync(scope, failed.Id);
        check(result.State.Messages.Single().Delivery == ChatDelivery.Sent && rig.Remote.Sends == 1, "retry reconciles an already accepted ID before inserting again");
        rig.Remote.Rows.Clear(); result = await rig.Chat.LoadAsync(scope);
        check(result.State.Messages.Count == 0, "an online empty history does not silently revive old confirmed cache entries");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); await rig.Chat.LoadAsync(scope); rig.Context.Online = false;
        result = await rig.Chat.SendAsync(scope, "Offline draft", null, "Resident", ""); var pending = result.State.Messages.Single();
        check(result.IsCached && pending.Delivery == ChatDelivery.Pending && rig.Remote.Sends == 0, "offline Send saves an explicit Pending message without pretending delivery");
        result = await rig.Chat.LoadAsync(scope);
        check(result.IsCached && result.State.Messages.Single().Delivery == ChatDelivery.Pending && rig.Remote.Sends == 0, "offline reading never automatically retries pending messages");
        await Reject<CircleMembershipOfflineException>(() => rig.Chat.RetryAsync(scope, pending.Id), check, "Retry requires an online membership confirmation");
        rig.Context.Online = true; await rig.Chat.LoadAsync(scope);
        check(rig.Remote.Sends == 0, "reconnecting and loading history does not silently send a pending message");
        result = await rig.Chat.RetryAsync(scope, pending.Id);
        check(result.State.Messages.Single().Delivery == ChatDelivery.Sent, "the user can explicitly retry a locally queued message after reconnecting");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a");
        result = await rig.Chat.SetAttachmentAsync(scope, Image);
        check(result.State.Attachment == Image && rig.Upload.Count == 0 && rig.Remote.Sends == 0, "selecting an attachment saves a scoped preview without uploading or sending");
        result = await rig.Chat.SetAttachmentAsync(scope, null);
        check(result.State.Attachment is null && rig.Upload.Count == 0 && result.State.Messages.Count == 0, "Cancel clears the selected attachment without any provider request");
        await rig.Chat.SetAttachmentAsync(scope, Image);
        await Reject<InvalidOperationException>(() => rig.Chat.SendAsync(scope, "Caption", "old-selection", "Resident", ""), check, "Send rejects an attachment replaced after its preview was captured");
        check(rig.Upload.Count == 0, "a stale preview cannot upload a different file");
        rig.Remote.FailSend = true;
        result = await rig.Chat.SendAsync(scope, "Photo", Image.Path, "Resident", ""); failed = result.State.Messages.Single();
        check(rig.Upload.Count == 1 && failed.Delivery == ChatDelivery.Failed && failed.LocalMedia == Image && failed.MediaUrl.StartsWith("https://"), "failed media submission retains the local file and confirmed upload URL for Retry");
        rig.Remote.FailSend = false; result = await rig.Chat.RetryAsync(scope, failed.Id);
        check(rig.Upload.Count == 1 && result.State.Messages.Single().Delivery == ChatDelivery.Sent && rig.Remote.Rows.Count == 1, "message retry reuses a confirmed media upload instead of uploading the attachment again");
        await rig.Chat.SetAttachmentAsync(scope, Video);
        result = await rig.Chat.SendAsync(scope, "Video", Video.Path, "Resident", "");
        check(result.State.Messages.Last().MediaType == "Video" && rig.Upload.Last?.Kind == "Video", "video drafts remain video through upload and confirmed submission");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); await rig.Chat.SetAttachmentAsync(scope, Image); rig.Upload.Result = "";
        result = await rig.Chat.SendAsync(scope, "Photo", Image.Path, "Resident", "");
        check(result.State.Messages.Single().Delivery == ChatDelivery.Failed && rig.Remote.Sends == 0, "an unconfirmed attachment upload cannot produce a Sent media message");
        await Reject<IOException>(() => rig.Chat.SetAttachmentAsync(scope, Image with { Path = "other-account/image.jpg" }), check, "attachment previews cannot read another account's local file");
        var photo = Image with { Path = "owned/local-photo.jpg", Kind = "LocalPhoto" };
        result = await rig.Chat.SetLocalPhotoAsync(scope, photo);
        check(result.State.LocalPhoto == photo && result.Notice.Contains("Other members do not receive") && rig.Upload.Count == 1, "group-photo changes remain explicitly local and cause no photo upload");
        await Reject<IOException>(() => rig.Chat.SetAttachmentAsync(scope, photo), check, "a local group photo is not accidentally queued as an attachment");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); rig.Store.FailWrites = true;
        await Reject<IOException>(() => rig.Chat.SendAsync(scope, "Unsaved", null, "Resident", ""), check, "a failed pending save prevents any cloud send");
        check(rig.Remote.Sends == 0 && rig.Store.State is null, "an unsaved message is not discarded into an untracked background send");
        rig.Store.FailWrites = false; rig.Store.FailSentWrites = true;
        result = await rig.Chat.SendAsync(scope, "Confirmed", null, "Resident", ""); sent = result.State.Messages.Single();
        check(sent.Delivery == ChatDelivery.Sent && !result.CacheSaved && result.Notice.Contains("local copy could not be saved"), "cloud acceptance followed by a local failure is reported separately");
        rig.Store.FailSentWrites = false; result = await rig.Chat.RetryAsync(scope, sent.Id);
        check(result.State.Messages.Single().Delivery == ChatDelivery.Sent && rig.Remote.Sends == 1, "a retry after cache failure reconciles the confirmed cloud ID without duplicating it");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); rig.Remote.WrongResponse = true;
        result = await rig.Chat.SendAsync(scope, "Original", null, "Resident", "");
        check(result.State.Messages.Single().Delivery == ChatDelivery.Failed && result.State.Messages.Single().Text == "Original", "a mismatched cloud response cannot replace a user's draft or claim Sent");
        rig.Remote.WrongResponse = false;
        rig.Remote.Rows.Add(result.State.Messages.Single() with { Id = "foreign", CircleId = "another-circle", Delivery = ChatDelivery.Sent });
        await Reject<InvalidDataException>(() => rig.Chat.LoadAsync(scope), check, "a server response for another Circle cannot enter the active history");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a"); await rig.Chat.LoadAsync(scope);
        rig.Membership.Circles.Clear(); int readCount = rig.Store.Reads;
        await Reject<UnauthorizedAccessException>(() => rig.Chat.LoadAsync(scope), check, "revoked membership blocks private chat even when a scoped cache exists");
        check(rig.Store.Reads == readCount, "membership is checked before loading the saved private chat file");
        rig.Membership.Circles.Add(new("circle-a", "Household", "ABCD23", "resident-a", Now.UtcDateTime));
        rig.Context.Current = rig.Context.Current with { UserId = "resident-b", Revision = 2 };
        await Reject<OperationCanceledException>(() => rig.Chat.LoadAsync(scope), check, "an old chat scope cannot load into a new signed-in account");
        rig = new Rig(); scope = rig.Chat.Capture("circle-a");
        rig.Store.State = new(scope.Account.Server, scope.Account.UserId, "other-circle", new());
        await Reject<IOException>(() => rig.Chat.LoadAsync(scope), check, "chat files validate their Circle owner envelope rather than trusting a filename");

        rig = new Rig(); scope = rig.Chat.Capture("circle-a");
        using var cancel = new CancellationTokenSource();
        await Reject<OperationCanceledException>(() => rig.Chat.SendAsync(scope, "Paused", null, "Resident", "", _ => cancel.Cancel(), cancel.Token), check, "page cancellation after the pending save prevents network dispatch");
        check(rig.Remote.Sends == 0 && rig.Store.State?.Messages.Single().Delivery == ChatDelivery.Pending, "an interrupted send retains its original pending message for explicit Retry");
        rig = new Rig(); scope = rig.Chat.Capture("circle-a");
        var blocked = new TaskCompletionSource<CircleChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Remote.Send = (_, message, _) => blocked.Task;
        var sending = rig.Chat.SendAsync(scope, "Late", null, "Resident", "");
        var original = rig.Store.State!.Messages.Single();
        rig.Context.Current = rig.Context.Current with { UserId = "resident-b", Revision = 2 };
        blocked.SetResult(original with { Delivery = ChatDelivery.Sent });
        await Reject<OperationCanceledException>(() => sending, check, "late send confirmations cannot publish after an account switch");
        check(rig.Store.State?.Messages.Single().Delivery == ChatDelivery.Pending, "an account switch cannot write another account's late Sent state");

        var resolver = new Supabase.Postgrest.PostgrestContractResolver(); resolver.SetState(true, false, false);
        var json = JObject.Parse(JsonConvert.SerializeObject(CircleChatWriteRow.From(original), new JsonSerializerSettings { ContractResolver = resolver }));
        check((string?)json["id"] == original.Id && (string?)json["user_id"] == original.UserId && (string?)json["circle_id"] == original.CircleId,
            "the actual Postgrest insert serializer includes the stable client message ID and ownership fields");
        check(json["LocalMedia"] is null && json["Delivery"] is null && json["Error"] is null, "local paths and delivery bookkeeping are never serialized as server columns");
        await Files(check);
    }

    private static async Task Files(Action<bool, string> check)
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string root = Path.GetFullPath(Path.Combine(repository, "artifacts/chat-unit-fixtures", Guid.NewGuid().ToString("N")));
        if (!Directory.Exists(Path.Combine(repository, "RescuAR.MAUI")) || !root.StartsWith(Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Chat fixtures must stay inside ignored worktree artifacts.");
        var files = new FileCircleChatStore(Path.Combine(root, "new-chat"));
        var scope = new CircleChatScope(new("https://example.test:54321/project-a", "resident-a", 1), "circle-a");
        using var input = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var media = await files.ImportAsync(scope, input, "photo.jpg", "Image", default);
        check(files.Owns(scope, media) && File.ReadAllBytes(media.Path).Length == 4, "picked media is copied into its exact account/server/Circle directory");
        check(!files.Owns(scope with { CircleId = "circle-b" }, media) && !files.Owns(scope with { Account = scope.Account with { UserId = "resident-b" } }, media), "a media path cannot be reused by another account or Circle");
        check(!files.Owns(scope with { Account = scope.Account with { Server = "https://example.test:54322/project-a" } }, media), "full server ports isolate chat media ownership");
        var state = new CircleChatState(scope.Account.Server, scope.Account.UserId, scope.CircleId, new(), media);
        files.Write(scope, state);
        check(new FileCircleChatStore(Path.Combine(root, "new-chat")).Read(scope)?.Attachment == media, "a preview draft survives a new store instance without uploading");
        string legacy = Path.Combine(root, "chat_cache_circle-a.json"); File.WriteAllText(legacy, "legacy-private-history");
        files.Write(scope, state with { Attachment = null });
        check(File.ReadAllText(legacy) == "legacy-private-history", "new chat storage neither imports nor deletes legacy unscoped history");
        string statePath = Path.Combine(files.DirectoryFor(scope), "state.json"); File.WriteAllText(statePath, "broken json");
        bool failed = false; try { files.Read(scope); } catch (IOException) { failed = true; }
        check(failed && File.ReadAllText(statePath) == "broken json", "corrupt scoped chat files are reported and preserved");
        using var empty = new MemoryStream();
        await Reject<IOException>(() => files.ImportAsync(scope, empty, "empty.jpg", "Image", default), check, "empty attachments cannot become sendable preview drafts");
        check(!Directory.EnumerateFiles(Path.Combine(files.DirectoryFor(scope), "media"), "*.partial").Any(), "failed media imports remove only their own incomplete copy");
    }
    private static async Task Reject<T>(Func<Task> action, Action<bool, string> check, string name) where T : Exception
    { bool rejected = false; try { await action(); } catch (T) { rejected = true; } check(rejected, name); }
    private sealed class Rig
    {
        public Context Context { get; } = new(); public Membership Membership { get; } = new();
        public Store Store { get; } = new(); public Remote Remote { get; } = new(); public Upload Upload { get; } = new();
        public CircleChatService Chat { get; }
        public Rig() => Chat = new(new CircleMembershipService(Context, Membership, new MembershipStore()), Store, Remote, Upload, () => Now);
    }
    private sealed class Context : ICircleMembershipContext
    {
        public CircleMembershipScope Current { get; set; } = new("https://server.example", "resident-a", 1); public bool Online = true;
        public bool IsOnline => Online; public CircleMembershipScope Capture() => Current;
        public void Require(CircleMembershipScope scope) { if (scope != Current) throw new OperationCanceledException(); }
    }
    private sealed class MembershipStore : ICircleMembershipStore
    { private CircleMembershipState? state; public CircleMembershipState? Read(CircleMembershipScope scope) => state; public void Write(CircleMembershipScope scope, CircleMembershipState value) => state = value; }
    private sealed class Membership : ICircleMembershipRemote
    {
        public List<CircleMembership> Circles { get; } = new() { new("circle-a", "Household", "ABCD23", "resident-a", Now.UtcDateTime) };
        public Task<List<CircleMembership>> GetAsync(CircleMembershipScope scope) => Task.FromResult(Circles.ToList());
        public Task<CircleMembership> CreateAsync(CircleMembershipScope scope, string name) => throw new NotSupportedException();
        public Task<CircleMembership> JoinAsync(CircleMembershipScope scope, string code) => throw new NotSupportedException();
        public Task<CircleMembership> RenameAsync(CircleMembershipScope scope, string id, string name) => throw new NotSupportedException();
        public Task LeaveAsync(CircleMembershipScope scope, string id) => throw new NotSupportedException(); public Task DeleteAsync(CircleMembershipScope scope, string id) => throw new NotSupportedException();
    }
    private sealed class Store : ICircleChatStore
    {
        public CircleChatState? State; public bool FailWrites, FailSentWrites; public int Reads;
        public CircleChatState? Read(CircleChatScope scope) { Reads++; return State; }
        public bool Owns(CircleChatScope scope, ChatFile file) => file.Path.StartsWith("owned/");
        public void Write(CircleChatScope scope, CircleChatState state)
        { if (FailWrites || FailSentWrites && state.Messages.Any(item => item.Delivery == ChatDelivery.Sent)) throw new IOException("storage failed"); State = state with { Messages = state.Messages.ToList() }; }
    }
    private sealed class Remote : ICircleChatRemote
    {
        public int? Count = 4; public int CountReads; public bool FailCount;
        public Task<int?> ReadMemberCountAsync(CircleChatScope scope, CancellationToken token)
        { CountReads++; if (FailCount) throw new IOException("count unavailable"); return Task.FromResult(Count); }
        public List<CircleChatMessage> Rows { get; } = new(); public int Sends; public bool FailSend, LoseResponse, WrongResponse;
        public Func<CircleChatScope, CircleChatMessage, CancellationToken, Task<CircleChatMessage>>? Send;
        public Task<List<CircleChatMessage>> ReadAsync(CircleChatScope scope, CancellationToken token) => Task.FromResult(Rows.ToList());
        public Task<CircleChatMessage?> FindAsync(CircleChatScope scope, string id, CancellationToken token) => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));
        public Task<CircleChatMessage> SendAsync(CircleChatScope scope, CircleChatMessage message, CancellationToken token)
        {
            Sends++; if (Send != null) return Send(scope, message, token);
            if (FailSend) throw new IOException("send failed");
            var row = message with { Delivery = ChatDelivery.Sent, Text = WrongResponse ? "wrong text" : message.Text }; Rows.Add(row);
            if (LoseResponse) throw new HttpRequestException("response lost"); return Task.FromResult(row);
        }
    }
    private sealed class Upload : ICircleChatUploader
    {
        public int Count; public ChatFile? Last; public string Result = "https://example.test/media";
        public Task<string> UploadAsync(CircleChatScope scope, ChatFile file, CancellationToken token) { Count++; Last = file; return Task.FromResult(Result); }
    }
}
