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
		public List<object> ParentObjects { get; set; } = new ();

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
			var childContext = new TraversalContext(childObject, childPath, Depth + 1) { Parent = this };
			childContext.ParentObjects.AddRange(ParentObjects);
			if (CurrentObject is Object)
			{
				childContext.ParentObjects.Add(CurrentObject);
			}
			return childContext;
		}
		
		public TraversalContext CreateChildContext(object childObject, string childPath, FieldInfo fieldInfo)
		{
			var childContext = new TraversalContext(childObject, childPath, Depth + 1, fieldInfo) { Parent = this };
			childContext.ParentObjects.AddRange(ParentObjects);
			if (CurrentObject is Object)
			{
				childContext.ParentObjects.Add(CurrentObject);
			}
			return childContext;
		}
		
		public TraversalContext CreateChildContext(object childObject, string childPath, PropertyInfo propertyInfo)
		{
			var childContext = new TraversalContext(childObject, childPath, Depth + 1, null, propertyInfo) { Parent = this };
			childContext.ParentObjects.AddRange(ParentObjects);
			if (CurrentObject is Object)
			{
				childContext.ParentObjects.Add(CurrentObject);
			}
			return childContext;
		}
	}
}