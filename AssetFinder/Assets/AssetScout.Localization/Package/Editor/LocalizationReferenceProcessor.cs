#if PACKAGE_LOCALIZATION
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Crawlers;
using AssetScout.Search;
using UnityEditor;
using UnityEngine;

namespace AssetScout.Localization
{
	internal class LocalizationReferenceProcessor : IReferenceProcessor
	{
		public string ProcessorId => typeof(LocalizationReferenceProcessor).FullName;

		private string _searchKey = string.Empty;

		public string DrawGUI(string searchKey, bool active)
		{
			GUI.enabled = active;
			_searchKey = EditorGUILayout.TextField("Localization Key:", _searchKey);
			GUI.enabled = true;
			return active ? _searchKey : searchKey;
		}

		public async Task ProcessElement(object element, TraversalContext context, string assetGuid, Dictionary<string, List<string>> result, CancellationToken cancellationToken)
		{
			if (context.FieldInfo?.FieldType == typeof(string) &&
				context.FieldInfo.GetCustomAttribute<LocalizationStringAttribute>() != null)
			{
				var localizationKey = element as string;
				if (!string.IsNullOrEmpty(localizationKey))
				{
					AddReference(result, localizationKey, context.CurrentPath);
				}
			}
		}

		public bool ShouldCrawlDeeper(object currentObject, TraversalContext context)
		{
			return true;
		}
		private void AddReference(Dictionary<string, List<string>> result, string guid, string path)
		{
			if (!result.ContainsKey(guid))
				result[guid] = new List<string>();

			result[guid].Add(path);
		}
	}
}
#endif