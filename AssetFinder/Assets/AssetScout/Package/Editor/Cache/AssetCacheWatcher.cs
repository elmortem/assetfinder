using UnityEditor;
using System.Linq;
using AssetScout.Utilities;

namespace AssetScout.Cache
{
	public class AssetCacheWatcher : AssetPostprocessor
	{
		private static void OnPostprocessAllAssets(
			string[] importedAssets,
			string[] deletedAssets,
			string[] movedAssets,
			string[] movedFromAssetPaths)
		{
			if (!AssetScoutSettings.Instance.AutoUpdateCache || AssetCache.Instance.IsRebuilding)
				return;
			
			var changedAssets = importedAssets.Concat(deletedAssets).Where(AssetUtility.IsBaseAsset).ToArray();
			if (changedAssets.Length > 0)
			{
				ProcessChangedAssets(changedAssets);
			}
		}

		private static void ProcessChangedAssets(string[] changedAssets)
		{
			if (changedAssets == null || changedAssets.Length == 0)
				return;

			var guidsToProcess = changedAssets
				.Select(AssetDatabase.AssetPathToGUID)
				.Where(guid => !string.IsNullOrEmpty(guid))
				.ToList();

			AssetCache.Instance.EnqueueAssetsForProcessing(guidsToProcess);
		}
	}
}
