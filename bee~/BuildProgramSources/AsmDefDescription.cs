using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NiceIO;
using Unity.BuildSystem.NativeProgramSupport;

public class AsmRefDescription
{
    public NPath Path { get; }
    public string PackageSource { get; }
    private JObject Json;

    public AsmRefDescription(NPath path, string packageSource)
    {
        Path = path;
        PackageSource = packageSource;
        Json = JObject.Parse(path.ReadAllText());
    }

    public string Reference => AsmDefConfigFile.GetRealAsmDefName(Json["reference"].Value<string>());
}


public class AsmDefDescription
{
    public NPath Path { get; }
    public string PackageSource { get; }
    internal JObject Json;

    public AsmDefDescription(NPath path, string packageSource)
    {
        Path = path;
        PackageSource = packageSource;
        Json = JObject.Parse(path.ReadAllText());
        
        Name = Json["name"].Value<string>();
        IncludedAsmRefs = AsmDefConfigFile.AsmRefs.Where(desc => desc.Reference == Name).ToList();
        
        AllowUnsafeCode = Json["allowUnsafeCode"]?.Value<bool>() == true;
        AutoReferenced = Json["autoReferenced"]?.Value<bool>() == true;
        NoEngineReferences = Json["noEngineReferences"]?.Value<bool>() == true;
        OverrideReferences = Json["overrideReferences"]?.Value<bool>() == true;
        
        DefineConstraints = Json["defineConstraints"]?.Values<string>().ToArray() ?? Array.Empty<string>();
        OptionalUnityReferences = Json["optionalUnityReferences"]?.Values<string>()?.ToArray() ?? Array.Empty<string>();
        PrecompiledReferences = Json["precompiledReferences"]?.Values<string>()?.ToArray() ?? Array.Empty<string>();

        IsTestAsmDef = DefineConstraints.Contains("UNITY_INCLUDE_TESTS") || OptionalUnityReferences.Contains("TestAssemblies") || PrecompiledReferences.Contains("nunit.framework.dll");
        IsILPostProcessorAssembly = Name.EndsWith(".CodeGen") && !DefineConstraints.Contains("!NET_DOTS");

        var shouldIgnoreEditorPlatform = IsTestAsmDef || IsILPostProcessorAssembly;
        IncludePlatforms = ReadPlatformList(shouldIgnoreEditorPlatform, Json["includePlatforms"]);
        ExcludePlatforms = ReadPlatformList(shouldIgnoreEditorPlatform, Json["excludePlatforms"]);
        
        NamedReferences = Json["references"]?.Values<string>().Select(AsmDefConfigFile.GetRealAsmDefName).ToArray() ?? Array.Empty<string>();        
    }

    public bool NeedsEntryPointAdded()
    {
        return !DefineConstraints.Contains("UNITY_DOTS_ENTRYPOINT") && References.All(r => r.NeedsEntryPointAdded());
    }

    public AsmDefDescription[] References => NamedReferences.Select(AsmDefConfigFile.AsmDefDescriptionFor)
                                                            .Where(d => d != null && IsSupported(d.Name))
                                                            .ToArray();

    public NPath Directory => Path.Parent;
    public bool IsTinyRoot { get; set; }

    public readonly string Name;
    public readonly List<AsmRefDescription> IncludedAsmRefs;
    public readonly string[] NamedReferences;

    public readonly bool IsTestAsmDef;

    public readonly Platform[] IncludePlatforms;
    public readonly Platform[] ExcludePlatforms;
    
    public readonly bool AllowUnsafeCode;
    public readonly bool IsILPostProcessorAssembly;

    public readonly string[] DefineConstraints;
    public readonly string[] OptionalUnityReferences;
    public readonly string[] PrecompiledReferences;

    public readonly bool AutoReferenced;
    public readonly bool NoEngineReferences;
    public readonly bool OverrideReferences;
    
    private static Platform[] ReadPlatformList(bool shouldIgnoreEditorPlatform, JToken platformList)
    {
        if (platformList == null)
            return Array.Empty<Platform>();

        return platformList.Select(token => PlatformFromAsmDefPlatformName(shouldIgnoreEditorPlatform, token.ToString())).Where(p => p != null).ToArray();
    }
    
    public class EditorPlatform : Platform
    {
        public EditorPlatform() { }

        public override bool HasPosix => false;
        public override string Name => "Editor";
        public override string DisplayName => "Editor";
    }

    private static Platform PlatformFromAsmDefPlatformName(bool shouldIgnoreEditorPlatform, string name)
    {
        switch(name)
        {
            case "macOSStandalone":
                return new MacOSXPlatform();
            case "WindowsStandalone32":
            case "WindowsStandalone64":
                return new WindowsPlatform();
            case "Editor":
                if (shouldIgnoreEditorPlatform)
                    return null;
                else
                    return new EditorPlatform();
            default:
            {
                var typeName = $"{name}Platform";
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (type == null)
                {
                    Console.WriteLine($"Couldn't find Platform for {name} (tried {name}Platform), ignoring it.");
                    return null;
                }
                return (Platform)Activator.CreateInstance(type);
            }
        }
    }
    private bool IsSupported(string referenceName)
    {
        if (referenceName.Contains("Unity.Collections.Tests"))
            return false;

        return true;
    }
}
