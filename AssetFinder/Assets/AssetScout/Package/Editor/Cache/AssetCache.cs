using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using System.Linq;
using Object = UnityEngine.Object;

namespace AssetFinder.Cache
{
    public class AssetCache
    {
        private const string CacheFilePath = "Library/AssetFinderCache.json";
        
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
        public bool IsRebuilding => _rebuildCts != null;
        public bool IsProcessing => _processingCount > 0;

        private AssetCache()
        {
            LoadCache();
            _isInitialized = true;
        }

        public async Task RebuildCache(bool force = false, Action<int, int> onProgress = null, CancellationToken cancellationToken = default)
        {
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

                var allAssets = AssetDatabase.FindAssets("t:GameObject t:ScriptableObject t:Material t:SceneAsset",
                    new[] { "Assets" });
                var assetsToProcess = new List<string>();

                if (!force)
                {
                    foreach (var guid in allAssets)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var lastModified = File.GetLastWriteTime(path).Ticks;

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

                    void OnProgress(int count)
                    {
                        processedCount += count;
                        onProgress?.Invoke(processedCount, totalCount);
                    }

                    await ProcessAssetsWithProgress(assetsToProcess, OnProgress, token, aggressive: true);
                    onProgress?.Invoke(totalCount, totalCount);
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
                
                Debug.Log($"Rebuild cache finished in {_lastRebuildDuration} seconds.");
            }
        }

        private async Task ProcessAssetsWithProgress(List<string> assets, Action<int> onProgress, CancellationToken token, bool aggressive = false)
        {
            if (aggressive)
            {
                var tasks = new List<Task>();
                var batchSize = Mathf.Max(10, Mathf.Min(100, Mathf.FloorToInt(500 / Mathf.Sqrt(assets.Count))));

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

                    await Task.WhenAll(tasks);
                    onProgress?.Invoke(currentBatch.Length);
                }
            }
            else
            {
                foreach (var guid in assets)
                {
                    if (token.IsCancellationRequested)
                        return;
                    
                    await ProcessAsset(guid, token);
                    await Task.Delay(1, token);
                    onProgress?.Invoke(1);
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

        private async Task ProcessAsset(string assetGuid, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _processingCount);
            try 
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset == null) 
                    return;

                var entry = new AssetCacheEntry
                {
                    References = new Dictionary<string, List<ReferenceInfo>>(),
                    LastModifiedTime = File.GetLastWriteTime(assetPath).Ticks
                };

                var dependencyGuids = AssetDatabase.GetDependencies(assetPath, false)
                    .Where(CorrectPath).Select(AssetDatabase.AssetPathToGUID).ToArray();

                if (dependencyGuids.Length > 0)
                {
                    var referenceResults = await ObjectReferenceSearcher.FindReferencePaths(
                        asset, dependencyGuids, cancellationToken);

                    foreach (var dependencyGuid in referenceResults.Keys)
                    {
                        var referenceList = new List<ReferenceInfo>();
                        foreach (var path in referenceResults[dependencyGuid])
                        {
                            referenceList.Add(new ReferenceInfo(path));
                        }

                        entry.References[dependencyGuid] = referenceList;
                    }

                    lock (_lockObject)
                    {
                        _assetCache[assetGuid] = entry;
                    }
                }
            }
            finally 
            {
                Interlocked.Decrement(ref _processingCount);
            }
        }

        public async void EnqueueAssetsForProcessing(IEnumerable<string> assetGuids)
        {
            foreach (var guid in assetGuids)
            {
                _assetsToProcess.Enqueue(guid);
            }

            if (_processingCts == null)
            {
                _processingCts = new CancellationTokenSource();
                await ProcessAssetsAsync(_processingCts.Token);
                
                _lastRebuildTime = DateTime.Now;
                SaveCache();
            }
        }

        private async Task ProcessAssetsAsync(CancellationToken token)
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

                    await Task.Delay(1, token);
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
        
        public static bool CorrectPath(string path)
        {
            if (!path.StartsWith("Assets"))
                return false;

            var ext = Path.GetExtension(path).ToLower();
            if (ext == ".meta" || ext == ".cs" || ext == ".asmdef" || ext == ".asmref" || ext == ".dll")
                return false;

            return true;
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
