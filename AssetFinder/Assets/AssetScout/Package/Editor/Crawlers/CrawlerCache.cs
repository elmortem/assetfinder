using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetScout.Crawlers
{
	[InitializeOnLoad]
	public static class CrawlerCache
	{
		private static readonly List<IAssetCrawler> _crawlers = new();
		private static bool _crawlersInitialized;
		
		static CrawlerCache()
		{
			Debug.Log("Crawlers re-initializing...");
			ClearCrawlers();
			InitializeCrawlers();
		}

		public static void ClearCrawlers()
		{
			_crawlers.Clear();
			_crawlersInitialized = false;
		}

		public static IReadOnlyList<IAssetCrawler> InitializeCrawlers()
		{
			if (_crawlersInitialized)
			{
				return _crawlers;
			}

			var crawlerTypes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(assembly => assembly.GetTypes())
				.Where(type => !type.IsInterface && !type.IsAbstract && typeof(IAssetCrawler).IsAssignableFrom(type));

			foreach (var type in crawlerTypes)
			{
				var crawler = (IAssetCrawler)Activator.CreateInstance(type);
				_crawlers.Add(crawler);
			}

			_crawlersInitialized = true;
			
			return _crawlers;
		}
	}
}