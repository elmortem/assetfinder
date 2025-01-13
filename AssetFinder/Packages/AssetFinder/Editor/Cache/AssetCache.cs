using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using System.Linq;
using Object = UnityEngine.Object;

namespace AssetFinder.Cache
{
    public class AssetCache
    {
        private const string CacheFilePath = "Library/AssetFinderCache.json";
        private static AssetCache _instance;

        public static AssetCache Instance { get; } = new();
        
        public event Action CacheSaveEvent;

        private readonly Dictionary<string, AssetCacheEntry> _assetCache = new();
        private readonly object _lockObject = new();
        private readonly Queue<string> _assetsToProcess = new();
        private bool _isInitialized;
        private CancellationTokenSource _rebuildCts;
        private CancellationTokenSource _processingCts;
        private DateTime _lastRebuildTime;
        private float _lastRebuildDuration;
        private int _processingCount;

        public DateTime LastRebuildTime => _lastRebuildTime;
        public float LastRebuildDuration => _lastRebuildDuration;
        public bool IsProcessing => _processingCount > 0;

        private AssetCache()
        {
            LoadCache();
            _isInitialized = true;
        }

        public async UniTask RebuildCache(bool force = false, Action<float> onProgress = null, CancellationToken cancellationToken = default)
        {
            // Отменяем текущую обработку
            CancelProcessing();

            lock (_lockObject)
            {
                _rebuildCts?.Cancel();
                _rebuildCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            var token = _rebuildCts.Token;
            var startTime = Time.realtimeSinceStartup;

            try
            {
                if (force)
                {
                    _assetCache.Clear();
                }

                // Ищем только ассеты в папке Assets
                var allAssets = AssetDatabase.FindAssets("t:GameObject t:ScriptableObject t:Material t:SceneAsset", new[] { "Assets" });
                var assetsToProcess = new List<string>();

                // Если force == false, обрабатываем только измененные файлы
                if (!force)
                {
                    foreach (var guid in allAssets)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var lastModified = File.GetLastWriteTime(path).Ticks;

                        // Если ассет не в кеше или был изменен - добавляем его в список на обработку
                        if (!_assetCache.TryGetValue(guid, out var entry) || entry.LastModifiedTime != lastModified)
                        {
                            assetsToProcess.Add(guid);
                        }
                    }
                }
                else
                {
                    assetsToProcess.AddRange(allAssets);
                }

                if (assetsToProcess.Count > 0)
                {
                    var processedCount = 0;
                    var totalCount = assetsToProcess.Count;

                    // Создаем обработчик прогресса для ProcessAssetsAsync
                    var progressHandler = new Progress<string>(_ =>
                    {
                        processedCount++;
                        onProgress?.Invoke(processedCount / (float)totalCount);
                    });

                    // Запускаем обработку с отслеживанием прогресса
                    await ProcessAssetsWithProgress(assetsToProcess, progressHandler, token, aggressive: true);
                }

				_lastRebuildTime = DateTime.Now;
                _lastRebuildDuration = Time.realtimeSinceStartup - startTime;
                SaveCache();
            }
            finally
            {
                lock (_lockObject)
                {
                    if (_rebuildCts != null)
                    {
                        _rebuildCts.Dispose();
                        _rebuildCts = null;
                    }
                }
                onProgress?.Invoke(1f);
            }
        }

        private async UniTask ProcessAssetsWithProgress(List<string> assets, IProgress<string> progress, CancellationToken token, bool aggressive = false)
        {
            if (aggressive)
            {
                var tasks = new List<UniTask>();
                var batchSize = 100;

                for (var i = 0; i < assets.Count; i += batchSize)
                {
                    if (token.IsCancellationRequested)
                        return;

                    var currentBatch = assets.Skip(i).Take(batchSize).ToArray();
                    
                    tasks.Clear();
                    foreach (var guid in currentBatch)
                    {
                        tasks.Add(ProcessAsset(guid, token));
                    }

                    await UniTask.WhenAll(tasks);

                    foreach (var guid in currentBatch)
                    {
                        progress?.Report(guid);
                    }
                    
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }
            else
            {
                foreach (var guid in assets)
                {
                    if (token.IsCancellationRequested)
                        return;

                    await UniTask.Yield(PlayerLoopTiming.Update);
                    await ProcessAsset(guid, token);
                    progress?.Report(guid);
                }
            }
        }

        public Dictionary<string, HashSet<string>> FindReferences(Object targetAsset)
        {
            if (!_isInitialized)
            {
                LoadCache();
                _isInitialized = true;
            }

            var targetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(targetAsset));
            var result = new Dictionary<string, HashSet<string>>();

            foreach (var (assetGuid, entry) in _assetCache)
            {
                if (entry.References.TryGetValue(targetGuid, out var refs))
                {
                    result[assetGuid] = new HashSet<string>(refs.Select(r => r.Path));
                }
            }

            return result;
        }

        public async UniTask ProcessAsset(string assetGuid, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _processingCount);
            var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null) 
                return;
            
            //Debug.Log($"Processing: {assetPath}...");

            var entry = new AssetCacheEntry
            {
                References = new Dictionary<string, List<ReferenceInfo>>(),
                LastModifiedTime = File.GetLastWriteTime(assetPath).Ticks
            };

