using System;
using System.Collections.Generic;
using AssetScout.Crawlers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Search
{
	internal class DefaultReferenceProcessor : IReferenceProcessor
	{
		public string Id => typeof(DefaultReferenceProcessor).FullName;

		private Object _targetAsset;
		private string _searchKey;

		public void Reset()
		{
		}

		public string DrawGUI(string searchKey, bool active)
		{
			if (_targetAsset == null && !string.IsNullOrEmpty(searchKey))
			{
				_targetAsset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(searchKey));
				_searchKey = searchKey;
			}

			var newAsset = EditorGUILayout.ObjectField(_targetAsset, typeof(Object), false);
			if (newAsset != _targetAsset)
			{
				_targetAsset = newAsset;
				_searchKey = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_targetAsset));
			}

			return _searchKey;
		}

		public void ProcessElement(object element, TraversalContext context, string assetGuid,
			Dictionary<string, HashSet<string>> results)
		{
			if (element == null || !(element is Object))
				return;

			if (element is Object unityObject)
			{
				if (unityObject == null)
					return;
				
				if (unityObject is Component)
					return;

				var referencedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(unityObject));
				if (!string.IsNullOrEmpty(referencedGuid))
				{
					AddReference(results, assetGuid, referencedGuid, context.CurrentPath);
				}
				
				if (unityObject is GameObject go)
                {
                	try
                	{
                		var prefabType = PrefabUtility.GetPrefabAssetType(go);
                		var isPartOfInstance = PrefabUtility.IsPartOfPrefabInstance(go);
                		var isPartOfAsset = PrefabUtility.IsPartOfPrefabAsset(go);
    
                		if (isPartOfAsset && !isPartOfInstance)
                		{
                			var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                			if (prefabAsset != null)
                			{
								string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
								var realPrefabAsset = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
								if (realPrefabAsset == prefabAsset)
								{
									string prefabAssetGuid =
										AssetDatabase.AssetPathToGUID(prefabPath);
									if (!string.IsNullOrEmpty(prefabAssetGuid))
									{
										AddReference(results, assetGuid, prefabAssetGuid, context.CurrentPath);
									}
								}
							}
                		}
                		else if (isPartOfInstance)
                		{
                			var prefabAsset = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
                			if (prefabAsset != null)
                			{
                				string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
								var realPrefabAsset = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
								if (realPrefabAsset == prefabAsset)
								{
									string prefabAssetGuid = AssetDatabase.AssetPathToGUID(prefabPath);

									if (!string.IsNullOrEmpty(prefabAssetGuid))
									{
										AddReference(results, assetGuid, prefabAssetGuid, context.CurrentPath);
									}
								}
							}
    
                			if (prefabType == PrefabAssetType.Variant)
                			{
                				var variantAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                				if (variantAsset != null)
                				{
                					string variantPath = AssetDatabase.GetAssetPath(variantAsset);
									var realVariantAsset = AssetDatabase.LoadAssetAtPath<Object>(variantPath);
									if (realVariantAsset == variantAsset)
									{
										string variantGuid = AssetDatabase.AssetPathToGUID(variantPath);

										if (!string.IsNullOrEmpty(variantGuid))
										{
											AddReference(results, assetGuid, variantGuid, context.CurrentPath);
										}
									}
								}
                			}
                		}
                	}
                	catch (Exception ex)
                	{
                		Debug.LogError(go + "\n" + ex);
                	}
                }
			}
		}

		public bool ShouldCrawlDeeper(object currentObject, TraversalContext context)
		{
			var unityObject = currentObject as Object;
			if (unityObject == null)
				return true;
			
			if (context.FieldInfo != null || context.PropertyInfo != null)
			{
				return false;
			}

			return true;
		}

		private void AddReference(Dictionary<string, HashSet<string>> results, string assetGuid, string referencedGuid, string path)
		{
			if (assetGuid == referencedGuid)
				return;

			if (!results.ContainsKey(referencedGuid))
			{
				results[referencedGuid] = new HashSet<string>();
			}

			results[referencedGuid].Add(path);
		}
	}
}