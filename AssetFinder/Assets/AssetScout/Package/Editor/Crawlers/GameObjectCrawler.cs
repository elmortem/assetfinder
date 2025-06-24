using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AssetScout.Crawlers
{
	public class GameObjectCrawler : IAssetCrawler
	{
		private static readonly HashSet<Type> IgnoreTypes = new()
		{
			typeof(Transform), typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)
		};
		
		public bool CanCrawl(object currentObject) => currentObject is GameObject;

		public IEnumerable<TraversalContext> GetChildren(object currentObject, TraversalContext parentContext)
		{
			if (currentObject is GameObject go && go != null)
			{
				foreach (var component in go.GetComponents<Component>())
				{
					if (component == null)
						continue;
					
					if (IgnoreTypes.Contains(component.GetType()))
						continue;
					
					yield return parentContext.CreateChildContext(component,
						$"{parentContext.CurrentPath}[{component.GetType().Name}]");
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