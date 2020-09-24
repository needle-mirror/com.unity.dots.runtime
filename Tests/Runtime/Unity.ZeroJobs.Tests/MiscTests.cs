using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Tiny.Tests.Common;
using System;
using Hash128 = UnityEngine.Hash128;
using System.Threading;
using System.Runtime.CompilerServices;
using Unity.Core;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unity.ZeroJobs.Tests
{
    public class MiscTests : TinyTestFixture
    {
        static unsafe void FillMemory(byte* mem, uint size)
        {
            for (int i = 0; i < size; ++i)
                *mem = (byte)i;
        }

        static void ExitTempScopesLocally()
        {
            TempMemoryScope.ExitScope();  // Per-test scope

            Assert.Zero(UnsafeUtility.GetTempUsed());
        }
        static void EnterTempScopesLocally()
        {
            Assert.Zero(UnsafeUtility.GetTempUsed());

            TempMemoryScope.EnterScope();  // Per-test scope
        }

        [Test]
        public unsafe void GetOrCreateSharedMemoryBasic()
        {
            const uint kMemSize = 64;
            const uint kAlignment = 16;
            Hash128 hash1 = new Hash128(0, 1, 2, 3);
            Hash128 hash2 = new Hash128(3, 2, 1, 0);

            // NOTE: These tests will leak as memory from GetOrCreateSharedMemory is meant to be freed with the process
            {
                void* pMem1 = Burst.LowLevel.BurstCompilerService.GetOrCreateSharedMemory(ref hash1, kMemSize, kAlignment);
                void* pMem2 = Burst.LowLevel.BurstCompilerService.GetOrCreateSharedMemory(ref hash1, kMemSize, kAlignment);
                Assert.AreEqual((ulong)pMem1, (ulong)pMem2);
                Assert.IsTrue(((ulong)pMem1 & (kAlignment - 1)) == 0);
                FillMemory((byte*)pMem1, kMemSize);
                Assert.IsTrue(UnsafeUtility.MemCmp(pMem1, pMem2, kMemSize) == 0);
            }

            {
                byte* pMem1 = (byte*)Burst.LowLevel.BurstCompilerService.GetOrCreateSharedMemory(ref hash1, kMemSize, kAlignment);
                byte* pMem2 = (byte*)Burst.LowLevel.BurstCompilerService.GetOrCreateSharedMemory(ref hash2, kMemSize, kAlignment);
                Assert.AreNotEqual((ulong)pMem1, (ulong)pMem2);
                Assert.IsTrue(((ulong)pMem1 & (kAlignment - 1)) == 0);
                Assert.IsTrue(((ulong)pMem2 & (kAlignment - 1)) == 0);
                FillMemory((byte*)pMem1, kMemSize);
                Assert.IsFalse(UnsafeUtility.MemCmp(pMem1, pMem2, kMemSize) == 0);
            }
        }

        // The following are modifications of Burst SharedStatic tests. Burst doesn't Build+Test against the
        // DOTS Runtime ecosystem so we validate SharedStatic support here as ZeroJobs has code to specifically allow
        // Shared Statics to work for DOTS Runtime
        [Test]
        public void SharedStaticsTestSimple()
        {
            var sharedStatic1 = SharedStatic<int>.GetOrCreate<MiscTests, TestSimpleContext>();
            sharedStatic1.Data = 5;
            var sharedStatic2 = SharedStatic<int>.GetOrCreate<MiscTests, TestSimpleContext>();
            Assert.AreEqual(sharedStatic1.Data, sharedStatic2.Data);
            var sharedStatic3 = SharedStatic<int>.GetOrCreate<MiscTests, TestDifferentContext>();
            Assert.AreNotEqual(sharedStatic1.Data, sharedStatic3.Data);
        }

        [Test]
        public void SharedStaticsTestGeneric()
        {
            var sharedStatic1 = TestGenericProvider<TestGenericContext>.Value;
            sharedStatic1.Data = 5;
            var sharedStatic2 = TestGenericProvider<TestGenericContext>.Value;
            Assert.AreEqual(sharedStatic1.Data, sharedStatic2.Data);
            var sharedStatic3 = TestGenericProvider<TestDifferentGenericContext>.Value;
            Assert.AreNotEqual(sharedStatic1.Data, sharedStatic3.Data);
        }

#if !NET_DOTS
        [Test]
        public void SharedStaticsTestInvalidSize()
        {
            var sharedStatic1 = SharedStatic<int>.GetOrCreate<MiscTests, TestInvalidSizeContext>();
            // This allocation should fail as we don't allow to reallocate with a different (bigger) size for the same key
            var exception = Assert.Throws<InvalidOperationException>(() => SharedStatic<long>.GetOrCreate<MiscTests, TestInvalidSizeContext>());
            StringAssert.Contains("Unable to create a SharedStatic", exception.Message);
            // This allocation should fail as we don't allow to reallocate with a different (smaller) size for the same key
            exception = Assert.Throws<InvalidOperationException>(() => SharedStatic<short>.GetOrCreate<MiscTests, TestInvalidSizeContext>());
            StringAssert.Contains("Unable to create a SharedStatic", exception.Message);
        }
#endif

        [Test]
        public unsafe void UnsafeUtilityReallocDoesntTrash()
        {
            byte* data = (byte*)UnsafeUtility.Malloc(128, 16, Collections.Allocator.Persistent);
            for (int i = 0; i < 128; i++)
                data[i] = (byte)i;
            data = (byte*)UnsafeUtility.Realloc(data, 256, 16, Collections.Allocator.Persistent);
            for (int i = 0; i < 128; i++)
                Assert.AreEqual(data[i], (byte)i);
            UnsafeUtility.Free(data, Collections.Allocator.Persistent);
        }

        [Test]
        public unsafe void UnsafeUtilityReallocDoesntLeak()
        {
            long oldSize = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);
            byte* data = (byte*)UnsafeUtility.Malloc(128, 16, Collections.Allocator.Persistent);
            data = (byte*)UnsafeUtility.Realloc(data, 256, 16, Collections.Allocator.Persistent);
            UnsafeUtility.Free(data, Collections.Allocator.Persistent);
            Assert.AreEqual(oldSize, UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent));
        }

        [Test]
        public unsafe void UnsafeUtilityReallocNull()
        {
            long oldSize = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);
            void* data = null;
            Assert.DoesNotThrow(() => data = (byte*)UnsafeUtility.Realloc(null, 256, 16, Collections.Allocator.Persistent));
            Assert.IsTrue(UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent) >= oldSize + 256);
            UnsafeUtility.Free(data, Collections.Allocator.Persistent);
            Assert.AreEqual(oldSize, UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent));
        }

        [Test]
        public unsafe void UnsafeUtilityReallocSize0()
        {
            long oldSize = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);
            byte* data = (byte*)UnsafeUtility.Malloc(128, 16, Collections.Allocator.Persistent);
            data = (byte*)UnsafeUtility.Realloc(data, 256, 16, Collections.Allocator.Persistent);
            // Should be equivalent to free
            data = (byte*)UnsafeUtility.Realloc(data, 0, 0, Collections.Allocator.Persistent);
            Assert.AreEqual(oldSize, UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent));
            Assert.IsTrue(data == null);
        }

        [Test]
        public unsafe void TempAllocatorRewindSavesUserData()
        {
            void*[] mem = new void*[4096];
            for (int i = 0; i < mem.Length; i++)
            {
                UnsafeUtility.EnterTempScope();
                UnsafeUtility.SetTempScopeUser((void*)(i + 1));
                mem[i] = UnsafeUtility.Malloc(4096, 0, Collections.Allocator.Temp);
            }

            for (int i = mem.Length - 1; i >= 0; i--)
            {
                Assert.IsTrue((void*)(i + 1) == UnsafeUtility.GetTempScopeUser());
                UnsafeUtility.Free(mem[i], Collections.Allocator.Temp);
                UnsafeUtility.ExitTempScope();
            }
        }

        [Test]
        public unsafe void TempAllocatorRewindFreesMemUsage()
        {
            ExitTempScopesLocally();

            void*[] mem = new void*[4096];
            for (int i = 0; i < mem.Length; i++)
            {
                TempMemoryScope.EnterScope();
                mem[i] = UnsafeUtility.Malloc(4096, 0, Collections.Allocator.Temp);
            }

            int memUsage = UnsafeUtility.GetTempUsed();
            for (int i = mem.Length - 1; i >= 0; i--)
            {
                UnsafeUtility.Free(mem[i], Collections.Allocator.Temp);
                Assert.AreEqual(memUsage, UnsafeUtility.GetTempUsed());

                TempMemoryScope.ExitScope();
                Assert.Less(UnsafeUtility.GetTempUsed(), memUsage);

                memUsage = UnsafeUtility.GetTempUsed();
            }

            Assert.Zero(memUsage);
            EnterTempScopesLocally();
        }

        [Test]
        public unsafe void TempAllocatorNullIfNoScope()
        {
            ExitTempScopesLocally();

            // Try to alloc
            Assert.IsTrue(UnsafeUtility.Malloc(24, 0, Collections.Allocator.Temp) == null);

            // Try get user
            Assert.IsTrue(UnsafeUtility.GetTempScopeUser() == null);

            EnterTempScopesLocally();
        }

        [Test]
        public unsafe void TempAllocatorRenestingDoesntLeak()
        {
            void*[] mem = new void*[4096];
            int tempUsedBase = UnsafeUtility.GetTempUsed();
            int tempUsedTop = -1;
            int tempCapTop = -1;

            for (int j = 0; j < 20; j++)
            {
                for (int i = 0; i < mem.Length; i++)
                {
                    UnsafeUtility.EnterTempScope();
                    mem[i] = UnsafeUtility.Malloc(4096, 0, Collections.Allocator.Temp);
                }

                if (tempUsedTop == -1)
                {
                    tempUsedTop = UnsafeUtility.GetTempUsed();
                    tempCapTop = UnsafeUtility.GetTempCapacity();
                }
                Assert.AreEqual(tempUsedTop, UnsafeUtility.GetTempUsed());
                Assert.AreEqual(tempCapTop, UnsafeUtility.GetTempCapacity());

                for (int i = mem.Length - 1; i >= 0; i--)
                {
                    UnsafeUtility.Free(mem[i], Collections.Allocator.Temp);
                    UnsafeUtility.ExitTempScope();
                }

                Assert.AreEqual(tempUsedBase, UnsafeUtility.GetTempUsed());
            }
        }

        [Test]
        public unsafe void TempAllocatorRenestingUserNull()
        {
            void*[] mem = new void*[4096];
            for (int j = 0; j < 2; j++)
            {
                for (int i = 0; i < mem.Length; i++)
                {
                    UnsafeUtility.EnterTempScope();
                    if (j == 0)
                        UnsafeUtility.SetTempScopeUser((void*)(i + 1));
                    else
                        Assert.IsTrue(UnsafeUtility.GetTempScopeUser() == null);
                    mem[i] = UnsafeUtility.Malloc(4096, 0, Collections.Allocator.Temp);
                }

                for (int i = mem.Length - 1; i >= 0; i--)
                {
                    UnsafeUtility.Free(mem[i], Collections.Allocator.Temp);
                    UnsafeUtility.ExitTempScope();
                }
            }
        }

        [Test]
        public unsafe void TempAllocatorCapacityGrowsAndResetsManyAllocs()
        {
            void*[] mem = new void*[4096];

            ExitTempScopesLocally();

            int oldCap = UnsafeUtility.GetTempCapacity();

            for (int i = 0; i < mem.Length; i++)
            {
                UnsafeUtility.EnterTempScope();
                mem[i] = UnsafeUtility.Malloc(4096, 0, Collections.Allocator.Temp);
            }

            Assert.Greater(UnsafeUtility.GetTempCapacity(), oldCap);

            for (int i = mem.Length - 1; i >= 0; i--)
            {
                UnsafeUtility.Free(mem[i], Collections.Allocator.Temp);
                UnsafeUtility.ExitTempScope();
            }

            Assert.AreEqual(UnsafeUtility.GetTempCapacity(), oldCap);

            EnterTempScopesLocally();
        }

        [Test]
        public unsafe void TempAllocatorLargerThanDefaultSize()
        {
            const int kSize = 1024 * 1024 * 4;

            ExitTempScopesLocally();

            UnsafeUtility.EnterTempScope();
            int oldCap = UnsafeUtility.GetTempCapacity();
            void* largeTemp = UnsafeUtility.Malloc(kSize, 0, Collections.Allocator.Temp);

            Assert.Greater(UnsafeUtility.GetTempCapacity(), oldCap);
            Assert.GreaterOrEqual(UnsafeUtility.GetTempUsed(), kSize);

            UnsafeUtility.Free(largeTemp, Collections.Allocator.Temp);
            UnsafeUtility.ExitTempScope();

            Assert.AreEqual(UnsafeUtility.GetTempCapacity(), oldCap);

            EnterTempScopesLocally();
        }

