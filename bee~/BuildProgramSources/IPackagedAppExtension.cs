using System;
using System.Collections.Generic;
using Bee.Core;
using NiceIO;
using Bee.NativeProgramSupport;

namespace Bee.Toolchain.Extension
{
    public interface IPackagedAppExtension
    {
        void SetAppPackagingParameters(String gameName, DotsConfiguration config);
    }
}
