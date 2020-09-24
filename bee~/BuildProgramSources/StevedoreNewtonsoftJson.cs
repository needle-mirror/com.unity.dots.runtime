using System;
using Bee.Core;
using Bee.Core.Stevedore;
using Bee.Stevedore;
using NiceIO;

static class StevedoreNewtonsoftJson
{
    public static NPath[] Paths => _paths.Value;
    
    static readonly Lazy<NPath[]> _paths = new Lazy<NPath[]>(() =>
    {
        var newtonsoftJsonArtifact = new StevedoreArtifact("newtonsoft-json");

        return new[]
        {
            newtonsoftJsonArtifact.Path.Combine("lib", "net40", "Newtonsoft.Json.dll"),
        };
    });
}
