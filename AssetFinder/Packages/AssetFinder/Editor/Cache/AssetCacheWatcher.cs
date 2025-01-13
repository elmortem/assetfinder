using System.Collections.Generic;
using System.IO;
using UnityEditor;
using System.Linq;

namespace AssetFinder.Cache
{
    public class AssetCacheWatcher : AssetPostprocessor
    {
		private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!AssetFinderSettings.Instance.AutoUpdateCache)
                return;
            
			var changedAssets = importedAssets.Concat(deletedAssets).Where(p => CorrectPath(p)).ToArray();
            if (changedAssets.Length > 0)
            {
                ProcessChangedAssets(changedAssets);
            }
		}
        
        private static bool CorrectPath(string path)
        {
            if (!path.StartsWith("Assets"))
                return false;

            var ext = Path.GetExtension(path).ToLower();
            if (ext == ".meta" || ext == ".cs" || ext == ".asmdef" || ext == ".asmref" || ext == ".dll")
                return false;

            return true;
        }

        private static void ProcessChangedAssets(string[] changedAssets)
        {
            if (changedAssets == null || changedAssets.Length == 0)
                return;

            var uniqueDependentAssets = new HashSet<string>();

            foreach (var assetPath in changedAssets)
            {
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    continue;

                uniqueDependentAssets.Add(assetPath);
            }

            var guidsToProcess = uniqueDependentAssets
                .Select(AssetDatabase.AssetPathToGUID)
                .Where(guid => !string.IsNullOrEmpty(guid))
                .ToList();

            AssetCache.Instance.EnqueueAssetsForProcessing(guidsToProcess);
        }
    }
}
