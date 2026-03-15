using System.Collections.Generic;

namespace AssetScout.Search
{
	public class SearchResultSet
	{
		public string ProviderId { get; set; }
		public List<SearchResult> Results { get; set; }
	}
}
