using DotsBuildTargets;
using Newtonsoft.Json.Linq;
using NiceIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bee.Core;
using Unity.BuildSystem.CSharpSupport;
using Unity.BuildSystem.NativeProgramSupport;

public static class DotsConfigs
{
    private static Dictionary<string, List<DotsRuntimeCSharpProgramConfiguration>> PerConfigBuildSettings =
        new Dictionary<string, List<DotsRuntimeCSharpProgramConfiguration>>();

    public static Dictionary<string, List<DotsRuntimeCSharpProgramConfiguration>> MakeConfigs()
    {
        var platformList = DotsBuildSystemTargets;

        var settingsDir = new NPath("settings");

        if (settingsDir.Exists())
        {
            foreach (var settingsRelative in settingsDir.Files("*.json"))
            {
                var settingsFile = settingsRelative.MakeAbsolute();
                if (settingsFile.Exists())
                {
                    Backend.Current.RegisterFileInfluencingGraph(settingsFile);
                    var settingsObject = new FriendlyJObject {Content = JObject.Parse(settingsFile.ReadAllText())};

                    if (settingsObject.GetInt("Version", 0) != AsmDefConfigFile.BuildSettingsFileVersion ||
                        AsmDefConfigFile.AsmDefDescriptionFor(settingsObject.GetString("RootAssembly")) == null)
                    {
                        Console.WriteLine($"Found old settings file '{settingsFile}', removing...");
                        settingsFile.Delete();
                        continue;
                    }

                    var id = settingsObject.GetString("PlatformTargetIdentifier");

                    var target = platformList.Single(t => t.Identifier == id);

                    if (!target.ToolChain.CanBuild)
                        continue;

                    // Need to know this prior to determining need for burst
                    var mdb = ShouldEnableDevelopmentOptionForSetting("EnableManagedDebugging",
                        new[] { DotsConfiguration.Debug }, settingsObject);
                    if (target.Identifier == "asmjs")
                        mdb = false;

                    var multithreading = settingsObject.GetBool("EnableMultithreading");
                    var targetUsesBurst = settingsObject.GetBool("EnableBurst");
                    if (!targetUsesBurst && multithreading)
                    {
                        // We currently do not support multithreaded debugging anywhere except .NET Framework and CoreCLR (so, Windows only)
                        // or il2cpp full profile
                        bool isFullIl2cpp = target.ScriptingBackend == ScriptingBackend.TinyIl2cpp && mdb;
                        bool isWindows = target.ToolChain.Platform is WindowsPlatform;
                        if (!(isFullIl2cpp || isWindows))
                        {
                            Console.WriteLine($"Warning: BuildConfiguration '{settingsFile.FileNameWithoutExtension}' " +
                                $"specified 'EnableBurst=False', but 'Multithreading=True'. Multithreading requires Burst, therefore enabling Burst.");
                            targetUsesBurst = true;
                        }
                    }

                    var enableProfiler = ShouldEnableDevelopmentOptionForSetting("EnableProfiler", new [] {DotsConfiguration.Develop}, settingsObject);

                    var dotsCfg = DotsConfigForSettings(settingsObject, out var codegen);
                    var enableUnityCollectionsChecks = ShouldEnableDevelopmentOptionForSetting("EnableSafetyChecks",
                        new[] {DotsConfiguration.Debug, DotsConfiguration.Develop}, settingsObject);

                    if (!target.CanUseBurst && targetUsesBurst)
                    {
                        Console.WriteLine($"Warning: BuildConfiguration '{settingsFile.FileNameWithoutExtension}' " +
                            $"specified 'EnableBurst', but target ({target.Identifier}) does not support burst yet. Not using burst.");
                        targetUsesBurst = false;
                    }

                    // Workaround to disable burst in web debug builds since it will fail to compile
                    // https://unity3d.atlassian.net/browse/DOTSR-1886
                    if (targetUsesBurst && target is DotsWebTarget && codegen == CSharpCodeGen.Debug)
                    {
                        Console.WriteLine($"Warning: Web currently does not support building in debug configuration with Burst. Disabling burst....");
                        targetUsesBurst = false;
                    }

                    var waitForManagedDebugger = settingsObject.GetBool("WaitForManagedDebugger");

                    var rootAssembly = settingsObject.GetString("RootAssembly");
                    string finalOutputDir = null;
                    if (settingsObject.Content.TryGetValue("FinalOutputDirectory", out var finalOutputToken))
                        finalOutputDir = finalOutputToken.Value<string>();

                    var defines = new List<string>();
                    if (settingsObject.Content.TryGetValue("ScriptingDefines", out var definesJToken))
                        defines = ((JArray) definesJToken).Select(token => token.Value<string>()).ToList();

                    if (!PerConfigBuildSettings.ContainsKey(rootAssembly))
                        PerConfigBuildSettings[rootAssembly] = new List<DotsRuntimeCSharpProgramConfiguration>();

                    var identifier = settingsFile.FileNameWithoutExtension;
                    do {
                        PerConfigBuildSettings[rootAssembly]
                            .Add(
                                target.CustomizeConfigForSettings(new DotsRuntimeCSharpProgramConfiguration(
                                    csharpCodegen: codegen,
                                    cppCodegen: codegen == CSharpCodeGen.Debug ? CodeGen.Debug : CodeGen.Release,
                                    nativeToolchain: target.ToolChain,
                                    scriptingBackend: target.ScriptingBackend,
                                    targetFramework: target.TargetFramework,
                                    identifier: identifier,
                                    enableUnityCollectionsChecks: enableUnityCollectionsChecks,
                                    enableManagedDebugging: mdb,
                                    waitForManagedDebugger: waitForManagedDebugger,
                                    multiThreadedJobs: multithreading,
                                    dotsConfiguration: dotsCfg,
                                    enableProfiler: enableProfiler,
                                    useBurst: targetUsesBurst,
                                    defines: defines,
                                    finalOutputDirectory: finalOutputDir),
                                    settingsObject));

                        //We have to introduce the concept of "complementary targets" to accommodate building "fat" binaries on Android,
                        //for both armv7 and arm64 architectures. This means we have to build two copies of the final binaries, and then do one
                        //packaging step to package both steps into a single apk. But also, since C# code is allowed to do #if UNITY_DOTSRUNTIME64
                        //(and indeed we need that ability for the static type registry to generate correct sizes of things), we need to compile
                        //two sets of c# assemblies, one per architecture, and run il2cpp and burst on each one separately, in order to produce
                        //these two sets of final binaries.
                        //For that purpose, we associate a second DotsRuntimeCSharpProgramConfiguration (known as the complementary target) to
                        //specify the build for the other architecture we wish to compile against, and we do all the building steps up to the final
                        //packaging step for that config as well as the main one. We skip the final packaging step for the complementary config,
                        //and make the packaging step for the main config consume both sets of binaries.
                        //This is a crazy scheme, and we theorize and hope that when we adopt the incremental buildpipeline, we will be able
                        //to use their concept of per-platform custom graph build steps to make this be more reasonable.
                        target = target.ComplementaryTarget;

                        //We use "identifier" as a directory name for all intermediate files.
                        //For complementary target these files will be generated in "target.Identifier" subdirectory.
                        //So for example in case of arm64 complemenary target a path would be
                        //"{target.Identifiler}/android_complementary_arm64".
                        if (target != null)
                        {
                            identifier += "/" + target.Identifier;
                        }
                    } while (target != null);
                }
            }
        }

        return PerConfigBuildSettings;
    }

