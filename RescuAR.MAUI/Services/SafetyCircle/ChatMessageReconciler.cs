using System.Collections.ObjectModel;

namespace RescuAR.App.Services.SafetyCircle;

/// <summary>Updates message rows by identity without resetting an unchanged conversation.</summary>
public static class ChatMessageReconciler
{
    public static bool Apply<TRow, TMessage>(ObservableCollection<TRow> rows, IReadOnlyList<TMessage> messages,
        Func<TRow, string> rowId, Func<TMessage, string> messageId,
        Func<TRow, TMessage, bool> same, Func<TMessage, TRow> create)
    {
        var previous = rows.Select(rowId).ToHashSet(StringComparer.Ordinal);
        for (int index = 0; index < messages.Count; index++)
        {
            var message = messages[index]; string id = messageId(message);
            if (index >= rows.Count || rowId(rows[index]) != id)
            {
                int found = -1;
                for (int candidate = index + 1; candidate < rows.Count; candidate++)
                    if (rowId(rows[candidate]) == id) { found = candidate; break; }
                if (found >= 0) rows.Move(found, index);
                else rows.Insert(index, create(message));
            }
            if (!same(rows[index], message)) rows[index] = create(message);
        }
        while (rows.Count > messages.Count) rows.RemoveAt(rows.Count - 1);
        return messages.Count > 0 && !previous.Contains(messageId(messages[^1]));
    }
}
