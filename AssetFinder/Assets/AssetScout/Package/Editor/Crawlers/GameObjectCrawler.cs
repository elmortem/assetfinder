using System.Collections.Generic;
using UnityEngine;

namespace AssetScout.Crawlers
{
	public class GameObjectCrawler : IAssetCrawler
	{
		public bool CanCrawl(object currentObject) => currentObject is GameObject;

		public IEnumerable<TraversalContext> GetChildren(object currentObject, TraversalContext parentContext)
		{
			if (currentObject is GameObject go && go != null)
			{
				foreach (var component in go.GetComponents<Component>())
				{
					if (component != null)
					{
						yield return parentContext.CreateChildContext(component,
							$"{parentContext.CurrentPath}[{component.GetType().Name}]");
					}
				}

				foreach (Transform child in go.transform)
				{
					if (child != null)
					{
						yield return parentContext.CreateChildContext(child.gameObject,
							$"{parentContext.CurrentPath}/{child.name}");
					}
				}
			}
		}
	}
}