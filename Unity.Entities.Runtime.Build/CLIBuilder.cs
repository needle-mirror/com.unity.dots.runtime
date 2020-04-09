using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Entities.Conversion;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using BuildPipeline = Unity.Build.BuildPipeline;
using BuildTarget = Unity.Platforms.BuildTarget;

namespace Unity.Entities.Runtime.Build
{
    public class CLIBuilder
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs().ToList();
            if (!args.Contains("-targets"))
                throw new Exception("No target is specified, please use arg -targets <target names to build>");

            var i = args.IndexOf("-targets");
            InitValidTargets();

            var targets = args.Skip(i).Where(item => !item.StartsWith("-"));

            foreach (var target in targets)
            {
                var targetInfo = target.Split('-');
                if (targetInfo.Length < 2)
                    continue;

                var buildTarget = BuildTarget.AvailableBuildTargets
                    .FirstOrDefault(t =>
                        t.CanBuild &&
                        !string.IsNullOrEmpty(t.BeeTargetName) &&
                        t.BeeTargetName.Contains(targetInfo[1]));
                var asmdef = validTargets.FirstOrDefault(t => t.FileNameWithoutExtension == targetInfo[0]);

                if (asmdef == null || buildTarget == null)
                    throw new Exception($"Invalid target {target}");

                UnityEngine.Debug.Log($"Building {target}");

                var result = DoBuild(asmdef, buildTarget, true);
                if (!result)
                    throw new Exception($"Building {target} failed. check the output build.log file");
            }
        }

        [Serializable]
        internal class AsmDefJsonObject
        {
            [SerializeField] public string name;
            [SerializeField] public string[] defineConstraints;
            [SerializeField] public string[] references;
            [SerializeField] public string[] optionalUnityReferences;
            public string guid;
            public NPath asmdefPath;

            public AsmDefJsonObject()
            {
                name = null;
                defineConstraints = null;
                references = null;
                optionalUnityReferences = null;
                asmdefPath = null;
            }
        }

        static void FillAllAsmDefs()
        {
            allAsmDefs = new List<AsmDefJsonObject>();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (var g in guids)
            {
                string asmdefPath = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var fullPath = new NPath(Path.GetFullPath(asmdefPath));
                var asmdefjson = JsonUtility.FromJson<AsmDefJsonObject>(fullPath.ReadAllText());
                asmdefjson.asmdefPath = fullPath;
                asmdefjson.guid = g;
                allAsmDefs.Add(asmdefjson);
            }
        }

        static IEnumerable<NPath> AsmDefEntryPoints()
        {
            foreach (var asmdefjson in allAsmDefs)
            {
                if (asmdefjson.references == null)
                    continue;
                if (!asmdefjson.references.Contains("Unity.Tiny.Main"))
                    continue;
                if (asmdefjson.name.Contains("BGFXEditorSetup"))
                    continue;

                yield return asmdefjson.asmdefPath;
            }
        }

        internal static NPath[] validTargets;
        internal static string[] validTargetNames;
        internal static List<AsmDefJsonObject> allAsmDefs;

        public static void InitValidTargets()
        {
            if (validTargets != null)
                return;

            var targets = new List<NPath>();

            FillAllAsmDefs();
            targets.AddRange(AsmDefEntryPoints());

            targets.Sort((a, b) => a.FileNameWithoutExtension.CompareTo(b.FileNameWithoutExtension));
            validTargets = targets.ToArray();
            validTargetNames = targets.Select(t => t.FileNameWithoutExtension).ToArray();
        }

        internal static BuildPipelineResult DoBuild(NPath asmdef, BuildTarget buildTarget, bool runBee = true, Action<BuildContext> onBuildCompleted = null)
        {
            var relativePath = asmdef.RelativeTo(".");
            var name = asmdef.FileNameWithoutExtension;
            var outputDir = new DirectoryInfo("./Library/DotsRuntimeBuild");
            var stagingDir = outputDir.Combine(name);
            var dataDir = stagingDir.Combine("Data");

            var dotsrtProfile = new DotsRuntimeBuildProfile
            {
                Target = buildTarget,
                Configuration = BuildType.Debug
            };

            var dotsrtRootAssembly = new DotsRuntimeRootAssembly
            {
                RootAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(relativePath.ToString())
            };

            var buildConfig = BuildConfiguration.CreateInstance((c) =>
            {
                c.hideFlags = HideFlags.HideAndDontSave;
                c.SetComponent(dotsrtProfile);
                c.SetComponent(dotsrtRootAssembly);
            });
            buildConfig.SetComponent(new OutputBuildDirectory { OutputDirectory = $"Library/DotsRuntimeBuild/build/Mole3D/{dotsrtRootAssembly.MakeBeeTargetName(buildConfig)}"});

            var convSettings = new ConversionSystemFilterSettings("Unity.Rendering.Hybrid");
            buildConfig.SetComponent(convSettings);

            var sceneList = new SceneList();
            var rootScenePath = ConversionUtils.GetScenePathForSceneWithName(name);
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(rootScenePath);
            sceneList.SceneInfos.Add(new SceneList.SceneInfo
            {
                Scene = GlobalObjectId.GetGlobalObjectIdSlow(scene),
                AutoLoad = true
            });
            buildConfig.SetComponent(sceneList);

            var pipeline = BuildPipeline.CreateInstance((p) =>
            {
                p.hideFlags = HideFlags.HideAndDontSave;
                p.BuildSteps.Add(new BuildStepExportEntities());
                p.BuildSteps.Add(new BuildStepExportConfiguration());
                p.BuildSteps.Add(new BuildStepGenerateBeeFiles());
                if (runBee)
                {
                    p.BuildSteps.Add(new BuildStepRunBee());
                };
            });

            // Run build pipeline
            using (var progress = new BuildProgress($"Build {dotsrtProfile.Target.DisplayName} {dotsrtProfile.Configuration}", "Building..."))
            {
                return pipeline.Build(buildConfig, progress);
            }
        }
    }
}
