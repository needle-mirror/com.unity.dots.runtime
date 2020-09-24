using System;
using System.Linq;
using System.Net;
using Bee.DotNet;
using Bee.Toolchain.Extension;
using NiceIO;
using Bee.CSharpSupport;

public class AsmDefCSharpProgram : DotsRuntimeCSharpProgram
{
    public AsmDefDescription AsmDefDescription { get; }

    // Not all packages explicitly opt-out of Tiny BCL by putting a defineConstraint: ["!NET_DOTS"] in their asmdef
    // so we explicitly filter them here until a proper fix has landed
    string[] IncompatibleTinyBCLAsmDefs =
    {
        "Unity.PerformanceTesting.dll"
    };

    public AsmDefCSharpProgram(AsmDefDescription asmDefDescription)
        : base(asmDefDescription.Directory,
            asmDefDescription.IncludedAsmRefs.Select(asmref => asmref.Path.Parent),
            deferConstruction: true)
    {
        AsmDefDescription = asmDefDescription;

        var asmDefReferences = AsmDefDescription.References.Select(asmDefDescription1 => BuildProgram.GetOrMakeDotsRuntimeCSharpProgramFor(asmDefDescription1)).ToList();
        var isExe = asmDefDescription.DefineConstraints.Contains("UNITY_DOTS_ENTRYPOINT") || asmDefDescription.Name.EndsWith(".Tests");

        Construct(asmDefDescription.Name, isExe);

        ProjectFile.AdditionalFiles.Add(asmDefDescription.Path);
        
        IncludePlatforms = AsmDefDescription.IncludePlatforms;
        ExcludePlatforms = AsmDefDescription.ExcludePlatforms;
        Unsafe = AsmDefDescription.AllowUnsafeCode;
        References.Add(config =>
        {
            if (config is DotsRuntimeCSharpProgramConfiguration dotsConfig)
            {
                if(dotsConfig.TargetFramework == TargetFramework.Tiny)
                    return asmDefReferences.Where(rp => rp.IsSupportedFor(dotsConfig) && !IncompatibleTinyBCLAsmDefs.Contains(rp.FileName));
                else
                    return asmDefReferences.Where(rp => rp.IsSupportedFor(dotsConfig));
            }

            //this codepath will be hit for the bindgem invocation
            return asmDefReferences;
        });

        if (AsmDefDescription.IsTinyRoot || isExe)
        {
            AsmDefCSharpProgramCustomizer.RunAllAddPlatformImplementationReferences(this);
        }

        if (BuildProgram.UnityTinyBurst != null)
            References.Add(BuildProgram.UnityTinyBurst);
        if (BuildProgram.ZeroJobs != null)
            References.Add(BuildProgram.ZeroJobs);
        if (BuildProgram.UnityLowLevel != null)
            References.Add(BuildProgram.UnityLowLevel);
        if (BuildProgram.TinyIO != null)
            References.Add(BuildProgram.TinyIO);

        if (IsTestAssembly)
        {
            var nunitLiteMain = BuildProgram.BeeRoot.Combine("CSharpSupport/NUnitLiteMain.cs");
            Sources.Add(nunitLiteMain);

            // Setup for IL2CPP
            var tinyTestFramework = BuildProgram.BeeRoot.Parent.Combine("TinyTestFramework");
            Sources.Add(c => ((DotsRuntimeCSharpProgramConfiguration)c).ScriptingBackend == ScriptingBackend.TinyIl2cpp || ((DotsRuntimeCSharpProgramConfiguration)c).TargetFramework == TargetFramework.Tiny, tinyTestFramework);
            Defines.Add(c => ((DotsRuntimeCSharpProgramConfiguration)c).ScriptingBackend == ScriptingBackend.TinyIl2cpp || ((DotsRuntimeCSharpProgramConfiguration)c).TargetFramework == TargetFramework.Tiny, "UNITY_PORTABLE_TEST_RUNNER");

            // Setup for dotnet
            References.Add(c => ((DotsRuntimeCSharpProgramConfiguration)c).ScriptingBackend == ScriptingBackend.Dotnet && ((DotsRuntimeCSharpProgramConfiguration)c).TargetFramework != TargetFramework.Tiny, BuildProgram.NUnitFramework);
            ProjectFile.AddCustomLinkRoot(nunitLiteMain.Parent, "TestRunner");
            References.Add(c => ((DotsRuntimeCSharpProgramConfiguration)c).ScriptingBackend == ScriptingBackend.Dotnet && ((DotsRuntimeCSharpProgramConfiguration)c).TargetFramework != TargetFramework.Tiny, BuildProgram.NUnitLite);

            // General setup
            References.Add(BuildProgram.GetOrMakeDotsRuntimeCSharpProgramFor(AsmDefConfigFile.AsmDefDescriptionFor("Unity.Entities")));
            References.Add(BuildProgram.GetOrMakeDotsRuntimeCSharpProgramFor(AsmDefConfigFile.AsmDefDescriptionFor("Unity.Tiny.Core")));
            References.Add(BuildProgram.GetOrMakeDotsRuntimeCSharpProgramFor(AsmDefConfigFile.AsmDefDescriptionFor("Unity.Tiny.UnityInstance")));
            References.Add(BuildProgram.GetOrMakeDotsRuntimeCSharpProgramFor(AsmDefConfigFile.AsmDefDescriptionFor("Unity.Collections")));
        }
        else if(IsILPostProcessorAssembly)
        {
            References.Add(BuildProgram.UnityCompilationPipeline);
            References.Add(MonoCecil.Paths);
            References.Add(Il2Cpp.Distribution.Path.Combine("build/deploy/net471/Unity.Cecil.Awesome.dll"));
        }
    }

    public override bool IsSupportedFor(CSharpProgramConfiguration config)
    {
        //UNITY_DOTS_ENTRYPOINT is actually a fake define constraint we use to signal the buildsystem,
        // so don't impose it as a constraint

        return DefineConstraintsHelper.IsDefineConstraintsCompatible(Defines.For(config).Append("UNITY_DOTS_ENTRYPOINT").ToArray(), AsmDefDescription.DefineConstraints)
            && base.IsSupportedFor(config);
    }

    protected override TargetFramework GetTargetFramework(CSharpProgramConfiguration config, DotsRuntimeCSharpProgram program)
    {
        if (IsILPostProcessorAssembly)
            return TargetFramework.NetStandard20;

        return base.GetTargetFramework(config, program);
    }

    public void AddPlatformImplementationFor(string baseFeatureAsmDefName, string platformImplAsmDefName)
    {
        if (AsmDefDescription.Name == platformImplAsmDefName)
            return;

        if (AsmDefDescription.References.Any(r => r.Name == baseFeatureAsmDefName))
        {
            var impl = AsmDefConfigFile.CSharpProgramFor(platformImplAsmDefName);
            if (impl == null)
            {
                Console.WriteLine($"Missing assembly for {platformImplAsmDefName}, named in a customizer for {baseFeatureAsmDefName}.  Are you missing a package, or is the customizer in the wrong place?");
                return;
            }
            References.Add(c => impl.IsSupportedFor(c), impl);
        }
    }

    protected override NPath DeterminePathForProjectFile() =>
        DoesPackageSourceIndicateUserHasControlOverSource(AsmDefDescription.PackageSource)
            ? AsmDefDescription.Path.Parent.Combine(AsmDefDescription.Name + ".gen.csproj")
            : base.DeterminePathForProjectFile();

    public bool IsTestAssembly => AsmDefDescription.Name.Contains(".Tests");
    public bool IsILPostProcessorAssembly => AsmDefDescription.Name.EndsWith(".CodeGen");
}
