using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using System.Linq;
using AssetScout.Search;
using AssetScout.Utilities;
using Object = UnityEngine.Object;

namespace AssetScout.Cache
{
	public class AssetCache
	{
		private const string CacheFilePath = "Library/AssetScoutCache.json";

		public static AssetCache Instance { get; } = new();

		public event Action CacheSaveEvent;

		private List<IReferenceProcessor> _processors;
		private readonly Dictionary<string, SerializedCacheEntry> _assetCache = new();
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

		public void SetProcessors(List<IReferenceProcessor> processors)
		{
			_processors = processors;
		}

		public async Task RebuildCache(bool force = false, Action<int, int> onProgress = null,
			CancellationToken cancellationToken = default)
		{
			if (_processors == null || _processors.Count <= 0)
			{
				Debug.LogError("Preprocessors not set");
				return;
			}

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
				if (force) _assetCache.Clear();

				//var allAssets = AssetDatabase.FindAssets("", new[] { "Assets" });
				var allAssets = AssetDatabase.FindAssets("t:GameObject t:ScriptableObject t:Material t:SceneAsset t:SpriteAtlas", new[] { "Assets" });
				//var allAssets = AssetDatabase.FindAssets("t:GameObject Test", new[] { "Assets" });
				//var allAssets = AssetDatabase.FindAssets("t:Scene Sample", new[] { "Assets" });

				allAssets = allAssets.Where(guid => AssetUtility.IsBaseAsset(AssetDatabase.GUIDToAssetPath(guid)))
					.ToArray();
				
				var assetsToProcess = new List<string>();

				if (!force)
				{
					foreach (var guid in allAssets)
					{
						var path = AssetDatabase.GUIDToAssetPath(guid);
						if (!AssetUtility.IsBaseAsset(path))
							continue;

						var lastModified = File.GetLastWriteTime(path).Ticks;

						lock (_lockObject)
						{
							if (!_assetCache.ContainsKey(guid) || _assetCache[guid].LastModified != lastModified)
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

					await ProcessAssetsWithProgress(assetsToProcess, OnProgress, token, true);
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

		private async Task ProcessAssetsWithProgress(List<string> assets, Action<int> onProgress,
			CancellationToken token, bool aggressive)
		{
			var searcher = new ObjectReferenceSearcher(_processors);

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
					foreach (var guid in currentBatch) tasks.Add(ProcessAsset(searcher, guid, token));

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

					await ProcessAsset(searcher, guid, token);
					await Task.Delay(1, token);
					onProgress?.Invoke(1);
				}
			}
		}

		public Dictionary<string, List<string>> FindReferences(string key)
		{
			if (!_isInitialized)
			{
				LoadCache();
				_isInitialized = true;
			}

			var result = new Dictionary<string, List<string>>();

			if (!_assetCache.TryGetValue(key, out var entry))
			{
				return result;
			}

			foreach (var reference in entry.References)
			{
				result[reference.TargetGuid] = reference.Paths;
			}

			return result;
		}

		private async Task ProcessAsset(ObjectReferenceSearcher searcher, string assetGuid,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _processingCount);

			try
			{
				var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
				var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
				if (asset == null)
					return;

				var referenceResults = await searcher.FindReferencePaths(asset, cancellationToken);

				if (!_assetCache.TryGetValue(assetGuid, out var currentAssetEntry))
				{
					currentAssetEntry = new SerializedCacheEntry
					{
						Guid = assetGuid,
						LastModified = File.GetLastWriteTime(assetPath).Ticks,
						References = new List<SerializedReference>()
					};
					_assetCache[assetGuid] = currentAssetEntry;
				}

				currentAssetEntry.LastModified = File.GetLastWriteTime(assetPath).Ticks;

				foreach (var kvp in referenceResults)
				{
					var targetGuid = kvp.Key;
					var paths = kvp.Value;

					lock (_lockObject)
					{
						if (!_assetCache.TryGetValue(targetGuid, out var entry))
						{
							entry = new SerializedCacheEntry
							{
								Guid = targetGuid,
								References = new List<SerializedReference>()
							};
							_assetCache[targetGuid] = entry;
						}

						entry.References.Add(new SerializedReference { TargetGuid = assetGuid, Paths = paths });
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
			var searcher = new ObjectReferenceSearcher(_processors);

			foreach (var guid in assetGuids) _assetsToProcess.Enqueue(guid);

			if (_processingCts == null)
			{
				_processingCts = new CancellationTokenSource();
				await ProcessAssetsAsync(searcher, _processingCts.Token);
			}
		}

		private async Task ProcessAssetsAsync(ObjectReferenceSearcher searcher, CancellationToken token)
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

					if (guid != null) await ProcessAsset(searcher, guid, token);

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
				{ Entries = _assetCache.Values.ToList(), LastRebuildTime = _lastRebuildTime.ToBinary() };
			var json = JsonUtility.ToJson(container, true);
			File.WriteAllText(CacheFilePath, json);
			CacheSaveEvent?.Invoke();
		}

		private void LoadCache()
		{
			if (!File.Exists(CacheFilePath)) return;

			try
			{
				var json = File.ReadAllText(CacheFilePath);
				var container = JsonUtility.FromJson<CacheContainer>(json);

				_assetCache.Clear();
				foreach (var entry in container.Entries) _assetCache[entry.Guid] = entry;

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
			public List<string> Paths = new();
		}
	}
}