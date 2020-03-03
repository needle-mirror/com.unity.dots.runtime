/*
 * 11/15/2019
 * We are temporarily using Json.NET while we wait for the new com.unity.serialization package release,
 * which will offer similar functionality.
 */
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using Unity.Build.Internals;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Name = "Generate Bee Files", Description = "Generating Bee Files", Category = "DOTS")]
    sealed class BuildStepGenerateBeeFiles : BuildStep
    {
        public override Type[] RequiredComponents => new[]
        {
            typeof(DotsRuntimeBuildProfile)
        };

        public override Type[] OptionalComponents => new[]
        {
            typeof(OutputBuildDirectory),
            typeof(DotsRuntimeScriptingDefines),
            typeof(IDotsRuntimeBuildModifier)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            var manifest = context.BuildManifest;
            var profile = GetRequiredComponent<DotsRuntimeBuildProfile>(context);
            var outputDir = profile.BeeRootDirectory;

            var buildConfigurationJObject = new JObject();

            BuildProgramDataFileWriter.WriteAll(outputDir.FullName);

            if (HasOptionalComponent<DotsRuntimeScriptingDefines>(context))
                buildConfigurationJObject["ScriptingDefines"] = new JArray(GetOptionalComponent<DotsRuntimeScriptingDefines>(context).ScriptingDefines);

            buildConfigurationJObject["PlatformTargetIdentifier"] = profile.Target.BeeTargetName;
            buildConfigurationJObject["UseBurst"] = profile.EnableBurst;
            buildConfigurationJObject["EnableManagedDebugging"] = profile.EnableManagedDebugging;
            buildConfigurationJObject["RootAssembly"] = profile.RootAssembly.name;
            buildConfigurationJObject["EnableMultiThreading"] = profile.EnableMultiThreading;
            buildConfigurationJObject["FinalOutputDirectory"] = this.GetOutputBuildDirectory(context);
            buildConfigurationJObject["DotsConfig"] = profile.Configuration.ToString();

            var config = BuildContextInternals.GetBuildConfiguration(context);
            //web is broken until we can get all components that modify a particular interface
            foreach (var component in config.GetComponents<IDotsRuntimeBuildModifier>())
            {
                component.Modify(buildConfigurationJObject);
            }

            var settingsDir = new NPath(outputDir.FullName).Combine("settings");
            settingsDir.Combine($"{config.name}.json")
                .UpdateAllText(buildConfigurationJObject.ToString());

            if (profile.ShouldWriteDataFiles)
            {
                var file = profile.StagingDirectory.Combine(config.name).GetFile("export.manifest");
                file.UpdateAllLines(manifest.ExportedFiles.Select(x => x.FullName).ToArray());
            }

            profile.Target.WriteBeeConfigFile(profile.BeeRootDirectory.ToString());

            return Success();
        }
    }
}
