using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using Unity.Collections;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Runtime.Build
{
    /// <summary>
    /// BuildStepExportScenes is collecting all scene artifacts generated from the import of SceneWithBuildConfigurationGUIDs files for each scenes, and add them to the manifest to be deployed to the staging build directory.
    /// The subscene importer is importing SceneWithBuildConfigurationGUIDs (scene guid + build configuration guid) files and detects what needs to be reconverted based on scene/build configuration changes and dependent assets
    /// It generates the following files for each scene converted (top scene + subscenes from the SceneList component):
    /// .entityheader
    /// .entities
    /// .conversionlog (if it contains errors or exceptions, it fails the build)
    /// .{assetguid} (asset files)
    /// </summary>
    [BuildStep(Description = "Exporting Scenes")]
    sealed class BuildStepExportScenes : BuildStepBase
    {
        public override Type[] UsedComponents { get; } =
        {
            typeof(DotsRuntimeBuildProfile),
            typeof(DotsRuntimeRootAssembly),
            typeof(SceneList)
        };

        public override BuildResult Run(BuildContext context)
        {
            var manifest = context.BuildManifest;
            var rootAssembly = context.GetComponentOrDefault<DotsRuntimeRootAssembly>();
            var buildScenes = context.GetComponentOrDefault<SceneList>();
            var targetName = rootAssembly.MakeBeeTargetName(context.BuildConfigurationName);
            var scenePaths = buildScenes.GetScenePathsForBuild();
            var buildConfigurationGuid = context.BuildConfigurationAssetGUID;
            var dataDirectory = WorldExport.GetOrCreateDataDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName));

            var sceneGuids = scenePaths.SelectMany(scenePath =>
            {
                List<Hash128> guids = SceneMetaDataImporter.GetSubSceneGuids(AssetDatabase.AssetPathToGUID(scenePath)).ToList();
                guids.Add(new Hash128(AssetDatabase.AssetPathToGUID(scenePath)));
                return guids;
            }).Distinct().ToList();

            var requiresRefresh = false;
            var sceneBuildConfigGuids = new NativeArray<GUID>(sceneGuids.Count, Allocator.TempJob);
            for (int i=0;i != sceneBuildConfigGuids.Length;i++)
            {
                sceneBuildConfigGuids[i] = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(sceneGuids[i], new Hash128(buildConfigurationGuid), out var thisRequiresRefresh);
                requiresRefresh |= thisRequiresRefresh;
            }
            if (requiresRefresh)
                AssetDatabase.Refresh();

            var artifactHashes = new NativeArray<UnityEngine.Hash128>(sceneGuids.Count, Allocator.TempJob);
            AssetDatabaseCompatibility.ProduceArtifactsRefreshIfNecessary(sceneBuildConfigGuids, typeof(SubSceneImporter), artifactHashes);

            bool succeeded = true;

            for (int i = 0; i != sceneBuildConfigGuids.Length; i++)
            {
                var sceneGuid = sceneGuids[i];
                var artifactHash = artifactHashes[i];

                AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out var artifactPaths);

                List<FileInfo> exportedFiles = new List<FileInfo>();
                bool foundEntityHeader = false;
                foreach (var artifactPath in artifactPaths)
                {
                    var ext = Path.GetExtension(artifactPath).ToLower().Replace(".", "");
                    if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader))
                    {
                        foundEntityHeader = true;
                        var destinationFile = dataDirectory.FullName + Path.DirectorySeparatorChar + EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesHeader, -1);
                        new NPath(artifactPath).MakeAbsolute().Copy(new NPath(destinationFile).MakeAbsolute().EnsureParentDirectoryExists());
                        exportedFiles.Add(new FileInfo(destinationFile));
                    }
                    else if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesBinary))
                    {
                        var destinationFile = dataDirectory.FullName + Path.DirectorySeparatorChar + EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesBinary, EntityScenesPaths.GetSectionIndexFromPath(artifactPath));
                        new NPath(artifactPath).MakeAbsolute().Copy(new NPath(destinationFile).MakeAbsolute().EnsureParentDirectoryExists());
                        exportedFiles.Add(new FileInfo(destinationFile));
                    }
                    else if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesConversionLog))
                    {
                        var destinationFile = dataDirectory.FullName + Path.DirectorySeparatorChar + EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesConversionLog, -1);
                        new NPath(artifactPath).MakeAbsolute().Copy(new NPath(destinationFile).MakeAbsolute().EnsureParentDirectoryExists());
                        exportedFiles.Add(new FileInfo(destinationFile));
                        succeeded = CheckConversionLog(artifactPath);
                        if(!succeeded)
                            UnityEngine.Debug.LogError("Failed to export scene: " + Path.GetFileName(AssetDatabase.GUIDToAssetPath(sceneGuid.ToString())));
                    }
                    else if (new Hash128(ext).IsValid) //Asset files are exported as {artifactHash}.{assetguid}
                    {
                        var destinationFile = dataDirectory.FullName + Path.DirectorySeparatorChar + ext;
                        new NPath(artifactPath).MakeAbsolute().Copy(new NPath(destinationFile).MakeAbsolute().EnsureParentDirectoryExists());
                        exportedFiles.Add(new FileInfo(destinationFile));
                    }
                }

                if (!foundEntityHeader)
                {
                    Debug.LogError($"Failed to build EntityScene for '{AssetDatabaseCompatibility.GuidToPath(sceneGuid)}'.");
                    succeeded = false;
                }

                //UpdateManifest
                manifest.Add(new Guid(sceneGuid.ToString()), AssetDatabase.GUIDToAssetPath(sceneGuid.ToString()), exportedFiles);
            }

            // TODO: We want to just add the written catalog file to the manifest but Platforms.Build currently
            // doesn't support non-classic BuildPipelines from doing so
            var finalOutputDirectory = BuildStepGenerateBeeFiles.GetFinalOutputDirectory(context, targetName);
            if (!Directory.Exists(finalOutputDirectory)) Directory.CreateDirectory(finalOutputDirectory);

            var catalogPath = Path.Combine(finalOutputDirectory, "Data");
            if (!Directory.Exists(catalogPath)) Directory.CreateDirectory(catalogPath);
            catalogPath = Path.Combine(catalogPath, SceneSystem.k_SceneInfoFileName);
            WriteCatalogFile(catalogPath, buildScenes);

            sceneBuildConfigGuids.Dispose();
            artifactHashes.Dispose();

            if(succeeded)
                return context.Success();
            return context.Failure($"Failed to export scenes");
        }

        struct CatalogEntry
        {
            public string Path;
            public ResourceMetaData MetaData;
        }

        static void WriteCatalogFile(string catalogPath, SceneList sceneList)
        {
            var catalogEntries = new List<CatalogEntry>();

            //Register catalog entries for each scene to build
            var sceneInfos = sceneList.GetSceneInfosForBuild();
            for (int i = 0; i < sceneInfos.Length; i++)
            {
                catalogEntries.Add(new CatalogEntry()
                {
                    Path = sceneInfos[i].Path,
                    MetaData = new ResourceMetaData()
                    {
                        ResourceFlags = sceneInfos[i].AutoLoad
                            ? ResourceMetaData.Flags.AutoLoad
                            : ResourceMetaData.Flags.None,
                        ResourceId = sceneInfos[i].Scene.assetGUID,
                        ResourceType = ResourceMetaData.Type.Scene
                    }
                });
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ResourceCatalogData>();
            var metas = builder.Allocate(ref root.resources, catalogEntries.Count);
            for (int i = 0; i < catalogEntries.Count; i++)
                metas[i] = catalogEntries[i].MetaData;

            var strings = builder.Allocate(ref root.paths, catalogEntries.Count);
            for (int i = 0; i < catalogEntries.Count; i++)
                builder.AllocateString(ref strings[i], catalogEntries[i].Path.ToLower());

            BlobAssetReference<ResourceCatalogData>.Write(builder, catalogPath, ResourceCatalogData.CurrentFileFormatVersion);
            builder.Dispose();
        }

        static bool CheckConversionLog(string artifactPath)
        {
            bool validLog = true;
            using (var reader = new StreamReader(artifactPath))
            {
                var line = "";
                while ((line = reader.ReadLine()) != null)
                {
                    var strArray = line.Split(':');
                    var prefix = strArray[0];
                    if (prefix.Contains(LogType.Error.ToString()) || prefix.Contains(LogType.Exception.ToString()))
                    {
                        Debug.LogError(line);
                        validLog = false;
                    }
                    else if(prefix.Contains(LogType.Warning.ToString()))
                        Debug.LogWarning(line);
                }
            }
            return validLog;
        }
    }
}
