using System;
using System.Collections.Generic;
using System.Linq;
using Bee.Core;
using Bee.Stevedore;
using Bee.Toolchain.VisualStudio;
using JetBrains.Annotations;
using NiceIO;
using Unity.BuildSystem.NativeProgramSupport;

public static class NativeJobsPrebuiltLibrary
{
    private static Dictionary<String, NPath> ArtifactPaths = new Dictionary<String, NPath>();

    private static NPath GetOrCreateArtifactPath(String name) {
        if (!ArtifactPaths.ContainsKey(name))
        {
            var artifact = new StevedoreArtifact(name);
            Backend.Current.Register(artifact);
            ArtifactPaths[name] = artifact.Path;
        }

        return ArtifactPaths[name];
    }
    
    public static string BaselibArchitectureName(NativeProgramConfiguration npc)
    {
        switch (npc.Platform)
        {
            case MacOSXPlatform _:
                if (npc.ToolChain.Architecture.IsX64) return "mac64";
                break;
            case WindowsPlatform _:
                if (npc.ToolChain.Architecture.IsX86) return "win32";
                if (npc.ToolChain.Architecture.IsX64) return "win64";
                break;
            default:
                if (npc.ToolChain.Architecture.IsX86) return "x86";
                if (npc.ToolChain.Architecture.IsX64) return "x64";
                if (npc.ToolChain.Architecture.IsArmv7) return "arm32";
                if (npc.ToolChain.Architecture.IsArm64) return "arm64";
                if (npc.ToolChain.Architecture is WasmArchitecture) return "wasm";
                if (npc.ToolChain.Architecture is AsmJsArchitecture) return "asmjs";
                //if (npc.ToolChain.Architecture is WasmArchitecture && HAS_THREADING) return "wasm_withthreads";
                break;
        }

        throw new InvalidProgramException($"Unknown toolchain and architecture for baselib: {npc.ToolChain.LegacyPlatformIdentifier} {npc.ToolChain.Architecture.Name}");
    }

    public static void Add(NativeProgram np)
    {
        var allPlatforms = new []
        {
            "Android",
            "Linux",
            "Windows",
            "OSX",
            "IOS",
            "WebGL"
        };

        var staticPlatforms = new[]
        {
            "IOS",
            "WebGL",
        };

        np.IncludeDirectories.Add(GetOrCreateArtifactPath("nativejobs-all-public").Combine("Include"));

        DotsConfiguration DotsConfig(NativeProgramConfiguration npc) => ((DotsRuntimeNativeProgramConfiguration)npc).CSharpConfig.DotsConfiguration;

        foreach (var platform in allPlatforms)
        {
            var prebuiltLibPath = GetOrCreateArtifactPath($"nativejobs-{platform}" + (staticPlatforms.Contains(platform) ? "-s" : "-d"));

            np.PublicDefines.Add(c => c.Platform.Name == platform, "BASELIB_USE_DYNAMICLIBRARY=1");
            np.IncludeDirectories.Add(c => c.Platform.Name == platform, GetOrCreateArtifactPath($"nativejobs-{platform}-public").Combine("Platforms", platform, "Include"));

            switch (platform)
            {
                case "Windows":
                    np.Libraries.Add(c => c.Platform.Name == platform,
                        c => new PrecompiledLibrary[] { 
                            new MsvcDynamicLibrary(prebuiltLibPath.Combine("lib", platform.ToLower(), BaselibArchitectureName(c), DotsConfig(c).ToString().ToLower(), "nativejobs.dll")),
                            new StaticLibrary(prebuiltLibPath.Combine("lib", platform.ToLower(), BaselibArchitectureName(c), DotsConfig(c).ToString().ToLower(), "nativejobs.dll.lib")),
                        });
                    break;
                case "Linux":
                case "Android":
                    np.Libraries.Add(c => c.Platform.Name == platform,
                        c => new[] { new DynamicLibrary(prebuiltLibPath.Combine("lib", platform.ToLower(), BaselibArchitectureName(c), DotsConfig(c).ToString().ToLower(), "libnativejobs.so")) });
                    break;
                case "OSX":
                    np.Libraries.Add(c => c.Platform.Name == platform,
                        c => new[] { new DynamicLibrary(prebuiltLibPath.Combine("lib", platform.ToLower(), BaselibArchitectureName(c), DotsConfig(c).ToString().ToLower(), "libnativejobs.dylib")) });
                    break;
                case "IOS":
                    np.Libraries.Add(c => c.Platform.Name == platform,
                        c => new[] { new StaticLibrary(prebuiltLibPath.Combine("lib", platform.ToLower(), BaselibArchitectureName(c), DotsConfig(c).ToString().ToLower(), "libnativejobs.a")) });
                    np.PublicDefines.Add(c => c.Platform.Name == platform, "FORCE_PINVOKE_nativejobs_INTERNAL=1");
                    break;
                case "WebGL":
                    np.Libraries.Add(c => c.Platform.Name == platform,
                        c => new[] { new StaticLibrary(prebuiltLibPath.Combine("lib", platform.ToLower(), BaselibArchitectureName(c), DotsConfig(c).ToString().ToLower(), "libnativejobs.bc")) });
                    break;
            }
        }
    }
}
