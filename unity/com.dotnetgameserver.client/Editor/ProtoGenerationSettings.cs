using UnityEditor;

namespace DotNetGameServer.UnityClient.Editor
{
    [FilePath("ProjectSettings/DotNetGameServerProtoSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ProtoGenerationSettings : ScriptableSingleton<ProtoGenerationSettings>
    {
        public string ProtocPath = string.Empty;
        public string InputDirectory = "Assets/Protos";
        public string OutputDirectory = "Assets/Generated/Protobuf";
        public bool IncludePackageProtos = true;

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
