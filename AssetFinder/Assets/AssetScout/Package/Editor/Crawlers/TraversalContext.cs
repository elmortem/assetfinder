using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AssetScout.Crawlers
{
	public class TraversalContext
	{
		public object CurrentObject { get; }
		public string CurrentPath { get; }
		public int Depth { get; }
		public FieldInfo FieldInfo { get; private set; }
		public PropertyInfo PropertyInfo { get; private set; }
		public TraversalContext Parent { get; set; }

		public IEnumerable<object> ParentObjects
		{
			get
			{
				var ctx = Parent;
				while (ctx != null)
				{
					if (ctx.CurrentObject is Object)
					{
						yield return ctx.CurrentObject;
					}
					ctx = ctx.Parent;
				}
			}
		}

		public TraversalContext(object currentObject, string currentPath, int depth, FieldInfo fieldInfo = null, PropertyInfo propertyInfo = null)
		{
			CurrentObject = currentObject;
			CurrentPath = currentPath;
			Depth = depth;
			FieldInfo = fieldInfo;
			PropertyInfo = propertyInfo;
		}

		public TraversalContext CreateChildContext(object childObject, string childPath)
		{
			return new TraversalContext(childObject, childPath, Depth + 1) { Parent = this };
		}

		public TraversalContext CreateChildContext(object childObject, string childPath, FieldInfo fieldInfo)
		{
			return new TraversalContext(childObject, childPath, Depth + 1, fieldInfo) { Parent = this };
		}

		public TraversalContext CreateChildContext(object childObject, string childPath, PropertyInfo propertyInfo)
		{
			return new TraversalContext(childObject, childPath, Depth + 1, null, propertyInfo) { Parent = this };
		}
	}
}