using System;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using BuildTarget = Unity.Build.DotsRuntime.BuildTarget;

namespace Unity.Entities.Runtime.Build
{
    sealed class DotsRuntimeBuildPipeline2 : DotsRuntimeBuildPipelineBase
    {
        public DotsRuntimeBuildPipeline2()
        {
        }

        public override Type[] UsedComponents
        {
            get
            {
                return m_UsedComponents.Concat(TargetUsedComponents).Distinct().ToArray();
            }
        }

        public override BuildStepCollection BuildSteps { get; } = new[]
        {
            typeof(BuildStepExportScenes),
            typeof(BuildStepExportConfiguration2),
            typeof(BuildStepGenerateBeeFiles),
            typeof(BuildStepRunBee)
        };

        // Todo: Remove when we commonize the pipelines
        Type[] m_UsedComponents = new[]
        {
            typeof(DotsRuntimeScriptingSettings),
            typeof(DotsRuntimeRootAssembly),
            typeof(SceneList),
            typeof(DotsRuntimeBuildProfile),
            typeof(OutputBuildDirectory),
            typeof(IDotsRuntimeBuildModifier),
        };

        protected override CleanResult OnClean(CleanContext context)
        {
            var artifacts = context.GetLastBuildArtifact<DotsRuntimeBuildArtifact>();
            if (artifacts == null)
                return context.Success();

            var buildDirectory = artifacts.OutputTargetFile.Directory;
            if (buildDirectory.Exists)
                buildDirectory.Delete(true);
            return context.Success();
        }

        protected override BuildResult OnBuild(BuildContext context)
        {
            return BuildSteps.Run(context);
        }

        protected override BoolResult OnCanRun(RunContext context)
        {
            if (!Target.CanRun)
            {
                return BoolResult.False("Run is not supported with current build settings");
            }

            var artifact = context.GetLastBuildArtifact<DotsRuntimeBuildArtifact>();
            if (artifact == null)
            {
                return BoolResult.False($"Could not retrieve build artifact '{nameof(DotsRuntimeBuildArtifact)}'.");
            }

            if (artifact.OutputTargetFile == null)
            {
                return BoolResult.False($"{nameof(DotsRuntimeBuildArtifact.OutputTargetFile)} is null.");
            }

            if (!File.Exists(artifact.OutputTargetFile.FullName))
            {
                return BoolResult.False($"Output target file '{artifact.OutputTargetFile.FullName}' not found.");
            }

            if (!context.TryGetComponent<DotsRuntimeBuildProfile>(out var profile))
            {
                return BoolResult.False($"Could not retrieve component '{nameof(DotsRuntimeBuildProfile)}'.");
            }

            if (profile.Target == null)
            {
                return BoolResult.False($"{nameof(DotsRuntimeBuildProfile)} target is null.");
            }

            return BoolResult.True();
        }

        protected override RunResult OnRun(RunContext context)
        {
            var artifact = context.GetLastBuildArtifact<DotsRuntimeBuildArtifact>();
            var profile = context.GetComponentOrDefault<DotsRuntimeBuildProfile>();

            if (!profile.Target.Run(artifact.OutputTargetFile))
            {
                return context.Failure($"Failed to start build target {profile.Target.DisplayName} at '{artifact.OutputTargetFile.FullName}'.");
            }

            //@TODO: BuildTarget.Run should return the process, so we can store it in DotsRuntimeRunInstance
            return context.Success(new DotsRuntimeRunInstance());
        }

        public override DirectoryInfo GetOutputBuildDirectory(BuildConfiguration config)
        {
            var artifact = BuildArtifacts.GetBuildArtifact<DotsRuntimeBuildArtifact>(config);
            return artifact.OutputTargetFile.Directory;
        }
    }
}