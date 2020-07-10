using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Build;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Unity.Entities.Runtime.Build
{
    //TODO internal class to remove when deprecating DotsRuntimeBuildPipeline (old way of exporting scenes in tiny)
    internal class TinyExportDriver : GameObjectConversionSettings
    {
        class Item
        {
            public Hash128 Guid;
            public string AssetPath;
            public FileInfo ExportFileInfo;
            public bool Exported;
        }

        readonly DirectoryInfo m_ExportDataRoot;
        readonly Dictionary<Object, Item> m_Items = new Dictionary<Object, Item>();

#if USE_INCREMENTAL_CONVERSION
        public TinyExportDriver(BuildConfiguration config, DirectoryInfo exportDataRoot, World destinationWorld, BlobAssetStore blobAssetStore) : base(destinationWorld, GameObjectConversionUtility.ConversionFlags.AddEntityGUID, blobAssetStore)
        {
            BuildConfiguration = config;
            m_ExportDataRoot = exportDataRoot;
            FilterFlags = WorldSystemFilterFlags.DotsRuntimeGameObjectConversion;
        }

#else
        public TinyExportDriver(BuildConfiguration config, DirectoryInfo exportDataRoot)
        {
            BuildConfiguration = config;
            m_ExportDataRoot = exportDataRoot;
            FilterFlags = WorldSystemFilterFlags.DotsRuntimeGameObjectConversion;
        }

#endif

        public override Hash128 GetGuidForAssetExport(Object asset)
        {
            if (!m_Items.TryGetValue(asset, out var found))
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                var guid = GetGuidForUnityObject(asset);
                if (!guid.IsValid)
                {
                    return new Hash128();
                }

                var exportFileInfo = m_ExportDataRoot.GetFile(guid.ToString());

                m_Items.Add(asset, found = new Item
                {
                    Guid = guid,
                    AssetPath = assetPath,
                    ExportFileInfo = exportFileInfo,
                });
            }

            return found.Guid;
        }

        public override Stream TryCreateAssetExportWriter(Object asset)
        {
            if (!m_Items.ContainsKey(asset))
            {
                UnityEngine.Debug.LogError($"TinyExportDriver: Trying to create export writer for asset {asset}, but it was never exported");
                return null;
            }

            var item = m_Items[asset];
            if (item.Exported)
                return null;

            item.Exported = true;
            item.ExportFileInfo.Directory.Create();

            return item.ExportFileInfo.Create();
        }

        public void Write(BuildManifest manifest)
        {
            foreach (var thing in m_Items.Values.Where(i => i.Exported))
                manifest.Add(new Guid(thing.Guid.ToString()), thing.AssetPath, EnumerableExtensions.ToSingleEnumerable<FileInfo>(thing.ExportFileInfo));
        }
    }
}
