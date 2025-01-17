using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AssetFinder
{
    internal static class ObjectReferenceSearcher
    {
        private static readonly HashSet<(Object obj, Object target, string path)> _processedObjects = new();

        public static async Task<Dictionary<string, List<string>>> FindReferencePaths(
            Object sourceAsset, 
            IList<string> targetAssetGuids, 
            CancellationToken token)
        {
            if (sourceAsset == null || targetAssetGuids == null)
                return new Dictionary<string, List<string>>();

            _processedObjects.Clear();
            var result = new Dictionary<string, List<string>>();

            if (sourceAsset is GameObject gameObject)
            {
                await SearchGameObjectAsync(gameObject, targetAssetGuids, result, token);
            }
            else if (sourceAsset is ScriptableObject scriptableObject)
            {
                await SearchScriptableObjectAsync(scriptableObject, targetAssetGuids, result, token);
            }
            else if (sourceAsset is Material material)
            {
                await SearchMaterialAsync(material, targetAssetGuids, result, token);
            }
            else if (sourceAsset is SceneAsset sceneAsset)
            {
                await SearchSceneAsync(sceneAsset, targetAssetGuids, result, token);
            }

            return result;
        }

        private static async Task SearchGameObjectAsync(
            GameObject gameObject, IList<string> targetAssetGuids,
            Dictionary<string, List<string>> result, CancellationToken token)
        {
            if (gameObject == null)
                return;

            var isSceneObject = !string.IsNullOrEmpty(gameObject.scene.path);
            var sceneName = isSceneObject ? System.IO.Path.GetFileNameWithoutExtension(gameObject.scene.path) : string.Empty;
            var gameObjectPath = PropertyPathUtils.GetGameObjectPath(gameObject);
            var fullPath = isSceneObject ? $"{sceneName}/{gameObjectPath}" : gameObjectPath;

            var components = gameObject.GetComponents<Component>();
            foreach (var component in components)
            {
                if (token.IsCancellationRequested)
                    return;

                if (component == null)
                    continue;

                await SearchObjectForReferencesAsync(
                    component, targetAssetGuids, $"{fullPath}[{component.GetType().Name}]",
                    result, token
                );
            }

            foreach (Transform child in gameObject.transform)
            {
                if (token.IsCancellationRequested)
                    return;

                await SearchGameObjectAsync(child.gameObject, targetAssetGuids, result, token);
            }
        }

        private static async Task SearchScriptableObjectAsync(
            ScriptableObject scriptableObject, IList<string> targetAssetGuids,
            Dictionary<string, List<string>> result, CancellationToken token)
        {
            if (scriptableObject == null)
                return;

            await SearchObjectForReferencesAsync(
                scriptableObject,
                targetAssetGuids,
                scriptableObject.GetType().Name,
                result,
                token
            );
        }

        private static async Task SearchMaterialAsync(
            Material material, IList<string> targetAssetGuids,
            Dictionary<string, List<string>> result, CancellationToken token)
        {
            if (material == null)
                return;

            var shader = material.shader;
            if (shader != null)
            {
                var shaderGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shader));
                if (targetAssetGuids.Contains(shaderGuid) && 
                    AddProcessedObject(material, shader, "Material.shader"))
                {
                    if (!result.ContainsKey(shaderGuid))
                        result[shaderGuid] = new List<string>();

                    result[shaderGuid].Add("Material.shader");
                }
            }

            var propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (token.IsCancellationRequested)
                    return;

                if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    var propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                    var propertyPath = $"Material.{propertyName}";
                    var texture = material.GetTexture(propertyName);

                    if (texture != null)
                    {
                        var textureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
                        if (targetAssetGuids.Contains(textureGuid) && 
                            AddProcessedObject(material, texture, propertyPath))
                        {
                            if (!result.ContainsKey(textureGuid))
                                result[textureGuid] = new List<string>();

                            result[textureGuid].Add(propertyPath);
                        }
                    }
                }
            }

            await SearchObjectForReferencesAsync(material, targetAssetGuids, "Material", result, token);
        }

        private static async Task SearchSceneAsync(
            SceneAsset sceneAsset, IList<string> targetAssetGuids,
            Dictionary<string, List<string>> result, CancellationToken token)
        {
            if (sceneAsset == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(sceneAsset);
            var scene = SceneManager.GetSceneByPath(scenePath);
            var needClose = false;

            if (!scene.isLoaded)
            {
                needClose = true;
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            try
            {
                if (!scene.IsValid())
                {
                    Debug.LogError($"Scene '{scenePath}' is invalid.");
                    return;
                }

                foreach (var rootGameObject in scene.GetRootGameObjects())
                {
                    if (token.IsCancellationRequested)
                        return;

                    await SearchGameObjectAsync(rootGameObject, targetAssetGuids, result, token);
                }
            }
            finally
            {
                if (scene.isLoaded && needClose)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static async Task SearchObjectForReferencesAsync(
            object obj, IList<string> targetAssetGuids,
            string objectPath, Dictionary<string, List<string>> result, CancellationToken token)
        {
            if (obj == null)
                return;

            var type = obj.GetType();
            while (type != null && type != typeof(Object) && type != typeof(Component) &&
                   type != typeof(MonoBehaviour) && type != typeof(ScriptableObject) && type != typeof(object))
            {
                if (token.IsCancellationRequested)
                    return;

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (token.IsCancellationRequested)
                        return;

                    var isSerializable = field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField));
                    if (!isSerializable)
                        continue;

                    await SearchFieldAsync(obj, field, targetAssetGuids, $"{objectPath}.{field.Name}", result, token);
                }

                type = type.BaseType;
            }
        }

        private static async Task SearchFieldAsync(
            object obj, FieldInfo field, IList<string> targetAssetGuids,
            string fieldPath, Dictionary<string, List<string>> result, CancellationToken token)
        {
            var value = field.GetValue(obj);
            if (value == null)
                return;

            if (value is Object unityObject)
            {
                var assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(unityObject));
                if (targetAssetGuids.Contains(assetGuid))
                {
                    if (!result.ContainsKey(assetGuid))
                        result[assetGuid] = new List<string>();

                    result[assetGuid].Add(fieldPath);
                }
            }
            else if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                int index = 0;
                foreach (var item in enumerable)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (item is Object unityObj)
                    {
                        var itemPath = $"{fieldPath}[{index}]";
                        var itemGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(unityObj));
                        if (targetAssetGuids.Contains(itemGuid))
                        {
                            if (!result.ContainsKey(itemGuid))
                                result[itemGuid] = new List<string>();

                            result[itemGuid].Add(itemPath);
                        }
                    }
					else
					{
						await SearchObjectForReferencesAsync(item, targetAssetGuids, $"{fieldPath}[{index}]", result, token);
					}

                    index++;
                }
            }
            else if (!value.GetType().IsPrimitive && value.GetType() != typeof(string))
            {
                await SearchObjectFieldsAsync(value, targetAssetGuids, fieldPath, result, token);
            }
        }

        private static async Task SearchObjectFieldsAsync(
            object obj, IList<string> targetAssetGuids,
            string objectPath, Dictionary<string, List<string>> result, CancellationToken token)
        {
            if (obj == null)
                return;

            var type = obj.GetType();
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                if (token.IsCancellationRequested)
                    return;

                if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)))
                    continue;

                try
                {
                    await SearchFieldAsync(obj, field, targetAssetGuids, $"{objectPath}.{field.Name}", result, token);
                }
                catch (ArgumentException)
                {
                }
            }
        }

        private static bool AddProcessedObject(Object obj, Object target, string path)
        {
            return _processedObjects.Add((obj, target, path));
        }
    }
}