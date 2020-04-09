using System;
using System.Collections.Generic;
using Unity.Build;
using Unity.Properties;
using Unity.Serialization.Json;
using Unity.Serialization.Json.Adapters.Contravariant;
using UnityEditor;
using UnityEngine;
using BuildPipeline = Unity.Build.BuildPipeline;
using BuildTarget = Unity.Platforms.BuildTarget;

namespace Unity.Entities.Runtime.Build
{
    public sealed class DotsRuntimeBuildProfile : IBuildPipelineComponent
    {
        BuildTarget m_Target;
        List<string> m_ExcludedAssemblies;

        /// <summary>
        /// Retrieve <see cref="BuildTypeCache"/> for this build profile.
        /// </summary>
        public BuildTypeCache TypeCache { get; } = new BuildTypeCache();

        /// <summary>
        /// Gets or sets which <see cref="Platforms.BuildTarget"/> this profile is going to use for the build.
        /// Used for building Dots Runtime players.
        /// </summary>
        [CreateProperty]
        public BuildTarget Target
        {
            get => m_Target;
            set
            {
                m_Target = value;
                TypeCache.PlatformName = m_Target?.UnityPlatformName;
            }
        }

        public int SortingIndex => 0;
        public bool SetupEnvironment() => false;

        /// <summary>
        /// Gets or sets which <see cref="Configuration"/> this profile is going to use for the build.
        /// </summary>
        [CreateProperty]
        public BuildType Configuration { get; set; } = BuildType.Develop;

#if UNITY_2020_1_OR_NEWER
        [CreateProperty] public LazyLoadReference<BuildPipeline> Pipeline { get; set; }
#else
        [CreateProperty] public BuildPipeline Pipeline { get; set; }
#endif

        /// <summary>
        /// List of assemblies that should be explicitly excluded for the build.
        /// </summary>
        //[CreateProperty]
        public List<string> ExcludedAssemblies
        {
            get => m_ExcludedAssemblies;
            set
            {
                m_ExcludedAssemblies = value;
                TypeCache.ExcludedAssemblies = value;
            }
        }

        public DotsRuntimeBuildProfile()
        {
            Target = BuildTarget.DefaultBuildTarget;
            ExcludedAssemblies = new List<string>();
        }

        class DotsRuntimeBuildProfileJsonAdapter : 
            IJsonAdapter<BuildTarget>
        {
            [InitializeOnLoadMethod]
            static void Initialize()
            {
                JsonSerialization.AddGlobalAdapter(new DotsRuntimeBuildProfileJsonAdapter());
            }
            
            public void Serialize(JsonStringBuffer writer, BuildTarget value)
            {
                writer.WriteEncodedJsonString(value?.BeeTargetName);
            }

            public object Deserialize(SerializedValueView view)
            {
                return BuildTarget.GetBuildTargetFromBeeTargetName(view.ToString());
            }
        }
    }
}
