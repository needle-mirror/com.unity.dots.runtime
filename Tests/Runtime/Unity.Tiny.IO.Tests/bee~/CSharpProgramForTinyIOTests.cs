using JetBrains.Annotations;
using NiceIO;
using Bee.NativeProgramSupport;

[UsedImplicitly]
class CustomizerForTinyIOTests : DotsRuntimeCSharpProgramCustomizer
{
    public override void Customize(DotsRuntimeCSharpProgram program)
    {
        if (program.MainSourcePath.FileName == "Unity.Tiny.IO.Tests")
        {
            NPath dataPath = program.MainSourcePath.Combine("Data");

            foreach (var filePath in dataPath.Files(true))
            {
                program.SupportFiles.Add(filePath);
            }
        }
    }
}
