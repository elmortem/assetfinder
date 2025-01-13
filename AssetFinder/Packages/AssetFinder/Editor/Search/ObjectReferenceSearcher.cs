using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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

        public static async UniTask<List<string>> FindReferencePaths(Object sourceAsset, Object targetAsset, CancellationToken token)
        {
            if (sourceAsset == null || targetAsset == null)
                return new List<string>();

            _processedObjects.Clear();
            var result = new List<string>();

            if (sourceAsset is GameObject gameObject)
            {
                await SearchGameObjectAsync(gameObject, targetAsset, result, token);
            }
            else if (sourceAsset is ScriptableObject scriptableObject)
            {
                await SearchScriptableObjectAsync(scriptableObject, targetAsset, result, token);
            }
            else if (sourceAsset is Material material)
            {
                await SearchMaterialAsync(material, targetAsset, result, token);
            }
            else if (sourceAsset is SceneAsset sceneAsset)
            {
                await SearchSceneAsync(sceneAsset, targetAsset, result, token);
            }

            return result;
        }

        private static async UniTask SearchGameObjectAsync(GameObject gameObject, Object targetAsset, List<string> paths, CancellationToken token)
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
                if (token.IsCancellationRequested) return;
                if (component == null) continue;

                await SearchObjectForReferencesAsync(
                    component,
                    targetAsset,
                    $"{fullPath}[{component.GetType().Name}]",
                    paths,
                    token
                );
            }

            foreach (Transform child in gameObject.transform)
            {
                if (token.IsCancellationRequested) return;
                await SearchGameObjectAsync(child.gameObject, targetAsset, paths, token);
            }
        }

        private static async UniTask SearchScriptableObjectAsync(ScriptableObject scriptableObject, Object targetAsset, List<string> paths, CancellationToken token)
        {
            if (scriptableObject == null)
                return;

            await SearchObjectForReferencesAsync(
                scriptableObject,
                targetAsset,
                scriptableObject.GetType().Name,
                paths,
                token
            );
        }

        private static async UniTask SearchMaterialAsync(Material material, Object targetAsset, List<string> paths, CancellationToken token)
        {
            if (material == null)
                return;

            var shader = material.shader;
            if (shader == targetAsset && AddProcessedObject(material, shader, "Material.shader"))
            {
                paths.Add("Material.shader");
            }

            var propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (token.IsCancellationRequested) return;

                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    var propertyName = ShaderUtil.GetPropertyName(shader, i);
                    var texture = material.GetTexture(propertyName);

                    if (texture == targetAsset && AddProcessedObject(material, texture, $"Material.{propertyName}"))
                    {
                        paths.Add($"Material.{propertyName}");
                    }
                }
            }

            await SearchObjectForReferencesAsync(material, targetAsset, "Material", paths, token);
        }

        private static async UniTask SearchSceneAsync(SceneAsset sceneAsset, Object targetAsset, List<string> paths, CancellationToken token)
        {
            if (sceneAsset == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(sceneAsset);
            var scene = SceneManager.GetSceneByPath(scenePath);

            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            try
            {
                foreach (var rootGameObject in scene.GetRootGameObjects())
                {
                    if (token.IsCancellationRequested) return;
                    await SearchGameObjectAsync(rootGameObject, targetAsset, paths, token);
                }
            }
            finally
            {
                if (scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static async UniTask SearchObjectForReferencesAsync(Object obj, Object targetAsset, string objectPath, List<string> paths, CancellationToken token)
        {
            if (obj == null)
                return;

            await UniTask.SwitchToMainThread();

            var type = obj.GetType();
            while (type != null)
            {
                if (token.IsCancellationRequested)
                    return;

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (token.IsCancellationRequested)
                        return;

                    var isSerializable = field.IsPublic ||
                                        Attribute.IsDefined(field, typeof(SerializeField));

                    if (!isSerializable)
                        continue;

                    await SearchFieldAsync(obj, field, targetAsset, $"{objectPath}.{field.Name}", paths, token);
                }

                type = type.BaseType;
            }
        }

        private static async UniTask SearchFieldAsync(object obj, FieldInfo field, Object targetAsset, string fieldPath, List<string> paths, CancellationToken token)
        {
            var value = field.GetValue(obj);
            if (value == null)
                return;

            if (value is Object unityObject)
            {
                var valuePath = AssetDatabase.GetAssetPath(unityObject);
                var targetPath = AssetDatabase.GetAssetPath(targetAsset);

                if ((unityObject == targetAsset || valuePath == targetPath) &&
                    AddProcessedObject(obj as Object, unityObject, fieldPath))
                {
                    paths.Add(fieldPath);
                }

                return;
            }

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                int index = 0;
                foreach (var item in enumerable)
                {
                    if (token.IsCancellationRequested) return;

                    if (item != null)
                    {
                        if (item is Object unityObj)
                        {
                            var valuePath = AssetDatabase.GetAssetPath(unityObj);
                            var targetPath = AssetDatabase.GetAssetPath(targetAsset);

                            if ((unityObj == targetAsset || valuePath == targetPath) &&
                                AddProcessedObject(obj as Object, unityObj, $"{fieldPath}[{index}]"))
                            {
                                paths.Add($"{fieldPath}[{index}]");
                            }
                        }
                        else if (!item.GetType().IsPrimitive && item.GetType() != typeof(string))
                        {
                            await SearchObjectFieldsAsync(item, targetAsset, $"{fieldPath}[{index}]", paths, token);
                        }
                    }

                    index++;
                }

                return;
            }

            if (!value.GetType().IsPrimitive && value.GetType() != typeof(string))
            {
                await SearchObjectFieldsAsync(value, targetAsset, fieldPath, paths, token);
            }
        }

        private static async UniTask SearchObjectFieldsAsync(object obj, Object targetAsset, string objectPath, List<string> paths, CancellationToken token)
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
                    await SearchFieldAsync(obj, field, targetAsset, $"{objectPath}.{field.Name}", paths, token);
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