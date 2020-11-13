using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Runtime.Build
{
    internal class LoadedSceneScope : IDisposable
    {
        private bool m_sceneLoaded;

        public Scene ProjectScene { get; private set; }

        public LoadedSceneScope(string ident, bool isName = false)
        {
            LoadScene(isName ? ConversionUtils.GetScenePathForSceneWithName(ident) : ident);
        }

        public LoadedSceneScope(Hash128 hash)
        {
            LoadScene(AssetDatabase.GUIDToAssetPath(hash.ToString()));
        }

        void LoadScene(string path)
        {
            var projScene = SceneManager.GetSceneByPath(path);
            m_sceneLoaded = projScene.IsValid() && projScene.isLoaded;
            if (!m_sceneLoaded)
            {
                ProjectScene = EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            }
            else
            {
                ProjectScene = projScene;
            }
        }

        public void Dispose()
        {
            if (!m_sceneLoaded)
            {
                EditorSceneManager.CloseScene(ProjectScene, true);
            }
        }
    }
}
