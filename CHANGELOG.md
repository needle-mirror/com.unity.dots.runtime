# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.32.0] - 2020-11-13

### Added

* `UnityInstance` was removed from com.unity.tiny and added to com.unity.dots.runtime.

### Changed

* Upgraded burst to 1.4.1.
* Upgraded com.unity.platforms to 0.9.0-preview.12.
* Update minimum editor version to 2020.1.2f1
* Added partial job safety support for generic job types. Generic Jobs will now have all non-generic parameter fields be validated as non-generic jobs would normally. We however still do not validate safety for container types that are included in a job via a generic parameter field (e.g. `struct MyJob<T> : IJob { public T m_WontBeChecked; }`).
* Updated platform packages to version `0.10.0-preview.1`.

### Fixed

* Selected configurations in the "Generate DOTS C# Solution" window would be ignored if the row was collapsed. They are now properly recognized as selected.
* All opened scenes are saved before making a build
* Move all editor logs (conversion logs, SceneExportLog.txt, BuildLog.txt, UnityShaderCompiler.log ) to the Unity Logs folder.



## [0.31.0] - 2020-09-24

### Added

* Per-thread Allocator.Temp scoping.

### Fixed

* "Generate Solution Window" will no longer generate a solution with build configurations that may conflict with existing test program build configurations.

## [0.30.0] - 2020-08-26

### Added

* Added support for `ISystemBase` system types.
* Logging includes log type information for editor player connection logs
* Logging includes stack trace
* Unity.Tiny.IO and Unity.Tiny.Thread.Native AssemblyDefinitions have been moved from the `com.unity.tiny` package into the `com.unity.dots.runtime` package.

### Changed

* Upgraded Burst to 1.4.0-preview.4 version.
* Managed Debugging's "Use Build Configuration" option now enabled Managed Debugging when build in `debug` or `develop` configurations. It is recommended to use the `develop` configuration when using the managed debugger (as debug builds are _very_ slow)
*Moved IL2CPPSettings and BuildSettingsToggle to Unity.Build.DotsRuntime

### Fixed

* WebGL log spam if player connection can't be established
* Multithreaded builds calling ScheduleBatch and ScheduleJobParallelFor can now be Burst compiled. Previously Burst would fail due to passing/returning structs to/from native functions.


## [0.29.0] - 2020-08-04

### Added

* Add support for managed code debugging with Wasm
* Unity.Tiny.<various>.Tests are now run in IL2CPP on desktop and part of our CI.
* Unity.Collections.Tests are now run in IL2CPP on desktop and part of our CI.
* Burst is now supported for non-debug Wasm and AsmJs builds.
* Optimized Allocator.Temp allocations

### Changed

* Upgraded Burst to 1.4.0-preview.2 version.
* Managed components are no longer supported when building for in the Tiny configuration. Thus all components must be defined using `struct` instead of `class`.

### Deprecated


### Removed


### Fixed

* Fixes an issue where PlayerConnection message handlers could not allocate memory from `Allocator.Temp`
* Adds build compatibility support (not feature parity) for Unity 2020.2 Unity.Jobs API
* Post-schedule dependency update in some job configurations
* Fixed a bug where IL2CPP Script Debugging would not be enabled when using the `UseBuildConfiguration` value and building in `develop`
* Specifically timed race conditions when creating markers while transmitting player connection buffers

### Security




## [0.28.0] - 2020-07-10

### Added

* Support for sources common for BuildProgram and for Editor assemblies (these sources should be in "bee" folders instead of "bee~" for BuildProgram specific sources)
* Job Debugger with 1:1 parity to Hybrid DOTS Job Debugger

### Changed

* Updated minimum Unity Editor version to 2020.1.0b15 (40d9420e7de8)
* Upgraded Burst to 1.4.0-preview.1 version.

### Deprecated


### Removed


### Fixed

* Negative define constraints in assembly definition files (e.g. !NET_DOTS) would prevent an assembly from being built even when the define constraint define was indeed not set.
* Fixed an issue where TypeInfo for components would be incorrect if the size of a underlying field type varies per platform (e.g. IntPtr)

### Security




## [0.27.0] - 2020-05-27

### Added

* Multithreading profiler support
* Multithreading memory tracking to unsafeutility
* unsafeutility_realloc for use in 3rd party libraries
* Support for profiler stats collection
* Player Connection buffers reuse memory rather than freeing and reallocate

### Changed

* Updated minimum Unity Editor version to 2020.1.0b9 (9c0aec301c8d)

### Fixed

* Submit player connection buffer more than once should not queue it more than once
* Regression - player connection is not burstable with string message guids
* Logging after DOTS Runtime shutdown would crash


## [0.26.0] - 2020-05-15

### Changed

* Fixes the `OutputBuildDirectory` BuildComponent to accept absolute paths. Previously builds would fail whenever an absolute path was used.
* Removed expired API `OptionSet.GetOptionForName(string option)`
* Adds [NativePInvokeCallback] to the global namespace. This is an IL2CPP attribute to signify a method is unmanaged/native and can thus be invoked directly from native code. `Marshal.GetFunctionPointerForDelegate(decoratedMethod)` will not generate a reverse callback wrapper attaching to the managed VM for the delegate method.

## [0.25.0] - 2020-04-30
* Added an error message guiding the user to fix their BuildConfiguration asset if the asset name contains a space, rather than having the build fail with a seemingly unrelated error.
* Fixed an issue where the first component defined in an assembly would not get a generated Equals() and GetHashCode() implementation which could result in incorrect runtime behaviour as components could not be properly compared.

## [0.24.0] - 2020-04-09
* Added Burst support for Android.
* Added Player Connection support allowing a DOTS Runtime application to interact with Unity Editor.
* Added Profiler support allowing DOTS Runtime to interact with Unity Editor profiler and integrate profiling into builds.
* Removed support for IJobForEach from DOTS-Runtime.
* Added `Generate DOTS C# Solution` option to the editor's Assets toolbar. This window allows DOTS Runtime users to select which buildconfigurations to include their DOTS Runtime solution explicitly, rather than the solution only containing whatever was built recently.
* The `DotsRuntimeBuildProfile.RootAssembly` field has been moved into a new `IBuildComponent` called `DotsRuntimeRootAssembly` which is required for building BuildConfiguration assets using the "Default DOTS Runtime Pipeline". This separation is made to allow build configurations to more easily share settings across multiple Root Assemblies.
* GUID references are now supported in asmdefs.
* The `DotsRuntimeBuildProfile.UseBurst` setting has been moved to the `DotsRuntimeBurstSettings` build component.
* The `DotsRuntimeScriptingDefines` component has been replaced by the `DotsRuntimeScriptingSettings` build component. This replacement is made to allow for scripting settings to all live in a common location.
* Fixes an issue where old build artifacts could cause builds to break after updating `com.unity.tiny` if build configuration processing code has been changed.

## [0.23.0] - 2020-03-03
* Added window to select what configs to generate the dots solution with.
* Stopped requiring a reference to Unity.Tiny.Main and Unity.Tiny.EntryPoint for a root assembly to work. Implicitly adds those if an entry point is not referenced.

## [0.22.0] - 2020-02-05
* Add new required Dots runtime project settings build component to author the DisplayInfo runtime component (resolution, color space ..)


## [0.2.0] - 2020-01-21

* Full codegen support for Jobs API

## [0.1.0] - 2020-12-14

* Fixed "Project 'buildprogram' has already been added to this solution" error visible when opening generated solutions in VisualStudio.
* Update `Bee.exe` with a signed version
* Update the package to use Unity '2019.3.0f1' or later
* Initial release of *Dots Runtime*.
