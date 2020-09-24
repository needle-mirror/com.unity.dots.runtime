using System;
using Unity.Build;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Runtime.Build
{
    /// <summary>
    /// Component system group used for any system that needs to run in the configuration build step.
    /// </summary>
    [DisableAutoCreation]
    public class ConfigurationSystemGroup : ComponentSystemGroup {}

    /// <summary>
    /// Base class for a configuration system. A configuration system must inherit from ConfigurationSystemBase to be in the ConfigurationSystemGroup group
    /// </summary>
    public abstract partial class ConfigurationSystemBase : SystemBase
    {
        public virtual Type[] UsedComponents { get; } = Array.Empty<Type>();
        public BuildContext BuildContext;
        public BuildAssemblyCache AssemblyCache;
        public string OutputDir;
    }
}
