using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetGameServer.UnityClient.Logging;
using DotNetGameServer.UnityClient.Unity;

using UnityEditor;
using UnityEngine;

using NetworkLogLevel = DotNetGameServer.UnityClient.Logging.NetworkLogLevel;

namespace DotNetGameServer.UnityClient.Editor
{
    public sealed class NetworkConsoleWindow : EditorWindow
    {
        private readonly List<NetworkLogEntry> _entries = new List<NetworkLogEntry>();
        private Vector2 _scroll;
        private string _search = string.Empty;
        private NetworkLogLevel _minimum = NetworkLogLevel.Debug;
        private bool _autoScroll = true;
        private int _selected = -1;

        [MenuItem("Window/DotNet Game Server/Network Console")]
        public static void Open()
        {
            GetWindow<NetworkConsoleWindow>("Network Console");
        }

        private void OnEnable()
        {
            ReloadSnapshot();
            NetworkLogHub.Published += OnLogPublished;
        }

        private void OnDisable()
        {
            NetworkLogHub.Published -= OnLogPublished;
        }

        private void OnLogPublished(NetworkLogEntry entry)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                _entries.Add(entry);
                Repaint();
            };
        }

        private void OnGUI()
        {
            DrawToolbar();
            var visible = _entries
                .Select((entry, index) => new { entry, index })
                .Where(x => x.entry.Level >= _minimum)
                .Where(x => Matches(x.entry, _search))
                .ToArray();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var item in visible)
            {
                var style = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = ColorFor(item.entry.Level) },
                    wordWrap = false
                };
                var endpoint = string.IsNullOrEmpty(item.entry.Endpoint) ? string.Empty : " [" + item.entry.Endpoint + "]";
                var text = item.entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff")
                    + " " + item.entry.Level.ToString().ToUpperInvariant().PadRight(11)
                    + " " + item.entry.Category + endpoint + " " + item.entry.Message;
                if (GUILayout.Button(text, style)) _selected = item.index;
            }
            if (_autoScroll && Event.current.type == EventType.Repaint)
                _scroll.y = float.MaxValue;
            EditorGUILayout.EndScrollView();

            if (_selected >= 0 && _selected < _entries.Count) DrawDetails(_entries[_selected]);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    NetworkLogHub.Clear();
                    _entries.Clear();
                    _selected = -1;
                }
                if (GUILayout.Button("Logs Folder", EditorStyles.toolbarButton, GUILayout.Width(78)))
                {
                    var path = UnityNetworkLoggingBootstrap.LogDirectory;
                    Directory.CreateDirectory(path);
                    EditorUtility.RevealInFinder(path);
                }
                _minimum = (NetworkLogLevel)EditorGUILayout.EnumPopup(_minimum, EditorStyles.toolbarPopup, GUILayout.Width(105));
                _search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSearchTextField"), GUILayout.MinWidth(120));
                _autoScroll = GUILayout.Toggle(_autoScroll, "Auto Scroll", EditorStyles.toolbarButton, GUILayout.Width(78));
                GUILayout.FlexibleSpace();
                GUILayout.Label(_entries.Count + " events", EditorStyles.miniLabel);
            }
        }

        private static void DrawDetails(NetworkLogEntry entry)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Selected Network Event", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                entry.TimestampUtc.ToString("O") + "  " + entry.Level + "  " + entry.Category
                + "\nEndpoint: " + entry.Endpoint
                + "\nRequest: " + entry.RequestId
                + "\n" + entry.Message
                + (entry.Exception == null ? string.Empty : "\n\n" + entry.Exception),
                EditorStyles.textArea,
                GUILayout.Height(92));
        }

        private void ReloadSnapshot()
        {
            _entries.Clear();
            _entries.AddRange(NetworkLogHub.Snapshot());
        }

        private static bool Matches(NetworkLogEntry entry, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            return Contains(entry.Category, search) || Contains(entry.Message, search)
                || Contains(entry.Endpoint, search) || Contains(entry.RequestId, search);
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Color ColorFor(NetworkLogLevel level)
        {
            switch (level)
            {
                case NetworkLogLevel.Warning: return new Color(1f, 0.72f, 0.25f);
                case NetworkLogLevel.Error: return new Color(1f, 0.35f, 0.35f);
                case NetworkLogLevel.Debug:
                case NetworkLogLevel.Trace: return new Color(0.65f, 0.65f, 0.65f);
                default: return EditorGUIUtility.isProSkin ? Color.white : Color.black;
            }
        }
    }
}
