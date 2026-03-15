using System;
using System.Collections.Generic;
using AssetScout.Cache;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Search
{
	public class GlobalSearchProvider : ISearchProvider
	{
		public string Id => typeof(GlobalSearchProvider).FullName;
		public string DisplayName => "Global (Asset Cache)";
		public int Priority => 0;

		public bool CanSearch(SearchContext context)
		{
			if (context.Target == null)
				return false;

			var assetPath = AssetDatabase.GetAssetPath(context.Target);
			return !string.IsNullOrEmpty(assetPath) && AssetDatabase.Contains(context.Target);
		}

		public void Search(SearchContext context, Action<SearchResultSet> onComplete)
		{
			var results = new List<SearchResult>();
			var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(context.Target));

			if (string.IsNullOrEmpty(guid))
			{
				onComplete(new SearchResultSet { ProviderId = Id, Results = results });
				return;
			}

			var rawResults = AssetCache.Instance.FindReferences(guid);

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

			onComplete(new SearchResultSet { ProviderId = Id, Results = results });
		}

		public void DrawExtraGUI(SearchContext context)
		{
		}
	}
}
