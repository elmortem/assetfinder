using System.Collections.Generic;
using AssetScout.Crawlers;

namespace AssetScout.Search
{
	public interface IReferenceIndexer
	{
		string Id { get; }
		void Reset();
		void ProcessElement(object element, TraversalContext context,
			string assetGuid, Dictionary<string, HashSet<string>> results);
		bool ShouldCrawlDeeper(object currentObject, TraversalContext context);
	}
}
