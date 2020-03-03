using System.Collections.Generic;
using Unity.Build;
using Unity.Properties;

namespace Unity.Entities.Runtime.Build
{
    internal sealed class DotsRuntimeScriptingDefines : IBuildComponent
    {
        [Property]
        public List<string> ScriptingDefines { get; set; } = new List<string>();
    }
}
