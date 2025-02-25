using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Object = UnityEngine.Object;

namespace AssetScout.Crawlers
{
	public class AssetCrawler
	{
		private IReadOnlyList<IAssetCrawler> _crawlers;
		private readonly HashSet<object> _processed = new();

		public void Crawl(object rootObject, string initialPath,
			Func<object, TraversalContext, bool> elementProcessor, CancellationToken cancellationToken)
		{
			if (rootObject == null || (rootObject is Object unityObject && unityObject == null))
				return;
			
			_processed.Clear();
			_crawlers = CrawlerCache.InitializeCrawlers();
			
			var initialContext = new TraversalContext(rootObject, initialPath, 0);
			//var initialContext = new TraversalContext(rootObject, string.Empty, 0);
			CrawlObject(initialContext, elementProcessor, cancellationToken);
		}

		private void CrawlObject(TraversalContext context,
			Func<object, TraversalContext, bool> elementProcessor, CancellationToken cancellationToken)
		{
			var currentObject = context.CurrentObject;
			if (currentObject == null)
			{
				return;
			}

			if (currentObject is Object unityObject && unityObject == null)
			{
				return;
			}
			
			if (!_processed.Add(currentObject))
			{
				return;
			}

			if (!elementProcessor(currentObject, context))
			{
				return;
			}

			var selectedCrawler = _crawlers.FirstOrDefault(p => p.CanCrawl(currentObject));
			if (selectedCrawler == null)
				return;

			var childrenContexts = selectedCrawler.GetChildren(currentObject, context);
			if (childrenContexts != null)
			{
				foreach (var childContext in childrenContexts)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						return;
					}
					
					CrawlObject(childContext, elementProcessor, cancellationToken);
				}
			}
		}
	}
}