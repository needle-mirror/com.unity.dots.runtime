
using Unity.Build.DotsRuntime;

namespace Unity.Entities.Runtime.Build
{
    /// <summary>
    /// Selects the right pipeline based on UseNewPipeline
    /// Also, re-creates it if the target has been changed
    /// </summary>
    class DotsRuntimeBuildPipelineSelector : DotsRuntimeBuildPipelineSelectorBase
    {
        public override DotsRuntimeBuildPipelineBase SelectFor(DotsRuntimeBuildPipelineBase basePipeline, BuildTarget target, bool useNewPipeline)
        {
            if (target != (basePipeline as DotsRuntimeBuildPipeline)?.Target)
            {
                var pipeline = new DotsRuntimeBuildPipeline();
                pipeline.Target = target;
                return pipeline;
            }

            return basePipeline;
        }
    }
}
