using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using Unity.Build.Editor;
using Unity.Entities.Conversion;
using UnityEditor;

namespace Unity.Entities.Runtime.Build
{
    static class MenuItemDotsRuntime
    {
        const string k_CreateBuildConfigurationAssetDotsRuntime = BuildConfigurationMenuItem.k_BuildConfigurationMenu + "DOTS Runtime Build Configuration";

        [MenuItem(k_CreateBuildConfigurationAssetDotsRuntime)]
        static void CreateBuildConfigurationAssetDotsRuntime()
        {
            var newAsset = BuildConfigurationMenuItem.CreateAssetInActiveDirectory("DotsRuntime", GetDefaultComponents());
            if (newAsset != null && newAsset)
                ProjectWindowUtil.ShowCreatedAsset(newAsset);
        }

        static IBuildComponent[] GetDefaultComponents()
        {
            return new IBuildComponent[]
            {
                new GeneralSettings(),
                new SceneList(),
                new ConversionSystemFilterSettings(),
                new DotsRuntimeBuildProfile()
            };
        }
    }
}
