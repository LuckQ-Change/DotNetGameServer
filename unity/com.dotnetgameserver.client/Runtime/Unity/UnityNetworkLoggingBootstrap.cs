using System.IO;
using DotNetGameServer.UnityClient.Logging;
using UnityEngine;

namespace DotNetGameServer.UnityClient.Unity
{
    public static class UnityNetworkLoggingBootstrap
    {
        public static string LogDirectory
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/DotNetGameServer/NetworkLogs"));
#else
                return Path.Combine(Application.persistentDataPath, "DotNetGameServer", "NetworkLogs");
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var settings = DotNetGameServerSettings.LoadOrTransientDefault();
            NetworkLogHub.MinimumLevel = settings.MinimumLogLevel;
            NetworkLogHub.Configure(
                settings.ConsoleCapacity,
                new RollingNetworkFileSink(
                    LogDirectory,
                    (long)settings.MaxLogFileKilobytes * 1024,
                    settings.LogRetentionDays));
            NetworkLogHub.Information("Bootstrap", "Dedicated network logging initialized: " + LogDirectory);
        }
    }
}
