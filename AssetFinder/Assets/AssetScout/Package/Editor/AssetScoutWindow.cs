using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using AssetScout.Cache;
using AssetScout.Search;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AssetScout.Editor
{
	public class AssetScoutWindow : EditorWindow
	{
		private readonly char[] _animChars = { '|', '|', '/', '/', '-', '-', '\\', '\\' };

		private Object _targetObject;
		private Vector2 _scrollPosition;
		private int _processingDrawIndex;
		private readonly List<AssetReference> _foundReferences = new();

		private bool _showProviders;
		private List<ISearchProvider> _providers;
		private Dictionary<string, bool> _providerStates;

		private SearchContext _searchContext;

		[MenuItem("Tools/Asset Scout/Asset Scout Window")]
		public static void ShowWindow()
		{
			GetWindow<AssetScoutWindow>("Asset Scout");
		}

		private void OnEnable()
		{
			AssetCache.Instance.CacheSaveEvent -= OnCacheSaved;
			AssetCache.Instance.CacheSaveEvent += OnCacheSaved;

			LoadProviders();
			EnsureIndexersSetOnCache();

			if (_targetObject != null &&
				_foundReferences.Count == 0 &&
				AssetScoutSettings.Instance.AutoRefresh)
			{
				StartNewSearch();
			}
		}

		private void OnCacheSaved()
		{
			if (_targetObject != null && AssetScoutSettings.Instance.AutoRefresh)
			{
				StartNewSearch();
			}
		}

		private void EnsureIndexersSetOnCache()
		{
			var allIndexers = IndexerRegistry.DiscoverIndexers();
			var enabledIndexers = allIndexers
				.Where(idx => AssetScoutSettings.Instance.GetIndexerState(idx.Id))
				.ToList();
			AssetCache.Instance.SetIndexers(enabledIndexers);
		}

		private void LoadProviders()
		{
			_providers = SearchProviderRegistry.DiscoverProviders();
			_providerStates = new Dictionary<string, bool>();

			foreach (var provider in _providers)
			{
				var typeName = provider.GetType().FullName;
				if (!string.IsNullOrEmpty(typeName))
					_providerStates[typeName] = AssetScoutSettings.Instance.GetProviderState(typeName);
			}
		}

		private void OnGUI()
		{
			// Detect stale target (e.g. left prefab stage, switched scene)
			if (_targetObject == null && _foundReferences.Count > 0)
			{
				ResetSearch();
			}

			EditorGUILayout.Space(10);

			EditorGUILayout.BeginHorizontal();
			var content = new GUIContent("Rebuild");
			var toolbarRect = GUILayoutUtility.GetRect(content, EditorStyles.toolbarDropDown);
			toolbarRect.width = 65f + 16f;
			var buttonRect = new Rect(toolbarRect.x, toolbarRect.y, toolbarRect.width - 16f, toolbarRect.height);
			var dropdownRect = new Rect(toolbarRect.x + toolbarRect.width - 16f, toolbarRect.y, 16f,
				toolbarRect.height);
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
			EditorGUILayout.LabelField($"Last Rebuild: {timeString}", EditorStyles.miniLabel,
				GUILayout.ExpandWidth(true));

			if (AssetCache.Instance.IsProcessing)
			{
				_processingDrawIndex++;
				if (_processingDrawIndex % _animChars.Length == 0)
					_processingDrawIndex = 0;

				EditorGUILayout.LabelField(_animChars[_processingDrawIndex] + " Processing...", EditorStyles.miniLabel,
					GUILayout.ExpandWidth(false));
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space(5);

			// Build search context
			_searchContext = new SearchContext();
			_searchContext.PrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
			_searchContext.ActiveScene = SceneManager.GetActiveScene();

			// Unified ObjectField
			EditorGUILayout.BeginVertical();

			var newTarget = EditorGUILayout.ObjectField(_targetObject, typeof(Object), true);
			if (newTarget != _targetObject)
			{
				_targetObject = newTarget;
				_searchContext.Target = _targetObject;

				if (AssetScoutSettings.Instance.AutoRefresh)
					StartNewSearch();
				else
					ResetSearch();
			}
			else
			{
				_searchContext.Target = _targetObject;
			}

			// Draw extra GUI from enabled providers
			foreach (var provider in _providers)
			{
				var typeName = provider.GetType().FullName;
				if (!string.IsNullOrEmpty(typeName) && _providerStates.TryGetValue(typeName, out var enabled) && enabled)
				{
					provider.DrawExtraGUI(_searchContext);
				}
			}

			if (!AssetScoutSettings.Instance.AutoRefresh)
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Refresh", GUILayout.Width(60)))
				{
					StartNewSearch();
				}

				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndVertical();

			EditorGUILayout.Space();

			// Search Providers foldout
			_showProviders = EditorGUILayout.BeginFoldoutHeaderGroup(_showProviders, "Search Providers");
			if (_showProviders)
			{
				foreach (var provider in _providers)
				{
					var typeName = provider.GetType().FullName;
					if (string.IsNullOrEmpty(typeName))
						continue;

					EditorGUI.BeginChangeCheck();
					var oldState = _providerStates.TryGetValue(typeName, out var s) && s;
					var newState = EditorGUILayout.ToggleLeft(provider.DisplayName, oldState);
					if (EditorGUI.EndChangeCheck() && newState != oldState)
					{
						_providerStates[typeName] = newState;
						AssetScoutSettings.Instance.SetProviderState(typeName, newState);
					}
				}
			}
			EditorGUILayout.EndFoldoutHeaderGroup();

			EditorGUILayout.Space();

			// Results
			if (_foundReferences.Count > 0)
			{
				EditorGUILayout.LabelField($"Found references in {_foundReferences.Count} sources:");
				_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

				foreach (var reference in _foundReferences)
				{
					EditorGUILayout.BeginVertical(EditorStyles.helpBox);

					EditorGUILayout.ObjectField(reference.SourceObject, typeof(Object), true);

					EditorGUI.indentLevel++;
					int num = 0;
					foreach (var path in reference.Paths)
					{
						var rect = EditorGUILayout.GetControlRect();
						EditorGUI.LabelField(rect, $"{++num}. {path}");

						// Click navigation for local results
						if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
						{
							if (reference.SourceObject != null)
							{
								EditorGUIUtility.PingObject(reference.SourceObject);
								Selection.activeObject = reference.SourceObject;
							}
							Event.current.Use();
						}
					}
					EditorGUI.indentLevel--;

					EditorGUILayout.EndVertical();
					EditorGUILayout.Space(2);
				}

				EditorGUILayout.EndScrollView();
			}

			if (_targetObject == null && (_searchContext.Extras == null || _searchContext.Extras.Count == 0
				|| !_searchContext.Extras.Any(kv => kv.Value is string s && !string.IsNullOrEmpty(s))))
			{
				EditorGUILayout.HelpBox(
					"Drag & Drop an asset or scene object here to find all references to it.\n\n" +
					"The tool will search through:\n" +
					"• Project assets (global search)\n" +
					"• Open prefab hierarchy (local search)\n" +
					"• Loaded scene hierarchy (local search)\n" +
					"And other sources via search providers...",
					MessageType.Info);
				return;
			}

			if (_foundReferences.Count == 0)
			{
				EditorGUILayout.HelpBox(
					"No references found.\n" +
					"Try checking if the asset/object is actually used in your project.",
					MessageType.Warning);
			}
		}

		private async void RebuildCache(bool force)
		{
			try
			{
				EnsureIndexersSetOnCache();
				var cancel = new CancellationTokenSource();
				EditorUtility.DisplayProgressBar("Rebuilding Asset Scout Cache", "Please wait...", 0.001f);
				AssetCache.Instance.RebuildCache(force, (count, max) =>
				{
					var progress = count / (float)max;
					if (EditorUtility.DisplayCancelableProgressBar("Rebuilding Asset Scout Cache",
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

			if (_searchContext == null)
			{
				_searchContext = new SearchContext();
				_searchContext.Target = _targetObject;
				_searchContext.PrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
				_searchContext.ActiveScene = SceneManager.GetActiveScene();
			}

			// Group results by SourceObject
			var groupedResults = new Dictionary<Object, AssetReference>();

			foreach (var provider in _providers)
			{
				var typeName = provider.GetType().FullName;
				if (string.IsNullOrEmpty(typeName))
					continue;
				if (!_providerStates.TryGetValue(typeName, out var enabled) || !enabled)
					continue;
				if (!provider.CanSearch(_searchContext))
					continue;

				provider.Search(_searchContext, resultSet =>
				{
					foreach (var result in resultSet.Results)
					{
						var sourceObj = result.SourceObject;
						if (sourceObj == null)
							continue;

						if (!groupedResults.TryGetValue(sourceObj, out var assetRef))
						{
							assetRef = new AssetReference(sourceObj);
							groupedResults[sourceObj] = assetRef;
						}

						// Build display path
						var displayPath = BuildDisplayPath(result);
						if (!string.IsNullOrEmpty(displayPath))
							assetRef.Paths.Add(displayPath);
					}
				});
			}

			_foundReferences.AddRange(groupedResults.Values);
			Repaint();
		}

		private string BuildDisplayPath(SearchResult result)
		{
			if (!string.IsNullOrEmpty(result.HierarchyPath))
			{
				// Local result
				var path = result.HierarchyPath;
				if (!string.IsNullOrEmpty(result.ComponentType))
					path += $" [{result.ComponentType}]";
				if (!string.IsNullOrEmpty(result.PropertyPath))
					path += $".{result.PropertyPath}";
				return path;
			}

			// Global result — just the property path
			return result.PropertyPath;
		}

		private void ResetSearch()
		{
			_foundReferences.Clear();

			if (_targetObject == null)
				_targetObject = null;
		}
	}
}
