using System;
using System.Linq;
using Unity.Build;
using Unity.Build.DotsRuntime;
using Unity.Entities;
using Unity.Entities.Runtime;
using Unity.Entities.Runtime.Build;

namespace Unity.Tiny.Authoring
{
    [DisableAutoCreation]
    public class ConfigurationSystem : ConfigurationSystemBase
    {
        public override Type[] UsedComponents { get; } =
        {
            typeof(DotsRuntimeBuildProfile)
        };

        protected override void OnUpdate()
        {
            Entity configEntity;
            configEntity = EntityManager.CreateEntity();
            EntityManager.AddComponent<ConfigurationTag>(configEntity);
        }
    }
}
