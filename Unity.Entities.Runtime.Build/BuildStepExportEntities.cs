using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.Internals;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Name = "Export Entities", Description = "Exporting Entities", Category = "DOTS")]
    sealed class BuildStepExportEntities : BuildStep
    {
        public BlobAssetStore m_BlobAssetStore;

        public override Type[] RequiredComponents => new[]
        {
            typeof(DotsRuntimeBuildProfile),
            typeof(SceneList)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            var manifest = context.BuildManifest;
            var profile = GetRequiredComponent<DotsRuntimeBuildProfile>(context);
            var buildScenes = GetRequiredComponent<SceneList>(context);

            var exportedSceneGuids = new HashSet<Guid>();

            var originalActiveScene = SceneManager.GetActiveScene();

#if USE_INCREMENTAL_CONVERSION
            m_BlobAssetStore = new BlobAssetStore();
#endif

            void ExportSceneToFile(Scene scene, Guid guid)
            {
                var config = BuildContextInternals.GetBuildConfiguration(context);
                var dataDirectory = profile.StagingDirectory.Combine(config.name).Combine("Data");
                var outputFile = dataDirectory.GetFile(guid.ToString("N"));
                using (var exportWorld = new World("Export World"))
                {
#if USE_INCREMENTAL_CONVERSION
                    var exportDriver = new TinyExportDriver(context, dataDirectory, exportWorld, m_BlobAssetStore);
#else
                    var exportDriver = new TinyExportDriver(context, profile.DataDirectory);
#endif
                    exportDriver.DestinationWorld = exportWorld;
                    exportDriver.SceneGUID = new Hash128(guid.ToString("N"));

                    SceneManager.SetActiveScene(scene);

                    GameObjectConversionUtility.ConvertScene(scene, exportDriver);
                    context.GetOrCreateValue<WorldExportTypeTracker>()?.AddTypesFromWorld(exportWorld);

                    WorldExport.WriteWorldToFile(exportWorld, outputFile);
                    exportDriver.Write(manifest);
                }

                manifest.Add(guid, scene.path, outputFile.ToSingleEnumerable());
            }

            foreach (var rootScenePath in buildScenes.GetScenePathsForBuild())
            {
                using (var loadedSceneScope = new LoadedSceneScope(rootScenePath))
                {
                    var thisSceneSubScenes = loadedSceneScope.ProjectScene.GetRootGameObjects()
                        .Select(go => go.GetComponent<SubScene>())
                        .Where(g => g != null && g);

                    foreach (var subScene in thisSceneSubScenes)
                    {
                        var guid = new Guid(subScene.SceneGUID.ToString());
                        if (exportedSceneGuids.Contains(guid))
                            continue;

                        var isLoaded = subScene.IsLoaded;
                        if (!isLoaded)
                            SubSceneInspectorUtility.EditScene(subScene);

                        var scene = subScene.EditingScene;
                        var sceneGuid = subScene.SceneGUID;

                        ExportSceneToFile(scene, guid);

                        if (!isLoaded)
                            SubSceneInspectorUtility.CloseSceneWithoutSaving(subScene);
                    }
                }
            }

            SceneManager.SetActiveScene(originalActiveScene);

#if USE_INCREMENTAL_CONVERSION
            m_BlobAssetStore.Dispose();
#endif

            return Success();
        }
    }
}
