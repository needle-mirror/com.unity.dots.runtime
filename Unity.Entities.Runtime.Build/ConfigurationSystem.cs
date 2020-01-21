#if UNITY_TINY
using Unity.Tiny;
#endif
using UnityEditor;
using System.Linq;

namespace Unity.Entities.Runtime.Build
{
    public class ConfigurationSystem : ConfigurationSystemBase
    {
        protected override void OnUpdate()
        {
            if (projectScene == null || !projectScene.IsValid())
                return;

            Entity configEntity;
            configEntity = EntityManager.CreateEntity();
#if UNITY_TINY
            //TODO: DisplayInfo needs to be set from a project/build settings
            EntityManager.AddComponentData(configEntity, DisplayInfo.Default);
            EntityManager.AddComponent<ConfigurationTag>(configEntity);
#endif
            var startupScenes = EntityManager.AddBuffer<StartupScenes>(configEntity);

            var subScenes = projectScene.GetRootGameObjects()
                       .Select(go => go.GetComponent<Unity.Scenes.SubScene>())
                       .Where(g => g != null && g);

            // Add this root scene to StartupScenes
            var projSceneGuid = new GUID(AssetDatabase.AssetPathToGUID(projectScene.path));
#if false
                    startupScenes.Add(new StartupScenes()
                    {
                        SceneReference = new Unity.Tiny.Scenes.SceneReference()
                            {SceneGuid = new System.Guid(projSceneGuid.ToString())}
                    });
#endif
            // Add all our subscenes with AutoLoadScene to StartupScenes
            // (technically not necessary?)
            var subSceneGuids = subScenes
                .Where(s => s != null && s.SceneAsset != null && s.AutoLoadScene)
                .Select(s => new System.Guid(s.SceneGUID.ToString()));
            foreach (var guid in subSceneGuids)
                startupScenes.Add(new StartupScenes()
                { SceneReference = new SceneReference() { SceneGuid = guid } });
        }
    }
}