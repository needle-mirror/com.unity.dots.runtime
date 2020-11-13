using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Assertions;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.DotsRuntime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using BuildTarget = Unity.Build.DotsRuntime.BuildTarget;
#if DOTS_TEST_RUNNER
using Unity.Dots.TestRunner;
#endif

namespace Unity.Entities.Runtime.Build
{
    public class GenerateDotsSolutionWindow : EditorWindow
    {
        const string k_WindowTitle = "Generate DOTS C# Solution";
        static HashSet<BuildConfiguration> s_BuildConfigurations;
        static HashSet<int> s_settingsFileHashes;
        TreeViewState m_TreeViewState;
        GenerateDotsSolutionView m_GenerateDotsSolutionView;

        static void RebuildGuidList()
        {
            if (s_BuildConfigurations == null)
            {
                var guids = AssetDatabase.FindAssets($"t:{typeof(BuildConfiguration)}");
                s_BuildConfigurations = new HashSet<BuildConfiguration>();
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var config = AssetDatabase.LoadAssetAtPath<BuildConfiguration>(path);
                    if (config != null)
                        s_BuildConfigurations.Add(config);
                }
            }
        }

        static void RebuildAlreadyGeneratedConfigList()
        {
            s_settingsFileHashes = new HashSet<int>();
            var settingsDirectory = GenerateDotsSolutionView.BeeRootDirectory.Combine("settings").ToString();
            if (Directory.Exists(settingsDirectory))
            {
                foreach (var path in Directory.GetFiles(settingsDirectory))
                {
                    s_settingsFileHashes.Add(Path.GetFileNameWithoutExtension(path).GetHashCode());
                }
            }
        }

        void OnEnable()
        {
            // Check whether there is already a serialized view state (state
            // that survived assembly reloading)
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();

            RebuildGuidList();
            RebuildAlreadyGeneratedConfigList();

            m_GenerateDotsSolutionView = new GenerateDotsSolutionView(m_TreeViewState);
        }

        void OnGUI()
        {
            if (m_GenerateDotsSolutionView == null)
                return;

            var buttonsRect = EditorGUILayout.BeginVertical();
            {
                EditorGUI.BeginDisabledGroup(!m_GenerateDotsSolutionView.AnyConfigsSelected());
                if (GUILayout.Button("Generate Solution"))
                    m_GenerateDotsSolutionView.GenerateSolution();
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!GetSolutionPath().Exists());
                if (GUILayout.Button("Open Solution"))
                    OpenSolution();
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Select All"))
                            m_GenerateDotsSolutionView.SelectAllConfigs();

                        if (GUILayout.Button("Select None"))
                            m_GenerateDotsSolutionView.UnselectAllConfigs();

                        if (GUILayout.Button("Expand All"))
                            m_GenerateDotsSolutionView.ExpandAll();

                        if (GUILayout.Button("Collapse All"))
                            m_GenerateDotsSolutionView.CollapseAll();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            var treeViewRect = EditorGUILayout.BeginVertical();
            {
                m_GenerateDotsSolutionView.OnGUI(new Rect(treeViewRect.x, treeViewRect.y, position.width, position.height - buttonsRect.height - 10));
            }
            EditorGUILayout.EndVertical();
        }

        [MenuItem("Assets/Generate DOTS C# Solution...", priority = 10000)]
        static void ShowWindow()
        {
            // Get existing open window or if none, make a new one
            var window = GetWindow<GenerateDotsSolutionWindow>();
            window.titleContent = new GUIContent(k_WindowTitle);
            window.Show();
        }

        [MenuItem("Assets/Open DOTS C# Solution", priority = 10001, validate = false)]
        static void GenerateAndOpenSolution()
        {
            GenerateDotsSolutionView.RunBeeProjectFiles();
            OpenSolution();
        }

        // The editor will call this function before rendering the "Open DOTS C# Solution" option
        // and will enable/disable this option based on the return result of this function
        [MenuItem("Assets/Open DOTS C# Solution", priority = 10001, validate = true)]
        static bool GenerateAndOpenSolutionValidation()
        {
            return GetSolutionPath().Exists();
        }

        internal static NPath GetSolutionPath()
        {
            var projectPath = new NPath(UnityEngine.Application.dataPath).Parent;
            var slnPath = projectPath.Combine(projectPath.FileName + "-Dots.sln");
            return slnPath;
        }

