using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Assertions;
using Unity.Build;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using BuildPipeline = Unity.Build.BuildPipeline;
#if DOTS_TEST_RUNNER
using Unity.Dots.TestRunner;
#endif

namespace Unity.Entities.Runtime.Build
{
    public class GenerateDotsSolutionWindow : EditorWindow
    {
        const string k_WindowTitle = "Generate DOTS Solution";
        static HashSet<BuildConfiguration> s_BuildConfigurations;
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

        void OnEnable()
        {
            // Check whether there is already a serialized view state (state 
            // that survived assembly reloading)
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();

            RebuildGuidList();

            m_GenerateDotsSolutionView = new GenerateDotsSolutionView(m_TreeViewState);
            GenerateDotsSolutionView.ShouldReload = true;
        }

        void OnGUI()
        {
            var buttonsRect = EditorGUILayout.BeginVertical();
            {
                EditorGUI.BeginDisabledGroup(!m_GenerateDotsSolutionView.AnyConfigsSelected());
                if (GUILayout.Button("Generate Solution"))
                    m_GenerateDotsSolutionView.GenerateSolution();
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Select All"))
                            m_GenerateDotsSolutionView.SelectAllConfigs();

                        if (GUILayout.Button("Select None"))
                            m_GenerateDotsSolutionView.UnselectAllConfigs();
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

        [MenuItem("Assets/Generate DOTS Solution...")]
        static void ShowWindow()
        {
            if (Unsupported.IsDeveloperMode())
            {
                // Get existing open window or if none, make a new one
                var window = GetWindow<GenerateDotsSolutionWindow>();
                window.titleContent = new GUIContent(k_WindowTitle);
                window.Show();
            }
            else
            {
                GenerateDotsSolutionView.RunBeeProjectFiles();
            }
        }

        class GenerateDotsSolutionView : TreeView
        {
            enum Column
            {
                IncludeInSolutionToggle,
                BuildAssetName,
                RootGameAssembly,
                Target
            }

            class GenerateDotsSolutionViewItem : TreeViewItem
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

                public GenerateDotsSolutionViewItem(BuildConfiguration buildConfig, DotsRuntimeBuildProfile profile)
                {
                    BuildConfiguration = buildConfig;
                    BuildProfile = profile;
                }

                public BuildConfiguration BuildConfiguration;
                public DotsRuntimeBuildProfile BuildProfile;
                public string BuildAssetName => BuildConfiguration != null ? BuildConfiguration.name : "";
                public string RootGameAssembly => BuildProfile != null ? BuildProfile.RootAssembly.name : "";
                public string Target => BuildProfile != null ? BuildProfile.Target.DisplayName : "";
                public bool IncludeInSolution { get; set; }

                public override string displayName => null;
                public override int id => BuildConfiguration.GetHashCode() * 7919 ^ BuildProfile.GetHashCode();
                public override int depth => parent?.depth + 1 ?? 0;
            }

            readonly MultiColumnHeaderState m_MultiColumnHeaderState;
            public static DirectoryInfo BeeRootDirectory { get; set; } = new DotsRuntimeBuildProfile().BeeRootDirectory;
            
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
                var sortedColumns = multiColumnHeader.state.sortedColumns;

                if (sortedColumns.Length == 0 || rootItem == null)
                    return;

                var items = rootItem.children.Cast<GenerateDotsSolutionViewItem>();
                var columnIndex = multiColumnHeader.sortedColumnIndex;
                var column = (Column)columnIndex;
                var isAscending = multiColumnHeader.IsSortedAscending(columnIndex);
                switch (column)
                {
                    case Column.IncludeInSolutionToggle:
                        items = isAscending ? items.OrderBy(item => item.IncludeInSolution) : items.OrderByDescending(item => item.IncludeInSolution);
                        break;
                    case Column.RootGameAssembly:
                        items = isAscending ? items.OrderBy(item => item.RootGameAssembly) : items.OrderByDescending(item => item.RootGameAssembly);
                        break;
                    case Column.Target:
                        items = isAscending ? items.OrderBy(item => item.Target) : items.OrderByDescending(item => item.Target);
                        break;
                    case Column.BuildAssetName:
                        items = isAscending ? items.OrderBy(item => item.BuildAssetName) : items.OrderByDescending(item => item.BuildAssetName);
                        break;
                };

                rootItem.children = items.Cast<TreeViewItem>().ToList();
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
                        headerContent = new GUIContent("Build Configuration Asset", "Build Configurations Assets in project 'blah'"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 160,
                        minWidth = 160,
                        autoResize = true
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Root Game Assembly", "Game to be built as specified by the Build Configuration Asset"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 140,
                        minWidth = 140,
                        autoResize = true
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Target", "Target the Root Game Assembly is to be built for"),
                        headerTextAlignment = TextAlignment.Center,
                        sortedAscending = true,
                        sortingArrowAlignment = TextAlignment.Right,
                        width = 110,
                        minWidth = 110,
                        autoResize = true
                    }
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
                    RebuildGuidList();
                    Reload();
                    OnSortingChanged(multiColumnHeader);
                    ShouldReload = false;
                }

                base.OnGUI(rect);
            }

            protected override float GetCustomRowHeight(int row, TreeViewItem item)
            {
                if (item is GenerateDotsSolutionViewItem)
                {
                    return 22.0f;
                }

                return 18.0f;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                switch (args.item)
                {
                    case GenerateDotsSolutionViewItem sceneRefItem:
                        for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                        {
                            DrawRowCell(args.GetCellRect(i), (Column)args.GetColumn(i), sceneRefItem, args);
                        }

                        break;
                }

                base.RowGUI(args);
            }

