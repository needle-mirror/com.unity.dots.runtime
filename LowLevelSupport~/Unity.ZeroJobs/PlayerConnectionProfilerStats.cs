#if ENABLE_PROFILER

using System;
using System.Runtime.InteropServices;
using UnityEngine.Networking.PlayerConnection;
using static System.Text.Encoding;
using static Unity.Baselib.LowLevel.Binding;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Development.PlayerConnection;
using Unity.Collections;
using UnityEngine.Assertions;

namespace Unity.Development.Profiling
{
    public class ProfilerStats
    {
        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct DrawStats
        {
            const int kMaxPlatformDependentStats = 16;
            public int setPassCalls;
            public int batches;
            public int drawCalls;
            public int triangles1024;
            public int vertices1024;

            public int dynamicBatchedDrawCalls;
            public int dynamicBatches;
            public int dynamicBatchedTriangles1024;
            public int dynamicBatchedVertices1024;

            public int staticBatchedDrawCalls;
            public int staticBatches;
            public int staticBatchedTriangles1024;
            public int staticBatchedVertices1024;

            public int hasInstancing;  // 0 or 1
            public int instancedBatchedDrawCalls;
            public int instancedBatches;
            public int instancedTriangles1024;
            public int instancedVertices1024;

            public int shadowCasters;

            public int usedTextureCount;
            public int usedTextureKB;

            public int renderTextureCount;
            public int renderTextureKB;
            public int renderTextureStateChanges;

            public int screenWidth;
            public int screenHeight;
            public int screenKB;

            public int vboTotal;
            public int vboTotalKB;
            public int vboUploads;
            public int vboUploadKB;
            public int ibUploads;
            public int ibUploadKB;

            public int visibleSkinnedMeshes;

            public int totalAvailableVRamMB;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryStats
        {
            const int kMaxPlatformDependentStats = 16;

            public int kbUsedTotal;             // Calculated from AccumulateStats
            public int kbUsedUnity;             // Calculated from AccumulateStats
            public int kbUsedMono;              // Calculated from AccumulateStats
            public int kbUsedGFX;               // Calculated from AccumulateStats
            public int kbUsedAudio;             // Calculated from AccumulateStats
            public int kbUsedVideo;             // Calculated from AccumulateStats
            public int kbUsedProfiler;          // Calculated from AccumulateStats

            public int kbReservedTotal;         // Calculated from AccumulateStats
            public int kbReservedUnity;         // Calculated from AccumulateStats
            public int kbReservedMono;          // Calculated from AccumulateStats
            public int kbReservedGFX;           // Calculated from AccumulateStats
            public int kbReservedAudio;         // Calculated from AccumulateStats
            public int kbReservedVideo;         // Calculated from AccumulateStats
            public int kbReservedProfiler;      // Calculated from AccumulateStats

            public int kbVirtual;               // Calculated from AccumulateStats

            public int textureCount;            // Calculated from AccumulateStats
            public int textureKB;               // Calculated from AccumulateStats

            public int meshCount;               // Calculated from AccumulateStats
            public int meshKB;                  // Calculated from AccumulateStats

            public int materialCount;           // Calculated from AccumulateStats
            public int materialKB;              // Calculated from AccumulateStats

            public int animationClipCount;      // Calculated from AccumulateStats
            public int animationClipKB;         // Calculated from AccumulateStats

            public int audioCount;              // Calculated from AccumulateStats
            public int audioKB;                 // Calculated from AccumulateStats

            public int assetCount;              // Calculated automatically

            public int sceneObjectCount;        // N/A DOTS
            public int gameObjectCount;         // N/A DOTS
            public int totalObjectsCount;       // N/A DOTS

            public int profilerMemUsedKB;       // obsolete (in Big Unity too)
            public int profilerNumAllocations;  // obsolete (in Big Unity too)

            // Number of GC allocations for the specific frame
            public int frameGCAllocCount;       // TBD
            public int frameGCAllocKB;          // TBD

            public int classCount;              // Unused - make this 0
            public int classEnd;                // Unused - make this -1

            // This should be IntPtr (size_t) according to Classic Unity, but it is serialized as 32-bit integers anyway
            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];  // Unused - kept for binary compatibilty
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct PhysicsStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int numActiveDynamicBodies;
            public int numActiveKinematicBodies;

            public int numShapePairs;

            public int numStaticBodies;
            public int numDynamicBodies;

            public int numTriggerOverlaps;
            public int numConstraints;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct Physics2DStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int m_TotalBodyCount;
            public int m_ActiveBodyCount;
            public int m_SleepingBodyCount;
            public int m_DynamicBodyCount;
            public int m_KinematicBodyCount;
            public int m_StaticBodyCount;
            public int m_DiscreteBodyCount;
            public int m_ContinuousBodyCount;
            public int m_JointCount;
            public int m_ContactCount;
            public int m_ActiveColliderShapesCount;
            public int m_SleepingColliderShapesCount;
            public int m_StaticColliderShapesCount;
            public int m_DiscreteIslandCount;
            public int m_ContinuousIslandCount;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct NetworkOperationStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int m_SystemUserMessage;
            public int m_SystemObjectDestroy;
            public int m_SystemRpc;
            public int m_SystemObjectSpawn;
            public int m_SystemOwner;
            public int m_SystemCommand;
            public int m_SystemLocalPlayerTransform;
            public int m_SystemSyncEvent;
            public int m_SystemUpdateVars;
            public int m_SystemSyncList;
            public int m_SystemObjectSpawnScene;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct NetworkMessageStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int bytesIn;
            public int bytesOut;

            public int packetsIn;
            public int HLAPIMsgsIn;
            public int LLAPIMsgsIn;

            public int packetsOut;
            public int HLAPIMsgsOut;
            public int LLAPIMsgsOut;

            public int numberConnections;
            public int rtt;
            public int HLAPIResends;
            public int HLAPIPending;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [Flags]
        public enum GpuProfilingStatisticsAvailabilityStates
        {
            Gathered = 1 << 0,
            Enabled = 1 << 1,
            Supported = 1 << 2,
            NotSupportedWithEditorProfiling = 1 << 3,
            NotSupportedByMacOS = 1 << 4,
            NotSupportedWithLegacyGfxJobs = 1 << 5,
            NotSupportedWithNativeGfxJobs = 1 << 6,
            NotSupportedByDevice = 1 << 7,
            NotSupportedByGraphicsAPI = 1 << 8,
            //GLES only
            NotSupportedDueToFrameTimingStatsAndDisjointTimerQuery = 1 << 9,
            NotSupportedWithVulkan = 1 << 10,
            NotSupportedWithMetal = 1 << 11,
        }

        // unity/Runtime/Export/BaseClass.cs
        public enum RuntimePlatform
        {
            OSXPlayer = 1,
            WindowsPlayer = 2,
            IPhonePlayer = 8,
            Android = 11,
            LinuxPlayer = 13,
            WebGLPlayer = 17,
            //WSAPlayerX86 = 18,
            //WSAPlayerX64 = 19,
            //WSAPlayerARM = 20,
            //PS4 = 25,
            //XboxOne = 27,
            //tvOS = 31,
            //Switch = 32,
            //Lumin = 33,
            //Stadia = 34,
        }

        // unity/Runtime/Utilities/UnityVersion.h
        public enum ReleaseType
        {
            Alpha = 0,
            Beta = 1,
            Public = 2,
            Patch = 3,
            Experimental = 4,
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct DebugStats
        {
            public int m_ProfilerMemoryUsage;
            public int m_ProfilerMemoryUsageOthers;
            public int m_AllocatedProfileSamples;
            public int m_GpuProfilingStatisticsAvailabilityStates;
            public int m_RuntimePlatform;
            public int m_UnityVersionMajor;
            public int m_UnityVersionMinor;
            public int m_UnityVersionRevision;
            public int m_UnityVersionReleaseType;
            public int m_UnityVersionIncrementalVersion;
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct AudioStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int totalAudioSourceCount;              // Calculated automatically
            public int playingSources;                     // Calculated from AccumulateStats
            public int pausedSources;                      // Calculated from AccumulateStats

            public int audioClipCount;                     // Calculated automatically

            public int numSoundChannelInstances;           // Calculated from AccumulateStats
            public int numSoundChannelHandles;             // N/A DOTS

            public int numSoundHandles;                    // N/A DOTS
            public int numSoundHandleInstances;            // N/A DOTS
            public int numPendingSoundHandleInstances;     // N/A DOTS
            public int numLoadedSoundHandleInstance;       // N/A DOTS
            public int numDisposedSoundHandleInstances;    // N/A DOTS
            public int numPendingSoundInstanceUnloads;     // N/A DOTS
            public int numSoundChannelInstanceWeakPtrs;    // N/A DOTS
            public int numSoundHandleInstanceWeakPtrs;     // N/A DOTS
            public int numSampleClipWeakPtrs;              // N/A DOTS
            public int numWeakPtrs;                        // N/A DOTS
            public int numWeakPtrSharedData;               // N/A DOTS
            public int numWeakPtrSharedDataSoundChannel;   // N/A DOTS
            public int numWeakPtrSharedDataSoundHandle;    // N/A DOTS
            public int numWeakPtrSharedDataSampleClip;     // N/A DOTS

            public int numFMODChannels;                    // N/A DOTS
            public int numVFSHandles;                      // N/A DOTS

            public int totalCPUx10;                        // Calculated automatically
            public int dspCPUx10;                          // Calculated from AccumulateStats
            public int streamCPUx10;                       // Calculated from AccumulateStats
            public int otherCPUx10;                        // Calculated from AccumulateStats

            public int totalMemoryUsage;                   // Calculated automatically
            public int streamDecodeMemory;                 // Calculated from AccumulateStats
            public int sampleMemory;                       // Calculated from AccumulateStats
            public int channelMemoryObsolete;              // obsolete (in Big Unity too)
            public int dspMemoryObsolete;                  // obsolete (in Big Unity too)
            public int extraDSPBufferMemoryObsolete;       // obsolete (in Big Unity too)
            public int codecMemory;                        // Calculated from AccumulateStats
            public int recordMemoryObsolete;               // obsolete (in Big Unity too)
            public int reverbMemoryObsolete;               // obsolete (in Big Unity too)
            public int streamFileMemory;                   // Calculated from AccumulateStats
            public int otherMemory;                        // Calculated from AccumulateStats

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct UIStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int batchCount;
            public int vertexCount;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct VideoStats
        {
            const int kMaxPlatformDependentStats = 8;

            public int totalVideoSourceCount;
            public int playingSources;
            public int swPlayingSources;
            public int preBufferedFrames;
            public int preBufferedFrameLimit;
            public int framesDropped;
            public int pausedSources;
            public int videoClipCount;

            public unsafe fixed int platformDependentStats[kMaxPlatformDependentStats];
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        [StructLayout(LayoutKind.Sequential)]
        public struct GlobalIlluminationStats
        {
            public float m_TotalCPUTime;
            public int m_TotalSystemCount;
            public int m_TotalProbeSetCount;
            public float m_ProbeTime;
            public int m_TotalProbesCount;
            public int m_SolvedProbesCount;
            public float m_SetupTime;
            public float m_EnvironmentTime;
            public float m_InputLightingTime;
            public float m_SystemsTime;
            public float m_SolveTasksTime;
            public float m_DynamicObjectsTime;
            public float m_TimeBetweenUpdates;
            public float m_OtherCommandsTime;
            public int m_BlockedBufferWritesCount;
            public float m_BlockedCommandWriteTime;
            public int m_PendingMaterialUpdateCount;
            public int m_PendingAlbedoUpdateCount;
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        // Stores samples for profiler charts
        [StructLayout(LayoutKind.Sequential)]
        public struct ChartSample
        {
            public float rendering;
            public float scripts;
            public float physics;
            public float animation;
            public float gc;
            public float vsync;
            public float gi;
            public float UI;
            public float others;

            public float gpuOpaque;
            public float gpuTransparent;
            public float gpuShadows;
            public float gpuPostProcess;
            public float gpuDeferredPrePass;
            public float gpuDeferredLighting;
            public float gpuOther;

            public int hasGPUProfiler;

            public float uisystemLayouting;
            public float uisystemRendering;
        }

        // unity/Modules/Profiler/Public/ProfilerStatsBase.h
        // THIS is what gets emitted in exact binary format for profiler stats
        [StructLayout(LayoutKind.Sequential)]
        public struct AllProfilerStats
        {
            public MemoryStats memoryStats;
            public DrawStats drawStats;
            public PhysicsStats physicsStats;
            public Physics2DStats physics2DStats;
            public NetworkOperationStats networkOperationStats;
            public NetworkMessageStats networkMessageStats;
            public DebugStats debugStats;
            public AudioStats audioStats;
            public VideoStats videoStats;
            public ChartSample chartSample;
            public ChartSample chartSampleSelected;
            public UIStats uiStats;
            public GlobalIlluminationStats globalIlluminationStats;
        }

        // The rest of these types are not based on code from big Unity

        [StructLayout(LayoutKind.Sequential)]
        public struct AccumulateStat
        {
            [DllImport("lib_unity_zerojobs")]
            private static extern void PlayerConnectionMt_AtomicAdd64(IntPtr ptr, long value);

            public unsafe void Accumulate(long accum)
            {
                fixed (long* reservedPtr = &value)
                {
                    PlayerConnectionMt_AtomicAdd64((IntPtr)reservedPtr, accum);
                }
            }

            public long value;
        }

        // These will replace the relative stats in AllProfilerStats before we send the raw binary data
        [StructLayout(LayoutKind.Sequential)]
        public struct AccumulateStats
        {
            public AccumulateStat memUsedExternal;
            public AccumulateStat memUsedUnity;
            public AccumulateStat memUsedMono;
            public AccumulateStat memUsedGFX;
            public AccumulateStat memUsedAudio;
            public AccumulateStat memUsedVideo;
            public AccumulateStat memUsedProfiler;
            public AccumulateStat memReservedExternal;
            public AccumulateStat memReservedUnity;
            public AccumulateStat memReservedMono;
            public AccumulateStat memReservedGFX;
            public AccumulateStat memReservedAudio;
            public AccumulateStat memReservedVideo;
            public AccumulateStat memReservedProfiler;
            public AccumulateStat memVirtual;
            public AccumulateStat memTexture;
            public AccumulateStat memTextureCount;
            public AccumulateStat memMesh;
            public AccumulateStat memMeshCount;
            public AccumulateStat memMaterial;
            public AccumulateStat memMaterialCount;
            public AccumulateStat memAnimationClip;
            public AccumulateStat memAnimationClipCount;
            public AccumulateStat memAudio;
            public AccumulateStat memAudioCount;
            public AccumulateStat memFrameGCAlloc;

            public AccumulateStat audioPlayingSources;
            public AccumulateStat audioPausedSources;
            public AccumulateStat audioNumSoundChannelInstances;  // could be different from audioPlayerSources if a source can play multiple sound clips
            public AccumulateStat audioSampleMemory;
            public AccumulateStat audioStreamDecodeMemory;
            public AccumulateStat audioStreamFileMemory;
            public AccumulateStat audioCodecMemory;
            public AccumulateStat audioOtherMemory;
            public AccumulateStat audioDspCPUx10;
            public AccumulateStat audioStreamCPUx10;
            public AccumulateStat audioOtherCPUx10;
        }

        private static readonly SharedStatic<ProfilerModes> sharedGatheredStats = SharedStatic<ProfilerModes>.GetOrCreate<ProfilerStats, ProfilerModes>();
        private static readonly SharedStatic<AllProfilerStats> sharedStats = SharedStatic<AllProfilerStats>.GetOrCreate<ProfilerStats, AllProfilerStats>();
        private static readonly SharedStatic<AccumulateStats> sharedAccumStats = SharedStatic<AccumulateStats>.GetOrCreate<ProfilerStats, AccumulateStats>();

        public static ref ProfilerModes GatheredStats => ref sharedGatheredStats.Data;
        public static ref AllProfilerStats Stats => ref sharedStats.Data;
        public static ref AccumulateStats AccumStats => ref sharedAccumStats.Data;

        public static void CalculateStatsSnapshot()
        {
            long memReserved = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent) + UnsafeUtility.GetHeapSize(Collections.Allocator.Temp) + UnsafeUtility.GetHeapSize(Collections.Allocator.TempJob);
            memReserved += AccumStats.memReservedExternal.value;

            long memUsed = memReserved
                - (AccumStats.memReservedExternal.value - AccumStats.memUsedExternal.value)
                - (AccumStats.memReservedProfiler.value - AccumStats.memUsedProfiler.value)
                //- (AccumStats.memReservedUnity.value - AccumStats.memUsedUnity.value)  // always 0 in DOTS Runtime
                //- (AccumStats.memReservedMono.value - AccumStats.memUsedMono.value)    // always 0 in DOTS Runtime
                - (AccumStats.memReservedGFX.value - AccumStats.memUsedGFX.value)
                - (AccumStats.memReservedVideo.value - AccumStats.memUsedVideo.value)
                - (AccumStats.memReservedAudio.value - AccumStats.memUsedAudio.value);

            Stats.memoryStats.kbUsedTotal = (int)(memUsed / 1024);
            Stats.memoryStats.kbUsedUnity = (int)(AccumStats.memUsedUnity.value / 1024);
            Stats.memoryStats.kbUsedMono = (int)(AccumStats.memUsedMono.value / 1024);
            Stats.memoryStats.kbUsedGFX = (int)(AccumStats.memUsedGFX.value / 1024);
            Stats.memoryStats.kbUsedAudio = (int)(AccumStats.memUsedAudio.value / 1024);
            Stats.memoryStats.kbUsedVideo = (int)(AccumStats.memUsedVideo.value / 1024);
            Stats.memoryStats.kbUsedProfiler = (int)(AccumStats.memUsedProfiler.value / 1024);
            Stats.memoryStats.kbReservedTotal = (int)(memReserved / 1024);
            Stats.memoryStats.kbReservedUnity = (int)(AccumStats.memReservedUnity.value / 1024);
            Stats.memoryStats.kbReservedMono = (int)(AccumStats.memReservedMono.value / 1024);
            Stats.memoryStats.kbReservedGFX = (int)(AccumStats.memReservedGFX.value / 1024);
            Stats.memoryStats.kbReservedAudio = (int)(AccumStats.memReservedAudio.value / 1024);
            Stats.memoryStats.kbReservedVideo = (int)(AccumStats.memReservedVideo.value / 1024);
            Stats.memoryStats.kbReservedProfiler = (int)(AccumStats.memReservedProfiler.value / 1024);
            Stats.memoryStats.kbVirtual = (int)(AccumStats.memVirtual.value / 1024);
            Stats.memoryStats.textureKB = (int)(AccumStats.memTexture.value / 1024);
            Stats.memoryStats.textureCount = (int)AccumStats.memTextureCount.value;
            Stats.memoryStats.meshKB = (int)(AccumStats.memMesh.value / 1024);
            Stats.memoryStats.meshCount = (int)AccumStats.memMeshCount.value;
            Stats.memoryStats.materialKB = (int)(AccumStats.memMaterial.value / 1024);
            Stats.memoryStats.materialCount = (int)AccumStats.memMaterialCount.value;
            Stats.memoryStats.animationClipKB = (int)(AccumStats.memAnimationClip.value / 1024);
            Stats.memoryStats.animationClipCount = (int)AccumStats.memAnimationClipCount.value;
            Stats.memoryStats.audioKB = (int)(AccumStats.memAudio.value / 1024);
            Stats.memoryStats.audioCount = (int)AccumStats.memAudioCount.value;
            Stats.memoryStats.frameGCAllocKB = (int)(AccumStats.memFrameGCAlloc.value / 1024);
            Stats.memoryStats.assetCount = Stats.memoryStats.textureCount + Stats.memoryStats.meshCount +
                Stats.memoryStats.materialCount + Stats.memoryStats.audioCount + Stats.memoryStats.animationClipCount;

            Stats.memoryStats.classCount = 0;
            Stats.memoryStats.classEnd = -1;

            // This is the only set of stats we always gather in DOTS Runtime.
            // Other enabled bit flags should be set by the relative subsystem.
            GatheredStats |= ProfilerModes.ProfileMemory;

            Stats.debugStats.m_AllocatedProfileSamples = ProfilerProtocolSession.TotalMarkers;
            Stats.debugStats.m_GpuProfilingStatisticsAvailabilityStates = (int)GpuProfilingStatisticsAvailabilityStates.NotSupportedByGraphicsAPI;
#if UNITY_WINDOWS
            Stats.debugStats.m_RuntimePlatform = (int)RuntimePlatform.WindowsPlayer;
#elif UNITY_MACOSX
            Stats.debugStats.m_RuntimePlatform = (int)RuntimePlatform.OSXPlayer;
#elif UNITY_LINUX
            Stats.debugStats.m_RuntimePlatform = (int)RuntimePlatform.LinuxPlayer;
#elif UNITY_IOS
            Stats.debugStats.m_RuntimePlatform = (int)RuntimePlatform.IPhonePlayer;
#elif UNITY_ANDROID
            Stats.debugStats.m_RuntimePlatform = (int)RuntimePlatform.Android;
#elif UNITY_WEBGL
            Stats.debugStats.m_RuntimePlatform = (int)RuntimePlatform.WebGLPlayer;
#endif

            // If ProfilerModes.ProfileAudio is disabled, these will continue to be 0
            Stats.audioStats.playingSources = (int)AccumStats.audioPlayingSources.value;
            Stats.audioStats.pausedSources = (int)AccumStats.audioPausedSources.value;
            Stats.audioStats.totalAudioSourceCount = Stats.audioStats.playingSources + Stats.audioStats.pausedSources;
            Stats.audioStats.audioClipCount = Stats.memoryStats.audioCount;
            Stats.audioStats.numSoundChannelInstances = (int)AccumStats.audioNumSoundChannelInstances.value;
            Stats.audioStats.sampleMemory = (int)AccumStats.audioSampleMemory.value;
            Stats.audioStats.streamDecodeMemory = (int)AccumStats.audioStreamDecodeMemory.value;
            Stats.audioStats.streamFileMemory = (int)AccumStats.audioStreamFileMemory.value;
            Stats.audioStats.codecMemory = (int)AccumStats.audioCodecMemory.value;
            Stats.audioStats.otherMemory = (int)AccumStats.audioOtherMemory.value;
            Stats.audioStats.totalMemoryUsage = Stats.audioStats.sampleMemory + Stats.audioStats.streamFileMemory + Stats.audioStats.streamDecodeMemory
                + Stats.audioStats.otherMemory + Stats.audioStats.codecMemory;
            Stats.audioStats.streamCPUx10 = (int)AccumStats.audioStreamCPUx10.value;
            Stats.audioStats.dspCPUx10 = (int)AccumStats.audioDspCPUx10.value;
            Stats.audioStats.otherCPUx10 = (int)AccumStats.audioOtherCPUx10.value;
            Stats.audioStats.totalCPUx10 = Stats.audioStats.dspCPUx10 + Stats.audioStats.otherCPUx10 + Stats.audioStats.streamCPUx10;
        }
    }
}

#endif