        internal static void GenerateAndOpenSolution(BuildConfiguration buildConfig, string buildTargetName)
        {
            using (var progress = new BuildProgress(k_WindowTitle, "Please wait..."))
            {
                GenerateSolution(buildConfig, progress, buildTargetName);
                GenerateDotsSolutionView.RunBeeProjectFiles();
                OpenSolution();
            }
        }

        static void GenerateSolution(BuildConfiguration buildConfig, BuildProgress progress, string buildTargetName)
        {
            progress.Title = $"Generating '{buildTargetName}'";

            var buildPipeline = buildConfig.GetComponent<DotsRuntimeBuildProfile>().Pipeline;
            var steps = new List<Type>();

            if (buildPipeline.BuildSteps.Contains(new BuildStepExportScenes()))
                steps.Add(typeof(BuildStepExportScenes));

            if (buildPipeline.BuildSteps.Contains(new BuildStepExportConfiguration()))
                steps.Add(typeof(BuildStepExportConfiguration));

            steps.Add(typeof(BuildStepGenerateBeeFiles));

            var pipeline = new GenerateDotsSolutionBuildPipeline(steps, (buildPipeline as DotsRuntimeBuildPipelineBase)?.Target);
            var result = pipeline.Build(buildConfig, progress);
            if (result.Failed)
            {
                UnityEngine.Debug.LogError(
                    $"{k_WindowTitle} failed.\n{(result.Exception != null ? result.Exception.ToString() : result.Message)}");
            }
        }

