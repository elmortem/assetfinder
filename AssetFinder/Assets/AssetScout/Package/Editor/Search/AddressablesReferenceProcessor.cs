#if PACKAGE_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Reflection;
using AssetScout.Crawlers;
using AssetScout.Search;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AssetScout.Addressables
{
	internal class AddressablesReferenceProcessor : IReferenceProcessor
	{
		public string Id => typeof(AddressablesReferenceProcessor).FullName;

		private string _searchKey;
		private static Dictionary<Type, bool> _assetReferenceTypeCache = new();

		public AddressablesReferenceProcessor()
		{
			CacheKnownAssetReferenceTypes();
		}
		
		public void Reset()
		{
		}

		private void CacheKnownAssetReferenceTypes()
		{
			_assetReferenceTypeCache.Clear();
			_assetReferenceTypeCache[typeof(AssetReference)] = true;
			_assetReferenceTypeCache[typeof(AssetReferenceGameObject)] = true;
			_assetReferenceTypeCache[typeof(AssetReferenceTexture)] = true;
			_assetReferenceTypeCache[typeof(AssetReferenceTexture2D)] = true;
			_assetReferenceTypeCache[typeof(AssetReferenceTexture3D)] = true;
			_assetReferenceTypeCache[typeof(AssetReferenceSprite)] = true;
			_assetReferenceTypeCache[typeof(AssetReferenceAtlasedSprite)] = true;
		}

		private bool IsAssetReferenceType(Type type)
		{
			if (type == null)
				return false;

			if (_assetReferenceTypeCache.TryGetValue(type, out bool result))
				return result;

			if (typeof(AssetReference).IsAssignableFrom(type))
			{
				_assetReferenceTypeCache[type] = true;
				return true;
			}

			if (type.BaseType != null)
			{
				bool isBaseAssetRef = IsAssetReferenceType(type.BaseType);
				_assetReferenceTypeCache[type] = isBaseAssetRef;
				return isBaseAssetRef;
			}
			

			_assetReferenceTypeCache[type] = false;
			return false;
		}

		public string DrawGUI(string searchKey, bool active)
		{
			_searchKey = searchKey;
			return _searchKey;
		}

		public void ProcessElement(object element, TraversalContext context, string assetGuid,
			Dictionary<string, HashSet<string>> results)
		{
			if (element == null)
				return;

			var elementType = element.GetType();

			if (IsAssetReferenceType(elementType))
			{
				ProcessAssetReferenceObject(element, elementType, context, assetGuid, results);
				return;
			}

			if (context.FieldInfo != null)
			{
				var fieldType = context.FieldInfo.FieldType;

				if (IsAssetReferenceType(fieldType))
				{
					var fieldValue = context.FieldInfo.GetValue(element);
					if (fieldValue != null)
					{
						ProcessAssetReferenceObject(fieldValue, fieldValue.GetType(), context, assetGuid, results);
					}
				}
			}
		}

		private void ProcessAssetReferenceObject(object assetRefObject, Type assetRefType,
			TraversalContext context,
			string assetGuid,
			Dictionary<string, HashSet<string>> results)
		{
			try
			{
				string guid = null;
				
				FieldInfo guidField = assetRefType.GetField("m_AssetGUID",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance);

				if (guidField != null)
				{
					guid = guidField.GetValue(assetRefObject) as string;
				}

				if (string.IsNullOrEmpty(guid))
				{
					guidField = assetRefType.GetField("m_assetGUID",
						BindingFlags.Public | BindingFlags.NonPublic |
						BindingFlags.Instance);

					if (guidField != null)
					{
						guid = guidField.GetValue(assetRefObject) as string;
					}
				}

				if (!string.IsNullOrEmpty(guid))
				{
					AddReference(results, assetGuid, guid, context.CurrentPath);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"Error processing AssetReference: {ex.Message}");
			}
		}

		public bool ShouldCrawlDeeper(object currentObject, TraversalContext context)
		{
			if (currentObject != null && IsAssetReferenceType(currentObject.GetType()))
			{
				return false;
			}

			return true;
		}

		private void AddReference(
			Dictionary<string, HashSet<string>> results,
			string assetGuid, string referencedGuid, string path)
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
#endif