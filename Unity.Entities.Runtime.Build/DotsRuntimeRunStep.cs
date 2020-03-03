using System.IO;
using Unity.Build;

namespace Unity.Entities.Runtime.Build
{
    internal sealed class DotsRuntimeRunStep : RunStep
    {
        public override bool CanRun(BuildConfiguration config, out string reason)
        {
            var artifact = BuildArtifacts.GetBuildArtifact<DotsRuntimeBuildArtifact>(config);
            if (artifact == null)
            {
                reason = $"Could not retrieve build artifact '{nameof(DotsRuntimeBuildArtifact)}'.";
                return false;
            }

            if (artifact.OutputTargetFile == null)
            {
                reason = $"{nameof(DotsRuntimeBuildArtifact.OutputTargetFile)} is null.";
                return false;
            }

            if (!File.Exists(artifact.OutputTargetFile.FullName))
            {
                reason = $"Output target file '{artifact.OutputTargetFile.FullName}' not found.";
                return false;
            }

            if (!config.TryGetComponent<DotsRuntimeBuildProfile>(out var profile))
            {
                reason = $"Could not retrieve component '{nameof(DotsRuntimeBuildProfile)}'.";
                return false;
            }

            if (profile.Target == null)
            {
                reason = $"{nameof(DotsRuntimeBuildProfile)} target is null.";
                return false;
            }

            reason = null;
            return true;
        }

        public override RunStepResult Start(BuildConfiguration config)
        {
            var artifact = BuildArtifacts.GetBuildArtifact<DotsRuntimeBuildArtifact>(config);
            var profile = config.GetComponent<DotsRuntimeBuildProfile>();

            if (!profile.Target.Run(artifact.OutputTargetFile))
            {
                return RunStepResult.Failure(config, this, $"Failed to start build target {profile.Target.DisplayName} at '{artifact.OutputTargetFile.FullName}'.");
            }

            //@TODO: BuildTarget.Run should return the process, so we can store it in DotsRuntimeRunInstance
            return RunStepResult.Success(config, this, new DotsRuntimeRunInstance());
        }
    }
}
