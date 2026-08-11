using System;
using System.Collections.Generic;
using System.Threading;
using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Unity;
using UnityEditor;
using UnityEngine;

namespace DotNetGameServer.UnityClient.Editor
{
    public sealed class ServiceBrowserWindow : EditorWindow
    {
        private IReadOnlyList<ServiceEndpoint> _services = Array.Empty<ServiceEndpoint>();
        private string _status = "Not queried";
        private bool _busy;
        private Vector2 _scroll;
        private CancellationTokenSource _queryCts;

        [MenuItem("Tools/DotNet Game Server/Service Browser")]
        public static void Open()
        {
            GetWindow<ServiceBrowserWindow>("DGS Services");
        }

        private void OnDisable()
        {
            if (_queryCts != null) _queryCts.Cancel();
            if (_queryCts != null) _queryCts.Dispose();
            _queryCts = null;
        }

        private void OnGUI()
        {
            var settings = DotNetGameServerSettings.Load();
            if (settings == null)
            {
                EditorGUILayout.HelpBox("请先在 Project Settings/DotNet Game Server 创建 Runtime Settings。", MessageType.Warning);
                if (GUILayout.Button("Create Settings")) DotNetGameServerSettingsProvider.CreateSettingsAsset();
                return;
            }

            EditorGUILayout.LabelField("Automatic Service Discovery", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Bootstrap", string.Join(", ", settings.BootstrapUrls));
            EditorGUILayout.LabelField("Responsibility", settings.Responsibility);
            EditorGUILayout.LabelField("Region", string.IsNullOrEmpty(settings.Region) ? "(any)" : settings.Region);
            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button(_busy ? "Querying…" : "Refresh Services", GUILayout.Height(28))) RefreshAsync(settings);
            }
            EditorGUILayout.HelpBox(_status, _status.StartsWith("Failed", StringComparison.Ordinal) ? MessageType.Error : MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var service in _services)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(service.Role + "  " + service.Host + ":" + service.Port, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("ID", service.ServiceId);
                    EditorGUILayout.LabelField("Responsibilities", string.Join(", ", service.Responsibilities));
                    EditorGUILayout.LabelField("Region / Version", service.Region + " / " + service.Version);
                    EditorGUILayout.LabelField("Load / TLS", service.Load.ToString("P0") + " / " + service.TlsEnabled);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private async void RefreshAsync(DotNetGameServerSettings settings)
        {
            if (_queryCts != null) _queryCts.Cancel();
            if (_queryCts != null) _queryCts.Dispose();
            _queryCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _busy = true;
            _status = "Querying service catalog…";
            Repaint();
            try
            {
                var provider = new UnityHttpServiceDiscoveryProvider(settings.BootstrapUrls, 5);
                _services = await provider.ResolveAsync(new ServiceQuery
                {
                    Responsibility = settings.Responsibility,
                    Region = settings.Region,
                    Limit = 32
                }, _queryCts.Token);
                _status = "Found " + _services.Count + " service(s).";
            }
            catch (Exception exception)
            {
                _services = Array.Empty<ServiceEndpoint>();
                _status = "Failed: " + exception.Message;
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }
    }
}
