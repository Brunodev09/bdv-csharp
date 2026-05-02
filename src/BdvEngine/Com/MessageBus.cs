namespace BdvEngine;

public static class MessageBus
{
    private static readonly Dictionary<string, List<IMessageHandler>> _subs = new();
    private static readonly Queue<(Message Message, IMessageHandler Handler)> _queue = new();
    private const int QueueMessageTick = 10;

    public static void Subscribe(string code, IMessageHandler handler)
    {
        if (!_subs.TryGetValue(code, out var list))
        {
            list = new List<IMessageHandler>();
            _subs[code] = list;
        }
        if (list.Contains(handler)) return;
        list.Add(handler);
    }

    public static void Unsubscribe(string code, IMessageHandler handler)
    {
        if (_subs.TryGetValue(code, out var list)) list.Remove(handler);
    }

    public static void Emit(Message message)
    {
        if (!_subs.TryGetValue(message.Code, out var handlers)) return;

        foreach (var handler in handlers)
        {
            if (message.Priority == MessagePriority.Critical)
                handler.OnMessage(message);
            else
                _queue.Enqueue((message, handler));
        }
    }

    public static void Update(double time)
    {
        if (_queue.Count == 0) return;
        int limit = Math.Min(QueueMessageTick, _queue.Count);
        for (int i = 0; i < limit; i++)
        {
            var (msg, handler) = _queue.Dequeue();
            handler.OnMessage(msg);
        }
    }
}
