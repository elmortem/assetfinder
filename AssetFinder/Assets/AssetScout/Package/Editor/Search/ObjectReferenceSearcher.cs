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

		public async Task<Dictionary<string, List<string>>> FindReferencePaths(
			Object sourceAsset,
			CancellationToken cancellationToken)
		{
			var result = new Dictionary<string, List<string>>();
			
			if (sourceAsset == null)
				return result;

			var assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sourceAsset));
			
			async Task<bool> ProccessElement(object currentObject, TraversalContext context)
			{
				bool shouldCrawlDeeper = true;
				foreach (var processor in _processors)
				{
					if (!processor.ShouldCrawlDeeper(currentObject, context))
					{
						shouldCrawlDeeper = false;
					}

					await processor.ProcessElement(currentObject, context, assetGuid, result, cancellationToken);
				}

				return shouldCrawlDeeper;
			}

			var assetCrawler = new AssetCrawler();
			await assetCrawler.Crawl(sourceAsset, sourceAsset.name, ProccessElement, cancellationToken);
			return result;
		}
	}
}