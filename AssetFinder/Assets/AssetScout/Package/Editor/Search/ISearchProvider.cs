using System;

namespace AssetScout.Search
{
	public interface ISearchProvider
	{
		string Id { get; }
		string DisplayName { get; }
		int Priority { get; }
		bool CanSearch(SearchContext context);
		void Search(SearchContext context, Action<SearchResultSet> onComplete);
		void DrawExtraGUI(SearchContext context);
	}
}
