using System;
using System.Collections.Generic;
using System.Linq;
using Bee.Core;
using Bee.Stevedore;
using Bee.Toolchain.VisualStudio;
using JetBrains.Annotations;
using NiceIO;
using Bee.NativeProgramSupport;
using Bee.Core.Stevedore;

public static class NativeJobsPrebuiltLibrary
{
    private static Dictionary<String, NPath> ArtifactPaths = new Dictionary<String, NPath>();
    private static bool UseLocalDev => Environment.GetEnvironmentVariable("NATIVEJOBS_FROM_LOCAL", EnvironmentVariableTarget.User) != null;
    private static NPath LocalDevRoot = BuildProgram.BeeRoot.Parent.Parent.Parent.Parent.Parent.Combine("nativejobs");

    private static NPath GetOrCreateSteveArtifactPath(String name)
    {
        if (!ArtifactPaths.ContainsKey(name))
        {
            var artifact = new StevedoreArtifact(name);
            ArtifactPaths[name] = artifact.Path;
        }

        return ArtifactPaths[name];
    }

    private static string GetNativeJobsConfigName(NativeProgramConfiguration npc)
    {
        var dotsrtCSharpConfig = ((DotsRuntimeNativeProgramConfiguration)npc).CSharpConfig;

        // If collection checks have been forced on in a release build, swap in the develop version of the native jobs prebuilt lib
        // as the release configuration will not contain the collection checks code paths.
        if (dotsrtCSharpConfig.EnableUnityCollectionsChecks && dotsrtCSharpConfig.DotsConfiguration == DotsConfiguration.Release)
            return DotsConfiguration.Develop.ToString().ToLower();

        // If building a debug build, make it a develop build so help with performance issues esp. regarding the Job Debugger.
        // Comment this out for an actual debug build if necessary.
        if (dotsrtCSharpConfig.DotsConfiguration == DotsConfiguration.Debug)
            return DotsConfiguration.Develop.ToString().ToLower();

        return dotsrtCSharpConfig.DotsConfiguration.ToString().ToLower();
    }

    private static string GetTargetName(NativeProgramConfiguration c)
    {
        // Look for this type from reference platforms package - if it doesn't exist we aren't building for web
        var tinyEmType = Type.GetType("TinyEmscripten");
        bool useWasmBackend = (bool)(tinyEmType?.GetProperty("UseWasmBackend")?.GetValue(tinyEmType) ?? false);
        bool useWebGlThreading = false;

        // Matches logic nativejobs/BuildUtils.bee.cs
        string targetName = c.ToolChain.Platform.DisplayName + "_" + c.ToolChain.Architecture.DisplayName;
        if (c.Platform.Name == "WebGL" && !useWasmBackend)
            targetName += "_fc";
        if (useWebGlThreading)
            targetName += "_withthreads";
        return targetName.ToLower();
    }

    private static NPath GetLibPath(NativeProgramConfiguration c)
    {
        var staticPlatforms = new[]
        {
            "IOS",
            "WebGL",
        };

        if (UseLocalDev)
        {
            return LocalDevRoot.Combine("artifacts", "nativejobs", GetNativeJobsConfigName(c) + "_" + GetTargetName(c) +
                "_nonlump" + (staticPlatforms.Contains(c.Platform.Name) ? "" : "_dll"));
        }

        var prebuiltLibPath = GetOrCreateSteveArtifactPath($"nativejobs-{c.Platform.Name}" + (staticPlatforms.Contains(c.Platform.Name) ? "-s" : "-d"));
        return prebuiltLibPath.Combine("lib", GetTargetName(c), GetNativeJobsConfigName(c));
    }

    public static void AddToNativeProgram(NativeProgram np)
    {
        np.PublicDefines.Add("BASELIB_USE_DYNAMICLIBRARY=1");
        np.PublicDefines.Add(c => c.Platform is IosPlatform, "FORCE_PINVOKE_nativejobs_INTERNAL=1");

        if (UseLocalDev)
        {
            np.IncludeDirectories.Add(LocalDevRoot.Combine("External", "baselib", "Include"));
            np.IncludeDirectories.Add(c => LocalDevRoot.Combine("External", "baselib", "Platforms", c.Platform.Name, "Include"));
        }
        else
        {
            np.IncludeDirectories.Add(GetOrCreateSteveArtifactPath("nativejobs-all-public").Combine("Include"));
            np.IncludeDirectories.Add(c => GetOrCreateSteveArtifactPath($"nativejobs-{c.Platform.Name}-public").Combine("Platforms", c.Platform.Name, "Include"));
        }

        np.Libraries.Add(c => c.Platform.Name == "Windows", c => new PrecompiledLibrary[] {
                new MsvcDynamicLibrary(GetLibPath(c).Combine("nativejobs.dll")),
                new StaticLibrary(GetLibPath(c).Combine("nativejobs.dll.lib")) });
        np.Libraries.Add(c => c.Platform.Name == "Linux" || c.Platform.Name == "Android", c => new[] {
                new DynamicLibrary(GetLibPath(c).Combine("libnativejobs.so")) });
        np.Libraries.Add(c => c.Platform.Name == "OSX", c => new[] {
                new DynamicLibrary(GetLibPath(c).Combine("libnativejobs.dylib")) });
        np.Libraries.Add(c => c.Platform.Name == "IOS", c => new[] {
                new StaticLibrary(GetLibPath(c).Combine("libnativejobs.a")) });
        np.Libraries.Add(c => c.Platform.Name == "WebGL", c => new[] {
                new StaticLibrary(GetLibPath(c).Combine("libnativejobs.bc")) });
    }

    public static void AddBindings(DotsRuntimeCSharpProgram csp, NPath defaultPath)
    {
        if (UseLocalDev)
        {
            csp.Sources.Add(LocalDevRoot.Combine("External", "baselib", "CSharp", "BindingsPinvoke"));
            csp.Sources.Add(LocalDevRoot.Combine("External", "baselib", "CSharp", "Error.cs"));
            csp.Sources.Add(LocalDevRoot.Combine("External", "baselib", "CSharp", "ManualBindings.cs"));
        }
        else
        {
            csp.Sources.Add(defaultPath);
        }
    }
}
