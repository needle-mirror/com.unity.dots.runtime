#include <string>
#include <map>
#include <cstdio>
#include <exception>

#if UNITY_WINDOWS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <libloaderapi.h>
#define DLLEXPORT __declspec(dllexport)
#else
    #if defined(__GNUC__) || defined(__clang__)
        // we compile with -fvisibility=hidden
        #define DLLEXPORT __attribute__((visibility("default")))
    #else // __GNUC__ and __clang__ are not defined
        #define DLLEXPORT
    #endif

#include <dlfcn.h>
#endif

using std::string;
using std::map;


void* loadLibrary(const char* libname)
{
    #if UNITY_WINDOWS
    return (void*)LoadLibraryA(libname);
    #else
    const char* name = (string(libname) + ".dylib").c_str();
    void* ret = (void*)dlopen(name, RTLD_NOW);
    if (ret == NULL)
    {
        const char* name2 = (string(libname) + ".bundle").c_str();
        ret = (void*)dlopen(name2, RTLD_NOW);
    }
        
    return ret;
    #endif
}

void* loadFn(void* library, const char* fname)
{
#if UNITY_WINDOWS
        return GetProcAddress((HMODULE)library, fname);
#else
        auto ret = dlsym(library, fname);
        return ret;
#endif
}


static map<string, void*> moduleNameToPointer;
static map<string, void*> functionNameToPointer;

//pasted with mild editing from big unity's Runtime/Burst/Burst.cpp
const void* NativeGetExternalFunctionPointerCallback(const char* name)
{
    if (name == NULL)
    {
        printf("ERROR: passed null to NativeGetExternalFunctionPointerCallback\n");
        return NULL;
    }
    string nameRef(name);

    auto it = functionNameToPointer.find(name);
    auto end = functionNameToPointer.end();

    if (it != end)
    {
        return it->second;
    }

    // Get DllImport function
    //starts_with in stl
    if (nameRef.rfind("#dllimport:", 0) == 0) 
    {
        auto separatorIndex = nameRef.find_first_of('|');
        //Assert(seperatorIndex > 11 && seperatorIndex != nameRef.length() - 1);

        // Length of the prefix
        auto libraryNameOffset = 11;
        string libraryName(nameRef.substr(libraryNameOffset, separatorIndex - libraryNameOffset));
        string functionName(nameRef.substr(separatorIndex + 1));

        auto moduleIt = moduleNameToPointer.find(libraryName);
        void* library;
        if (moduleIt == moduleNameToPointer.end())
        {
            // Load the library
            //hax
            //HMODULE library = SetDllDirectoryA(libraryName.c_str(), 0);
            if (libraryName.size() > 4 && libraryName.substr(libraryName.size()-4, 4) == ".dll")
            {
                libraryName = libraryName.substr(0, libraryName.size()-4);
            }
           
            library = loadLibrary(libraryName.c_str());
            if (library == NULL)
            {
                printf("Unable to load plugin `%s`\n", libraryName.c_str());
                return NULL;
            }

            moduleNameToPointer[libraryName] = library;
        }
        else
        {
            library = (void*)moduleIt->second;
        }
        void* functionPtr = loadFn(library, functionName.c_str());


        if (functionPtr == NULL)
        {
            printf("Unable to load function `%s` from plugin `%s`", functionName.c_str(), libraryName.c_str());
        }
        functionNameToPointer[functionName] = functionPtr;
        return functionPtr;
    }

    printf("Unable to find internal function `%s`", name);
    return NULL;

}

extern "C" DLLEXPORT void burst_abort(const char* exceptionName, const char* errorMessage)
{
    printf("Exception %s thrown with error message %s. Turn off burst for this job to learn more.\n", exceptionName, errorMessage);
    std::terminate();
}


typedef const void* (*BurstInitializeCallbackDelegate)(const char* name);


//todo: move to platforms repo when platforms gets easier to deal with,
//and/or this code stabilizes more
#if UNITY_IOS
extern "C" void* Staticburst_initialize(BurstInitializeCallbackDelegate cb);
#endif

typedef void(*BurstInitializeDelegate)(BurstInitializeCallbackDelegate callback);


void BurstInit_iOS()
{
#if UNITY_IOS // avoid linker errors
    Staticburst_initialize(NativeGetExternalFunctionPointerCallback);
#endif
}

void BurstInit_Desktop()
{
    functionNameToPointer["burst_abort"] = (void*)burst_abort;
    auto library = loadLibrary("lib_burst_generated");

    if (library == NULL)
    {
#if DEBUG
        printf("ERROR: failed to load lib_burst_generated shared library.\n");
        fflush(stdout);
#endif //DEBUG
        return;
    }

    auto burstInit = (BurstInitializeDelegate)loadFn(library, "burst.initialize");

    if (burstInit != NULL)
        burstInit(NativeGetExternalFunctionPointerCallback);
    else
    {
#if DEBUG
        printf("ERROR: Couldn't find method burst.initialize in lib_burst_generated.dll\n");
        fflush(stdout);
#endif //DEBUG
    }
    
}



extern "C" DLLEXPORT void BurstInit()
{
#if UNITY_WEBGL || UNITY_ANDROID
	//burst is disabled on webgl & android
    return;
#elif UNITY_IOS
    BurstInit_iOS();
#else 
    BurstInit_Desktop();
#endif //UNITY_IOS
}

