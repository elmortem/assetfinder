using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using AssetScout.Cache;
using AssetScout.Search;
using Object = UnityEngine.Object;

namespace AssetScout.Editor
{
	public class AssetScoutWindow : EditorWindow
	{
		private readonly char[] _animChars = { '|', '|', '/', '/', '-', '-', '\\', '\\' };

		private string _searchKey = string.Empty;
		private Vector2 _scrollPosition;
		private int _processingDrawIndex;
		private readonly List<AssetReference> _foundReferences = new();

		private bool _showProcessors;
		private List<Type> _processorTypes;
		private List<IReferenceProcessor> _processors;
		private Dictionary<string, bool> _processorStates;


		[MenuItem("Tools/Asset Scout")]
		public static void ShowWindow()
		{
			GetWindow<AssetScoutWindow>("Asset Scout");
		}

		private void OnEnable()
		{
			AssetCache.Instance.CacheSaveEvent -= OnCacheSaved;
			AssetCache.Instance.CacheSaveEvent += OnCacheSaved;

			LoadProcessors();

			if (!string.IsNullOrEmpty(_searchKey) && 
				_foundReferences.Count == 0 && 
				AssetScoutSettings.Instance.AutoRefresh)
			{
				StartNewSearch();
			}
		}

		private void OnCacheSaved()
		{
			if (!string.IsNullOrEmpty(_searchKey) && AssetScoutSettings.Instance.AutoRefresh)
			{
				StartNewSearch();
			}
		}

		private void LoadProcessors()
		{
			_processors = new List<IReferenceProcessor>();
			_processorStates = new Dictionary<string, bool>();

			_processorTypes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(assembly => assembly.GetTypes())
				.Where(type =>
					typeof(IReferenceProcessor).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
				.ToList();
			_processorTypes.Sort((pt1, pt2) =>
			{
				if (pt1 == typeof(DefaultReferenceProcessor))
					return -1;
				if (pt2 == typeof(DefaultReferenceProcessor))
					return 1;
				return string.Compare(pt1.Name, pt2.Name, StringComparison.Ordinal);
			});

			foreach (var type in _processorTypes)
			{
				try
				{
					var typeName = type.FullName;
					if (string.IsNullOrEmpty(typeName))
						continue;
					
					var state = AssetScoutSettings.Instance.GetProcessorState(typeName);
					_processorStates[typeName] = state;
					if (state)
					{
						var processor = (IReferenceProcessor)Activator.CreateInstance(type);
						_processors.Add(processor);
					}
				}
				catch (Exception e)
				{
					Debug.LogError($"Failed to create instance of IReferenceProcessor: {type.FullName}.  Error: {e}");
				}
			}
			
			SortProcessors();
			AssetCache.Instance.SetProcessors(_processors);
		}
		
		private void SortProcessors()
		{
			_processors.Sort((p1, p2) =>
			{
				if (p1 is DefaultReferenceProcessor)
					return -1;
				if (p2 is DefaultReferenceProcessor)
					return 1;
				return string.Compare(p1.GetType().Name, p2.GetType().Name, StringComparison.Ordinal);
			});
		}

		private void OnGUI()
		{
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
			
			
			EditorGUILayout.BeginVertical();

			var newSearchKey = _searchKey;
			foreach (var processor in _processors)
			{
				newSearchKey = processor.DrawGUI(newSearchKey, string.IsNullOrEmpty(newSearchKey));
			}
			
			if (newSearchKey != _searchKey)
			{
				_searchKey = newSearchKey;
				
				if (AssetScoutSettings.Instance.AutoRefresh)
					StartNewSearch();
				else
					ResetSearch();
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

			//GUI.enabled = false;
			//EditorGUILayout.TextField("Search Key:", _searchKey);
			//GUI.enabled = true;
			
			EditorGUILayout.EndVertical();
			
			EditorGUILayout.Space();

			_showProcessors = EditorGUILayout.BeginFoldoutHeaderGroup(_showProcessors, "Processors");
			if (_showProcessors)
			{
				foreach (var type in _processorTypes)
				{
					var typeName = type.FullName;
					if (string.IsNullOrEmpty(typeName))
						continue;

					EditorGUI.BeginChangeCheck();
					var oldState = _processorStates[typeName];
					var newState = EditorGUILayout.ToggleLeft(type.Name, oldState);
					if (EditorGUI.EndChangeCheck())
					{
						_processorStates[typeName] = newState;
						AssetScoutSettings.Instance.SetProcessorState(typeName, newState);

						if (newState != oldState)
						{
							if (newState)
							{
								var processor = (IReferenceProcessor)Activator.CreateInstance(type);
								_processors.Add(processor);
							}
							else
							{
								var toRemove = _processors.Where(p => p.GetType() == type).ToList();
								foreach (var processor in toRemove)
								{
									//processor.Dispose();
									_processors.Remove(processor);
								}
							}

							SortProcessors();
							AssetCache.Instance.SetProcessors(_processors);
						}
					}
				}
			}
			EditorGUILayout.EndFoldoutHeaderGroup();

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
						//EditorGUILayout.LabelField($"• {path}");
					}
					EditorGUI.indentLevel--;

					EditorGUILayout.EndVertical();
					EditorGUILayout.Space(2);
				}

				EditorGUILayout.EndScrollView();
			}

			if (string.IsNullOrEmpty(_searchKey))
			{
				EditorGUILayout.HelpBox(
					"Drag & Drop an asset here to find all references to it in your project.\n\n" +
					"The tool will search through:\n" +
					"• Sprites\n" +
					"• Prefabs\n" +
					"• Scriptable Objects\n" +
					"• Materials\n" +
					"And other Unity assets...",
					MessageType.Info);
				return;
			}

			if (_foundReferences.Count == 0 && !string.IsNullOrEmpty(_searchKey))
			{
				EditorGUILayout.HelpBox(
					$"No references found for '{_searchKey}'.\n" +
					"Try checking if the asset/key is actually used in your project.",
					MessageType.Warning);
				return;
			}
		}

		private async void RebuildCache(bool force)
		{
			try
			{
				var cancel = new CancellationTokenSource();
				EditorUtility.DisplayProgressBar("Rebuilding Asset Finder Cache", "Please wait...", 0.001f);
				await AssetCache.Instance.RebuildCache(force, (count, max) =>
				{
					if (count % 10 != 0) 
						return;
					var progress = count / (float)max;
					if (EditorUtility.DisplayCancelableProgressBar("Rebuilding Asset Finder Cache",
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

			if (string.IsNullOrEmpty(_searchKey))
				return;

			try
			{
				var rawResults = AssetCache.Instance.FindReferences(_searchKey);

				foreach (var (assetGuid, paths) in rawResults)
				{
					var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
					var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
					if (asset == null) 
						continue;

					var reference = new AssetReference(asset);
					reference.Paths.UnionWith(paths);
					_foundReferences.Add(reference);
				}
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