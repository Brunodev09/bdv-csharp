namespace BdvEngine;

public enum MessagePriority { Default, Critical }

public interface IMessageHandler
{
    void OnMessage(Message message);
}

public sealed class Message
{
    public string Code { get; }
    public object? Sender { get; }
    public object? Context { get; }
    public MessagePriority Priority { get; }

    public Message(string code, object? sender, object? context = null, MessagePriority priority = MessagePriority.Default)
    {
        Code = code;
        Sender = sender;
        Context = context;
        Priority = priority;
    }

    public static void Send(string code, object? sender, object? context = null)
        => MessageBus.Emit(new Message(code, sender, context, MessagePriority.Default));

    public static void SendCritical(string code, object? sender, object? context = null)
        => MessageBus.Emit(new Message(code, sender, context, MessagePriority.Critical));

    public static void Subscribe(string code, IMessageHandler handler) => MessageBus.Subscribe(code, handler);
    public static void Unsubscribe(string code, IMessageHandler handler) => MessageBus.Unsubscribe(code, handler);
}
