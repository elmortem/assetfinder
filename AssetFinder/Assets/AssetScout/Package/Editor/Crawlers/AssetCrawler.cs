using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Crawlers
{
	public class AssetCrawler
	{
		private static readonly List<IAssetCrawler> _crawlers = new();
		private static bool _crawlersInitialized;
		private readonly HashSet<(Object target, string path)> _processedObjects = new();
		private readonly HashSet<object> _processed = new();

		public void Crawl(object rootObject, string initialPath,
			Func<object, TraversalContext, bool> elementProcessor, CancellationToken cancellationToken)
		{
			if (rootObject == null || (rootObject is Object unityObject && unityObject == null))
				return;
			
			_processedObjects.Clear();
			_processed.Clear();
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

			if (currentObject is Object unityObject)
			{
				if (unityObject == null)
					return;
				
				/*(Object target, string path) processedKey = (unityObject, context.CurrentPath);

				if (!_processedObjects.Add(processedKey))
				{
					return;
				}*/
			}
			
			if (!_processed.Add(currentObject))
			{
				return;
			}

			if (!elementProcessor(currentObject, context))
			{
				return;
			}

			InitializeCrawlers();
			
			if (cancellationToken.IsCancellationRequested)
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
		
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		public static void Initialize()
		{
			ClearCrawlers();
			InitializeCrawlers();
		}

		public static void ClearCrawlers()
		{
			_crawlers.Clear();
			_crawlersInitialized = false;
		}

		private static void InitializeCrawlers()
		{
			if (_crawlersInitialized)
				return;

			var crawlerTypes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(assembly => assembly.GetTypes())
				.Where(type => !type.IsInterface && !type.IsAbstract && typeof(IAssetCrawler).IsAssignableFrom(type));

			foreach (var type in crawlerTypes)
			{
				var crawler = (IAssetCrawler)Activator.CreateInstance(type);
				_crawlers.Add(crawler);
			}

			_crawlersInitialized = true;
		}
	}
}