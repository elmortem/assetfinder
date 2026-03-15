using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Search
{
	public class LocalSearchProvider : ISearchProvider
	{
		public string Id => typeof(LocalSearchProvider).FullName;
		public string DisplayName => "Local (Scene/Prefab)";
		public int Priority => 10;

		public bool CanSearch(SearchContext context)
		{
			if (context.Target == null)
				return false;

			var targetGO = ResolveGameObject(context.Target);
			if (targetGO == null)
				return false;

			// If it's an asset on disk (not in a scene), this provider doesn't handle it
			var assetPath = AssetDatabase.GetAssetPath(context.Target);
			if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.Contains(context.Target))
			{
				// Check if it's from the prefab stage hierarchy
				if (context.PrefabStage != null && targetGO.scene == context.PrefabStage.scene)
					return true;

				return false;
			}

			// Object in prefab stage
			if (context.PrefabStage != null && targetGO.scene == context.PrefabStage.scene)
				return true;

			// Object in a loaded scene
			var scene = targetGO.scene;
			return scene.IsValid() && scene.isLoaded;
		}

		public void Search(SearchContext context, Action<SearchResultSet> onComplete)
		{
			var results = new List<SearchResult>();
			var targetGO = ResolveGameObject(context.Target);

			if (targetGO == null)
			{
				onComplete(new SearchResultSet { ProviderId = Id, Results = results });
				return;
			}

			var roots = GetSearchRoots(context, targetGO);
			var targetSet = BuildTargetSet(context.Target);

			foreach (var root in roots)
			{
				SearchRecursive(root.transform, targetSet, results);
			}

			onComplete(new SearchResultSet { ProviderId = Id, Results = results });
		}

		public void DrawExtraGUI(SearchContext context)
		{
		}

		private GameObject[] GetSearchRoots(SearchContext context, GameObject targetGameObject)
		{
			if (context.PrefabStage != null
				&& targetGameObject.scene == context.PrefabStage.scene)
			{
				return new[] { context.PrefabStage.prefabContentsRoot };
			}

			var scene = targetGameObject.scene;
			if (scene.IsValid() && scene.isLoaded)
				return scene.GetRootGameObjects();

			return Array.Empty<GameObject>();
		}

		private HashSet<Object> BuildTargetSet(Object target)
		{
			var targets = new HashSet<Object>();

			if (target is Component component)
			{
				targets.Add(component);
			}
			else if (target is GameObject go)
			{
				targets.Add(go);
				foreach (var comp in go.GetComponents<Component>())
				{
					if (comp != null)
						targets.Add(comp);
				}
			}

			return targets;
		}

		private void SearchRecursive(Transform current, HashSet<Object> targetSet,
			List<SearchResult> results)
		{
			var components = current.GetComponents<Component>();
			foreach (var component in components)
			{
				if (component == null) continue;

				using (var so = new SerializedObject(component))
				{
					var prop = so.GetIterator();
					while (prop.NextVisible(true))
					{
						if (prop.propertyType != SerializedPropertyType.ObjectReference)
							continue;

						if (prop.objectReferenceValue != null
							&& targetSet.Contains(prop.objectReferenceValue))
						{
							results.Add(new SearchResult
							{
								HierarchyPath = GetHierarchyPath(current),
								ComponentType = component.GetType().Name,
								PropertyPath = prop.propertyPath,
								SourceObject = component
							});
						}
					}
				}
			}

			for (int i = 0; i < current.childCount; i++)
			{
				SearchRecursive(current.GetChild(i), targetSet, results);
			}
		}

		private string GetHierarchyPath(Transform transform)
		{
			var parts = new List<string>();
			var current = transform;
			while (current != null)
			{
				parts.Add(current.name);
				current = current.parent;
			}
			parts.Reverse();
			return string.Join("/", parts);
		}

		private GameObject ResolveGameObject(Object target)
		{
			if (target is GameObject go) return go;
			if (target is Component comp) return comp.gameObject;
			return null;
		}
	}
}
