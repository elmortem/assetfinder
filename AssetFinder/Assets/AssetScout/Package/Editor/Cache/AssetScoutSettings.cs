using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Serialization;

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

		[SerializeField]
		private bool _autoUpdateCache = true;
		[SerializeField]
		private bool _autoRefresh = true;
		[SerializeField, HideInInspector]
		[FormerlySerializedAs("_processorStates")]
		private List<string> _processorDisabled = new();

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

		public bool GetProcessorState(string processorId)
		{
			return !_processorDisabled.Contains(processorId);
		}

		public void SetProcessorState(string processorId, bool state)
		{
			if (!state)
				_processorDisabled.Add(processorId);
			else
				_processorDisabled.Remove(processorId);
			
			SaveSettings();
		}
	}

	internal class AssetFinderSettingsProvider : SettingsProvider
	{
		private bool _autoUpdateCache;
		private bool _autoRefresh;
		private int _maxBackgroundTasks;
		private float _updateDelay;

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
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Reset to Defaults", GUILayout.Width(120)))
				{
					settings.AutoUpdateCache = false;
					settings.AutoRefresh = false;

					_autoUpdateCache = settings.AutoUpdateCache;
					_autoRefresh = settings.AutoRefresh;
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