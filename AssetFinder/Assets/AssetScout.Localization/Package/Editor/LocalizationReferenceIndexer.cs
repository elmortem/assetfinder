#if PACKAGE_LOCALIZATION
using System.Collections.Generic;
using System.Reflection;
using AssetScout.Crawlers;
using AssetScout.Search;
using UnityEditor;

namespace AssetScout.Localization
{
	internal class LocalizationReferenceIndexer : IReferenceIndexer
	{
		public string Id => typeof(LocalizationReferenceIndexer).FullName;

		public void Reset()
		{
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
