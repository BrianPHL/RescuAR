using System.Collections.ObjectModel;
using System.Collections.Specialized;
using RescuAR.App.Services.SafetyCircle;

internal static class ChatPresentationChecks
{
    private sealed record Message(string Id, string Text, ChatDelivery Delivery = ChatDelivery.Sent);
    private sealed class Row(Message message) { public Message Message { get; } = message; }
    public static void Run(Action<bool, string> check)
    {
        var first = new Message("one", "First"); var second = new Message("two", "Pending", ChatDelivery.Pending);
        var rows = new ObservableCollection<Row> { new(first), new(second) };
        int changes = 0, resets = 0;
        rows.CollectionChanged += (_, args) => { changes++; if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
        bool Apply(params Message[] messages) => ChatMessageReconciler.Apply(rows, messages, row => row.Message.Id, message => message.Id,
            (row, message) => row.Message == message, message => new Row(message));
        var original = rows.ToArray();
        check(!Apply(first with { }, second with { }) && changes == 0 && ReferenceEquals(rows[0], original[0]) && ReferenceEquals(rows[1], original[1]),
            "an unchanged poll leaves every chat row and the collection untouched");
        check(!Apply(first, second with { Delivery = ChatDelivery.Sent }) && changes == 1 && rows[1].Message.Delivery == ChatDelivery.Sent && ReferenceEquals(rows[0], original[0]),
            "Pending-to-Sent changes replace only the acknowledged row even when its ID is unchanged");
        check(Apply(first, second with { Delivery = ChatDelivery.Sent }, new("three", "New message")) && rows.Count == 3 && ReferenceEquals(rows[0], original[0]),
            "a genuinely new tail message may request follow-to-end without resetting history");
        var tail = rows[2];
        check(!Apply(new("older", "Older history"), first, second with { Delivery = ChatDelivery.Sent }, tail.Message) && ReferenceEquals(rows[3], tail),
            "older history insertion preserves existing rows and does not request end scrolling");
        Apply(first, tail.Message, second with { Delivery = ChatDelivery.Failed });
        check(rows.Select(row => row.Message.Id).SequenceEqual(new[] { "one", "three", "two" }) && rows[2].Message.Delivery == ChatDelivery.Failed,
            "row removal/reordering and Failed state updates retain identity without stale delivery labels");
        Apply(); check(rows.Count == 0 && resets == 0, "empty history removes rows without a full collection reset");
    }
}