        static void OpenSolution()
        {
            var scriptEditor = new NPath(ScriptEditorUtility.GetExternalScriptEditor());
            var sln = GetSolutionPath().InQuotes();
#if UNITY_EDITOR_OSX
            var pi = new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = $"-a {scriptEditor.InQuotes()} {sln}",
                UseShellExecute = false
            };
#else
            var pi = new ProcessStartInfo
            {
                FileName = scriptEditor.ToString(),
                Arguments = sln,
            };
#endif
            var proc = new Process { StartInfo = pi };
            proc.Start();
        }

        class GenerateDotsSolutionView : TreeView
        {
            enum Column
            {
                IncludeInSolutionToggle,
                RootGameAssembly,
                Target,
                Configuration,
                BuildTargetName,
            }

            abstract class ConfigViewItem : TreeViewItem
            {
                public BuildType Configuration;
                public string TargetName;
                public bool IncludeInSolution;
                public string BuildTargetName;

                public override string displayName => null; // we don't want a display name for the row
                public override int depth => parent?.depth + 1 ?? 0;
            }

            class BuildConfigViewItem : ConfigViewItem
            {
                class BuildConfigurationAssetPostProcessor : AssetPostprocessor
                {
                    static void OnPostprocessAllAssets(
                        string[] importedAssets,
                        string[] deletedAssets,
                        string[] movedAssets,
                        string[] movedFromAssetPaths)
                    {
                        var changedBuildConfig = deletedAssets.Concat(importedAssets).Any(path =>
                        {
                            var extension = Path.GetExtension(path);
                            return extension == ".buildconfiguration";
                        });

                        if (changedBuildConfig)
                        {
                            GenerateDotsSolutionView.ShouldReload = true;
                            s_BuildConfigurations = null;
                        }
                    }
                }

                public BuildConfigViewItem(BuildConfiguration buildConfig)
                {
                    BuildConfiguration = buildConfig;

                    var buildProfile = buildConfig.GetComponent<DotsRuntimeBuildProfile>();
                    var rootAssembly = buildConfig.GetComponent<DotsRuntimeRootAssembly>();
                    Configuration = buildProfile.Configuration;
                    TargetName = buildProfile.Target.DisplayName;

                    BuildTargetName = rootAssembly.MakeBeeTargetName(buildConfig.name);
                    IncludeInSolution = s_settingsFileHashes.Contains(BuildTargetName.GetHashCode());
                }

                public readonly BuildConfiguration BuildConfiguration;
                public override int id => BuildConfiguration.GetHashCode() * 7919 ^ Configuration.GetHashCode() * 7919 ^ TargetName.GetHashCode();
            }

#if DOTS_TEST_RUNNER
            class RuntimeTestTargetConfigViewItem : ConfigViewItem
            {
                public RuntimeTestTargetConfigViewItem(RuntimeTestTarget target)
                {
                    RuntimeTestTarget = target;
                    Configuration = target.BuildType;
                    TargetName = target.BuildTarget.DisplayName;

                    BuildTargetName = target.FullName;
                    IncludeInSolution = s_settingsFileHashes.Contains(BuildTargetName.GetHashCode());
                }

                public readonly RuntimeTestTarget RuntimeTestTarget;
                public override int id => RuntimeTestTarget.GetHashCode();
            }
#endif

            class RootAssemblyViewItem : TreeViewItem
            {
                public RootAssemblyViewItem(RootAssemblyInfo info)
                {
                    BuildTargetInfo = info;
                }

                public RootAssemblyInfo BuildTargetInfo;
                public string ProjectName => BuildTargetInfo != null ? BuildTargetInfo.ProjectName : "";
                public bool IncludeAllInSolution { get; set; }
                public override string displayName => null;
                public override int id => BuildTargetInfo.GetHashCode();
                public override int depth => parent?.depth + 1 ?? 0;
            }

            readonly MultiColumnHeaderState m_MultiColumnHeaderState;
            public static DirectoryInfo BeeRootDirectory { get; set; } = DotsRuntimeRootAssembly.BeeRootDirectory;
            internal static bool ShouldReload { get; set; }

            public GenerateDotsSolutionView(TreeViewState state)
                : base(state, new MultiColumnHeader(CreateMultiColumnHeaderState()))
            {
                multiColumnHeader.sortingChanged += OnSortingChanged;
                showAlternatingRowBackgrounds = true;
                Reload();
                OnSortingChanged(multiColumnHeader);
            }

            void OnSortingChanged(MultiColumnHeader _)
            {
                SortIfNeeded(rootItem, GetRows());
            }

            void SortIfNeeded(TreeViewItem root, IList<TreeViewItem> rows)
            {
                if (rows.Count <= 1)
                    return;

                if (multiColumnHeader.sortedColumnIndex == -1)
                {
                    return; // No column to sort for (just use the order the data are in)
                }

                SortColumn();
                TreeToList(root, rows);
                Repaint();
            }

            void SortColumn()
            {
                var expandedIds = state.expandedIDs;
                ExpandAll();

                var sortedColumns = multiColumnHeader.state.sortedColumns;
                if (sortedColumns.Length == 0 || rootItem == null)
                    return;

                var columnIndex = multiColumnHeader.sortedColumnIndex;
                var column = (Column)columnIndex;
                if (column == Column.RootGameAssembly)
                {
                    var items = rootItem.children.Cast<RootAssemblyViewItem>();
                    var isAscending = multiColumnHeader.IsSortedAscending(columnIndex);
                    items = isAscending ? items.OrderBy(item => item.ProjectName) : items.OrderByDescending(item => item.ProjectName);
                    rootItem.children = items.Cast<TreeViewItem>().ToList();
                }
                else
                {
                    foreach (var child in rootItem.children)
                    {
                        var items = child.children.Cast<ConfigViewItem>();
                        var isAscending = multiColumnHeader.IsSortedAscending(columnIndex);
                        switch (column)
                        {
                            case Column.IncludeInSolutionToggle:
                                items = isAscending ? items.OrderBy(item => item.IncludeInSolution) : items.OrderByDescending(item => item.IncludeInSolution);
                                break;
                            case Column.Target:
                                items = isAscending ? items.OrderBy(item => item.TargetName) : items.OrderByDescending(item => item.TargetName);
                                break;
                            case Column.Configuration:
                                items = isAscending ? items.OrderBy(item => item.Configuration) : items.OrderByDescending(item => item.Configuration);
                                break;
                            case Column.BuildTargetName:
                                items = isAscending ? items.OrderBy(item => item.BuildTargetName) : items.OrderByDescending(item => item.BuildTargetName);
                                break;
                        }
                        child.children = items.Cast<TreeViewItem>().ToList();
                    }
                }

                CollapseAll();
                SetExpanded(expandedIds);
            }

            static void TreeToList(TreeViewItem root, IList<TreeViewItem> result)
            {
                if (root == null)
                    throw new NullReferenceException("root");
                if (result == null)
                    throw new NullReferenceException("result");

                result.Clear();

                if (root.children == null)
                    return;

                Stack<TreeViewItem> stack = new Stack<TreeViewItem>();
                for (int i = root.children.Count - 1; i >= 0; i--)
                    stack.Push(root.children[i]);

                while (stack.Count > 0)
                {
                    TreeViewItem current = stack.Pop();
                    result.Add(current);

                    if (current.hasChildren && current.children[0] != null)
                    {
                        for (int i = current.children.Count - 1; i >= 0; i--)
                        {
                            stack.Push(current.children[i]);
                        }
                    }
                }
            }

            static MultiColumnHeaderState CreateMultiColumnHeaderState()
            {
                var columns = new[]
                {
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Include in Solution", "Adds toggled build configurations to the generated solution"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 125,
                        minWidth = 125,
                        maxWidth = 125,
                        autoResize = false
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Root Game Assembly", "Game to be built as specified by the Build Configuration Asset"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 180,
                        minWidth = 180,
                        autoResize = true
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Target", "Target the Root Game Assembly is to be built for"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 220,
                        minWidth = 220,
                        autoResize = true
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Configuration", "Configuration the Root Game Assembly is to be built with"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 110,
                        minWidth = 110,
                        autoResize = true
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Build Target Name", "Name of build target invoked by the build system. This is typically the output directory name under '<ProjectRoot>/Builds/'"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 350,
                        minWidth = 350,
                        autoResize = true
                    },
                };

                // Number of columns should match number of enum values: You probably forgot to update one of them
                Assert.AreEqual(columns.Length, Enum.GetValues(typeof(Column)).Length);
                var state = new MultiColumnHeaderState(columns);
                state.sortedColumnIndex = (int)Column.RootGameAssembly;

                return state;
            }

            public override void OnGUI(Rect rect)
            {
                if (ShouldReload)
                {
                    var expandedIds = state.expandedIDs;
                    ExpandAll();

                    RebuildGuidList();
                    RebuildAlreadyGeneratedConfigList();
                    Reload();
                    OnSortingChanged(multiColumnHeader);
                    ShouldReload = false;

                    CollapseAll();
                    SetExpanded(expandedIds);
                }

                base.OnGUI(rect);
            }

            protected override float GetCustomRowHeight(int row, TreeViewItem item)
            {
                if (item is ConfigViewItem)
                {
                    return 22.0f;
                }
                if (item is RootAssemblyViewItem)
                {
                    return 24.0f;
                }

                return 18.0f;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                switch (args.item)
                {
                    case ConfigViewItem buildConfigItem:
                        for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                        {
                            DrawRowCell(args.GetCellRect(i), (Column)args.GetColumn(i), buildConfigItem, args);
                        }

                        break;
                    case RootAssemblyViewItem projectInfoItem:
                        for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                        {
                            DrawRowCell(args.GetCellRect(i), (Column)args.GetColumn(i), projectInfoItem, args);
                        }

                        break;
                }

                base.RowGUI(args);
            }

            void DrawRowCell(Rect rect, Column column, ConfigViewItem item, RowGUIArgs args)
            {
                CenterRectUsingSingleLineHeight(ref rect);

                switch (column)
                {
                    case Column.IncludeInSolutionToggle:
                    {
                        var toggleRect = rect;
                        int toggleWidth = 20;
                        toggleRect.x += rect.width * 0.5f - toggleWidth * 0.5f;

                        bool toggleVal = EditorGUI.Toggle(toggleRect, item.IncludeInSolution);
                        if (toggleVal != item.IncludeInSolution)
                        {
                            item.IncludeInSolution = toggleVal;

                            Repaint();
                        }

                        break;
                    }
                    case Column.Target:
                    {
                        DefaultGUI.Label(rect, item.TargetName, args.selected, args.focused);
                        break;
                    }
                    case Column.Configuration:
                    {
                        DefaultGUI.Label(rect, item.Configuration.ToString(), args.selected, args.focused);
                        break;
                    }
                    case Column.BuildTargetName:
                    {
                        DefaultGUI.Label(rect, item.BuildTargetName, args.selected, args.focused);
                        break;
                    }
                }
            }

            void DrawRowCell(Rect rect, Column column, RootAssemblyViewItem viewItem, RowGUIArgs args)
            {
                CenterRectUsingSingleLineHeight(ref rect);

                switch (column)
                {
                    case Column.IncludeInSolutionToggle:
                    {
                        var toggleRect = rect;
                        int toggleWidth = 20;
                        toggleRect.x += rect.width * 0.5f - toggleWidth * 0.5f;

                        bool allChildrenChecked = true;
                        bool anyChildrenChecked = false;
                        foreach (var child in args.item.children)
                        {
                            var buildConfigViewItem = child as ConfigViewItem;
                            if (buildConfigViewItem != null)
                            {
                                allChildrenChecked &= buildConfigViewItem.IncludeInSolution;
                                anyChildrenChecked |= buildConfigViewItem.IncludeInSolution;
                            }
                        }

                        bool isMixed = !allChildrenChecked && anyChildrenChecked;
                        bool originalIncludeAllInSolutionValue = viewItem.IncludeAllInSolution;
                        EditorGUI.showMixedValue = isMixed;
                        var toggleValue = EditorGUI.Toggle(toggleRect, viewItem.IncludeAllInSolution);

                        if (toggleValue != viewItem.IncludeAllInSolution)
                        {
                            viewItem.IncludeAllInSolution = toggleValue;
                            EditorGUI.showMixedValue = false;

                            // If the parent and children are all selected, and the user de-selects
                            // a child, don't unset all other children, but allow the parent to be listed
                            // as mixed (which is equivalent to false, so setting via toggleValue is no longer ok)
                            if (!(originalIncludeAllInSolutionValue && !toggleValue && isMixed))
                            {
                                foreach (var child in args.item.children)
                                {
                                    if (child is ConfigViewItem buildConfigViewItem)
                                        buildConfigViewItem.IncludeInSolution = toggleValue;
                                }
                            }

                            Repaint();
                        }
                        else
                        {
                            EditorGUI.showMixedValue = false;

                            if(allChildrenChecked)
                                viewItem.IncludeAllInSolution = true;
                        }

                        break;
                    }
                    case Column.RootGameAssembly:
                    {
                        DefaultGUI.Label(rect, viewItem.ProjectName, args.selected, args.focused);
                        break;
                    }
                }
            }

            class RootAssemblyInfo : IEquatable<RootAssemblyInfo>
            {
                public string ProjectName;
                public RootAssemblyInfo(string projectName)
                {
                    ProjectName = projectName;
                }

                public bool Equals(RootAssemblyInfo obj)
                {
                    if (obj == null )
                        return false;

                    return obj.ProjectName == ProjectName;
                }

                public override int GetHashCode()
                {
                    return ProjectName.GetHashCode();
                }
            }

            class RootAssemblyConfigInfo
            {
                public List<BuildConfiguration> BuildConfigurations = new List<BuildConfiguration>();
#if DOTS_TEST_RUNNER
                public List<RuntimeTestTarget> RuntimeTestTargets = new List<RuntimeTestTarget>();
#endif

                public bool HasConfigs()
                {
                    return BuildConfigurations.Count > 0
#if DOTS_TEST_RUNNER
                           || RuntimeTestTargets.Count > 0
#endif
                    ;
                }
            }

            protected override TreeViewItem BuildRoot()
            {
                var idsToExpand = new List<int>();
                var root = new TreeViewItem { id = int.MaxValue, depth = -1, displayName = "Root" };

                List<BuildConfiguration> buildConfigurations = new List<BuildConfiguration>();
                buildConfigurations.AddRange(s_BuildConfigurations);

                Dictionary<RootAssemblyInfo, RootAssemblyConfigInfo> configMap = new Dictionary<RootAssemblyInfo, RootAssemblyConfigInfo>();
                foreach (var buildConfig in buildConfigurations)
                {
                    bool hasRequiredComponents = true;

                    hasRequiredComponents &= buildConfig.TryGetComponent(typeof(DotsRuntimeBuildProfile), out var buildProfile);
                    hasRequiredComponents &= buildConfig.TryGetComponent(typeof(DotsRuntimeRootAssembly), out var buildTarget);
                    if (hasRequiredComponents)
                    {
                        var dotsrtBuildProfile = (DotsRuntimeBuildProfile)buildProfile;
                        var dotsrtRootAssembly = (DotsRuntimeRootAssembly)buildTarget;

                        if (dotsrtBuildProfile.Target.CanBuild)
                        {
                            var rootAssemblyInfo = new RootAssemblyInfo(dotsrtRootAssembly.ProjectName);
                            if (!configMap.TryGetValue(rootAssemblyInfo, out var configInfo))
                            {
                                configInfo = new RootAssemblyConfigInfo();
                                configMap.Add(rootAssemblyInfo, configInfo);
                            }
                            configInfo.BuildConfigurations.Add(buildConfig);
                        }
                    }
                }

#if DOTS_TEST_RUNNER
                var testTargets = GetTestTargets();
                foreach (var target in testTargets)
                {
                    var rootAssemblyInfo = new RootAssemblyInfo(target.RootAssembly.name);
                    if (!configMap.TryGetValue(rootAssemblyInfo, out var configInfo))
                    {
                        configInfo = new RootAssemblyConfigInfo();
                        configMap.Add(rootAssemblyInfo, configInfo);
                    }
                    configInfo.RuntimeTestTargets.Add(target);
                }
#endif

                foreach (var assemblyInfo in configMap.Keys)
                {
                    var configInfo = configMap[assemblyInfo];
                    if (configInfo.HasConfigs())
                    {
                        var projectViewItem = new RootAssemblyViewItem(assemblyInfo);
                        root.AddChild(projectViewItem);

                        bool includeAllInSolution = true;
                        bool expandParent = false;
                        foreach (var buildConfig in configInfo.BuildConfigurations)
                        {
                            var configViewItem = new BuildConfigViewItem(buildConfig);
                            includeAllInSolution &= configViewItem.IncludeInSolution;
                            expandParent |= configViewItem.IncludeInSolution;

                            projectViewItem.AddChild(configViewItem);
                        }
#if DOTS_TEST_RUNNER
                        foreach (var target in configInfo.RuntimeTestTargets)
                        {
                            var configViewItem = new RuntimeTestTargetConfigViewItem(target);
                            includeAllInSolution &= configViewItem.IncludeInSolution;
                            expandParent |= configViewItem.IncludeInSolution;

                            projectViewItem.AddChild(configViewItem);
                        }
#endif

                        projectViewItem.IncludeAllInSolution = includeAllInSolution;
                        if (expandParent)
                            idsToExpand.Add(projectViewItem.id);
                    }
                }

                if (!root.hasChildren)
                {
                    root.AddChild(new TreeViewItem(0, 0, "No BuildConfiguration Assets Found in Project."));
                }

                SetupDepthsFromParentsAndChildren(root);

                SetExpanded(idsToExpand);

                return root;
            }

