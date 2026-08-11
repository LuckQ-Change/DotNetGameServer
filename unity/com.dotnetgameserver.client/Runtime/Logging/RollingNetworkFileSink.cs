using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DotNetGameServer.UnityClient.Logging
{
    public sealed class RollingNetworkFileSink : INetworkLogSink
    {
        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly long _maxFileBytes;
        private readonly int _retentionDays;
        private string _date = string.Empty;
        private int _sequence;
        private StreamWriter _writer;

        public RollingNetworkFileSink(string directory, long maxFileBytes = 5 * 1024 * 1024, int retentionDays = 14)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Log directory is required.", nameof(directory));
            _directory = directory;
            _maxFileBytes = Math.Max(64 * 1024, maxFileBytes);
            _retentionDays = Math.Max(1, retentionDays);
            Directory.CreateDirectory(_directory);
            DeleteExpiredFiles();
        }

        public void Write(NetworkLogEntry entry)
        {
            if (entry == null) return;
            lock (_gate)
            {
                EnsureWriter(entry.TimestampUtc);
                _writer.WriteLine(ToJson(entry));
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_writer != null) _writer.Dispose();
                _writer = null;
            }
        }

        private void EnsureWriter(DateTime timestamp)
        {
            var date = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (_writer != null && date == _date && _writer.BaseStream.Length < _maxFileBytes) return;
            if (_writer != null) _writer.Dispose();
            if (date != _date) { _date = date; _sequence = 0; }
            string path;
            do
            {
                path = Path.Combine(_directory, "network-" + date + "-" + _sequence.ToString("000") + ".jsonl");
                _sequence++;
            } while (File.Exists(path) && new FileInfo(path).Length >= _maxFileBytes);
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        }

        private void DeleteExpiredFiles()
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            foreach (var path in Directory.GetFiles(_directory, "network-*.jsonl"))
            {
                try { if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path); }
                catch (Exception exception) { System.Diagnostics.Debug.WriteLine("[NetworkLog] retention cleanup failed: " + exception.Message); }
            }
        }

        private static string ToJson(NetworkLogEntry entry)
        {
            return "{\"timestampUtc\":\"" + entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)
                + "\",\"level\":\"" + entry.Level
                + "\",\"category\":\"" + Escape(entry.Category)
                + "\",\"message\":\"" + Escape(entry.Message)
                + "\",\"endpoint\":\"" + Escape(entry.Endpoint)
                + "\",\"requestId\":\"" + Escape(entry.RequestId)
                + "\",\"exception\":\"" + Escape(entry.Exception == null ? string.Empty : entry.Exception.ToString()) + "\"}";
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
