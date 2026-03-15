#if PACKAGE_LOCALIZATION
using System;
using System.Collections.Generic;
using AssetScout.Cache;
using AssetScout.Search;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Localization
{
	public class LocalizationSearchProvider : ISearchProvider
	{
		public string Id => typeof(LocalizationSearchProvider).FullName;
		public string DisplayName => "Localization";
		public int Priority => 20;

		public bool CanSearch(SearchContext context)
		{
			return context.Extras.TryGetValue("localizationKey", out var key)
				   && key is string str && !string.IsNullOrEmpty(str);
		}

		public void Search(SearchContext context, Action<SearchResultSet> onComplete)
		{
			var results = new List<SearchResult>();

			if (context.Extras.TryGetValue("localizationKey", out var keyObj)
				&& keyObj is string key && !string.IsNullOrEmpty(key))
			{
				var processorId = typeof(LocalizationReferenceIndexer).FullName;
				var rawResults = AssetCache.Instance.FindReferences(key, processorId);

				foreach (var (assetGuid, paths) in rawResults)
				{
					var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
					var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
					if (asset == null)
						continue;

					foreach (var path in paths)
					{
						results.Add(new SearchResult
						{
							AssetPath = assetPath,
							PropertyPath = path,
							SourceObject = asset
						});
					}
				}
			}

			onComplete(new SearchResultSet { ProviderId = Id, Results = results });
		}

		public void DrawExtraGUI(SearchContext context)
		{
			if (!context.Extras.TryGetValue("localizationKey", out var keyObj))
				keyObj = "";

			var currentKey = keyObj as string ?? "";
			var newKey = EditorGUILayout.TextField("Localization Key:", currentKey);
			context.Extras["localizationKey"] = newKey;
		}
	}
}
#endif
