using System;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using Unity.Build.Internals;
using Unity.Serialization.Json;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Name = "Generate Bee Files", Description = "Generating Bee Files", Category = "DOTS")]
    sealed class BuildStepGenerateBeeFiles : BuildStep
    {
        public static readonly int BuildSettingsFileVersion = 1;
        public override Type[] RequiredComponents => new[]
        {
            typeof(DotsRuntimeBuildProfile),
            typeof(DotsRuntimeRootAssembly)
        };

        public override Type[] OptionalComponents => new[]
        {
            typeof(OutputBuildDirectory),
            typeof(IDotsRuntimeBuildModifier)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            var manifest = context.BuildManifest;
            var profile = GetRequiredComponent<DotsRuntimeBuildProfile>(context);
            var rootAssembly = GetRequiredComponent<DotsRuntimeRootAssembly>(context);
            var outputDir = DotsRuntimeRootAssembly.BeeRootDirectory;

            BuildProgramDataFileWriter.WriteAll(outputDir.FullName);

            var jsonObject = new JsonObject();
            var config = BuildContextInternals.GetBuildConfiguration(context);
            var targetName = rootAssembly.MakeBeeTargetName(config);

            jsonObject["Version"] = BuildSettingsFileVersion;
            jsonObject["PlatformTargetIdentifier"] = profile.Target.BeeTargetName;
            jsonObject["RootAssembly"] = rootAssembly.RootAssembly.name;

            // Managed debugging is disabled by default. It can be enabled
            // using the IL2CPPSettings object, which implements IDotsRuntimeBuildModifier.
            // See the code below which reads that object.
            jsonObject["EnableManagedDebugging"] = BuildSettingToggle.UseBuildConfiguration.ToString();

            // EnableBurst is defaulted to true but can be configured via the DotsRuntimeBurstSettings object
            jsonObject["EnableBurst"] = true;

            // Scripting Settings defaults but can be overriden via the DotsRuntimeScriptingSettings object
            jsonObject["EnableMultithreading"] = false;
            jsonObject["EnableSafetyChecks"] = BuildSettingToggle.UseBuildConfiguration.ToString();
            jsonObject["EnableProfiler"] = BuildSettingToggle.UseBuildConfiguration.ToString();

            jsonObject["FinalOutputDirectory"] = GetFinalOutputDirectory(context, targetName, this);
            jsonObject["DotsConfig"] = profile.Configuration.ToString();

            foreach (var component in config.GetComponents<IDotsRuntimeBuildModifier>())
            {
                component.Modify(jsonObject);
            }

            var settingsDir = new NPath(outputDir.FullName).Combine("settings");
            var json = JsonSerialization.ToJson(jsonObject, new JsonSerializationParameters
            {
                DisableRootAdapters = true,
                SerializedType = typeof(JsonObject)
            });
            settingsDir.Combine($"{targetName}.json").UpdateAllText(json);

            var file = rootAssembly.StagingDirectory.Combine(targetName).GetFile("export.manifest");
            file.UpdateAllLines(manifest.ExportedFiles.Select(x => x.FullName).ToArray());

            profile.Target.WriteBeeConfigFile(DotsRuntimeRootAssembly.BeeRootDirectory.ToString());

            return Success();
        }

        public static string GetFinalOutputDirectory(BuildContext context, string beeTargetName, BuildStep step = null)
        {
            if (step != null && step.HasOptionalComponent<OutputBuildDirectory>(context))
            {
                return step.GetOptionalComponent<OutputBuildDirectory>(context).OutputDirectory;
            }
            return $"Builds/{beeTargetName}";
        }
    }
}
