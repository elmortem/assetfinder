using UnityEngine;

namespace AssetScout.Search
{
	public class SearchResult
	{
		public string AssetPath { get; set; }
		public string HierarchyPath { get; set; }
		public string ComponentType { get; set; }
		public string PropertyPath { get; set; }
		public Object SourceObject { get; set; }
	}
}
