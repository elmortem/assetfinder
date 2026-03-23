using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using AssetScout.Search;

namespace AssetScout.Cache
{
	[System.Serializable]
	public class AssetScoutSettings : ScriptableObject
	{
		public const string ProjectSettingsPath = "Project/Asset Scout";
		private const string SettingsPath = "ProjectSettings/AssetScoutSettings.json";
		private static AssetScoutSettings _instance;

		public static AssetScoutSettings Instance
		{
			get
			{
				if (_instance != null) return _instance;

				if (File.Exists(SettingsPath))
				{
					try
					{
						var json = File.ReadAllText(SettingsPath);
						_instance = CreateInstance<AssetScoutSettings>();
						JsonUtility.FromJsonOverwrite(json, _instance);
					}
					catch
					{
						_instance = CreateInstance<AssetScoutSettings>();
						SaveSettings();
					}
				}
				else
				{
					_instance = CreateInstance<AssetScoutSettings>();
					SaveSettings();
				}

				return _instance;
			}
		}

		private static void SaveSettings()
		{
			if (_instance == null) return;

			try
			{
				var json = JsonUtility.ToJson(_instance, true);
				File.WriteAllText(SettingsPath, json);
			}
			catch (System.Exception e)
			{
				Debug.LogError($"Failed to save Asset Scout settings: {e.Message}");
			}
		}

		public static readonly string[] DefaultCacheAssetTypes =
		{
			"GameObject", "ScriptableObject", "Material", "SceneAsset",
			"SpriteAtlas", "AnimatorController", "AnimatorOverrideController", "AnimationClip"
		};

		[SerializeField]
		private bool _autoUpdateCache;
		[SerializeField]
		private bool _autoRefresh = true;
		[SerializeField]
		private List<string> _cacheAssetTypes = new(DefaultCacheAssetTypes);
		[SerializeField, HideInInspector]
		private List<string> _indexerDisabled = new();
		[SerializeField, HideInInspector]
		private List<string> _providerDisabled = new();

		public bool AutoUpdateCache
		{
			get => _autoUpdateCache;
			set
			{
				if (_autoUpdateCache != value)
				{
					_autoUpdateCache = value;
					SaveSettings();
				}
			}
		}

		public bool AutoRefresh
		{
			get => _autoRefresh;
			set
			{
				if (_autoRefresh != value)
				{
					_autoRefresh = value;
					SaveSettings();
				}
			}
		}

		public List<string> CacheAssetTypes => _cacheAssetTypes;

		public string BuildAssetTypeFilter()
		{
			return string.Join(" ", _cacheAssetTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => $"t:{t}"));
		}

		public void SetCacheAssetTypes(List<string> types)
		{
			_cacheAssetTypes = new List<string>(types);
			SaveSettings();
		}

		public void ResetCacheAssetTypes()
		{
			_cacheAssetTypes = new List<string>(DefaultCacheAssetTypes);
			SaveSettings();
		}

		public bool GetIndexerState(string typeName)
		{
			return !_indexerDisabled.Contains(typeName);
		}

		public void SetIndexerState(string typeName, bool state)
		{
			if (!state)
			{
				if (!_indexerDisabled.Contains(typeName))
					_indexerDisabled.Add(typeName);
			}
			else
			{
				_indexerDisabled.Remove(typeName);
			}

			SaveSettings();
		}

		public bool GetProviderState(string typeName)
		{
			return !_providerDisabled.Contains(typeName);
		}

		public void SetProviderState(string typeName, bool state)
		{
			if (!state)
			{
				if (!_providerDisabled.Contains(typeName))
					_providerDisabled.Add(typeName);
			}
			else
			{
				_providerDisabled.Remove(typeName);
			}

			SaveSettings();
		}

