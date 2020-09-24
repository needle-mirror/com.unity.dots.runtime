using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using Unity.Build.Internals;
using Unity.Core.Compression;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Description = "Exporting Configuration")]
    sealed class BuildStepExportConfiguration : BuildStepBase
    {
        static readonly BuildAssemblyCache s_AssemblyCache = new BuildAssemblyCache();

        static Type[] GetConfigurationSystemUsedTypes()
        {
            using (var tmpWorld = new World(nameof(GetConfigurationSystemUsedTypes), WorldFlags.Editor))
            {
                var types = TypeCache.GetTypesDerivedFrom(typeof(ConfigurationSystemBase));
                var systems = types.Select(type => tmpWorld.GetOrCreateSystem(type)).Cast<ConfigurationSystemBase>();
                return systems.SelectMany(system => system.UsedComponents).Distinct().ToArray();
            }
        }

        public override Type[] UsedComponents { get; } = new[]
        {
            typeof(DotsRuntimeBuildProfile),
            typeof(DotsRuntimeRootAssembly),
            typeof(SceneList)
        }.Concat(GetConfigurationSystemUsedTypes()).Distinct().ToArray();

        void WriteDebugFile(BuildContext context, BuildManifest manifest)
        {
            var rootAssembly = context.GetComponentOrDefault<DotsRuntimeRootAssembly>();
            var targetName = rootAssembly.MakeBeeTargetName(context.BuildConfigurationName);
            var outputDir = BuildStepGenerateBeeFiles.GetFinalOutputDirectory(context, targetName);
            var debugFile = new NPath(outputDir).Combine("Logs/SceneExportLog.txt");
            var debugSceneAssets = manifest.Assets.OrderBy(x => x.Value)
                .Select(x => $"{x.Key.ToString("N")} = {x.Value}").ToList();

            var debugLines = new List<string>();

            debugLines.Add("::Exported Scenes::");
            debugLines.AddRange(debugSceneAssets);
            debugLines.Add("\n");

            // Write out a separate list of all types
            for (int group = 0; group < 2; ++group)
            {
                IEnumerable<TypeManager.TypeInfo> typesToWrite = TypeManager.AllTypes;
                debugLines.Add($"::All Types in TypeManager (by stable hash)::");

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

            s_AssemblyCache.BaseAssemblies = rootAssembly.RootAssembly.asset;
            s_AssemblyCache.PlatformName = profile.Target.UnityPlatformName;

            // Record any log errors/exceptions to be able to stop the build if any
            ExportConfigurationLogHandler logHandler = new ExportConfigurationLogHandler();
            logHandler.Hook();

            using (var tmpWorld = new World(ConfigurationScene.Guid.ToString()))
            {
                var dataDirInfo =
                    WorldExport.GetOrCreateDataDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName));
                var subScenePath = WorldExport
                    .GetOrCreateSubSceneDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName)).ToString();
                var outputDir = BuildStepGenerateBeeFiles.GetFinalOutputDirectory(context, targetName);

                // Run configuration systems
                ConfigurationSystemGroup configSystemGroup = tmpWorld.GetOrCreateSystem<ConfigurationSystemGroup>();
                var systems = TypeCache.GetTypesDerivedFrom(typeof(ConfigurationSystemBase));
                foreach (var type in systems)
                {
                    ConfigurationSystemBase baseSys = (ConfigurationSystemBase) tmpWorld.GetOrCreateSystem(type);
                    baseSys.BuildContext = context;
                    baseSys.AssemblyCache = s_AssemblyCache;
                    baseSys.OutputDir = outputDir;
                    configSystemGroup.AddSystemToUpdateList(baseSys);
                }

                configSystemGroup.SortSystems();
                configSystemGroup.Update();

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
                    throw new ArgumentException(
                        "We are serializing a world that contains UnityEngine.Object references which are not supported in Dots Runtime.");

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
                EditorEntityScenes.WriteSceneHeader(ConfigurationScene.Guid, sections, ConfigurationScene.Name, null,
                    tmpWorld.EntityManager, writeEntitySceneSettings);

                WorldExportTypeTracker allTypes = new WorldExportTypeTracker();
                allTypes.AddTypesFromWorld(tmpWorld);

                WorldExport.UpdateManifest(manifest, ConfigurationScene.Name, ConfigurationScene.Guid, sections,
                    dataDirInfo, writeEntitySceneSettings.OutputPath);

                // Export additional general build debug logs
                WriteDebugFile(context, manifest);

                // Write exported types of the configuration scene to its export logs.
                EditorEntityScenes.CheckExportedTypes(writeEntitySceneSettings, true,
                    allTypes.TypesInUse.Select(t => TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(t))),
                    ref logHandler.JournalData);
                EditorEntityScenes.WriteConversionLog(ConfigurationScene.Guid,
                    logHandler.JournalData.SelectLogEventsOrdered().ToList(), null,
                    writeEntitySceneSettings.OutputPath);

                // Deploy configuration export log file
                var exportLogFile = EditorEntityScenes.GetSceneWritePath(
                    EntityScenesPaths.PathType.EntitiesConversionLog, "", ConfigurationScene.Guid,
                    writeEntitySceneSettings.OutputPath);
                manifest.AddAdditionalFilesToDeploy(new FileInfo(exportLogFile));

                logHandler.Unhook();

                if (logHandler.ContainsFailureLogs)
                    return context.Failure("Failed to export configuration scene");
                return context.Success();
            }
        }
    }
}
