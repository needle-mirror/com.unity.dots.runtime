using System;
using System.Collections.Generic;
using Unity.Build;
using Unity.Build.DotsRuntime;
using Unity.Properties;
using Unity.Serialization.Json;
using UnityEngine;
using UnityEngine.Serialization;

namespace Unity.Entities.Runtime.Build
{
    internal sealed class DotsRuntimeScriptingSettings : IDotsRuntimeBuildModifier
    {
        [CreateProperty, HideInInspector]
        public bool EnableMultithreading { get; set; } = false;

        [CreateProperty]
        [Tooltip("Controls if safety checks are enabled. UseBuildConfiguration: Debug and Develop builds will have safety checks enabled.")]
        public BuildSettingToggle EnableSafetyChecks = BuildSettingToggle.UseBuildConfiguration;

        [CreateProperty]
        [Tooltip("Controls if profiler is enabled. UseBuildConfiguration: Debug and Develop builds will have profiler enabled.")]
        public BuildSettingToggle EnableProfiler = BuildSettingToggle.UseBuildConfiguration;

        [CreateProperty]
        public List<string> ScriptingDefines = new List<string>();

        public void Modify(JsonObject jsonObject)
        {
            jsonObject["EnableSafetyChecks"] = EnableSafetyChecks.ToString();
            jsonObject["EnableProfiler"] = EnableProfiler.ToString();
            jsonObject["EnableMultithreading"] = EnableMultithreading;
            jsonObject["ScriptingDefines"] = ScriptingDefines;
        }
    }
}