using JetBrains.Annotations;
using System;
using System.Linq;
using Unity.Build.Editor;
using Unity.Properties.Editor;
using UnityEngine;
using BuildTarget = Unity.Build.DotsRuntime.BuildTarget;

namespace Unity.Entities.Runtime.Build
{
    [UsedImplicitly]
    sealed class BuildTargetInspector : TypeInspector<BuildTarget>
    {
        public override Func<Type, bool> TypeFilter => type => BuildTarget.AvailableBuildTargets
            .Where(target => !target.HideInBuildTargetPopup)
            .Select(target => target.GetType())
            .Any(target => target == type);

        public override Func<Type, string> TypeName => type => TypeConstruction.Construct<BuildTarget>(type).DisplayName;
        public override Func<Type, string> TypeCategory => type => string.Empty;
        public override Func<Type, Texture2D> TypeIcon => type => TypeConstruction.Construct<BuildTarget>(type).Icon;
    }
}
