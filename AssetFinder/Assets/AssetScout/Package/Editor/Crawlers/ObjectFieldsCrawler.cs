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
			if (currentObject == null)
				yield break;
			
			var type = currentObject.GetType();
			FieldInfo[] fields;
			try
			{
				fields = GetSerializedFields(type);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error accessing fields on {currentObject}\n{ex}");
                yield break;
            }
			if (fields == null)
                yield break;

			foreach (var field in fields)
			{
				object fieldValue;
				try
				{
					fieldValue = field.GetValue(currentObject);
				}
				catch (Exception ex) //when (ex is TargetInvocationException || ex is FieldAccessException || 
									//	   ex is NullReferenceException)
				{
					Debug.LogError($"Error accessing property {field.Name} on {currentObject}\n{ex}");
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

			PropertyInfo[] properties;
			try
			{
				properties = GetSerializedProperties(type);
			}
			catch (Exception ex)
			{
				Debug.LogError($"Error accessing properties on {currentObject}\n{ex}");
				yield break;
			}
			if (properties == null)
				yield break;
			
			foreach (var property in properties)
			{
				object propertyValue;
				try
				{
					propertyValue = property.GetValue(currentObject);
				}
				catch (Exception ex)
				{
					Debug.LogError($"Error accessing property {property.Name} on {currentObject}\n{ex}");
					continue;
				}

				if (propertyValue is IList list)
				{
					for (int i = 0; i < list.Count; i++)
					{
						var item = list[i];
						if (item != null)
						{
							yield return parentContext.CreateChildContext(item,
								$"{parentContext.CurrentPath}.{property.Name}[{i}]", property);
						}
					}
				}
				else
				{
					yield return parentContext.CreateChildContext(propertyValue,
						$"{parentContext.CurrentPath}.{property.Name}", property);
				}
			}

			if (currentObject is SpriteRenderer spriteRenderer && spriteRenderer != null)
			{
				if (spriteRenderer.sprite != null)
				{
					yield return parentContext.CreateChildContext(spriteRenderer.sprite,
						$"{parentContext.CurrentPath}.Sprite");
				}
			}

			if (currentObject is Material material && material != null)
			{
				foreach (var childContext in CustomProcessMaterial(material, parentContext))
					yield return childContext;
			}
		}

		private static IEnumerable<TraversalContext> CustomProcessMaterial(Material material,
			TraversalContext parentContext)
		{
			if (material == null)
                yield break;
			
			var shader = material.shader;
			if (shader != null)
			{
				yield return parentContext.CreateChildContext(shader,
					$"{parentContext.CurrentPath}.shader");
			}

			var propertyCount = ShaderUtil.GetPropertyCount(material.shader);
			for (int i = 0; i < propertyCount; i++)
			{
				if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
				{
					var propertyName = ShaderUtil.GetPropertyName(material.shader, i);
					var texture = material.GetTexture(propertyName);

					if (texture != null)
					{
						yield return parentContext.CreateChildContext(texture,
							$"{parentContext.CurrentPath}.{propertyName}");
					}
				}
			}
		}

		private static readonly Dictionary<Type, FieldInfo[]> _cachedFields = new();
		private static readonly Dictionary<Type, PropertyInfo[]> _cachedProperties = new();

		public static FieldInfo[] GetSerializedFields(Type type)
		{
			if (!_cachedFields.TryGetValue(type, out var fields))
			{
				fields = type
					.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					.Where(f => f.IsPublic || Attribute.IsDefined(f, typeof(SerializeField)))
					.ToArray();
				_cachedFields[type] = fields;
			}
			return fields;
		}

		public static PropertyInfo[] GetSerializedProperties(Type type)
		{
			if (!_cachedProperties.TryGetValue(type, out var properties))
			{
				properties = type
					.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					//.Where(p => p.CanRead)
					.Where(p => p.CanRead && 
								(p.GetCustomAttribute<SerializeField>() != null ||
								p.GetCustomAttribute<SerializeReference>() != null))
					.ToArray();
				_cachedProperties[type] = properties;
			}
			return properties;
		}
	}
}