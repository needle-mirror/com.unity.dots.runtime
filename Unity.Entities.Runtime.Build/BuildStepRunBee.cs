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
            typeof(DotsRuntimeBuildProfile),
            typeof(DotsRuntimeRootAssembly)
        };

        public override Type[] OptionalComponents => new[]
        {
            typeof(OutputBuildDirectory)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            var config = BuildContextInternals.GetBuildConfiguration(context);
            var profile = GetRequiredComponent<DotsRuntimeBuildProfile>(context);
            var rootAssembly = GetRequiredComponent<DotsRuntimeRootAssembly>(context);
            var targetName = rootAssembly.MakeBeeTargetName(config);
            var workingDir = DotsRuntimeRootAssembly.BeeRootDirectory;
            var outputDir = new DirectoryInfo(BuildStepGenerateBeeFiles.GetFinalOutputDirectory(context, targetName));

            var result = BeeTools.Run(targetName, workingDir, context.BuildProgress);
            outputDir.Combine("Logs").GetFile("BuildLog.txt").WriteAllText(result.Output);
            workingDir.GetFile("runbuild" + ShellScriptExtension()).UpdateAllText(result.Command);

            if (result.Failed)
            {
                return Failure(result.Error);
            }

            if (!string.IsNullOrEmpty(rootAssembly.ProjectName))
            {
                var outputTargetFile = outputDir.GetFile(rootAssembly.ProjectName + profile.Target.ExecutableExtension);
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