		[MenuItem("Tools/Asset Scout/Settings")]
		static void OpenProjectSettings()
		{
			SettingsService.OpenProjectSettings(ProjectSettingsPath);
		}
	}

	internal class AssetFinderSettingsProvider : SettingsProvider
	{
		private bool _autoUpdateCache;
		private bool _autoRefresh;
		private List<Type> _indexerTypes;
		private Dictionary<string, bool> _indexerStates;
		private List<string> _cacheAssetTypes;
		private ReorderableList _assetTypesReorderableList;

		private static class Styles
		{
			public static readonly GUIContent AutoUpdateCache = new("Auto Update Cache", "Automatically update cache when assets change");
			public static readonly GUIContent AutoRefresh = new("Auto Refresh", "Automatically refresh search results when assets change");
		}

		public AssetFinderSettingsProvider()
			: base(AssetScoutSettings.ProjectSettingsPath, SettingsScope.Project)
		{
		}

		public override void OnActivate(string searchContext, VisualElement rootElement)
		{
			var settings = AssetScoutSettings.Instance;
			_autoUpdateCache = settings.AutoUpdateCache;
			_autoRefresh = settings.AutoRefresh;
			_cacheAssetTypes = new List<string>(settings.CacheAssetTypes);

			InitAssetTypesReorderableList();
			LoadIndexerTypes();
		}

		private void InitAssetTypesReorderableList()
		{
			_assetTypesReorderableList = new ReorderableList(_cacheAssetTypes, typeof(string), true, false, true, true)
			{
				drawElementCallback = (rect, index, _, _) =>
				{
					rect.y += 2;
					rect.height = EditorGUIUtility.singleLineHeight;

					EditorGUI.BeginChangeCheck();
					var newValue = EditorGUI.TextField(rect, _cacheAssetTypes[index]);
					if (EditorGUI.EndChangeCheck())
					{
						_cacheAssetTypes[index] = newValue;
						AssetScoutSettings.Instance.SetCacheAssetTypes(_cacheAssetTypes);
					}
				},
				onAddCallback = list =>
				{
					_cacheAssetTypes.Add("");
					AssetScoutSettings.Instance.SetCacheAssetTypes(_cacheAssetTypes);
				},
				onRemoveCallback = list =>
				{
					_cacheAssetTypes.RemoveAt(list.index);
					AssetScoutSettings.Instance.SetCacheAssetTypes(_cacheAssetTypes);
				},
				onReorderCallback = _ =>
				{
					AssetScoutSettings.Instance.SetCacheAssetTypes(_cacheAssetTypes);
				}
			};
		}

		private void LoadIndexerTypes()
		{
			_indexerStates = new Dictionary<string, bool>();

			_indexerTypes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(IReferenceIndexer).IsAssignableFrom(t)
							&& !t.IsInterface && !t.IsAbstract)
				.ToList();

			foreach (var type in _indexerTypes)
			{
				var typeName = type.FullName;
				if (!string.IsNullOrEmpty(typeName))
					_indexerStates[typeName] = AssetScoutSettings.Instance.GetIndexerState(typeName);
			}
		}

		public override void OnGUI(string searchContext)
		{
			var settings = AssetScoutSettings.Instance;
			EditorGUI.BeginChangeCheck();

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				EditorGUILayout.LabelField("Cache Settings", EditorStyles.boldLabel);
				EditorGUILayout.Space();

				_autoUpdateCache = EditorGUILayout.Toggle(Styles.AutoUpdateCache, _autoUpdateCache);
				_autoRefresh = EditorGUILayout.Toggle(Styles.AutoRefresh, _autoRefresh);
			}

			if (EditorGUI.EndChangeCheck())
			{
				settings.AutoUpdateCache = _autoUpdateCache;
				settings.AutoRefresh = _autoRefresh;
			}

			EditorGUILayout.Space();

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				EditorGUILayout.LabelField("Cache Asset Types", EditorStyles.boldLabel);
				EditorGUILayout.Space();
				_assetTypesReorderableList?.DoLayoutList();
			}

			EditorGUILayout.Space();

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				EditorGUILayout.LabelField("Indexers", EditorStyles.boldLabel);
				EditorGUILayout.Space();

				if (_indexerTypes != null)
				{
					foreach (var type in _indexerTypes)
					{
						var typeName = type.FullName;
						if (string.IsNullOrEmpty(typeName))
							continue;

						EditorGUI.BeginChangeCheck();
						var oldState = _indexerStates.TryGetValue(typeName, out var s) && s;
						var newState = EditorGUILayout.ToggleLeft(type.Name, oldState);
						if (EditorGUI.EndChangeCheck())
						{
							_indexerStates[typeName] = newState;
							settings.SetIndexerState(typeName, newState);
						}
					}
				}
			}

			EditorGUILayout.Space();
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Reset to Defaults", GUILayout.Width(120)))
				{
					settings.AutoUpdateCache = false;
					settings.AutoRefresh = true;
					settings.ResetCacheAssetTypes();

					_autoUpdateCache = settings.AutoUpdateCache;
					_autoRefresh = settings.AutoRefresh;
					_cacheAssetTypes = new List<string>(settings.CacheAssetTypes);
					InitAssetTypesReorderableList();
				}
			}
		}

		[SettingsProvider]
		internal static SettingsProvider CreateSettingsProvider()
		{
			return new AssetFinderSettingsProvider();
		}
	}
}
