using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AssetFinder.Cache;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace AssetFinder
{
	public class AssetFinderWindow : EditorWindow
	{
		private readonly char[] _animChars = { '|', '|', '/', '/', '-', '-', '\\', '\\' };
		
		private Object _targetAsset;
		private Vector2 _scrollPosition;
		private int _processingDrawIndex;
		private readonly List<AssetReference> _foundReferences = new();

		[MenuItem("Tools/Asset Scout")]
		public static void ShowWindow()
		{
			GetWindow<AssetFinderWindow>("Asset Scout");
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
				RebuildCache(false).Forget();
			}
			
			if (EditorGUI.DropdownButton(dropdownRect, GUIContent.none, FocusType.Passive, EditorStyles.toolbarDropDown))
			{
				var menu = new GenericMenu();
				menu.AddItem(new GUIContent("Force Rebuild"), false, () => RebuildCache(true).Forget());
				menu.DropDown(dropdownRect);
			}
			
			var lastRebuildTime = AssetCache.Instance.LastRebuildTime;
			var timeString = lastRebuildTime == DateTime.MinValue ? "Never" : $"{lastRebuildTime:g}";
			if (AssetCache.Instance.LastRebuildDuration > 0f)
				timeString += $" ({AssetCache.Instance.LastRebuildDuration:F3}s)";
			EditorGUILayout.LabelField($"Last Rebuild: {timeString}", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
			
			if (AssetCache.Instance.IsProcessing)
			{
				_processingDrawIndex++;
				if (_processingDrawIndex % _animChars.Length == 0)
					_processingDrawIndex = 0;
				
				EditorGUILayout.LabelField(_animChars[_processingDrawIndex] + " Processing...", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
			}

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

			EditorGUILayout.Space();

			if (_foundReferences.Count > 0)
			{
				EditorGUILayout.LabelField($"Found references in {_foundReferences.Count} assets:");
				_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

				foreach (var reference in _foundReferences)
				{
					EditorGUILayout.BeginVertical(EditorStyles.helpBox);

					EditorGUILayout.ObjectField(reference.Asset, typeof(Object), false);

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
			
			if (_targetAsset == null)
			{
				EditorGUILayout.HelpBox(
					"Drag & Drop an asset here to find all references to it in your project.\n\n" +
					"The tool will search through:\n" +
					"• Scenes\n" +
					"• Prefabs\n" +
					"• Scriptable Objects\n" +
					"• Materials\n" +
					"And other Unity assets.", 
					MessageType.Info);
				return;
			}

			if (_foundReferences.Count == 0 && _targetAsset != null)
			{
				EditorGUILayout.HelpBox(
					$"No references found to '{_targetAsset.name}'.\n" +
					"Try checking if the asset is actually used in your project.", 
					MessageType.Warning);
				return;
			}
		}

		private async UniTaskVoid RebuildCache(bool force)
		{
			try
			{
				var cancel = new CancellationTokenSource();
				EditorUtility.DisplayProgressBar("Rebuilding Asset Finder Cache", "Please wait...", 0.001f);
				await AssetCache.Instance.RebuildCache(force, (count, max) => 
				{
					var progress = count / (float)max;
					if(EditorUtility.DisplayCancelableProgressBar("Rebuilding Asset Finder Cache", 
						$"Progress: {count}/{max} ({progress * 100f:F2}%)", progress))
					{
						cancel.Cancel();
					}
				}, cancel.Token);
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private void StartNewSearch()
		{
			ResetSearch();
			
			if (_targetAsset == null) 
				return;

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
