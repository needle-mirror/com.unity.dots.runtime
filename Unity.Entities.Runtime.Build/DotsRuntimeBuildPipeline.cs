using System.IO;
using Unity.Build;

namespace Unity.Entities.Runtime.Build
{
    sealed class DotsRuntimeBuildPipeline : BuildPipelineBase
    {
        public override BuildStepCollection BuildSteps { get; } = new[]
        {
            typeof(BuildStepExportEntities),
            typeof(BuildStepExportConfiguration),
            typeof(BuildStepGenerateBeeFiles),
            typeof(BuildStepRunBee)
        };

        protected override BuildResult OnBuild(BuildContext context)
        {
            return BuildSteps.Run(context);
        }

        protected override BoolResult OnCanRun(RunContext context)
        {
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
    }
}
