using System;
using System.IO;
using Unity.Build;
using Unity.Properties;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.Entities.Runtime.Build
{
    public sealed class DotsRuntimeRootAssembly : IBuildComponent
    {
        /// <summary>
        /// Retrieve <see cref="BuildTypeCache"/> for this build profile.
        /// </summary>
        public BuildTypeCache TypeCache { get; } = new BuildTypeCache();

        /// <summary>
        /// Gets or sets the root assembly for this DOTS Runtime build.  This root
        /// assembly determines what other assemblies will be pulled in for the build.
        /// </summary>
        [CreateProperty]
        public AssemblyDefinitionAsset RootAssembly
        {
            get { return m_RootAssembly; }
            set
            {
                m_RootAssembly = value;
                TypeCache.BaseAssemblies = new[] { m_RootAssembly };
            }
        }
        AssemblyDefinitionAsset m_RootAssembly;

        public string ProjectName
        {
            get
            {
                if (RootAssembly == null || !RootAssembly)
                    return null;

                // FIXME should maybe be RootAssembly.name, but this is super confusing
                var asmdefPath = AssetDatabase.GetAssetPath(RootAssembly);
                var asmdefFilename = Path.GetFileNameWithoutExtension(asmdefPath);

                // just require that they're identical for this root assembly
                if (!asmdefFilename.Equals(RootAssembly.name))
                    throw new InvalidOperationException($"Root asmdef {asmdefPath} must have its assembly name (currently '{RootAssembly.name}') set to the same as the filename (currently '{asmdefFilename}')");

                return asmdefFilename;
            }
        }

        public static DirectoryInfo BeeRootDirectory => new DirectoryInfo("Library/DotsRuntimeBuild");
        public DirectoryInfo StagingDirectory => new DirectoryInfo($"Library/DotsRuntimeBuild/{ProjectName}");

        [CreateProperty, HideInInspector]
        public string BeeTargetOverride { get; set; }

        public string MakeBeeTargetName(BuildConfiguration buildConfig)
        {
            return $"{RootAssembly.name}-{buildConfig.name}".ToLower();
        }
    }
}
