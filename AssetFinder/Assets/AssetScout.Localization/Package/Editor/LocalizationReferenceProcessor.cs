#if PACKAGE_LOCALIZATION
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Crawlers;
using AssetScout.Search;
using UnityEditor;
using UnityEngine;
using System.Collections;

namespace AssetScout.Localization
{
	internal class LocalizationReferenceProcessor : IReferenceProcessor
	{
		public string Id => typeof(LocalizationReferenceProcessor).FullName;

		private string _searchKey = string.Empty;

		public string DrawGUI(string searchKey, bool active)
		{
			GUI.enabled = active;
			_searchKey = EditorGUILayout.TextField("Localization Key:", _searchKey);
			GUI.enabled = true;
			return active ? _searchKey : searchKey;
		}

		public void ProcessElement(object element, TraversalContext context, string assetGuid, Dictionary<string, HashSet<string>> results)
		{
			if (context.FieldInfo?.FieldType == typeof(string) &&
				context.FieldInfo.GetCustomAttribute<LocalizationStringAttribute>() != null)
			{
				var localizationKey = element as string;
				if (!string.IsNullOrEmpty(localizationKey))
				{
					AddReference(results, localizationKey, context.CurrentPath);
				}
			}
		}

		public bool ShouldCrawlDeeper(object currentObject, TraversalContext context)
		{
			return true;
		}

		private void AddReference(Dictionary<string, HashSet<string>> results, string guid, string path)
		{
			if (!results.TryGetValue(guid, out var paths))
			{
				paths = new HashSet<string>();
				results[guid] = paths;
			}

			paths.Add(path);
		}
	}
}
#endif