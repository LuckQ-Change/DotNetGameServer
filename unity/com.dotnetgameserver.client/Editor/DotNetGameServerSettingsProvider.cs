using System.IO;
using DotNetGameServer.UnityClient.Unity;
using UnityEditor;
using UnityEngine;

namespace DotNetGameServer.UnityClient.Editor
{
    internal static class DotNetGameServerSettingsProvider
    {
        private const string AssetPath = "Assets/Resources/DotNetGameServerSettings.asset";
        private static UnityEditor.Editor _assetEditor;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/DotNet Game Server", SettingsScope.Project)
            {
                label = "DotNet Game Server",
                keywords = new System.Collections.Generic.HashSet<string>
                {
                    "network", "server", "protobuf", "service discovery", "logging"
                },
                guiHandler = _ => DrawSettings()
            };
        }

        private static void DrawSettings()
        {
            EditorGUILayout.LabelField("Unity Client Runtime", EditorStyles.boldLabel);
            var settings = AssetDatabase.LoadAssetAtPath<DotNetGameServerSettings>(AssetPath);
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "尚未创建 Runtime Settings。没有该资源时 SDK 使用本机开发默认值，但构建不会携带项目配置。",
                    MessageType.Warning);
                if (GUILayout.Button("Create/Repair Runtime Settings", GUILayout.Height(28)))
                    settings = CreateSettingsAsset();
                return;
            }

            if (_assetEditor == null || _assetEditor.target != settings)
            {
                if (_assetEditor != null) Object.DestroyImmediate(_assetEditor);
                _assetEditor = UnityEditor.Editor.CreateEditor(settings);
            }
            _assetEditor.OnInspectorGUI();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Service Browser")) ServiceBrowserWindow.Open();
                if (GUILayout.Button("Open Proto Generator")) ProtoGeneratorWindow.Open();
                if (GUILayout.Button("Open Network Console")) NetworkConsoleWindow.Open();
            }
        }

        internal static DotNetGameServerSettings CreateSettingsAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var existing = AssetDatabase.LoadAssetAtPath<DotNetGameServerSettings>(AssetPath);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<DotNetGameServerSettings>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            return asset;
        }
    }
}
