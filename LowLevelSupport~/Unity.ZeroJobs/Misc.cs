using System;
using System.Runtime.InteropServices;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;

namespace UnityEngine
{
    // Needed to support SharedStatic<> in Burst
    [StructLayout(LayoutKind.Sequential)]
    public unsafe partial struct Hash128
    {
        public Hash128(uint u32_0, uint u32_1, uint u32_2, uint u32_3)
        {
            m_u32_0 = u32_0;
            m_u32_1 = u32_1;
            m_u32_2 = u32_2;
            m_u32_3 = u32_3;
        }

        public Hash128(ulong u64_0, ulong u64_1)
        {
            var ptr0 = (uint*)&u64_0;
            var ptr1 = (uint*)&u64_1;

            m_u32_0 = *ptr0;
            m_u32_1 = *(ptr0 + 1);
            m_u32_2 = *ptr1;
            m_u32_3 = *(ptr1 + 1);
        }

        uint m_u32_0;
        uint m_u32_1;
        uint m_u32_2;
        uint m_u32_3;
    }
}

//unity.properties has an unused "using UnityEngine.Bindings".
namespace UnityEngine.Bindings
{
    public class Dummy
    {
    }
}

namespace UnityEngine.Internal
{
    public class ExcludeFromDocsAttribute : Attribute {}
}

namespace Unity.Burst
{
    namespace LowLevel
    {
        public static class BurstCompilerService
        {
            // Support SharedStatic<>
            [DllImport("lib_unity_zerojobs")]
            public static extern unsafe void* GetOrCreateSharedMemory(ref Hash128 subKey, uint sizeOf, uint alignment);
        }
    }

    //why is this not in the burst package!?
    public class BurstDiscardAttribute : Attribute{}

    // Static init to support burst. Still needs called if burst not used (i.e. some tests)
    //
    // It is not needed outside of DOTS RT because the static init happening
    // is actually impl. in C++ code in Big DOTS, whereas here we init C#
    // statics that will potentially be burst compiled.
    public class DotsRuntimeInitStatics
    {
        internal static bool needInitStatics = true;

        public static void Init()
        {
            if (needInitStatics)
            {
                needInitStatics = false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // This is a good space to do static initialization or other Burst-unfriendly things
                // if it doesn't need reflection (i.e. happens with CodeGen) to keep code burst compilable.
                Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.StaticInit();
#endif
            }
        }

    }
}

namespace System
{
    public class CodegenShouldReplaceException : NotImplementedException
    {
        public CodegenShouldReplaceException() : base("This function should have been replaced by codegen")
        {
        }

        public CodegenShouldReplaceException(string msg) : base(msg)
        {
        }
    }
}