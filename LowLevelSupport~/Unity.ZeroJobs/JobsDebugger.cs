#if ENABLE_UNITY_COLLECTIONS_CHECKS

using System.Runtime.InteropServices;
using System;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using UnityEngine.Assertions;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Diagnostics;
using Unity.Burst;

namespace Unity.Development.JobsDebugger
{
    [StructLayout(LayoutKind.Explicit, Size = 32, Pack = 4, CharSet = CharSet.Ansi)]
    public struct JobNameStorage
    {
        [FieldOffset(0)]
        public char zero;
    }

    public class JobNames
    {
        private static readonly JobNameStorage m_NameStorage;

        public unsafe static char* NameBlobPtr { get; private set; }

        public static unsafe void Initialize()
        {
            fixed (JobNameStorage* p = &m_NameStorage)
                NameBlobPtr = (char*)p;
        }
    }

    static unsafe class ExceptionReporter
    {
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void* ExceptionReporter_Check();


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static unsafe void Check()
        {
            void* err = ExceptionReporter_Check();
            if (err != null)
                throw new InvalidOperationException(new string((char*) err));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    [BurstCompile]
    public unsafe struct DependencyValidator
    {
        // The reason AtomicSafetyNode isn't enough for dependency checking is:
        // a) For writable resources, we need the AtomicSafetyHandle representing a reference to the resource
        //    so that we can check if it's using the secondary version while updating dependency information.
        // b) Regardless, for all resources, we need a safety id representing the reflected name for understandable
        //    and useful error messages
        private AtomicSafetyHandle** ReadOnlyHandlePtr;
        private int ReadOnlyHandleCapacity;
        private int ReadOnlyHandleKnown;
        private int ReadOnlyHandleCount;
        private AtomicSafetyHandle** WritableHandlePtr;
        private int WritableHandleCapacity;
        private int WritableHandleKnown;
        private int WritableHandleCount;
        private AtomicSafetyNode** DeallocateNodePtr;
        private int DeallocateNodeCapacity;
        private int DeallocateNodeKnown;
        private int DeallocateNodeCount;

        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_AllocateKnown(void* data, int numReadOnly, int numWritable, int numDeallocate);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_Cleanup(void* data);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_RecordAndSanityCheckReadOnly(void* data, void* handle, int fieldNameBlodOffset, int jobNameOffset);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_RecordAndSanityCheckWritable(void* data, void* handle, int fieldNameBlodOffset, int jobNameOffset);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_RecordAndSanityCheckDynamic(void* data, void* firstHandle,
            int fieldNameBlobOffset, int jobNameOffset, int handleCountReadOnly, int handleCountWritable, int handleCountForceReadOnly);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_ValidateScheduleSafety(void* _data, void* _dependsOn, int jobNameOffset);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_UpdateDependencies(void* data, void* handle, int jobNameOffset);
        [DllImport(AtomicSafetyHandle.nativejobslib)]
        private static extern unsafe void DependencyValidator_ValidateDeferred(void* data, void* deferredHandle);

        // Allocate memory for resources with known usage semantics.
        // Excludes dynamic safety handles which are unknown and determined at runtime.
        public static void AllocateKnown(ref DependencyValidator data, int numReadOnly, int numWritable, int numDeallocate)
        {
            fixed (DependencyValidator* _data = &data)
            {
                DependencyValidator_AllocateKnown(_data, numReadOnly, numWritable, numDeallocate);
            }
        }

        public static void Cleanup(ref DependencyValidator data)
        {
            fixed (DependencyValidator* _data = &data)
            {
                DependencyValidator_Cleanup(_data);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordAndSanityCheckReadOnly(ref DependencyValidator data, ref AtomicSafetyHandle handle, int fieldNameBlobOffset, int jobNameOffset)
        {
            fixed (DependencyValidator* _data = &data)
            {
                fixed(AtomicSafetyHandle* _handle = &handle)
                {
                    DependencyValidator_RecordAndSanityCheckReadOnly(_data, _handle, fieldNameBlobOffset, jobNameOffset);
                }
            }
            ExceptionReporter.Check();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordAndSanityCheckWritable(ref DependencyValidator data, ref AtomicSafetyHandle handle, int fieldNameBlobOffset, int jobNameOffset)
        {
            fixed (DependencyValidator* _data = &data)
            {
                fixed(AtomicSafetyHandle* _handle = &handle)
                {
                    DependencyValidator_RecordAndSanityCheckWritable(_data, _handle, fieldNameBlobOffset, jobNameOffset);
                }
            }
            ExceptionReporter.Check();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordDeallocate(ref DependencyValidator data, ref AtomicSafetyHandle handle)
        {
            data.DeallocateNodePtr[data.DeallocateNodeCount++] = handle.nodePtr;
        }

        [BurstCompile]
        public static void RecordAndSanityCheckDynamic(ref DependencyValidator data, ref AtomicSafetyHandle firstHandle, int fieldNameBlobOffset, int jobNameOffset, int handleCountReadOnly, int handleCountWritable, int handleCountForceReadOnly)
        {
            fixed (DependencyValidator* _data = &data)
            {
                fixed(AtomicSafetyHandle* _firstHandle = &firstHandle)
                {
                    DependencyValidator_RecordAndSanityCheckDynamic(_data, _firstHandle, fieldNameBlobOffset, jobNameOffset, handleCountReadOnly, handleCountWritable, handleCountForceReadOnly);
                }
            }
            ExceptionReporter.Check();
        }

        // Checks dependencies and aliasing
        public static void ValidateScheduleSafety(ref DependencyValidator data, ref JobHandle dependsOn, int jobNameOffset)
        {
            fixed (DependencyValidator* _data = &data)
            {
                fixed (JobHandle* _dependsOn = &dependsOn)
                {
                    DependencyValidator_ValidateScheduleSafety(_data, _dependsOn, jobNameOffset);
                }
            }
            ExceptionReporter.Check();
        }

        // Checks deferred array ownership
        public static void ValidateDeferred(ref DependencyValidator data, void *deferredHandle)
        {
            fixed (DependencyValidator* _data = &data)
            {
                  DependencyValidator_ValidateDeferred(_data, deferredHandle);
            }
            ExceptionReporter.Check();
        }

        // Checks deallocation constraints
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateDeallocateOnJobCompletion(Allocator allocType)
        {
            if (allocType != Allocator.Persistent && allocType != Allocator.TempJob)
                throw new InvalidOperationException("Only Allocator.Persistent and Allocator.TempJob can be deallocated from a job");
        }

        public static void UpdateDependencies(ref DependencyValidator data, ref JobHandle handle, int jobNameOffset)
        {
            fixed (DependencyValidator* _data = &data)
            {
                fixed (JobHandle* _handle = &handle)
                {
                    DependencyValidator_UpdateDependencies(_data, _handle, jobNameOffset);
                }
            }
            ExceptionReporter.Check();
        }
    }
}

#endif // ENABLE_UNITY_COLLECTIONS_CHECKS
