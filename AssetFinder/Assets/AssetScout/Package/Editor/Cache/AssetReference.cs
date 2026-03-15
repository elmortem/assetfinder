using System.Collections.Generic;
using UnityEngine;

namespace AssetScout.Cache
{
	public class AssetReference
	{
		public Object SourceObject { get; }
		public HashSet<string> Paths { get; } = new();

		public AssetReference(Object sourceObject)
		{
			SourceObject = sourceObject;
		}
	}
}
