using System;
using System.IO;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.Internals;

namespace Unity.Entities.Runtime.Build
{
    [BuildStep(Name = "Run Bee", Description = "Running Bee", Category = "DOTS")]
    sealed class BuildStepRunBee : BuildStep
    {
        public override Type[] RequiredComponents => new[]
        {
            typeof(DotsRuntimeBuildProfile)
        };

        public override Type[] OptionalComponents => new[]
        {
            typeof(OutputBuildDirectory)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            var arguments = BuildContextInternals.GetBuildConfiguration(context).name;
            var profile = GetRequiredComponent<DotsRuntimeBuildProfile>(context);
            var workingDir = profile.BeeRootDirectory;
            var outputDir = new DirectoryInfo(this.GetOutputBuildDirectory(context));

            var result = BeeTools.Run(arguments, workingDir, context.BuildProgress);
            outputDir.Combine("Logs").GetFile("BuildLog.txt").WriteAllText(result.Output);
            workingDir.GetFile("runbuild" + ShellScriptExtension()).UpdateAllText(result.Command);

            if (result.Failed)
            {
                return Failure(result.Error);
            }

            if (!string.IsNullOrEmpty(profile.ProjectName))
            {
                var outputTargetFile = outputDir.GetFile(profile.ProjectName + profile.Target.ExecutableExtension);
                context.SetValue(new DotsRuntimeBuildArtifact { OutputTargetFile = outputTargetFile });
            }

            return Success();
        }

        string ShellScriptExtension()
        {
#if UNITY_EDITOR_WIN
            return ".bat";
#else
            return ".sh";
#endif
        }
    }
}
