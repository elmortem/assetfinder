using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AssetScout.Crawlers
{
	public class ObjectFieldsCrawler : IAssetCrawler
	{
		public bool CanCrawl(object currentObject) => !(currentObject is GameObject) && !(currentObject is SceneAsset);

		public IEnumerable<TraversalContext> GetChildren(object currentObject, TraversalContext parentContext)
		{
			var type = currentObject.GetType();
			var fields = GetSerializedFields(type);

			foreach (var field in fields)
			{
				object fieldValue;
				try
				{
					fieldValue = field.GetValue(currentObject);
				}
				catch (Exception ex) when (ex is TargetInvocationException || ex is FieldAccessException || 
										   ex is NullReferenceException)
				{
					continue;
				}

				if (fieldValue is IList list)
				{
					for (int i = 0; i < list.Count; i++)
					{
						var item = list[i];
						if (item != null)
						{
							yield return parentContext.CreateChildContext(item,
								$"{parentContext.CurrentPath}.{field.Name}[{i}]", field);
						}
					}
				}
				else
				{
					yield return parentContext.CreateChildContext(fieldValue,
						$"{parentContext.CurrentPath}.{field.Name}", field);
				}
			}
		}
		
		private static readonly Dictionary<Type, FieldInfo[]> _cachedFields = new();
		public static FieldInfo[] GetSerializedFields(Type type)
		{
			if (!_cachedFields.TryGetValue(type, out var fields))
			{
				fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					.Where(f => f.IsPublic || Attribute.IsDefined(f, typeof(SerializeField)))
					.ToArray();
				_cachedFields[type] = fields;
			}

			return fields;
		}
	}
}