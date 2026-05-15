namespace Core.MessagePipeline;

public sealed class MessagePipeline
{
    private readonly List<Func<MessageDelegate, MessageDelegate>> _components = new();

    public MessagePipeline Use(Func<MessageDelegate, MessageDelegate> middleware)
    {
        _components.Add(middleware);
        return this;
    }

    public MessageDelegate Build()
    {
        MessageDelegate app = ctx => Task.CompletedTask;
        for (int i = _components.Count - 1; i >= 0; i--)
            app = _components[i](app);
        return app;
    }
}
