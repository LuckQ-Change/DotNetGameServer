using System;
using System.Threading;

namespace DotNetGameServer.UnityClient
{
    /// <summary>
    /// Controls where unsolicited client events are invoked. Request tasks keep their normal
    /// await semantics; this dispatcher is used for notifications and connection-closed events.
    /// </summary>
    public interface IClientEventDispatcher
    {
        void Post(Action callback);
    }

    public sealed class ImmediateClientEventDispatcher : IClientEventDispatcher
    {
        public static readonly ImmediateClientEventDispatcher Instance = new ImmediateClientEventDispatcher();

        private ImmediateClientEventDispatcher() { }

        public void Post(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            callback();
        }
    }

    public sealed class SynchronizationContextClientEventDispatcher : IClientEventDispatcher
    {
        private readonly SynchronizationContext _context;

        public SynchronizationContextClientEventDispatcher(SynchronizationContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void Post(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _context.Post(state => ((Action)state)(), callback);
        }
    }
}
