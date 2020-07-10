using System;
using System.Collections.Generic;
using System.IO;
using Unity.Build;
using Unity.Collections;
using Unity.Entities.Serialization;
using Unity.Scenes;
using Unity.Core.Compression;
using Unity.Entities.Runtime;

namespace Unity.Entities.Runtime.Build
{
    internal static class WorldExport
    {
        internal static void UpdateManifest(BuildManifest manifest, string scenePath, Hash128 subSceneGuid, SceneSectionData[] sections, DirectoryInfo dataDirectory, string outputDirectory)
        {
            //Add the entity header file and all the sections to the manifest
            List<FileInfo> exportedFiles = new List<FileInfo>();
            var headerFile = dataDirectory.GetFile(Path.Combine(outputDirectory,  subSceneGuid + "." + EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader)));
            exportedFiles.Add(headerFile);
            foreach (var section in sections)
            {
                var entityFile = dataDirectory.GetFile(Path.Combine(outputDirectory, section.SceneGUID + "." + section.SubSectionIndex + "." + EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesBinary)));
                exportedFiles.Add(entityFile);
            }
            manifest.Add(new Guid(subSceneGuid.ToString()), scenePath, exportedFiles);
        }

        internal static DirectoryInfo GetOrCreateDataDirectoryFrom(DirectoryInfo targetDirectory)
        {
            var dataDirectory = targetDirectory.Combine("Data");
            if (string.IsNullOrEmpty(dataDirectory.FullName))
            {
                throw new ArgumentException($"Invalid output file directory: {dataDirectory.FullName}", nameof(dataDirectory.FullName));
            }
            if (!Directory.Exists(dataDirectory.FullName))
            {
                Directory.CreateDirectory(dataDirectory.FullName);
            }
            return dataDirectory;
        }

        internal static DirectoryInfo GetOrCreateSubSceneDirectoryFrom(DirectoryInfo targetDirectory)
        {
            var subscenesDirectory = GetOrCreateDataDirectoryFrom(targetDirectory).Combine("SubScenes");
            if (string.IsNullOrEmpty(subscenesDirectory.FullName))
            {
                throw new ArgumentException($"Invalid output file directory: {subscenesDirectory.FullName}", nameof(subscenesDirectory.FullName));
            }
            if (!Directory.Exists(subscenesDirectory.FullName))
            {
                Directory.CreateDirectory(subscenesDirectory.FullName);
            }
            return subscenesDirectory;
        }

        public static bool WriteWorldToFile(World world, FileInfo outputFile)
        {
            // TODO need to bring this back
#if false
            // Check for missing assembly references
            var unresolvedComponentTypes = GetAllUsedComponentTypes(world).Where(t => !DomainCache.IsIncludedInProject(project, t.GetManagedType())).ToArray();
            if (unresolvedComponentTypes.Length > 0)
            {
                foreach (var unresolvedComponentType in unresolvedComponentTypes)
                {
                    var type = unresolvedComponentType.GetManagedType();
                    Debug.LogError($"Could not resolve component type '{type.FullName}' while exporting {scenePath.ToHyperLink()}. Are you missing an assembly reference to '{type.Assembly.GetName().Name}' ?");
                }
                return false;
            }
#endif

            var directoryName = Path.GetDirectoryName(outputFile.FullName);
            if (string.IsNullOrEmpty(directoryName))
            {
                throw new ArgumentException($"Invalid output file directory: {directoryName}", nameof(outputFile));
            }

            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            // Merges the entities and shared component streams, (optionally) compresses them, and finally serializes to disk with a small header in front
            using (var fileStream = new StreamBinaryWriter(outputFile.FullName))
            using (var entitiesWriter = new MemoryBinaryWriter())
            {
                var entityRemapInfos = new NativeArray<EntityRemapUtility.EntityRemapInfo>(world.EntityManager.EntityCapacity, Allocator.Temp);
                SerializeUtility.SerializeWorldInternal(world.EntityManager, entitiesWriter, out var referencedObjects, entityRemapInfos, isDOTSRuntime: true);
                entityRemapInfos.Dispose();

                if (referencedObjects != null)
                {
                    throw new ArgumentException("We are serializing a world that contains UnityEngine.Object references which are not supported in Dots Runtime.");
                }

                unsafe
                {
                    var worldHeader = new SceneHeader();
                    worldHeader.DecompressedSize = entitiesWriter.Length;
                    worldHeader.Codec = Codec.LZ4;
                    worldHeader.SerializeHeader(fileStream);

                    if (worldHeader.Codec != Codec.None)
                    {
                        int compressedSize = CodecService.Compress(worldHeader.Codec, entitiesWriter.Data, entitiesWriter.Length, out var compressedData);
                        fileStream.WriteBytes(compressedData, compressedSize);
                    }
                    else
                    {
                        fileStream.WriteBytes(entitiesWriter.Data, entitiesWriter.Length);
                    }
                }
            }

            return true;
        }
    }
}
