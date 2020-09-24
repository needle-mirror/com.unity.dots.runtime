using System;
using System.Collections.Generic;
using System.Linq;
using NiceIO;
using Bee.Core;
using Bee.CSharpSupport;
using Bee.VisualStudioSolution;
using Bee.DotNet;
using Bee.Stevedore;
using Bee.Tools;
using Bee.TundraBackend;
using Newtonsoft.Json.Linq;
using Bee.NativeProgramSupport;
using Bee.Core.Stevedore;

class BuildProgramBuildProgram
{
    static CSharpProgramConfiguration CSharpConfig { get; set; }

    static void Main(string[] path)
    {
        var bee = new NPath(typeof(CSharpProgram).Assembly.Location);

        Backend.Current.StevedoreSettings = new StevedoreSettings
        {
            // Manifest entries always override artifact IDs hard-coded in Bee
            // Setting EnforceManifest to true will also ensure no artifacts
            // are used without being listed in a manifest.
            EnforceManifest = true,
            Manifest =
            {
                bee.Parent.Combine("manifest.stevedore").ToString(),
            },
        };
        CSharpConfig = new CSharpProgramConfiguration(CSharpCodeGen.Release, null, DebugFormat.PortablePdb);

        var asmdefsFile = new NPath("asmdefs.json").MakeAbsolute();

        var asmDefInfo = JObject.Parse(asmdefsFile.ReadAllText());
        var asmDefs = asmDefInfo["asmdefs"].Value<JArray>();
        var asmRefs = asmDefInfo["asmrefs"].Value<JArray>();
        var asmDefToAsmRefs = new Dictionary<string, List<string>>();
        foreach (var asmref in asmRefs)
        {
            var target = asmref["AsmRefTarget"].Value<string>();
            if (target.StartsWith("GUID:"))
            {
                target = asmDefs.First(ad => ad["Guid"].Value<string>() == target.Substring(5))["AsmdefName"]
                    .Value<string>();
            }
            List<string> toaddto;
            if (asmDefToAsmRefs.TryGetValue(target, out var list))
            {
                toaddto = list;
            }
            else
            {
                toaddto = new List<string>(); 
                asmDefToAsmRefs[target] = toaddto;
            }
            toaddto.Add(asmref["FullPath"].Value<string>());
        }
          
        var buildProgram = new CSharpProgram()
        {
            Path = path[0],
            Sources = {
                bee.Parent.Combine("BuildProgramSources"),
                asmDefs.SelectMany(asmdef=>
                {
                    var apath = new NPath(asmdef["FullPath"].Value<string>());
                    var beeFiles = HarvestBeeFilesFrom(apath.Parent);
                    var asmdefName = asmdef["AsmdefName"].Value<string>();

                    if (asmDefToAsmRefs.TryGetValue(asmdefName, out var asmrefs))
                    {
                        foreach (var asmref in asmrefs)
                        {
                            beeFiles = beeFiles.Concat(HarvestBeeFilesFrom(asmref.ToNPath().Parent));
                        }
                    }

                    return beeFiles;
                }),
                HarvestBeeFilesFrom(bee.Parent.Parent.Combine("LowLevelSupport~","Unity.ZeroJobs"))
            },
            Framework = {Framework.Framework471},
            LanguageVersion = "7.2",
            References = { bee },
            ProjectFile = { Path = NPath.CurrentDirectory.Combine("buildprogram.gen.csproj")}
        };
        buildProgram.Defines.Add("DOTS_BUILD_PROGRAM");
        // This can be removed at some point in the future but it allowed me to not break
        // backwards compat and land things earlier in the burst repo 
        buildProgram.Defines.Add("NEW_BEE_NAMESPACES_REMOVE"); 

        Backend.Current.AddExtensionToBeScannedByHashInsteadOfTimeStamp("json", "config");
        
        buildProgram.ProjectFile.AddCustomLinkRoot(bee.Parent.Combine("BuildProgramSources"), ".");
        buildProgram.SetupSpecificConfiguration(CSharpConfig);

        var buildProgrambuildProgram = new CSharpProgram()
        {
            FileName = "buildprogrambuildprogram.exe",
            Sources = {
                bee.Parent.Combine("BuildProgramBuildProgramSources")
            },
            Defines = {"NEW_BEE_NAMESPACES_REMOVE"}, // see comment above
            LanguageVersion = "7.2",
            Framework = { Framework.Framework471},
            References = { bee }
        };
        buildProgrambuildProgram.SetupSpecificConfiguration(CSharpConfig);

        new VisualStudioSolution()
        {
            Path = "build.gen.sln",
            Projects = { buildProgram, buildProgrambuildProgram }
        }.Setup();
    }

    private static IEnumerable<NPath> HarvestBeeFilesFrom(NPath asmdefDirectory)
    {
        // Build program specific files
        var beeDir = asmdefDirectory.Combine("bee~");
        var beeFiles = !beeDir.DirectoryExists() ? Enumerable.Empty<NPath>() : beeDir.Files("*.cs", true);
        // files which required for both Build program and for Editor assemblies
        beeDir = asmdefDirectory.Combine("bee");
        return !beeDir.DirectoryExists() ? beeFiles : beeFiles.Concat(beeDir.Files("*.cs", true));
    }
}
