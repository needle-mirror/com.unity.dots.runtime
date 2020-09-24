using System;

using Bee.Core;
using Bee.Core.Stevedore;
using Bee.Stevedore;

using NiceIO;

static class StevedoreExtensions
{
    public static NPath GenerateUnusualPath(this StevedoreArtifact artifact)
    {
        var path = new NPath("artifacts").Combine("Stevedore", artifact.ArtifactName);
        return artifact.UnpackToUnusualLocation(path);
    }

    public static NPath GetUnusualPath(this StevedoreArtifact artifact)
    {
        var path = new NPath("artifacts").Combine("Stevedore", artifact.ArtifactName);
        return path;
    }
}
