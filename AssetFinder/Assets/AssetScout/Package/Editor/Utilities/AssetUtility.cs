using System.IO;
using UnityEngine;

namespace AssetScout.Utilities
{
	public static class AssetUtility
	{
		public static bool IsBaseAsset(string path)
		{
			if (!path.StartsWith("Assets"))
				return false;

			var ext = Path.GetExtension(path).ToLower();
			if (ext == ".meta" || ext == ".cs" || ext == ".asmdef" || ext == ".asmref" || ext == ".dll" || 
				ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".svg" || ext == ".psd")
				return false;

			return true;
		}
	}
}