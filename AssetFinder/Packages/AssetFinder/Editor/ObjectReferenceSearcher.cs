using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Object = UnityEngine.Object;

namespace AssetFinder
{
    public class ObjectReferenceSearcher
    {
        private readonly object _lockObject;
        private readonly Dictionary<Object, AssetReference> _foundReferences;
        private readonly Object _targetAsset;
        private readonly string _targetAssetPath;
        private readonly HashSet<(Object obj, Object target, string path)> _processedObjects = new HashSet<(Object, Object, string)>();

        public ObjectReferenceSearcher(Object targetAsset, List<AssetReference> foundReferences, object lockObject)
        {
            _targetAsset = targetAsset;
            _targetAssetPath = AssetDatabase.GetAssetPath(targetAsset);
            _foundReferences = new Dictionary<Object, AssetReference>();
            _lockObject = lockObject;
        }

        public async UniTask SearchGameObjectAsync(GameObject gameObject, string assetPath, CancellationToken token)
        {
            if (gameObject == null) 
                return;

            var isSceneObject = assetPath.EndsWith(".unity");
            var sceneName = isSceneObject ? System.IO.Path.GetFileNameWithoutExtension(assetPath) : string.Empty;
            var gameObjectPath = PropertyPathUtils.GetGameObjectPath(gameObject);
            var fullPath = isSceneObject ? $"{sceneName}/{gameObjectPath}" : gameObjectPath;

            var components = gameObject.GetComponents<Component>();
            foreach (var component in components)
            {
                if (token.IsCancellationRequested) return;
                if (component == null) continue;

                await SearchObjectForReferencesAsync(
                    component,
                    assetPath,
                    $"{fullPath}[{component.GetType().Name}]",
                    token
                );
            }

            foreach (Transform child in gameObject.transform)
            {
                if (token.IsCancellationRequested) return;
                await SearchGameObjectAsync(child.gameObject, assetPath, token);
            }
        }

        public async UniTask SearchScriptableObjectAsync(ScriptableObject scriptableObject, string assetPath, CancellationToken token)
        {
            if (scriptableObject == null)
                return;

            await SearchObjectForReferencesAsync(
                scriptableObject,
                assetPath,
                scriptableObject.GetType().Name,
                token
            );
        }

        public async UniTask SearchMaterialAsync(Material material, string assetPath, CancellationToken token)
        {
            if (material == null)
                return;

            var shader = material.shader;
            if (shader == _targetAsset && AddProcessedObject(material, shader, "Material.shader"))
            {
                AddReference(material, assetPath, "Material.shader");
            }

            var propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (token.IsCancellationRequested) return;

                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    var propertyName = ShaderUtil.GetPropertyName(shader, i);
                    var texture = material.GetTexture(propertyName);
                    
                    if (texture == _targetAsset && AddProcessedObject(material, texture, $"Material.{propertyName}"))
                    {
                        AddReference(material, assetPath, $"Material.{propertyName}");
                    }
                }
            }

            await SearchObjectForReferencesAsync(material, assetPath, "Material", token);
        }

        public async UniTask SearchSceneAsync(SceneAsset sceneAsset, string assetPath, CancellationToken token)
        {
            if (sceneAsset == null)
                return;

            var scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
            try
            {
                var rootObjects = scene.GetRootGameObjects();
                foreach (var rootObject in rootObjects)
                {
                    if (token.IsCancellationRequested) break;
                    await SearchGameObjectAsync(rootObject, assetPath, token);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private async UniTask SearchObjectForReferencesAsync(Object obj, string assetPath, string objectPath, CancellationToken token)
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

                    await SearchFieldAsync(obj, field, assetPath, $"{objectPath}.{field.Name}", token);
                }

                type = type.BaseType;
            }
        }

        private async UniTask SearchFieldAsync(object obj, FieldInfo field, string assetPath, string fieldPath, CancellationToken token)
        {
            var value = field.GetValue(obj);
            if (value == null)
                return;

            if (value is Object unityObject)
            {
                var valuePath = AssetDatabase.GetAssetPath(unityObject);

                if ((unityObject == _targetAsset || valuePath == _targetAssetPath) && 
                    AddProcessedObject(obj as Object, unityObject, fieldPath))
                {
                    AddReference(obj as Object, assetPath, fieldPath);
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

                            if ((unityObj == _targetAsset || valuePath == _targetAssetPath) && 
                                AddProcessedObject(obj as Object, unityObj, $"{fieldPath}[{index}]"))
                            {
                                AddReference(obj as Object, assetPath, $"{fieldPath}[{index}]");
                            }
                        }
                        else if (!item.GetType().IsPrimitive && item.GetType() != typeof(string))
                        {
                            await SearchObjectFieldsAsync(obj as Object, item, assetPath, $"{fieldPath}[{index}]", token);
                        }
                    }
                    index++;
                }
                return;
            }

            if (!value.GetType().IsPrimitive && value.GetType() != typeof(string))
            {
                await SearchObjectFieldsAsync(obj as Object, value, assetPath, fieldPath, token);
            }
        }

        private async UniTask SearchObjectFieldsAsync(Object owner, object obj, string assetPath, string objectPath, CancellationToken token)
        {
            if (obj == null)
                return;

            var type = obj.GetType();
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            foreach (var field in fields)
            {
                if (token.IsCancellationRequested) return;

                if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)))
                    continue;

                try 
                {
                    await SearchFieldAsync(obj, field, assetPath, $"{objectPath}.{field.Name}", token);
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }
        }

        private async UniTask SearchValueAsync(Object owner, object value, string assetPath, string path, CancellationToken token)
        {
            if (value is Object unityObject)
            {
                var valuePath = AssetDatabase.GetAssetPath(unityObject);

                if ((unityObject == _targetAsset || valuePath == _targetAssetPath) && 
                    AddProcessedObject(owner, unityObject, path))
                {
                    AddReference(owner, assetPath, path);
                }
            }
            else if (!value.GetType().IsPrimitive && value.GetType() != typeof(string))
            {
                await SearchObjectFieldsAsync(owner, value, assetPath, path, token);
            }
        }

        private bool AddProcessedObject(Object obj, Object target, string path)
        {
            lock (_lockObject)
            {
                return _processedObjects.Add((obj, target, path));
            }
        }

        private void AddReference(Object obj, string assetPath, string referencePath)
        {
            lock (_lockObject)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                
                if (!_foundReferences.TryGetValue(asset, out var reference))
                {
                    reference = new AssetReference(asset);
                    _foundReferences[asset] = reference;
                }

                reference.AddPath(referencePath);
            }
        }

        public List<AssetReference> GetResults()
        {
            return _foundReferences.Values.ToList();
        }
    }
}
