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

		public void Crawl(object rootObject, string initialPath,
			Func<object, TraversalContext, bool> elementProcessor, CancellationToken cancellationToken)
		{
			_processedObjects.Clear();
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

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			if (currentObject is Object unityObject)
			{
				(Object target, string path) processedKey = (unityObject, context.CurrentPath);

				if (!_processedObjects.Add(processedKey))
				{
					return;
				}
			}

			if (!elementProcessor(currentObject, context))
			{
				return;
			}

			InitializeCrawlers();

			var selectedCrawler = _crawlers.FirstOrDefault(p => p.CanCrawl(currentObject));
			if (selectedCrawler == null)
				return;

			var childrenContexts = selectedCrawler.GetChildren(currentObject, context);
			if (childrenContexts != null)
			{
				foreach (var childContext in childrenContexts)
				{
					if (childContext == null) 
						continue;
					
					if (childContext.CurrentObject is Object unityChildObject)
					{
						if (unityChildObject == null)
							continue;
						
						(Object target, string path) processedKey = (unityChildObject, context.CurrentPath);
						if (!_processedObjects.Add(processedKey))
						{
							Debug.Log($"Skipping already processed object: {unityChildObject.name}");
							continue;
						}
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