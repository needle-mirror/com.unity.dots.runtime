using System.IO;
using Unity.Build;
using Unity.Properties;

namespace Unity.Entities.Runtime.Build
{
    internal sealed class DotsRuntimeBuildArtifact : IBuildArtifact
    {
        [Property] public FileInfo OutputTargetFile { get; set; }
    }
}