            var dependencies = AssetDatabase.GetDependencies(assetPath, false);
            
            foreach (var dependencyPath in dependencies)
            {
                if (dependencyPath == assetPath)
                    continue;

                var dependencyAsset = AssetDatabase.LoadAssetAtPath<Object>(dependencyPath);
                if (dependencyAsset == null)
                    continue;

                var paths = await ObjectReferenceSearcher.FindReferencePaths(asset, dependencyAsset, cancellationToken);
                if (paths.Count == 0)
                    continue;

                var dependencyGuid = AssetDatabase.AssetPathToGUID(dependencyPath);
                if (string.IsNullOrEmpty(dependencyGuid))
                    continue;

                var referenceList = new List<ReferenceInfo>();
                foreach (var path in paths)
                {
                    referenceList.Add(new ReferenceInfo(path));
                }

                entry.References[dependencyGuid] = referenceList;
            }

            lock (_lockObject)
            {
                _assetCache[assetGuid] = entry;
            }
            Interlocked.Decrement(ref _processingCount);
        }

        public void EnqueueAssetsForProcessing(IEnumerable<string> assetGuids)
        {
            lock (_lockObject)
            {
                foreach (var guid in assetGuids)
                {
                    _assetsToProcess.Enqueue(guid);
                }

                if (_processingCts == null)
                {
                    _processingCts = new CancellationTokenSource();
                    ProcessAssetsAsync(_processingCts.Token).Forget();
                }
            }
        }

        private async UniTask ProcessAssetsAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string guid;
                    
                    lock (_lockObject)
                    {
                        if (_assetsToProcess.Count > 0)
                        {
                            guid = _assetsToProcess.Dequeue();
                        }
                        else
                        {
                            _processingCts?.Dispose();
                            _processingCts = null;
                            return;
                        }
                    }

                    if (guid != null)
                    {
                        await ProcessAsset(guid, token);
                    }

                    await UniTask.Yield();
                }
            }
            finally
            {
                lock (_lockObject)
                {
                    if (_processingCts?.Token == token)
                    {
                        _processingCts?.Dispose();
                        _processingCts = null;
                    }
                }
                
                _lastRebuildTime = DateTime.Now;
                SaveCache();
            }
        }

        public void CancelProcessing()
        {
            lock (_lockObject)
            {
                _processingCts?.Cancel();
                _processingCts?.Dispose();
                _processingCts = null;
                _assetsToProcess.Clear();
            }
        }

        private void SaveCache()
        {
            var container = new CacheContainer
                { Entries = new List<SerializedCacheEntry>(), LastRebuildTime = _lastRebuildTime.ToBinary() };

            foreach (var (guid, entry) in _assetCache)
            {
                var serializedEntry = new SerializedCacheEntry
                {
                    Guid = guid,
                    LastModified = entry.LastModifiedTime,
                    References = new List<SerializedReference>()
                };

                foreach (var (targetGuid, refs) in entry.References)
                {
                    foreach (var reference in refs)
                    {
                        serializedEntry.References.Add(new SerializedReference
                        {
                            TargetGuid = targetGuid,
                            Path = reference.Path,
                            PathHash = reference.PathHash
                        });
                    }
                }

                container.Entries.Add(serializedEntry);
            }

            var json = JsonUtility.ToJson(container, true);
            File.WriteAllText(CacheFilePath, json);
            
            CacheSaveEvent?.Invoke();
        }

        private void LoadCache()
        {
            if (!File.Exists(CacheFilePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(CacheFilePath);
                var container = JsonUtility.FromJson<CacheContainer>(json);

                _assetCache.Clear();
                foreach (var entry in container.Entries)
                {
                    var cacheEntry = new AssetCacheEntry
                    {
                        LastModifiedTime = entry.LastModified,
                        References = new Dictionary<string, List<ReferenceInfo>>()
                    };

                    foreach (var reference in entry.References)
                    {
                        if (!cacheEntry.References.ContainsKey(reference.TargetGuid))
                        {
                            cacheEntry.References[reference.TargetGuid] = new List<ReferenceInfo>();
                        }

                        cacheEntry.References[reference.TargetGuid].Add(
                            new ReferenceInfo(reference.Path));
                    }

                    _assetCache[entry.Guid] = cacheEntry;
                }
                
                _lastRebuildTime = DateTime.FromBinary(container.LastRebuildTime);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load AssetFinder cache: {e.Message}");
                _assetCache.Clear();
            }
        }

        [Serializable]
        private class CacheContainer
        {
            public List<SerializedCacheEntry> Entries = new();
            public long LastRebuildTime;
        }

        [Serializable]
        private class SerializedCacheEntry
        {
            public string Guid;
            public long LastModified;
            public List<SerializedReference> References = new();
        }

        [Serializable]
        private class SerializedReference
        {
            public string TargetGuid;
            public string Path;
            public int PathHash;
        }

        [Serializable]
        private class AssetCacheEntry
        {
            public long LastModifiedTime;
            public Dictionary<string, List<ReferenceInfo>> References = new();
        }

        [Serializable]
        private class ReferenceInfo
        {
            public string Path;
            public int PathHash;

            public ReferenceInfo(string path)
            {
                Path = path;
                PathHash = path.GetHashCode();
            }
        }
    }
}
