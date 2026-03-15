using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetScout.Search
{
	public static class IndexerRegistry
	{
		public static List<IReferenceIndexer> DiscoverIndexers()
		{
			var indexers = new List<IReferenceIndexer>();

			var indexerTypes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(IReferenceIndexer).IsAssignableFrom(t)
							&& !t.IsInterface && !t.IsAbstract);

			foreach (var type in indexerTypes)
				indexers.Add((IReferenceIndexer)Activator.CreateInstance(type));

			return indexers;
		}
	}
}
