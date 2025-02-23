using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Crawlers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Search
{
	internal class DefaultReferenceProcessor : IReferenceProcessor
	{
		private Object _targetAsset;
		private string _searchKey;

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

		public async Task ProcessElement(object element, TraversalContext context, string assetGuid,
			Dictionary<string, List<string>> result, CancellationToken cancellationToken)
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
					AddReference(result, assetGuid, referencedGuid, context.CurrentPath);
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
										AddReference(result, assetGuid, prefabAssetGuid, context.CurrentPath);
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
										AddReference(result, assetGuid, prefabAssetGuid, context.CurrentPath);
									}
								}
							}
    
                			if (prefabType == PrefabAssetType.Variant)
                			{
                				var variantPrefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
                				if (variantPrefab != null)
                				{
                					string variantPath = AssetDatabase.GetAssetPath(variantPrefab);
                					string variantGuid = AssetDatabase.AssetPathToGUID(variantPath);
    
                					if (!string.IsNullOrEmpty(variantGuid))
                					{
                						AddReference(result, assetGuid, variantGuid, context.CurrentPath);
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
			if (currentObject is Object unityObject && context.FieldInfo != null &&
				!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(unityObject)))
				return false;

			return true;
		}

		private void AddReference(Dictionary<string, List<string>> result, string assetGuid, string referencedGuid, string path)
		{
			if (assetGuid == referencedGuid)
				return;

			if (!result.ContainsKey(referencedGuid))
			{
				result[referencedGuid] = new List<string>();
			}

			result[referencedGuid].Add(path);
		}
	}
}