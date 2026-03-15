using System.Collections.Generic;
using System.Threading;
using AssetScout.Crawlers;
using UnityEditor;
using UnityEngine;

namespace AssetScout.Search
{
	internal class ObjectReferenceSearcher
	{
		private readonly List<IReferenceIndexer> _indexers;

		public ObjectReferenceSearcher(List<IReferenceIndexer> indexers)
		{
			_indexers = indexers ?? new List<IReferenceIndexer>();
			foreach (var indexer in _indexers)
			{
				indexer.Reset();
			}
		}

		public void FindReferencePaths(
			Object sourceAsset,
			Dictionary<string, Dictionary<string, HashSet<string>>> results,
			CancellationToken cancellationToken)
		{
			if (sourceAsset == null)
				return;

			var assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sourceAsset));

			bool ProccessElement(object currentObject, TraversalContext context)
			{
				bool shouldCrawlDeeper = true;
				foreach (var indexer in _indexers)
				{
					if (!indexer.ShouldCrawlDeeper(currentObject, context))
					{
						shouldCrawlDeeper = false;
					}

					if (!results.TryGetValue(indexer.Id, out var indexerResults))
					{
						indexerResults = new Dictionary<string, HashSet<string>>();
						results[indexer.Id] = indexerResults;
					}

					indexer.ProcessElement(currentObject, context, assetGuid, indexerResults);
				}

				return shouldCrawlDeeper;
			}

			var assetCrawler = new AssetCrawler();
			assetCrawler.Crawl(sourceAsset, sourceAsset.name, ProccessElement, cancellationToken);
		}
	}
}
