#include <Unity/Runtime.h>
#include <Baselib.h>
#include <C/Baselib_Memory.h>
#include <C/Baselib_Atomic.h>
#include <Cpp/mpmc_node_queue.h>
#include <Cpp/mpmc_node.h>
#include <Cpp/Lock.h>
#include <baselibext.h>
#include <allocators.h>

#if UNITY_MACOSX
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <unistd.h>
#include <arpa/inet.h>
#include <net/if.h>
#include <ifaddrs.h>
#include <errno.h>
#include <string.h>
#elif UNITY_WINDOWS
#pragma comment(lib, "iphlpapi.lib")
#include <winsock2.h>
#include <iphlpapi.h>
#include <windns.h>
#elif UNITY_IOS
#include <ifaddrs.h>
#include <arpa/inet.h>
#include <sys/ioctl.h>
#include <net/if.h>
#include <unistd.h>
#include <string.h>
#elif UNITY_LINUX
#include <ifaddrs.h>
#include <arpa/inet.h>
#include <net/if.h>
#include <sys/wait.h>
#include <unistd.h>
#include <string.h>
#elif UNITY_ANDROID
#include <sys/socket.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <arpa/inet.h>
#include <net/if.h>
#include <unistd.h>
#include <errno.h>
#include <string.h>
#include <android/log.h>
#endif

using namespace Unity::LowLevel;

static baselib::mpmc_node_queue<baselib::mpmc_node> sendQueue;
static baselib::mpmc_node_queue<baselib::mpmc_node> freeQueue;

static void** builders = nullptr;
static int buildersSize = 0;
static int buildersCapacity = 1024;
static baselib::Lock buildersLock;
static baselib::Lock profilerHashLock;

DOTS_EXPORT(void) PlayerConnectionMt_Init()
{
    if (builders)
        return;
    builders = (void**)unsafeutility_malloc(buildersCapacity * sizeof(void*), 0, Allocator::Persistent);
    unsafeutility_memset((void*)builders, 0, buildersCapacity * sizeof(void*));
}

DOTS_EXPORT(void) PlayerConnectionMt_Shutdown()
{
    // We assume there will be no more processing and we are in a shutdown sequence by the application, so
    // no locking is necessary, and wouldn't help any contention issues anyway since it's freed after this.
    if (!builders)
        return;
    unsafeutility_free(builders, Allocator::Persistent);
    builders = nullptr;
    buildersSize = 0;
    while (!freeQueue.empty())
        freeQueue.try_pop_front();
    while (!sendQueue.empty())
        sendQueue.try_pop_front();
}

DOTS_EXPORT(void) PlayerConnectionMt_QueueFreeStream(void *stream)
{
    freeQueue.push_back((baselib::mpmc_node*)stream);
}

DOTS_EXPORT(void *) PlayerConnectionMt_DequeFreeStream()
{
    return freeQueue.try_pop_front();
}

DOTS_EXPORT(void) PlayerConnectionMt_QueueSendStream(void *stream)
{
    sendQueue.push_back((baselib::mpmc_node*)stream);
}

DOTS_EXPORT(void *) PlayerConnectionMt_DequeSendStream()
{
    return sendQueue.try_pop_front();
}

DOTS_EXPORT(int) PlayerConnectionMt_IsAvailableSendStream()
{
    return sendQueue.empty() ? 0 : 1;
}

DOTS_EXPORT(void**) PlayerConnectionMt_LockStreamBuilders()
{
    buildersLock.Acquire();
    return builders;
}

DOTS_EXPORT(void) PlayerConnectionMt_UnlockStreamBuilders()
{
    buildersLock.Release();
}

DOTS_EXPORT(void) PlayerConnectionMt_RegisterStreamBuilder(void* builder)
{
    BaselibLock lock(buildersLock);

    // The last element must be null so we resize now instead of at capacity
    if (buildersSize + 1 == buildersCapacity)
    {
        int oldCapacity = buildersCapacity;
        void** oldBuilders = builders;

        buildersCapacity *= 2;
        builders = (void**)unsafeutility_malloc(buildersCapacity * sizeof(void*), 0, Allocator::Persistent);
        unsafeutility_memcpy((void*)builders, (void*)oldBuilders, oldCapacity * sizeof(void*));
        unsafeutility_memset((void*)((char*)builders + oldCapacity * sizeof(void*)), 0, (buildersCapacity - oldCapacity) * sizeof(void*));

        unsafeutility_free(oldBuilders, Allocator::Persistent);
    }

    builders[buildersSize++] = builder;
}

DOTS_EXPORT(void) PlayerConnectionMt_UnregisterStreamBuilder(void* builder)
{
    BaselibLock lock(buildersLock);
    for (int i = 0; i < buildersSize; i++)
    {
        if (builders[i] == builder)
        {
            buildersSize--;
            builders[i] = builders[buildersSize];
            builders[buildersSize] = nullptr;
            return;
        }
    }
}

DOTS_EXPORT(void) PlayerConnectionMt_AtomicStore(void** ptr, void* value)
{
    Baselib_atomic_store_ptr_seq_cst_v(ptr, &value);
}

DOTS_EXPORT(int) PlayerConnectionMt_AtomicCompareExchange(void** ptr, void* compare, void* value)
{
    return Baselib_atomic_compare_exchange_strong_ptr_seq_cst_seq_cst_v(ptr, &compare, &value) ? 1 : 0;
}

