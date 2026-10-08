using System;
using System.Threading;
using DotNetGameServer.UnityClient.Protocol;
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
            _client.TokenRefreshHandler = RefreshSessionAsync;
            try
            {
                var endpoint = await _client.ConnectAsync(
                    UnityGameNetworkClientFactory.CreateDefaultQuery(SystemInfo.deviceUniqueIdentifier),
                    _lifetime.Token);
                Debug.Log("Connected through discovery: " + endpoint);

                _status = "已连接，正在游客登录…";
                _detail = endpoint.ToString();
                _client.On(PingResponse.Parser, pushed =>
                    Debug.Log("Pushed PingResponse: " + pushed.Message));

                var login = await _client.RequestResultAsync(
                    new GuestLoginRequest { DisplayName = "unity" },
                    GuestLoginResponse.Parser,
                    _lifetime.Token);
                if (!login.HasResponse || login.Response == null || !login.Response.Success)
                {
                    _succeeded = false;
                    _status = "登录失败";
                    _detail = login.Response != null
                        ? login.Response.Code + " / " + login.Response.Message
                        : login.ErrorCode + " / " + login.ErrorMessage;
                    return;
                }

                _client.ApplyAccessToken(AccessTokenGrant.Create(
                    login.Response.AccessToken,
                    login.Response.UserId,
                    login.Response.ExpireUnix,
                    login.Response.RecommendedRefreshSeconds));

                _status = "已登录，正在发送 Ping…";
                var result = await _client.RequestResultAsync(
                    new PingRequest { Message = "hello from Unity" },
                    PingResponse.Parser,
                    _lifetime.Token);

                var response = result.Response;
                _succeeded = result.HasResponse && response != null && response.Success;
                _status = _succeeded ? "连接成功 ✓" : "服务端拒绝了请求";
                _detail = response != null
                    ? response.Message
                    : result.ErrorCode + " / " + result.ErrorMessage;
                if (response != null && response.Success) Debug.Log("Server response: " + response.Message);
                else if (response != null) Debug.LogError("Server rejected Ping: " + response.Code + " / " + response.Message);
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

        private async System.Threading.Tasks.Task<AccessTokenGrant> RefreshSessionAsync(CancellationToken cancellationToken)
        {
            if (_client == null) return null;
            if (!string.IsNullOrEmpty(_client.AccessToken))
            {
                var refreshed = await _client.RequestResultAsync(
                    new RefreshSessionRequest { AccessToken = _client.AccessToken },
                    RefreshSessionResponse.Parser,
                    cancellationToken);
                if (refreshed.HasResponse && refreshed.Response != null && refreshed.Response.Success)
                {
                    return AccessTokenGrant.Create(
                        refreshed.Response.AccessToken,
                        refreshed.Response.UserId,
                        refreshed.Response.ExpireUnix,
                        refreshed.Response.RecommendedRefreshSeconds);
                }
            }

            var login = await _client.RequestResultAsync(
                new GuestLoginRequest { DisplayName = "unity" },
                GuestLoginResponse.Parser,
                cancellationToken);
            if (!login.HasResponse || login.Response == null || !login.Response.Success)
                return null;
            return AccessTokenGrant.Create(
                login.Response.AccessToken,
                login.Response.UserId,
                login.Response.ExpireUnix,
                login.Response.RecommendedRefreshSeconds);
        }

        private void OnGUI()
        {
            var previousColor = GUI.color;
            GUI.color = _succeeded ? new Color(0.75f, 1f, 0.75f) : Color.white;
            GUILayout.BeginArea(new Rect(24, 24, Math.Min(620, Screen.width - 48), 150), GUI.skin.box);
            GUILayout.Label("DotNet Game Server · Basic Client", GUI.skin.label);
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
