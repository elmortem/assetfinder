using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetScout.Search
{
	public static class SearchProviderRegistry
	{
		public static List<ISearchProvider> DiscoverProviders()
		{
			return AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(ISearchProvider).IsAssignableFrom(t)
							&& !t.IsInterface && !t.IsAbstract)
				.Select(t => (ISearchProvider)Activator.CreateInstance(t))
				.OrderBy(p => p.Priority)
				.ToList();
		}
	}
}
