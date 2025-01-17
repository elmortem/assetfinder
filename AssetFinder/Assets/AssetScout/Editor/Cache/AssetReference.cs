using System.Collections.Generic;
using UnityEngine;

namespace AssetFinder.Cache
{
	public class AssetReference
	{
		public Object Asset { get; }
		public HashSet<string> Paths { get; } = new();

		public AssetReference(Object asset)
		{
			Asset = asset;
		}
	}
}
