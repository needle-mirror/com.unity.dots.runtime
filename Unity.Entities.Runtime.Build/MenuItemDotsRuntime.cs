using System.IO;
using Unity.Build.Common;
using Unity.Build.Editor;
using Unity.Entities.Conversion;
using UnityEditor;
using UnityEngine;
using BuildPipeline = Unity.Build.BuildPipeline;

namespace Unity.Entities.Runtime.Build
{
    static class MenuItemDotsRuntime
    {
        const string k_CreateBuildConfigurationAssetDotsRuntime = BuildConfigurationMenuItem.k_BuildConfigurationMenu + "DOTS Runtime Build Configuration";
        const string k_BuildPipelineDotsRuntimeAssetPath = "Packages/com.unity.dots.runtime/BuildPipelines/Default DOTS Runtime Pipeline.buildpipeline";

        [MenuItem(k_CreateBuildConfigurationAssetDotsRuntime, true)]
        static bool CreateBuildConfigurationAssetDotsRuntimeValidation()
        {
            return Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem(k_CreateBuildConfigurationAssetDotsRuntime)]
        static void CreateBuildConfigurationAssetDotsRuntime()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<BuildPipeline>(k_BuildPipelineDotsRuntimeAssetPath);
            Selection.activeObject = BuildConfigurationMenuItem.CreateAssetInActiveDirectory("DotsRuntime",
                new GeneralSettings(),
                new SceneList(),
                new ConversionSystemFilterSettings(),
                new DotsRuntimeBuildProfile
                {
#if UNITY_2020_1_OR_NEWER
                    Pipeline = new LazyLoadReference<BuildPipeline> { asset = pipeline }
#else
                    Pipeline = pipeline
#endif
                });
        }
    }
}
