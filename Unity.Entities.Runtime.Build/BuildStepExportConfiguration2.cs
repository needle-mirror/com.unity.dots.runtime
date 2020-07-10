using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using Unity.Build.Internals;
using Unity.Collections;
using Unity.Core.Compression;
using Unity.Entities.Serialization;
using Unity.Scenes.Editor;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Description = "Exporting Configuration2")]
    sealed class BuildStepExportConfiguration2 : BuildStepBase
    {
        static BuildAssemblyCache s_AssemblyCache = new BuildAssemblyCache();
        public override Type[] UsedComponents { get; } =
        {
            typeof(DotsRuntimeBuildProfile),
            typeof(DotsRuntimeRootAssembly),
            typeof(SceneList)
        };

        void WriteDebugFile(BuildContext context, BuildManifest manifest, IReadOnlyList<Type> allTypes)
        {
            var rootAssembly = context.GetComponentOrDefault<DotsRuntimeRootAssembly>();
            var targetName = rootAssembly.MakeBeeTargetName(context.BuildConfigurationName);
            var outputDir = BuildStepGenerateBeeFiles.GetFinalOutputDirectory(context, targetName);
            var debugFile = new NPath(outputDir).Combine("Logs/SceneExportLog.txt");
            var debugAssets = manifest.Assets.OrderBy(x => x.Value)
                .Select(x => $"{x.Key.ToString("N")} = {x.Value}").ToList();

            var debugLines = new List<string>();

            debugLines.Add("::Exported Assets::");
            debugLines.AddRange(debugAssets);
            debugLines.Add("\n");

            // Write out a separate list of types that we see in the dest world
            // as well as all types
            for (int group = 0; group < 2; ++group)
            {
                IEnumerable<TypeManager.TypeInfo> typesToWrite;
                if (group == 0)
                {
                    typesToWrite = allTypes.Select(t =>
                        TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(t)));

                    //Verify if an exported type is included in the output, if not print error message
                    foreach (TypeManager.TypeInfo exportedType in typesToWrite)
                    {
                        if (!s_AssemblyCache.HasType(exportedType.Type))
                            throw new InvalidOperationException($"The {exportedType.Type.Name} component is defined in the {exportedType.Type.Assembly.GetName().Name} assembly, but that assembly is not referenced by the current build configuration. Either add it as a reference, or ensure that the conversion process that is adding that component does not run.");
                    }
                    debugLines.Add($"::Exported Types (by stable hash)::");
                }
                else
                {
                    typesToWrite = TypeManager.AllTypes;
                    debugLines.Add($"::All Types in TypeManager (by stable hash)::");
                }

                var debugTypeHashes = typesToWrite.OrderBy(ti => ti.StableTypeHash)
                    .Where(ti => ti.Type != null).Select(ti =>
                        $"0x{ti.StableTypeHash:x16} - {ti.StableTypeHash,22} - {ti.Type.FullName}");

                debugLines.AddRange(debugTypeHashes);
                debugLines.Add("\n");
            }

            debugFile.MakeAbsolute().WriteAllLines(debugLines.ToArray());
        }

        public override BuildResult Run(BuildContext context)
        {
            var manifest = context.BuildManifest;
            var buildConfiguration = BuildContextInternals.GetBuildConfiguration(context);
            var profile = context.GetComponentOrDefault<DotsRuntimeBuildProfile>();
            var rootAssembly = context.GetComponentOrDefault<DotsRuntimeRootAssembly>();
            var targetName = rootAssembly.MakeBeeTargetName(context.BuildConfigurationName);
            var scenes = context.GetComponentOrDefault<SceneList>();
            var firstScene = scenes.GetScenePathsForBuild().FirstOrDefault();
            var originalActiveScene = SceneManager.GetActiveScene();

            s_AssemblyCache.BaseAssemblies = rootAssembly.RootAssembly.asset;
            s_AssemblyCache.PlatformName = profile.Target.UnityPlatformName;

            using (var loadedSceneScope = new LoadedSceneScope(firstScene))
            {
                var projectScene = loadedSceneScope.ProjectScene;

                using (var tmpWorld = new World(ConfigurationScene.Guid.ToString()))
                {
                    try
                    {
                        // Run configuration systems
                        ConfigurationSystemGroup configSystemGroup = tmpWorld.GetOrCreateSystem<ConfigurationSystemGroup>();
                        var systems = TypeCache.GetTypesDerivedFrom(typeof(ConfigurationSystemBase));
                        foreach (var type in systems)
                        {
                            ConfigurationSystemBase baseSys = (ConfigurationSystemBase)tmpWorld.GetOrCreateSystem(type);
                            baseSys.ProjectScene = projectScene;
                            baseSys.BuildConfiguration = buildConfiguration;
                            baseSys.AssemblyCache = s_AssemblyCache;
                            configSystemGroup.AddSystemToUpdateList(baseSys);
                        }

                        configSystemGroup.SortSystems();
                        configSystemGroup.Update();

                        var dataDirInfo = WorldExport.GetOrCreateDataDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName));
                        var subScenePath = WorldExport.GetOrCreateSubSceneDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName)).ToString();

                        // Export configuration scene
                        var writeEntitySceneSettings = new WriteEntitySceneSettings()
                        {
                            Codec = Codec.LZ4,
                            IsDotsRuntime = true,
                            OutputPath = subScenePath,
                            BuildAssemblyCache = s_AssemblyCache
                        };
                        var (decompressedSize, compressedSize) = EditorEntityScenes.WriteEntitySceneSection(
                            tmpWorld.EntityManager, ConfigurationScene.Guid, "0", null, writeEntitySceneSettings,
                            out var objectRefCount, out var objRefs, default);

                        if (objectRefCount > 0)
                            throw new ArgumentException("We are serializing a world that contains UnityEngine.Object references which are not supported in Dots Runtime.");

                        // Export configuration scene header file
                        var sceneSections = new List<SceneSectionData>();
                        sceneSections.Add(new SceneSectionData
                        {
                            FileSize = compressedSize,
                            SceneGUID = ConfigurationScene.Guid,
                            ObjectReferenceCount = objectRefCount,
                            SubSectionIndex = 0,
                            BoundingVolume = MinMaxAABB.Empty,
                            Codec = writeEntitySceneSettings.Codec,
                            DecompressedFileSize = decompressedSize
                        });
                        var sections = sceneSections.ToArray();
                        EditorEntityScenes.WriteSceneHeader(ConfigurationScene.Guid, sections, ConfigurationScene.Name, null, tmpWorld.EntityManager, writeEntitySceneSettings);

                        WorldExportTypeTracker allTypes = new WorldExportTypeTracker();
                        allTypes.AddTypesFromWorld(tmpWorld);

                        WorldExport.UpdateManifest(manifest, ConfigurationScene.Name, ConfigurationScene.Guid, sections, dataDirInfo, writeEntitySceneSettings.OutputPath);

                        // Dump debug file
                        WriteDebugFile(context, manifest, allTypes.TypesInUse);
                    }
                    catch (Exception e)
                    {
                        return context.Failure($"Exception thrown during SubScene export: {e}");
                    }
                }
            }
            return context.Success();
        }
    }
}
