using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Crawlers;
using UnityEngine;

namespace AssetScout.Search
{
	public interface IReferenceProcessor
	{
		void ProcessElement(object element, TraversalContext context, string assetGuid, Dictionary<string, List<string>> result);

		bool ShouldCrawlDeeper(object currentObject, TraversalContext context);
		string DrawGUI(string searchKey, bool active);
	}
}