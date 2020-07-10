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
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Description = "Exporting Entities 2")]
    sealed class BuildStepExportEntities2 : BuildStepBase
    {
        public BlobAssetStore blobAssetStore;
        static BuildAssemblyCache s_AssemblyCache = new BuildAssemblyCache();

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
            var config = BuildContextInternals.GetBuildConfiguration(context);
            var buildScenes = context.GetComponentOrDefault<SceneList>();
            var profile = context.GetComponentOrDefault<DotsRuntimeBuildProfile>();
            var targetName = rootAssembly.MakeBeeTargetName(context.BuildConfigurationName);
            var dataDirInfo = WorldExport.GetOrCreateDataDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName));
            var subScenePath = WorldExport.GetOrCreateSubSceneDirectoryFrom(rootAssembly.StagingDirectory.Combine(targetName)).ToString();
            var exportedSceneGuids = new HashSet<Hash128>();
            var catalogEntries = new List<CatalogEntry>();
            var originalActiveScene = SceneManager.GetActiveScene();

            blobAssetStore = new BlobAssetStore();

            s_AssemblyCache.BaseAssemblies = rootAssembly.RootAssembly.asset;
            s_AssemblyCache.PlatformName = profile.Target.UnityPlatformName;

            void ExportSceneToFile(Scene scene, Hash128 sceneGuid)
            {
                var exportDriver = new TinyExportDriver(config, dataDirInfo, null, blobAssetStore);
                exportDriver.SceneGUID = sceneGuid;
                exportDriver.BuildConfiguration = BuildContextInternals.GetBuildConfiguration(context);

                // Serialize all sections of the subscene
                List<ReferencedUnityObjects> unityObjects = new List<ReferencedUnityObjects>();
                var sections = EditorEntityScenes.ConvertAndWriteEntitySceneInternal(scene, exportDriver, unityObjects, new WriteEntitySceneSettings()
                {
                    Codec = Codec.LZ4,
                    OutputPath = subScenePath,
                    BuildAssemblyCache = s_AssemblyCache,
                    IsDotsRuntime = true
                });
                if (unityObjects.Count != 0)
                    throw new ArgumentException("We are serializing a world that contains UnityEngine.Object references which are not supported in Dots Runtime.");

                exportDriver.Write(manifest);

                WorldExport.UpdateManifest(manifest, scene.path, sceneGuid, sections, dataDirInfo, subScenePath);
            }

            try
            {
                foreach (var sceneInfo in buildScenes.GetSceneInfosForBuild())
                {
                    using (var loadedSceneScope = new LoadedSceneScope(sceneInfo.Path))
                    {
                        Hash128 sceneGuid = sceneInfo.Scene.assetGUID;
                        if (exportedSceneGuids.Contains(sceneGuid))
                            continue;

                        exportedSceneGuids.Add(sceneGuid);
                        catalogEntries.Add(new CatalogEntry()
                        {
                            Path = sceneInfo.Path,
                            MetaData = new ResourceMetaData()
                            {
                                ResourceFlags = sceneInfo.AutoLoad
                                    ? ResourceMetaData.Flags.AutoLoad
                                    : ResourceMetaData.Flags.None,
                                ResourceId = sceneGuid,
                                ResourceType = ResourceMetaData.Type.Scene
                            }
                        });

                        var rootScene = EditorSceneManager.OpenScene(sceneInfo.Path, OpenSceneMode.Additive);

                        // The root scene should be active to get the right rendering settings
                        SceneManager.SetActiveScene(rootScene);

                        ExportSceneToFile(rootScene, sceneGuid);

                        var subscenes = loadedSceneScope.ProjectScene.GetRootGameObjects()
                            .Select(go => go.GetComponent<SubScene>())
                            .Where(g => g != null && g);

                        foreach (var subScene in subscenes)
                        {
                            if (exportedSceneGuids.Contains(subScene.SceneGUID))
                                continue;

                            catalogEntries.Add(new CatalogEntry()
                            {
                                Path = subScene.EditableScenePath,
                                MetaData = new ResourceMetaData()
                                {
                                    ResourceFlags = ResourceMetaData.Flags.None,
                                    ResourceId = subScene.SceneGUID,
                                    ResourceType = ResourceMetaData.Type.Scene
                                }
                            });

                            bool wasSubSceneLoaded = subScene.EditingScene.isLoaded;
                            var scene = EditorSceneManager.OpenScene(subScene.EditableScenePath,
                                OpenSceneMode.Additive);
                            scene.isSubScene = true;
                            ExportSceneToFile(scene, subScene.SceneGUID);
                            if (!wasSubSceneLoaded)
                                EditorSceneManager.CloseScene(scene, true);
                        }
                    }
                }

                // TODO: We want to just add the written catalog file to the manifest but Platforms.Build currently
                // doesn't support non-classic BuildPipelines from doing so
                var finalOutputDirectory = BuildStepGenerateBeeFiles.GetFinalOutputDirectory(context, targetName);
                if (!Directory.Exists(finalOutputDirectory)) Directory.CreateDirectory(finalOutputDirectory);

                var catalogPath = Path.Combine(finalOutputDirectory, "Data");
                if (!Directory.Exists(catalogPath)) Directory.CreateDirectory(catalogPath);
                catalogPath = Path.Combine(catalogPath, SceneSystem.k_SceneInfoFileName);
                WriteCatalogFile(catalogPath, catalogEntries);

                blobAssetStore.Dispose();
            }
            catch (Exception e)
            {
                blobAssetStore.Dispose();
                return context.Failure($"Exception thrown during SubScene export: {e}");
            }
            finally
            {
                if(originalActiveScene.IsValid())
                    SceneManager.SetActiveScene(originalActiveScene);
            }

            return context.Success();
        }

        struct CatalogEntry
        {
            public string Path;
            public ResourceMetaData MetaData;
        }

        static void WriteCatalogFile(string sceneInfoPath, List<CatalogEntry> catalogEntries)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ResourceCatalogData>();
            var metas = builder.Allocate(ref root.resources, catalogEntries.Count);
            for (int i = 0; i < catalogEntries.Count; i++)
                metas[i] = catalogEntries[i].MetaData;

            var strings = builder.Allocate(ref root.paths, catalogEntries.Count);
            for (int i = 0; i < catalogEntries.Count; i++)
                builder.AllocateString(ref strings[i], catalogEntries[i].Path.ToLower());

            BlobAssetReference<ResourceCatalogData>.Write(builder, sceneInfoPath, ResourceCatalogData.CurrentFileFormatVersion);
            builder.Dispose();
        }
    }
}