    public static DotsConfiguration DotsConfigForSettings(FriendlyJObject settingsObject, out CSharpCodeGen codegen)
    {
        DotsConfiguration dotsCfg;
        var codegenString = settingsObject.GetString("DotsConfig");
        switch (codegenString)
        {
            case "Debug":
                codegen = CSharpCodeGen.Debug;
                dotsCfg = DotsConfiguration.Debug;
                break;
            case "Develop":
                codegen = CSharpCodeGen.Release;
                dotsCfg = DotsConfiguration.Develop;
                break;
            case "Release":
                codegen = CSharpCodeGen.Release;
                dotsCfg = DotsConfiguration.Release;
                break;
            default:
                throw new ArgumentException(
                    $"Error: Unrecognized codegen {codegenString} in build json file. This is a bug.");
        }

        return dotsCfg;
    }

    public static bool ShouldEnableDevelopmentOptionForSetting(string optionName, DotsConfiguration[] enabledByDefaultForConfigurations, FriendlyJObject settingsObject)
    {
        var optionString = settingsObject.GetString(optionName);
        if (string.IsNullOrEmpty(optionString) || optionString == "UseBuildConfiguration")
        {
            var dotsConfig = DotsConfigForSettings(settingsObject, out var unused);
            return enabledByDefaultForConfigurations.Contains(dotsConfig);
        }
        if (optionString == "Enabled")
            return true;
        if (optionString == "Disabled")
            return false;
        throw new ArgumentException($"Error: Unrecognized '{optionName}' option '{optionString}' in build json file. This is a bug.");
    }

    private static List<DotsBuildSystemTarget> _dotsBuildSystemTargets;

    private static List<DotsBuildSystemTarget> DotsBuildSystemTargets
    {
        get
        {
            if (_dotsBuildSystemTargets != null)
                return _dotsBuildSystemTargets;

            var platformList = new List<DotsBuildSystemTarget>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                foreach (var type in types)
                {
                    if (type.IsAbstract)
                        continue;

                    if (!type.IsSubclassOf(typeof(DotsBuildSystemTarget)))
                        continue;

                    platformList.Add((DotsBuildSystemTarget) Activator.CreateInstance(type));
                }
            }

            _dotsBuildSystemTargets = platformList;

            return _dotsBuildSystemTargets;
        }
    }

    private static Lazy<DotsRuntimeCSharpProgramConfiguration> _multiThreadedJobsTestConfig =
        new Lazy<DotsRuntimeCSharpProgramConfiguration>(() =>
            HostDotnet.WithMultiThreadedJobs(true).WithIdentifier(HostDotnet.Identifier + "-mt"));

    public static DotsRuntimeCSharpProgramConfiguration MultithreadedJobsTestConfig =>
        _multiThreadedJobsTestConfig.Value;

    public static DotsRuntimeCSharpProgramConfiguration HostDotnet
    {
        get
        {
            var target = DotsBuildSystemTargets.First(c =>
                c.ScriptingBackend == ScriptingBackend.Dotnet &&
                c.ToolChain.Platform.GetType() == Platform.HostPlatform.GetType());
            return new DotsRuntimeCSharpProgramConfiguration(
                csharpCodegen: CSharpCodeGen.Release,
                cppCodegen: CodeGen.Release,
                nativeToolchain: target.ToolChain,
                scriptingBackend: ScriptingBackend.Dotnet,
                targetFramework: TargetFramework.NetStandard20,
                identifier: "HostDotNet",
                enableUnityCollectionsChecks: true,
                enableManagedDebugging: false,
                waitForManagedDebugger: false,
                multiThreadedJobs: false,
                dotsConfiguration: DotsConfiguration.Develop,
                enableProfiler: false,
                useBurst: true);
        }
    }
}
