//
// File autogenerated from Include/C/Baselib_ErrorState.h
//

using System;
using System.Runtime.InteropServices;
using size_t = System.UIntPtr;

namespace Unity.Baselib.LowLevel
{
    internal static unsafe partial class Binding
    {
        /// <summary>Native error code type.</summary>
        public enum Baselib_ErrorState_NativeErrorCodeType : byte // Baselib_ErrorState_NativeErrorCodeType_t
        {
            /// <summary>Native error code is not present.</summary>
            None = 0x0,
            /// <summary>All platform error codes types must be bigger or equal to this value.</summary>
            PlatformDefined = 0x1,
        }
        /// <summary>Extra information type.</summary>
        public enum Baselib_ErrorState_ExtraInformationType : byte // Baselib_ErrorState_ExtraInformationType_t
        {
            /// <summary>Extra information is not present.</summary>
            None = 0x0,
            /// <summary>
            /// Extra information is a pointer of const char* type.
            /// Pointer guaranteed to be valid for lifetime of the program (static strings, buffers, etc).
            /// </summary>
            StaticString = 0x1,
            /// <summary>Extra information is a generation counter to ErrorState internal static buffer.</summary>
            GenerationCounter = 0x2,
        }
        /// <summary>Baselib error information.</summary>
        /// <remarks>
        /// All functions that expect a pointer to a error state object will *not* allow to pass a nullptr for it
        /// If an error state with code other than Success is passed, the function is guaranteed to early out.
        /// Note that even if an error state is expected, there might be no full argument validation. For details check documentation of individual functions.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_ErrorState
        {
            public Baselib_SourceLocation sourceLocation;
            public UInt64 nativeErrorCode;
            public UInt64 extraInformation;
            public Baselib_ErrorCode code;
            public Baselib_ErrorState_NativeErrorCodeType nativeErrorCodeType;
            public Baselib_ErrorState_ExtraInformationType extraInformationType;
        }
        public enum Baselib_ErrorState_ExplainVerbosity : Int32
        {
            /// <summary>Include error type with platform specific value (if specified).</summary>
            ErrorType = 0x0,
            /// <summary>
            /// Include error type with platform specific value (if specified),
            /// source location (subject to BASELIB_ENABLE_SOURCELOCATION define) and an error explanation if available.
            /// </summary>
            ErrorType_SourceLocation_Explanation = 0x1,
        }
        /// <summary>Writes a null terminated string containing native error code value and explanation if possible.</summary>
        /// <param name="errorState">Error state to explain. If null an empty string will be written into buffer.</param>
        /// <param name="buffer">
        /// Buffer to write explanation into.
        /// If nullptr is passed, nothing will be written but function will still return correct amount of bytes.
        /// </param>
        /// <param name="bufferLen">
        /// Length of buffer in bytes.
        /// If 0 is passed, behaviour is the same as passing nullptr as buffer.
        /// </param>
        /// <param name="verbosity">Verbosity level of the explanation string.</param>
        /// <returns>the number of characters that would have been written if buffer had been sufficiently large, including the terminating null character.</returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UInt32 Baselib_ErrorState_Explain(Baselib_ErrorState* errorState, byte* buffer, UInt32 bufferLen, Baselib_ErrorState_ExplainVerbosity verbosity);
    }
}
