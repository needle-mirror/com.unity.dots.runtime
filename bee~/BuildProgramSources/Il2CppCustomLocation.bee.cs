using System;

public static class Il2CppCustomLocation
{
    private static bool checkedCustomLocation = false;
    private static string computedCustomLocation = null;

    private static string ComputeCustomLocation()
    {
        string path = null; // Set local il2cpp path here

        if (path != null)
            return path;

        string envPath = Environment.GetEnvironmentVariable("IL2CPP_FROM_LOCAL"); // Set in CI for testing
        if (envPath != null)
            return envPath;

        // look for a magic asmdef for devmode
        var asmdef = AsmDefConfigFile.AsmDefDescriptionFor("Il2Cpp.Devmode");
        if (asmdef != null)
        {
            // make sure it's configured
            var il2cppPath = asmdef.Path.Parent.Parent.Parent;
            var exePath = il2cppPath.Combine("build/deploy/net471/il2cpp.exe");
            if (!exePath.Exists())
            {
                throw new ArgumentException(
                    $"You have an Il2Cpp.Devmode asmdef in a package you have referenced, but we couldn't find an il2cpp.exe at {exePath}.  Either il2cpp isn't actually under devmode (don't reference the package if not), or you didn't build it (run perl build.sh inside PackageSources/il2cpp)");
            }
            return il2cppPath.ToString();
        }

        return null;
    }

    public static string CustomLocation
    {
        get
        {
            if (!checkedCustomLocation)
            {
                computedCustomLocation = ComputeCustomLocation();
                checkedCustomLocation = true;
            }
            return computedCustomLocation;
        }
    }
}