#if !NET_DOTS
        [Test]
        public unsafe void TempAllocatorMultithreadUnique()
        {
            Thread[] threads = new Thread[16];
            System.Random rand = new System.Random();
            ManualResetEvent mre = new ManualResetEvent(false);

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object ra) =>
                {
                    int r = (int)ra;
                    UnsafeUtility.EnterTempScope();
                    int oldSize = UnsafeUtility.GetTempUsed();
                    void* mem = UnsafeUtility.Malloc(r, 0, Collections.Allocator.Temp);
                    mre.WaitOne();

                    Assert.GreaterOrEqual(UnsafeUtility.GetTempUsed(), oldSize + r);
                    Assert.Less(UnsafeUtility.GetTempUsed(), oldSize + r + 128);

                    UnsafeUtility.Free(mem, Collections.Allocator.Temp);
                    UnsafeUtility.ExitTempScope();
                });
                threads[i].Start(rand.Next(1, 32768));
            }

            Thread.Sleep(200);
            mre.Set();

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();
        }
#endif

        // For now, this test can't work because we no longer track memory in safety handles due to them
        // existing in NativeJobs. Soon, we want to implement tracking and leak detection since safety
        // handle leaks are one of the most common memory leaks in DOTS/DOTSRT.
/*        [Test]
        public unsafe void AtomicSafetyShutdownFreesReleased()
        {
			// Start fresh
			AtomicSafetyHandle.Shutdown();
            long oldSize = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);

			AtomicSafetyHandle.Initialize();
			var ash = AtomicSafetyHandle.Create();
			var ash2 = AtomicSafetyHandle.Create();
			AtomicSafetyHandle.Release(ash);
			AtomicSafetyHandle.Release(ash2);

			// Cached the created safety nodes in a concurrent queue, so the memory wasn't freed
            Assert.IsTrue(UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent) > oldSize);

			AtomicSafetyHandle.Shutdown();
			
			// NOW they should be freed
            Assert.AreEqual(oldSize, UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent));
        }
*/
        struct TestSimpleContext
        {
        }

        struct TestDifferentContext
        {
        }

        struct TestGenericContext
        {
        }

        struct TestDifferentGenericContext
        {
        }

        struct TestInvalidSizeContext
        {
        }

        struct TestGenericProvider<T>
        {
            public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<MiscTests, T>();
        }
    }
}
