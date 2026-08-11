using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using UnityEditor;

using UnityEngine;

using Debug = UnityEngine.Debug;


namespace DotNetGameServer.UnityClient.Editor
{
    public sealed class ProtoGeneratorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _lastOutput = string.Empty;

        [MenuItem("Tools/DotNet Game Server/Proto Generator")]
        public static void Open()
        {
            GetWindow<ProtoGeneratorWindow>("DGS Proto Generator");
        }

        private void OnGUI()
        {
            var settings = ProtoGenerationSettings.instance;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Protocol Buffers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "生成器调用标准 protoc --csharp_out。protoc 可来自 PATH、DGS_PROTOC 环境变量、Grpc.Tools NuGet 缓存或手工选择。",
                MessageType.Info);

            settings.ProtocPath = PathField("protoc", settings.ProtocPath, false);
            settings.InputDirectory = PathField("Proto Input", settings.InputDirectory, true);
            settings.OutputDirectory = PathField("C# Output", settings.OutputDirectory, true);
            settings.IncludePackageProtos = EditorGUILayout.Toggle("Include Package Protos", settings.IncludePackageProtos);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Auto Detect protoc")) settings.ProtocPath = DetectProtoc() ?? string.Empty;
                if (GUILayout.Button("Generate All", GUILayout.Height(28))) Generate(settings);
            }

            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_lastOutput, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string PathField(string label, string value, bool folder)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button("…", GUILayout.Width(32)))
                {
                    var selected = folder
                        ? EditorUtility.OpenFolderPanel(label, ProjectRoot, string.Empty)
                        : EditorUtility.OpenFilePanel(label, ProjectRoot, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty);
                    if (!string.IsNullOrEmpty(selected)) value = MakeProjectRelative(selected);
                }
            }
            return value;
        }

        private void Generate(ProtoGenerationSettings settings)
        {
            try
            {
                var protoc = ResolveAbsolute(settings.ProtocPath);
                if (!File.Exists(protoc)) protoc = DetectProtoc();
                if (string.IsNullOrEmpty(protoc) || !File.Exists(protoc))
                    throw new FileNotFoundException("找不到 protoc。请点击 Auto Detect 或手工选择 Grpc.Tools 中的 protoc。");

                var input = ResolveAbsolute(settings.InputDirectory);
                var output = ResolveAbsolute(settings.OutputDirectory);
                if (!Directory.Exists(input)) Directory.CreateDirectory(input);
                Directory.CreateDirectory(output);
                var packageProto = Path.Combine(PackageRoot, "Protos~");
                var protoFiles = Directory.GetFiles(input, "*.proto", SearchOption.AllDirectories).ToList();
                if (settings.IncludePackageProtos && Directory.Exists(packageProto))
                    protoFiles.AddRange(Directory.GetFiles(packageProto, "*.proto", SearchOption.AllDirectories));
                protoFiles = protoFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (protoFiles.Count == 0) throw new InvalidOperationException("没有找到 .proto 文件。");

                var args = new List<string>
                {
                    "--csharp_out=" + Quote(output),
                    "--proto_path=" + Quote(input)
                };
                if (Directory.Exists(packageProto)) args.Add("--proto_path=" + Quote(packageProto));
                args.AddRange(protoFiles.Select(Quote));

                var start = new ProcessStartInfo
                {
                    FileName = protoc,
                    Arguments = string.Join(" ", args),
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("无法启动 protoc。");
                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    _lastOutput = stdout + Environment.NewLine + stderr;
                    if (process.ExitCode != 0) throw new InvalidOperationException("protoc 失败，退出码 " + process.ExitCode + Environment.NewLine + stderr);
                }
                AssetDatabase.Refresh();
                Debug.Log("DotNetGameServer Proto generated: " + protoFiles.Count + " files -> " + output);
            }
            catch (Exception exception)
            {
                _lastOutput = exception.ToString();
                Debug.LogException(exception);
            }
        }

        internal static string DetectProtoc()
        {
            var configured = Environment.GetEnvironmentVariable("DGS_PROTOC");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

            var command = Application.platform == RuntimePlatform.WindowsEditor ? "where" : "which";
            try
            {
                var start = new ProcessStartInfo(command, "protoc")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(start))
                {
                    if (process != null)
                    {
                        var found = process.StandardOutput.ReadLine();
                        process.WaitForExit();
                        if (!string.IsNullOrWhiteSpace(found) && File.Exists(found)) return found;
                    }
                }
            }
            catch { }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var toolsRoot = Path.Combine(home, ".nuget", "packages", "grpc.tools");
            if (!Directory.Exists(toolsRoot)) return null;
            var platform = Application.platform == RuntimePlatform.WindowsEditor
                ? "windows_x64/protoc.exe"
                : Application.platform == RuntimePlatform.OSXEditor ? "macosx_x64/protoc" : "linux_x64/protoc";
            return Directory.GetDirectories(toolsRoot)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "tools", platform.Replace('/', Path.DirectorySeparatorChar)))
                .FirstOrDefault(File.Exists);
        }

        private static string Quote(string path) { return "\"" + path.Replace("\"", "\\\"") + "\""; }
        private static string ResolveAbsolute(string path) { return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectRoot, path)); }
        private static string MakeProjectRelative(string path)
        {
            var absolute = Path.GetFullPath(path);
            var root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? absolute.Substring(root.Length).Replace('\\', '/')
                : absolute;
        }

        private static string ProjectRoot { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ProtoGeneratorWindow).Assembly);
                return info == null ? string.Empty : info.resolvedPath;
            }
        }
    }
}
