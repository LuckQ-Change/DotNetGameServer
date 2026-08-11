using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetGameServer.UnityClient.Logging
{
    public enum NetworkLogLevel { Trace = 0, Debug = 1, Information = 2, Warning = 3, Error = 4 }

    public sealed class NetworkLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public NetworkLogLevel Level { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public Exception Exception { get; set; }
    }

    public interface INetworkLogSink : IDisposable
    {
        void Write(NetworkLogEntry entry);
    }

    public static class NetworkLogHub
    {
        private static readonly object Gate = new object();
        private static readonly Queue<NetworkLogEntry> Entries = new Queue<NetworkLogEntry>();
        private static int _capacity = 2000;
        private static INetworkLogSink _sink;

        public static event Action<NetworkLogEntry> Published;
        public static NetworkLogLevel MinimumLevel { get; set; } = NetworkLogLevel.Information;

        public static void Configure(int capacity, INetworkLogSink sink)
        {
            lock (Gate)
            {
                _capacity = Math.Max(100, capacity);
                var previous = _sink;
                _sink = sink;
                if (!ReferenceEquals(previous, sink) && previous != null) previous.Dispose();
                while (Entries.Count > _capacity) Entries.Dequeue();
            }
        }

        public static IReadOnlyList<NetworkLogEntry> Snapshot()
        {
            lock (Gate) return Entries.ToArray();
        }

        public static void Clear()
        {
            lock (Gate) Entries.Clear();
        }

        public static void Debug(string category, string message, string endpoint = "", string requestId = "")
        {
            Publish(NetworkLogLevel.Debug, category, message, endpoint, requestId, null);
        }

        public static void Information(string category, string message, string endpoint = "", string requestId = "")
        {
            Publish(NetworkLogLevel.Information, category, message, endpoint, requestId, null);
        }

        public static void Warning(string category, string message, string endpoint = "", string requestId = "", Exception exception = null)
        {
            Publish(NetworkLogLevel.Warning, category, message, endpoint, requestId, exception);
        }

        public static void Error(string category, string message, string endpoint = "", string requestId = "", Exception exception = null)
        {
            Publish(NetworkLogLevel.Error, category, message, endpoint, requestId, exception);
        }

        private static void Publish(
            NetworkLogLevel level,
            string category,
            string message,
            string endpoint,
            string requestId,
            Exception exception)
        {
            if (level < MinimumLevel) return;
            var entry = new NetworkLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Level = level,
                Category = category ?? string.Empty,
                Message = message ?? string.Empty,
                Endpoint = endpoint ?? string.Empty,
                RequestId = requestId ?? string.Empty,
                Exception = exception
            };
            INetworkLogSink sink;
            Action<NetworkLogEntry> subscribers;
            lock (Gate)
            {
                Entries.Enqueue(entry);
                while (Entries.Count > _capacity) Entries.Dequeue();
                sink = _sink;
                subscribers = Published;
            }

            try { if (sink != null) sink.Write(entry); }
            catch (Exception sinkException) { System.Diagnostics.Debug.WriteLine("[NetworkLog] sink failed: " + sinkException.Message); }
            if (subscribers == null) return;
            foreach (var subscriber in subscribers.GetInvocationList().Cast<Action<NetworkLogEntry>>())
            {
                try { subscriber(entry); }
                catch (Exception subscriberException) { System.Diagnostics.Debug.WriteLine("[NetworkLog] subscriber failed: " + subscriberException.Message); }
            }
        }
    }
}
