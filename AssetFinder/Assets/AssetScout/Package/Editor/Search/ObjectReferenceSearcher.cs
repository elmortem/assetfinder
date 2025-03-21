using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Crawlers;
using UnityEditor;
using UnityEngine;

namespace AssetScout.Search
{
	internal class ObjectReferenceSearcher
	{
		private readonly List<IReferenceProcessor> _processors;

		public ObjectReferenceSearcher(List<IReferenceProcessor> processors)
		{
			_processors = processors ?? new List<IReferenceProcessor>();
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
				foreach (var processor in _processors)
				{
					if (!processor.ShouldCrawlDeeper(currentObject, context))
					{
						shouldCrawlDeeper = false;
					}

					if (!results.TryGetValue(processor.Id, out var processorResults))
					{
						processorResults = new Dictionary<string, HashSet<string>>();
						results[processor.Id] = processorResults;
					}

					processor.ProcessElement(currentObject, context, assetGuid, processorResults);
				}

				return shouldCrawlDeeper;
			}

			var assetCrawler = new AssetCrawler();
			assetCrawler.Crawl(sourceAsset, sourceAsset.name, ProccessElement, cancellationToken);
		}
	}
}