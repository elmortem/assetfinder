using System.Collections.Generic;

namespace AssetScout.Crawlers
{
	public interface IAssetCrawler
	{
		bool CanCrawl(object currentObject);
		IEnumerable<TraversalContext> GetChildren(object currentObject, TraversalContext parentContext);
	}
}