DOTS_EXPORT(void) PlayerConnectionMt_AtomicAdd64(void* ptr, int64_t value)
{
    int64_t old;
    Baselib_atomic_fetch_add_64_relaxed_v(ptr, &value, &old);
}

DOTS_EXPORT(void) PlayerConnectionMt_LockProfilerHashTables()
{
    profilerHashLock.Acquire();
}

DOTS_EXPORT(void) PlayerConnectionMt_UnlockProfilerHashTables()
{
    profilerHashLock.Release();
}

#if !UNITY_WEBGL

// Based on
// unity\Runtime\Network\NetworkUtility.cpp
DOTS_EXPORT(int) GetIPs(char ips[10][16])
{
    memset(ips, 0, 10 * 16);
    int ipnumber = 0;

#if UNITY_MACOSX || UNITY_LINUX

    struct ifaddrs* myaddrs, * ifa;
    struct sockaddr_in* addr;
    int status;
    status = getifaddrs(&myaddrs);

    for (ifa = myaddrs; ifa != NULL; ifa = ifa->ifa_next)
    {
        if (ifa->ifa_addr == NULL)
            continue;
        // Discard inactive interfaces
        if ((ifa->ifa_flags & IFF_UP) == 0)
            continue;

        // Get IPV4 address
        if (ifa->ifa_addr->sa_family == AF_INET)
        {
            addr = (struct sockaddr_in*)(ifa->ifa_addr);
            if (inet_ntop(ifa->ifa_addr->sa_family, (void*) & (addr->sin_addr), ips[ipnumber], sizeof(ips[ipnumber])) == NULL)
            {
                // skip
            }
            else
            {
                if (strcmp(ips[ipnumber], "127.0.0.1") == 0)
                    continue;
                else
                {
                    ipnumber++;
                    if (ipnumber == 10)
                        break;
                }
            }
        }
    }

    freeifaddrs(myaddrs);

#elif UNITY_WINDOWS

    char internalMemory[1024];
    DWORD tableSize = sizeof(internalMemory);

    PMIB_IPADDRTABLE ipAddrTable = (MIB_IPADDRTABLE*)internalMemory;

    //First we try to use internal buffer
    DWORD status = GetIpAddrTable(ipAddrTable, &tableSize, 0);

    if (status == ERROR_INSUFFICIENT_BUFFER)
    {
        // If internal buffer is not enough, allocate appropriate size
        ipAddrTable = (MIB_IPADDRTABLE*)unsafeutility_malloc(tableSize, 0, Allocator::Persistent);

        // Memory allocation failed for GetIpAddrTable
        if (ipAddrTable == NULL)
            return 0;

        status = GetIpAddrTable(ipAddrTable, &tableSize, 0);
    }

    if (status != NO_ERROR)
    {
        ipnumber = 0;
    }
    else
    {
        union AddressIp4
        {
            struct { uint8_t ip4[4]; };
            DWORD ipPacked;
        };

        AddressIp4 localhost;
        localhost.ip4[0] = 127;
        localhost.ip4[1] = 0;
        localhost.ip4[2] = 0;
        localhost.ip4[3] = 1;

        IN_ADDR in_addr;

        for (int i = 0; i < (int)ipAddrTable->dwNumEntries; i++)
        {
            if (localhost.ipPacked == ipAddrTable->table[i].dwAddr)
                continue;

            in_addr.S_un.S_addr = (u_long)ipAddrTable->table[i].dwAddr;

            strcpy(ips[ipnumber], inet_ntoa(in_addr));

            if (++ipnumber == 10)
                break;
        }
    }

    if ((void*)ipAddrTable != (void*)internalMemory)
    {
        unsafeutility_free((void*)ipAddrTable, Allocator::Persistent);
        ipAddrTable = NULL;
    }

#else

    struct ifconf ifc;
    struct ifreq ifreqs[8];
    memset(&ifc, 0, sizeof(ifc));
    ifc.ifc_buf = (char*)(ifreqs);
    ifc.ifc_len = sizeof(ifreqs);

    struct ifreq* IFR;

    int sock = socket(AF_INET, SOCK_DGRAM, 0);
    if (sock < 0)
    {
        // android.permission.INTERNET not available?
        return 0;
    }

    if ((ioctl(sock, SIOCGIFCONF, (char*)&ifc)) < 0)
        ifc.ifc_len = 0;

    char* ifrp;
    struct ifreq* ifr, ifr2;
    IFR = ifc.ifc_req;
    for (ifrp = ifc.ifc_buf;
        (ifrp - ifc.ifc_buf) < ifc.ifc_len;
        ifrp += sizeof(struct ifreq))
    {
        ifr = (struct ifreq*)ifrp;

        // Get network interface flags
        ifr2 = *ifr;
        if (ioctl(sock, SIOCGIFFLAGS, &ifr2) < 0)
            continue;

        // Discard inactive interfaces
        if ((ifr2.ifr_flags & IFF_UP) == 0)
            continue;

        // Skip the loopback/localhost interface
        if ((ifr2.ifr_flags & IFF_LOOPBACK))
            continue;

        if (ifr->ifr_addr.sa_family != AF_INET)
            continue;

        strcpy(&ips[ipnumber++][0], inet_ntoa(((struct sockaddr_in*)(&ifr->ifr_addr))->sin_addr));
        if (ipnumber == 10)
            break;
    }

    close(sock);

#endif

    return ipnumber;
}

#endif  // !UNITY_WEBGL