#if DOTS_TEST_RUNNER
            List<RuntimeTestTarget> GetTestTargets()
            {
                var testFinder = new TestTargetFinder();
                testFinder.LoadTestConfiguration();
                testFinder.RetrieveTestTree();

                return testFinder.Tests;
            }
#endif

            protected override void KeyEvent()
            {
                base.KeyEvent();
                if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Space)
                {
                    ToggleSelection();
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
                {
                    Event.current.Use();
                    var window = EditorWindow.GetWindow<GenerateDotsSolutionWindow>();
                    window.Close();
                }
            }

            void ToggleSelection()
            {
                Selection.instanceIDs = new int[0];

                foreach (var item in GetSelection()
                         .Select(id => FindItem(id, rootItem))
                         .OfType<ConfigViewItem>())
                {
                    item.IncludeInSolution = !item.IncludeInSolution;
                }
            }

            protected override void SingleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);
                switch (item)
                {
                    case BuildConfigViewItem viewItem:
                        Selection.activeObject = viewItem.BuildConfiguration;
                        break;
                }
            }

            protected override void DoubleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);
                switch (item)
                {
                    case RootAssemblyViewItem viewItem:
                    {
                        var expandedIds = new List<int>();
                        expandedIds.AddRange(GetExpanded());

                        if (!IsExpanded(viewItem.id))
                            expandedIds.Add(viewItem.id);
                        else
                            expandedIds.Remove(viewItem.id);

                        SetExpanded(expandedIds);
                        break;
                    }
                }
            }

            internal void SelectAllConfigs()
            {
                foreach (var item in GetRows().OfType<RootAssemblyViewItem>())
                {
                    item.IncludeAllInSolution = false;
                    foreach (var child in item.children.OfType<ConfigViewItem>())
                        child.IncludeInSolution = true;
                }
            }

            internal void UnselectAllConfigs()
            {
                foreach (var item in GetRows().OfType<RootAssemblyViewItem>())
                {
                    item.IncludeAllInSolution = false;
                    foreach (var child in item.children.OfType<ConfigViewItem>())
                        child.IncludeInSolution = false;
                }
            }

            internal bool AnyConfigsSelected()
            {
                foreach (var item in GetRows().OfType<RootAssemblyViewItem>())
                {
                    foreach (var child in item.children.OfType<ConfigViewItem>())
                        if (child.IncludeInSolution)
                            return true;
                }

                return false;
            }

            internal void GenerateSolution()
            {
                using (var progress = new BuildProgress(k_WindowTitle, "Please wait..."))
                {
                    var settingsDirectory = BeeRootDirectory.Combine("settings").ToString();
                    if (Directory.Exists(settingsDirectory))
                        Directory.Delete(settingsDirectory, true);

                    foreach (var item in GetRows().OfType<RootAssemblyViewItem>())
                    {
                        foreach (var configItem in item.children.OfType<ConfigViewItem>().Where(child => child.IncludeInSolution))
                        {
                            progress.Title = $"Generating '{configItem.BuildTargetName}'";

                            BuildConfiguration buildConfig = null;
                            if (configItem is BuildConfigViewItem buildConfigItem)
                                buildConfig = buildConfigItem.BuildConfiguration;
#if DOTS_TEST_RUNNER
                            else if (configItem is RuntimeTestTargetConfigViewItem runtimeTestTargetConfigViewItem)
                            {
                                buildConfig =
                                    DotsTestRunner.GenerateBuildConfiguration(runtimeTestTargetConfigViewItem
                                        .RuntimeTestTarget);
                                DotsTestRunner.HackPreRunBuildConfigFixup(buildConfig);
                            }
#endif
                            else
                            {
                                throw new Exception("Invalid row type in generate solution window view!");
                            }

                            GenerateDotsSolutionWindow.GenerateSolution(buildConfig, progress, configItem.BuildTargetName);
                        }
                    }

                    RunBeeProjectFiles(progress);
                }

#if DOTS_TEST_RUNNER
                DotsTestRunner.ClearTestBuildConfigs();
#endif
            }

            public static void RunBeeProjectFiles(BuildProgress progress = null)
            {
                bool ownProgress = progress == null;
                if (ownProgress)
                {
                    progress = new BuildProgress(k_WindowTitle, "Please wait...");

                    BuildProgramDataFileWriter.WriteAll(BeeRootDirectory.FullName);
                }

                var result = BeeTools.Run("ProjectFiles -f", BeeRootDirectory, progress);
                if (!result.Succeeded)
                {
                    UnityEngine.Debug.LogError($"{k_WindowTitle} failed.\n{result.Error}");
                    if (ownProgress)
                        progress.Dispose();
                    return;
                }

                if (ownProgress)
                    progress.Dispose();
            }
        }

        sealed class GenerateDotsSolutionBuildPipeline : BuildPipelineBase
        {
            public BuildTarget Target { get; set; }
            public override Type[] UsedComponents =>
                base.UsedComponents
                .Concat(Target?.UsedComponents)
                // @TODO: Remove this and fix in each BuildTarget instead
                .Concat(new[] { typeof(GeneralSettings) })
                .Distinct().ToArray();

            public GenerateDotsSolutionBuildPipeline(IEnumerable<Type> steps, BuildTarget target) : base(steps.ToArray())
            {
                Target = target;
            }

            protected override CleanResult OnClean(CleanContext context) => throw new NotImplementedException();
            protected override BuildResult OnBuild(BuildContext context) => BuildSteps.Run(context);
            protected override RunResult OnRun(RunContext context) => throw new NotImplementedException();

            public override DirectoryInfo GetOutputBuildDirectory(BuildConfiguration config) => throw new NotImplementedException();
        }
    }
}