            void DrawRowCell(Rect rect, Column column, GenerateDotsSolutionViewItem item, RowGUIArgs args)
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
                    case Column.BuildAssetName:
                    {
                        DefaultGUI.Label(rect, item.BuildAssetName, args.selected, args.focused);
                        break;
                    }
                    case Column.RootGameAssembly:
                    {
                        DefaultGUI.Label(rect, item.RootGameAssembly, args.selected, args.focused);
                        break;
                    }
                    case Column.Target:
                    {
                        DefaultGUI.Label(rect, item.Target, args.selected, args.focused);
                        break;
                    }
                }
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = int.MaxValue, depth = -1, displayName = "Root" };

                foreach (var buildConfig in s_BuildConfigurations)
                {
                    if (buildConfig.TryGetComponent(typeof(DotsRuntimeBuildProfile), out var buildComponent))
                    {
                        if (buildComponent is DotsRuntimeBuildProfile profile && profile.RootAssembly != null && profile.Target.CanBuild)
                        {
                            root.AddChild(new GenerateDotsSolutionViewItem(buildConfig, profile));
                        }
                    }
                }
                  
#if DOTS_TEST_RUNNER
                var testItems = GetTestItems();
                foreach (var item in testItems)
                {
                    root.AddChild(item);
                }
#endif

                if (!root.hasChildren)
                {
                    root.AddChild(new TreeViewItem(0, 0, "No BuildConfiguration Assets Found in Project."));
                }

                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

#if DOTS_TEST_RUNNER
            GenerateDotsSolutionViewItem[] GetTestItems()
            {
                
                List<GenerateDotsSolutionViewItem> TestViewItems = new List<GenerateDotsSolutionViewItem>();
                var testFinder = new TestTargetFinder();
                testFinder.RetrieveUnitTests();
                testFinder.RetrieveMultithreadingTests();
                foreach (var test in testFinder.Tests)
                {
                   var bc = DotsTestRunner.GenerateBuildConfiguration(test);
                   var profile = bc.GetComponent<DotsRuntimeBuildProfile>();
                   TestViewItems.Add(new GenerateDotsSolutionViewItem(bc, profile)); 
                }
                return TestViewItems.ToArray();
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
                    .OfType<GenerateDotsSolutionViewItem>())
                {
                    item.IncludeInSolution = !item.IncludeInSolution;
                }
            }

            protected override void DoubleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);
                switch (item)
                {
                    case GenerateDotsSolutionViewItem viewItem:
                        Selection.activeObject = viewItem.BuildConfiguration;
                        break;
                }
            }

            internal void SelectAllConfigs()
            {
                foreach (var item in GetRows().OfType<GenerateDotsSolutionViewItem>())
                    item.IncludeInSolution = true;
            }

            internal void UnselectAllConfigs()
            {
                foreach (var item in GetRows().OfType<GenerateDotsSolutionViewItem>())
                    item.IncludeInSolution = false;
            }

            internal bool AnyConfigsSelected()
            {
                return GetRows().OfType<GenerateDotsSolutionViewItem>().Any(item => item.IncludeInSolution);
            }

            internal void GenerateSolution()
            {
                using (var progress = new BuildProgress(k_WindowTitle, "Please wait..."))
                {
                    var pipeline = BuildPipeline.CreateInstance((p) =>
                    {
                        p.hideFlags = HideFlags.HideAndDontSave;
                    });
                    
                    //uncomment this line when we have tests in the sln as well
                    var settingsDirectory = BeeRootDirectory.Combine("settings").ToString();
                    if (Directory.Exists(settingsDirectory))
                        Directory.Delete(settingsDirectory, true);
                    foreach(var project in GetRows().OfType<GenerateDotsSolutionViewItem>().Where(item => item.IncludeInSolution))
                    {
                        progress.Title =$"Generating '{project.BuildAssetName}'";

                        var buildPipeline = project.BuildConfiguration.GetComponent<DotsRuntimeBuildProfile>()
                            .Pipeline;
                        
                        if (buildPipeline.BuildSteps.Contains(new BuildStepExportEntities()))
                            pipeline.BuildSteps.Add(new BuildStepExportEntities());
                        
                        if (buildPipeline.BuildSteps.Contains(new BuildStepExportConfiguration()))
                            pipeline.BuildSteps.Add(new BuildStepExportConfiguration());
                        
                        pipeline.BuildSteps.Add(new BuildStepGenerateBeeFiles());
                        
                        pipeline.Build(project.BuildConfiguration, progress);
                    }

                    RunBeeProjectFiles(progress);
                }
            }

            public static void RunBeeProjectFiles(BuildProgress progress = null)
            {
                bool ownProgress = progress == null;
                if (ownProgress)
                {
                    progress = new BuildProgress(k_WindowTitle, "Please wait...");

                    BuildProgramDataFileWriter.WriteAll(BeeRootDirectory.FullName);
                }

                var result = BeeTools.Run("ProjectFiles", BeeRootDirectory, progress);
                if (!result.Succeeded)
                {
                    UnityEngine.Debug.LogError($"{k_WindowTitle} failed.\n{result.Error}");
                    if (ownProgress)
                        progress.Dispose();
                    return;
                }

                var scriptEditor = new NPath(ScriptEditorUtility.GetExternalScriptEditor());
                var projectPath = new NPath(UnityEngine.Application.dataPath).Parent;
                var sln = projectPath.Combine(projectPath.FileName + "-Dots.sln").InQuotes();
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
                var proc = new Process {StartInfo = pi};
                proc.Start();
                
                if (ownProgress)
                    progress.Dispose();
            }
        }
    }
}
