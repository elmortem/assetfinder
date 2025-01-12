using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Linq;
using System.Threading;
using Object = UnityEngine.Object;

namespace AssetFinder
{
    public class AssetFinderWindow : EditorWindow
    {
        private Object _targetAsset;
        private Vector2 _scrollPosition;
        private readonly List<AssetReference> _foundReferences = new();
        private bool _isSearching;
        private float _searchProgress;
        private string _currentAssetPath;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        private const int MaxConcurrentThreads = 8;

        [MenuItem("Tools/Asset Finder")]
        public static void ShowWindow()
        {
            GetWindow<AssetFinderWindow>("Asset Finder");
        }

        private void OnEnable()
        {
            if (_targetAsset != null && _foundReferences.Count == 0)
            {
                StartNewSearch();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newTargetAsset = EditorGUILayout.ObjectField("Asset:", _targetAsset, typeof(Object), false);
            
            if (_targetAsset != null && !_isSearching && GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                StartNewSearch();
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (EditorGUI.EndChangeCheck())
            {
                if (newTargetAsset != _targetAsset)
                {
                    _targetAsset = newTargetAsset;
                    if (_targetAsset != null)
                    {
                        StartNewSearch();
                    }
                    else
                    {
                        ResetSearch();
                    }
                }
            }

            EditorGUILayout.Space(10);
            
            if (_isSearching)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Searching references to '{_targetAsset.name}'... {(_searchProgress * 100):F1}%");
                if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                {
                    ResetSearch();
                    _targetAsset = null;
                }
                EditorGUILayout.EndHorizontal();
                
                if (!string.IsNullOrEmpty(_currentAssetPath))
                {
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), _searchProgress, _currentAssetPath);
                }
            }
            else
            {
                EditorGUILayout.Space();

                if (_targetAsset == null)
                {
                    EditorGUILayout.HelpBox(
                        "Drag & Drop an asset here to find all references to it in your project.\n\n" +
                        "The tool will search through:\n" +
                        "• Scenes\n" +
                        "• Prefabs\n" +
                        "• Scriptable Objects\n" +
                        "• Materials\n" +
                        "And other Unity assets.", 
                        MessageType.Info);
                    return;
                }

                if (_foundReferences.Count == 0 && !_isSearching)
                {
                    EditorGUILayout.HelpBox(
                        $"No references found to '{_targetAsset.name}'.\n" +
                        "Try checking if the asset is actually used in your project.", 
                        MessageType.Warning);
                    return;
                }

                EditorGUILayout.LabelField($"Found References ({_foundReferences.Count}):", EditorStyles.boldLabel);
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                
                foreach (var reference in _foundReferences)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    
                    EditorGUILayout.ObjectField(reference.Asset, typeof(Object), false);
                    
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < reference.Paths.Count; i++)
                    {
                        EditorGUILayout.LabelField($"{i + 1}. {reference.Paths[i]}");
                    }
                    EditorGUI.indentLevel--;
                    
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(5);
                }
                
                EditorGUILayout.EndScrollView();
            }
        }

        private void StartNewSearch()
        {
            ResetSearch();
            FindReferencesAsync().Forget();
        }

        private void ResetSearch()
        {
            CancelSearch();
            _foundReferences.Clear();
            _searchProgress = 0f;
            _currentAssetPath = string.Empty;
            Repaint();
        }

        private void CancelSearch()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            _isSearching = false;
        }

        private void OnDestroy()
        {
            CancelSearch();
        }

        private async UniTaskVoid FindReferencesAsync()
        {
            if (_targetAsset == null || _isSearching) return;

            _isSearching = true;
            _searchProgress = 0f;
            _foundReferences.Clear();
            
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            
            try
            {
                string targetPath = AssetDatabase.GetAssetPath(_targetAsset);
                if (string.IsNullOrEmpty(targetPath)) return;

                var searcher = new ObjectReferenceSearcher(_targetAsset, _foundReferences, _lockObject);
                
                // Получаем только префабы и ScriptableObject'ы
                var allAssetPaths = AssetDatabase.GetAllAssetPaths()
                    .Where(path => path.StartsWith("Assets/") && IsSearchableAsset(path))
                    .ToArray();

                var totalAssets = allAssetPaths.Length;
                var processedCount = 0;

                using var semaphore = new SemaphoreSlim(MaxConcurrentThreads);
                var tasks = new List<UniTask>();

                foreach (var assetPath in allAssetPaths)
                {
                    if (token.IsCancellationRequested) break;

                    await semaphore.WaitAsync(token);

                    var task = UniTask.RunOnThreadPool(async () =>
                    {
                        try
                        {
                            await ProcessAssetAsync(searcher, assetPath, token);
                        }
                        finally
                        {
                            lock (_lockObject)
                            {
                                processedCount++;
                                _searchProgress = processedCount / (float)totalAssets;
                            }
							
                            semaphore.Release();
                        }
                    }, true, token);

                    tasks.Add(task);

                    if (tasks.Count % 10 == 0)
                    {
                        await UniTask.SwitchToMainThread(token);
                        Repaint();
                        await UniTask.Yield();
                    }
                }

                await UniTask.WhenAll(tasks);

                if (!token.IsCancellationRequested)
                {
                    await UniTask.SwitchToMainThread();
                    _foundReferences.Clear();
                    _foundReferences.AddRange(searcher.GetResults());
                    _currentAssetPath = "Search completed!";
                    await UniTask.Delay(1000, cancellationToken: token);
                }
                else
                {
                    _targetAsset = null;
                }
                
                CancelSearch();
                Repaint();
            }
            finally
            {
                CancelSearch();
                Repaint();
            }
        }

        private bool IsSearchableAsset(string assetPath)
        {
            // Ищем только в префабах и ScriptableObject'ах
            return assetPath.EndsWith(".prefab") || assetPath.EndsWith(".asset") || assetPath.EndsWith(".unity") || assetPath.EndsWith(".mat");
        }

        private async UniTask ProcessAssetAsync(ObjectReferenceSearcher searcher, string assetPath, CancellationToken token)
        {
            await UniTask.SwitchToMainThread();
            
            // Сначала проверяем зависимости
            var dependencies = AssetDatabase.GetDependencies(assetPath, false);
            var targetPath = AssetDatabase.GetAssetPath(_targetAsset);
            
            if (dependencies.Contains(targetPath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset == null) 
                    return;

                _currentAssetPath = assetPath;

                if (asset is ScriptableObject scriptableObject)
                {
                    await searcher.SearchScriptableObjectAsync(scriptableObject, assetPath, token);
                }
                else if (asset is GameObject gameObject)
                {
                    await searcher.SearchGameObjectAsync(gameObject, assetPath, token);
                }
                else if (asset is Material material)
                {
                    await searcher.SearchMaterialAsync(material, assetPath, token);
                }
                else if (asset is SceneAsset sceneAsset)
                {
                    await searcher.SearchSceneAsync(sceneAsset, assetPath, token);
                }
            }
        }
    }
}
