using System;
using System.Threading;
using DotNetGameServer.UnityClient.Samples.Generated;
using DotNetGameServer.UnityClient.Unity;
using UnityEngine;

namespace DotNetGameServer.UnityClient.Samples
{
    public sealed class BasicClientBehaviour : MonoBehaviour
    {
        private GameNetworkClient _client;
        private CancellationTokenSource _lifetime;
        private string _status = "正在连接本机 BasicServer…";
        private string _detail = "请先在仓库根目录运行 Demo/BasicServer。";
        private bool _succeeded;

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

                _status = "已连接，正在发送 Ping…";
                _detail = endpoint.ToString();
                var response = await _client.RequestAsync(
                    new PingRequest { Message = "hello from Unity" },
                    PingResponse.Parser,
                    _lifetime.Token);

                _succeeded = response.Success;
                _status = response.Success ? "连接成功 ✓" : "服务端拒绝了请求";
                _detail = response.Message;
                if (response.Success) Debug.Log("Server response: " + response.Message);
                else Debug.LogError("Server rejected Ping: " + response.Code + " / " + response.Message);
            }
            catch (OperationCanceledException) when (_lifetime != null && _lifetime.IsCancellationRequested)
            {
                // Play Mode stopped or the component was destroyed.
            }
            catch (Exception exception)
            {
                _succeeded = false;
                _status = "连接失败";
                _detail = exception.GetBaseException().Message
                    + "\n确认 BasicServer 正在运行，并检查 Project Settings > DotNet Game Server。";
                Debug.LogException(exception);
            }
        }

        private void OnGUI()
        {
            var previousColor = GUI.color;
            GUI.color = _succeeded ? new Color(0.75f, 1f, 0.75f) : Color.white;
            GUILayout.BeginArea(new Rect(24, 24, Math.Min(620, Screen.width - 48), 150), GUI.skin.box);
            GUILayout.Label("DotNetGameServer · Basic Client", GUI.skin.label);
            GUILayout.Space(8);
            GUILayout.Label(_status);
            GUILayout.Label(_detail);
            GUILayout.EndArea();
            GUI.color = previousColor;
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
