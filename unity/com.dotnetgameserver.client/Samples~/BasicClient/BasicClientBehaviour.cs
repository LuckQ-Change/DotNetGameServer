using System;
using System.Threading;
using DotNetGameServer.UnityClient.Unity;
using UnityEngine;

namespace DotNetGameServer.UnityClient.Samples
{
    public sealed class BasicClientBehaviour : MonoBehaviour
    {
        private GameNetworkClient _client;
        private CancellationTokenSource _lifetime;

        private async void Start()
        {
            _lifetime = new CancellationTokenSource();
            _client = UnityGameNetworkClientFactory.CreateDefault();
            try
            {
                var endpoint = await _client.ConnectAsync(
                    UnityGameNetworkClientFactory.CreateDefaultQuery(SystemInfo.deviceUniqueIdentifier),
                    _lifetime.Token);
                Debug.Log("Connected through discovery: " + endpoint);

                // 先通过 Proto Generator 生成 demo.proto，再取消下列代码的注释：
                // var response = await _client.RequestAsync(
                //     new Generated.PingRequest { Message = "hello from Unity" },
                //     Generated.PingResponse.Parser,
                //     _lifetime.Token);
                // Debug.Log("Server response: " + response.Message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async void OnDestroy()
        {
            if (_lifetime != null) _lifetime.Cancel();
            if (_client != null)
            {
                try { await _client.DisconnectAsync(CancellationToken.None); }
                catch { }
                _client.Dispose();
            }
            if (_lifetime != null) _lifetime.Dispose();
        }
    }
}
