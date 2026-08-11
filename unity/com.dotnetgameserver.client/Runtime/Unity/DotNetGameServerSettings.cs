using System;
using UnityEngine;

namespace DotNetGameServer.UnityClient.Unity
{
    [CreateAssetMenu(
        fileName = "DotNetGameServerSettings",
        menuName = "DotNet Game Server/Network Settings",
        order = 100)]
    public sealed class DotNetGameServerSettings : ScriptableObject
    {
        private const string ResourceName = "DotNetGameServerSettings";

        [Header("Service Discovery")]
        [Tooltip("HTTP base URLs. The client appends /.well-known/dotnet-game-server/services.")]
        public string[] BootstrapUrls = { "http://127.0.0.1:8080" };
        public string Responsibility = "gameplay";
        public string Region = string.Empty;
        public string RoutingKey = string.Empty;

        [Header("Protocol")]
        public string ClientVersion = "0.1.0";
        [Min(1024)] public int MaxFrameBytes = 1024 * 1024;
        [Min(1)] public int RequestTimeoutSeconds = 10;

        [Header("Dedicated Network Logs")]
        public Logging.NetworkLogLevel MinimumLogLevel = Logging.NetworkLogLevel.Information;
        [Min(100)] public int ConsoleCapacity = 2000;
        [Min(64)] public int MaxLogFileKilobytes = 5120;
        [Range(1, 90)] public int LogRetentionDays = 14;

        public static DotNetGameServerSettings Load()
        {
            return Resources.Load<DotNetGameServerSettings>(ResourceName);
        }

        public static DotNetGameServerSettings LoadOrTransientDefault()
        {
            var settings = Load();
            if (settings != null) return settings;
            settings = CreateInstance<DotNetGameServerSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            return settings;
        }
    }
}
