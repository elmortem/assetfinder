using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Threading;
using AssetFinder.Cache;
using System;
using Object = UnityEngine.Object;

namespace AssetFinder
{
    public class AssetFinderWindow : EditorWindow
    {
        private Object _targetAsset;
        private Vector2 _scrollPosition;
        private readonly List<AssetReference> _foundReferences = new();
        private float _rebuildProgress;

        [MenuItem("Tools/Asset Finder")]
        public static void ShowWindow()
        {
            GetWindow<AssetFinderWindow>("Asset Finder");
        }

        private void OnEnable()
        {
            AssetCache.Instance.CacheSaveEvent -= OnCacheSaved;
            AssetCache.Instance.CacheSaveEvent += OnCacheSaved;
            
            if (_targetAsset != null && _foundReferences.Count == 0)
            {
                StartNewSearch();
            }
        }
        
        private void OnCacheSaved()
        {
            if (_targetAsset != null && AssetFinderSettings.Instance.AutoRefresh)
            {
                StartNewSearch();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            
            var content = new GUIContent("Rebuild");
            var toolbarRect = GUILayoutUtility.GetRect(content, EditorStyles.toolbarDropDown);
            toolbarRect.width = 65f + 16f;
            
            var buttonRect = new Rect(toolbarRect.x, toolbarRect.y, toolbarRect.width - 16f, toolbarRect.height);
            var dropdownRect = new Rect(toolbarRect.x + toolbarRect.width - 16f, toolbarRect.y, 16f, toolbarRect.height);
            
            if (GUI.Button(buttonRect, content, EditorStyles.toolbarButton))
            {
                RebuildCache(false);
            }
            
            if (EditorGUI.DropdownButton(dropdownRect, GUIContent.none, FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Force Rebuild"), false, () => RebuildCache(true));
                menu.DropDown(dropdownRect);
            }
            
            var lastRebuildTime = AssetCache.Instance.LastRebuildTime;
            var timeString = lastRebuildTime == DateTime.MinValue ? "Never" : $"{lastRebuildTime:g}";
            if (AssetCache.Instance.LastRebuildDuration > 0f)
                timeString += $" ({AssetCache.Instance.LastRebuildDuration:F3}s)";
            EditorGUILayout.LabelField($"Last Rebuild: {timeString}", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newTargetAsset = EditorGUILayout.ObjectField(_targetAsset, typeof(Object), false);

            if (_targetAsset != null && GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                StartNewSearch();
            }
            if (EditorGUI.EndChangeCheck() && newTargetAsset != _targetAsset)
            {
                _targetAsset = newTargetAsset;
                StartNewSearch();
            }

            EditorGUILayout.EndHorizontal();

            if (AssetCache.Instance.IsProcessing)
            {
                EditorGUILayout.Space(5);
                var rect = EditorGUILayout.GetControlRect(false, 16);
                EditorGUI.ProgressBar(rect, _rebuildProgress, "Processing assets...");
            }

            EditorGUILayout.Space();

            if (_foundReferences.Count > 0)
            {
                EditorGUILayout.LabelField($"Found references in {_foundReferences.Count} assets:");
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                foreach (var reference in _foundReferences)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        Selection.activeObject = reference.Asset;
                        EditorGUIUtility.PingObject(reference.Asset);
                    }
                    EditorGUILayout.ObjectField(reference.Asset, typeof(Object), false);
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel++;
                    int num = 0;
                    foreach (var path in reference.Paths)
                    {
                        EditorGUILayout.LabelField($"{++num}. {path}");
                    }
                    EditorGUI.indentLevel--;

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.EndScrollView();
            }
            else if (_targetAsset != null)
            {
                EditorGUILayout.LabelField("No references found.");
            }
        }

        private async void RebuildCache(bool force)
        {
            _rebuildProgress = 0f;
            await AssetCache.Instance.RebuildCache(force, p => _rebuildProgress = p);
            _rebuildProgress = 1f;
        }

        private void StartNewSearch()
        {
            if (_targetAsset == null) return;

            ResetSearch();

            try
            {
                _foundReferences.Clear();

                var rawResults = AssetCache.Instance.FindReferences(_targetAsset);
                
                foreach (var (assetGuid, paths) in rawResults)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset == null) continue;
                    
                    var reference = new AssetReference(asset);
                    reference.Paths.UnionWith(paths);
                    _foundReferences.Add(reference);
                }
                
                Repaint();
            }
            finally
            {
                Repaint();
            }
        }

        private void ResetSearch()
        {
            _foundReferences.Clear();
        }
    }
}
