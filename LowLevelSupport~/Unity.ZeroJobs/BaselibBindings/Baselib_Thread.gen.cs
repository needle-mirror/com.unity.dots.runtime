﻿//
// File autogenerated from Include/C/Baselib_Thread.h
//

using System;
using System.Runtime.InteropServices;
using size_t = System.UIntPtr;

namespace Unity.Baselib.LowLevel
{
    internal static unsafe partial class Binding
    {
        /// <summary>The minimum guaranteed number of max concurrent threads that works on all platforms.</summary>
        /// <remarks>
        /// This only applies if all the threads are created with Baselib.
        /// In practice, it might not be possible to create this many threads either. If memory is
        /// exhausted, by for example creating threads with very large stacks, that might translate to
        /// a lower limit in practice.
        /// Note that on many platforms the actual limit is way higher.
        /// </remarks>
        public const int Baselib_Thread_MinGuaranteedMaxConcurrentThreads = 64;
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_Thread
        {
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Baselib_Thread_EntryPointFunction(IntPtr arg);
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_Thread_Config
        {
            /// <summary>Don't set this, it is set by Baselib_Thread_ConfigCreate</summary>
            public UInt32 uninitializedDetectionMagic;
            /// <summary>Length of the name (optional)</summary>
            public UInt32 nameLen;
            /// <summary>Name of the created thread (optional)</summary>
            /// <remarks>Does not need to contain null terminator</remarks>
            public byte* name;
            /// <summary>The minimum size in bytes to allocate for the thread stack. (optional)</summary>
            /// <remarks>
            /// If not set, a platform/system specific default stack size will be used.
            /// If the value set does not conform to platform specific minimum values or alignment requirements,
            /// the actual stack size used will be bigger than what was requested.
            /// </remarks>
            public UInt64 stackSize;
            /// <summary>Required, this is set by calling Baselib_Thread_ConfigCreate with a valid entry point function.</summary>
            public IntPtr entryPoint; // Baselib_Thread_EntryPointFunction
            /// <summary>Argument to the entry point function, does only need to be set if entryPoint takes an argument.</summary>
            public IntPtr entryPointArgument;
        }
        /// <summary>Baselib_Thread_Id that is guaranteed not to represent a thread</summary>
        public static readonly IntPtr Baselib_Thread_InvalidId = IntPtr.Zero;
        /// <summary>
        /// Creates a thread configuration (defined above), which is an argument to Thread_Create further down.
        /// </summary>
        /// <remarks>
        /// Always use this function to create a new configuration to ensure that it is properly initialized.
        /// </remarks>
        /// <param name="entryPoint">The function that will be executed by the thread</param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        public static extern Baselib_Thread_Config Baselib_Thread_ConfigCreate(Baselib_Thread_EntryPointFunction entryPoint);
        /// <summary>
        /// Creates and starts a new thread.
        /// </summary>
        /// <remarks>
        /// On some platforms the thread name is not set until the thread has begun executing, which is not guaranteed
        /// to have happened when the creation function returns. On some platforms there is a limit on the length of
        /// the thread name. If config->name is longer than that (platform dependent) limit, the name will be truncated.
        /// 
        /// Possible error codes:
        /// - Baselib_ErrorCode_UninitializedThreadConfig:        config is null or uninitialized
        /// - Baselib_ErrorCode_ThreadEntryPointFunctionNotSet:   config->entryPoint is null
        /// - Baselib_ErrorCode_OutOfSystemResources:             there is not enough memory to create a thread with that stack size or the system limit of number of concurrent threads has been reached
        /// </remarks>
        /// <param name="config">A pointer to a config object. This object should be constructed with Baselib_Thread_ConfigCreate</param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        public static extern Baselib_Thread* Baselib_Thread_Create(Baselib_Thread_Config* config, Baselib_ErrorState* errorState);
        /// <summary>
        /// Waits until a thread has finished its execution.
        /// </summary>
        /// <remarks>
        /// Also frees its resources.
        /// If called and completed successfully, no Baselib_Thread function can be called again on the same Baselib_Thread.
        /// 
        /// Possible error codes:
        /// - Baselib_ErrorCode_InvalidArgument:       thread is null
        /// - Baselib_ErrorCode_ThreadCannotJoinSelf:  the thread parameter points to the current thread, i.e. the thread that is calling this function
        /// - Baselib_ErrorCode_Timeout:               timeout is reached before the thread has finished
        /// </remarks>
        /// <param name="thread">A pointer to a thread object.</param>
        /// <param name="timeoutInMilliseconds">Time to wait for the thread to finish</param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        public static extern void Baselib_Thread_Join(Baselib_Thread* thread, UInt32 timeoutInMilliseconds, Baselib_ErrorState* errorState);
        /// <summary>
        /// Yields the execution context of the current thread to other threads, potentially causing a context switch.
        /// </summary>
        /// <remarks>
        /// The operating system may decide to not switch to any other thread.
        /// </remarks>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        public static extern void Baselib_Thread_YieldExecution();
        /// <summary>
        /// Return the thread id of the current thread, i.e. the thread that is calling this function
        /// </summary>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        public static extern IntPtr Baselib_Thread_GetCurrentThreadId();
        /// <summary>
        /// Return the thread id of the thread given as argument
        /// </summary>
        /// <param name="thread">A pointer to a thread object.</param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        public static extern IntPtr Baselib_Thread_GetId(Baselib_Thread* thread);
        /// <summary>
        /// Returns true if there is support in baselib for threads on this platform, otherwise false.
        /// </summary>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention=CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool Baselib_Thread_SupportsThreads();
    }
}
