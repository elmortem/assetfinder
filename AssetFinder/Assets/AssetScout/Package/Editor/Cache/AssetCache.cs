using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Search;
using AssetScout.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetScout.Cache
{
	public class AssetCache
	{
		private const string CacheFilePath = "Library/AssetScoutCache.bin";
		private const string LegacyCacheFilePath = "Library/AssetScoutCache.json";

		public static AssetCache Instance { get; } = new();

		public event Action CacheSaveEvent;

		private List<IReferenceIndexer> _indexers;
		private readonly Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> _assetCache = new();
		private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _forwardIndex = new();
		private readonly Dictionary<string, long> _assetHashMap = new();
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

		public void SetIndexers(List<IReferenceIndexer> indexers)
		{
			_indexers = indexers;
		}

		public void RebuildCache(bool force = false, Action<int, int> onProgress = null,
			CancellationToken cancellationToken = default)
		{
			if (_indexers == null || _indexers.Count <= 0)
			{
				Debug.LogError("Indexers not set");
				return;
			}

			CancelProcessing();

			_rebuildCts?.Cancel();
			_rebuildCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			var token = _rebuildCts.Token;
			var startTime = Time.realtimeSinceStartup;

			try
			{
				ClearFileModifierTimeCache();
				if (force)
				{
					_assetCache.Clear();
					_forwardIndex.Clear();
					_assetHashMap.Clear();
				}

				var allAssets = AssetDatabase.FindAssets(
					"t:GameObject t:ScriptableObject t:Material t:SceneAsset t:SpriteAtlas", new[] { "Assets" });

				var assetsToProcess = new List<string>();

				if (!force)
				{
					foreach (var guid in allAssets)
					{
						var assetPath = AssetDatabase.GUIDToAssetPath(guid);
						var currentHash = CalculateAssetHash(assetPath);

						if (!_assetHashMap.TryGetValue(guid, out var savedHash) || savedHash != currentHash)
							assetsToProcess.Add(guid);
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

					ProcessAssets(assetsToProcess, OnProgress, token);
					onProgress?.Invoke(totalCount, totalCount);
				}

				_lastRebuildTime = DateTime.Now;
				_lastRebuildDuration = Time.realtimeSinceStartup - startTime;
				SaveCache();
			}
			finally
			{
				if (_rebuildCts != null)
				{
					_rebuildCts.Dispose();
					_rebuildCts = null;
				}

				Debug.Log($"Rebuild cache finished in {_lastRebuildDuration} seconds.");
			}
		}

		private void ProcessAssets(List<string> assets, Action<int> onProgress,
			CancellationToken token)
		{
			var searcher = new ObjectReferenceSearcher(_indexers);

			var batchSize = Mathf.Max(10, Mathf.Min(100, Mathf.FloorToInt(Mathf.Sqrt(assets.Count))));
			Debug.Log($"Assets count: {assets.Count}");
			Debug.Log($"Batch size: {batchSize}");

			for (var i = 0; i < assets.Count; i += batchSize)
			{
				if (token.IsCancellationRequested)
					return;

				var end = Mathf.Min(i + batchSize, assets.Count);
				for (var j = i; j < end; j++)
				{
					ProcessAsset(searcher, assets[j], token);
				}

				onProgress?.Invoke(end - i);
			}
		}

		public Dictionary<string, HashSet<string>> FindReferences(string key, string indexerId = null)
		{
			if (!_isInitialized)
			{
				LoadCache();
				_isInitialized = true;
			}

			var results = new Dictionary<string, HashSet<string>>();

			if (!_assetCache.TryGetValue(key, out var entry))
			{
				return results;
			}

			if (!string.IsNullOrEmpty(indexerId))
			{
				if (entry.TryGetValue(indexerId, out var indexerData))
				{
					foreach (var reference in indexerData)
					{
						results[reference.Key] = new HashSet<string>(reference.Value);
					}
				}
			}
			else
			{
				foreach (var indexerEntry in entry)
				{
					foreach (var reference in indexerEntry.Value)
					{
						if (!results.TryGetValue(reference.Key, out var paths))
						{
							paths = new HashSet<string>(reference.Value);
							results[reference.Key] = paths;
						}
						else
						{
							foreach (var path in reference.Value)
							{
								paths.Add(path);
							}
						}
					}
				}
			}

			return results;
		}

		private void ProcessAsset(ObjectReferenceSearcher searcher, string assetGuid,
			CancellationToken cancellationToken)
		{
			_processingCount++;

			try
			{
				var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
				var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
				if (asset == null)
					return;

				_assetHashMap[assetGuid] = CalculateAssetHash(assetPath);

				RemoveForwardReferences(assetGuid);

				var referenceResults = new Dictionary<string, Dictionary<string, HashSet<string>>>();
				searcher.FindReferencePaths(asset, referenceResults, cancellationToken);

				foreach (var indexerEntry in referenceResults)
				{
					var indexerIdKey = indexerEntry.Key;
					var references = indexerEntry.Value;

					foreach (var referenceEntry in references)
					{
						var targetGuid = referenceEntry.Key;
						var paths = referenceEntry.Value;

						if (!_assetCache.TryGetValue(targetGuid, out var targetEntry))
						{
							targetEntry = new Dictionary<string, Dictionary<string, List<string>>>();
							_assetCache[targetGuid] = targetEntry;
						}

						if (!targetEntry.TryGetValue(indexerIdKey, out var indexerDict))
						{
							indexerDict = new Dictionary<string, List<string>>();
							targetEntry[indexerIdKey] = indexerDict;
						}

						indexerDict[assetGuid] = new List<string>(paths);

						AddForwardReference(assetGuid, indexerIdKey, targetGuid);
					}
				}
			}
			finally
			{
				_processingCount--;
			}
		}

		private void RemoveForwardReferences(string sourceGuid)
		{
			if (!_forwardIndex.TryGetValue(sourceGuid, out var oldIndexers))
				return;

			foreach (var indexerEntry in oldIndexers)
			{
				var indexerId = indexerEntry.Key;
				var targetGuids = indexerEntry.Value;

				foreach (var targetGuid in targetGuids)
				{
					if (_assetCache.TryGetValue(targetGuid, out var targetEntry) &&
						targetEntry.TryGetValue(indexerId, out var indexerDict))
					{
						indexerDict.Remove(sourceGuid);
					}
				}
			}

			_forwardIndex.Remove(sourceGuid);
		}

		private void AddForwardReference(string sourceGuid, string indexerId, string targetGuid)
		{
			if (!_forwardIndex.TryGetValue(sourceGuid, out var indexers))
			{
				indexers = new Dictionary<string, HashSet<string>>();
				_forwardIndex[sourceGuid] = indexers;
			}

			if (!indexers.TryGetValue(indexerId, out var targets))
			{
				targets = new HashSet<string>();
				indexers[indexerId] = targets;
			}

			targets.Add(targetGuid);
		}

		public async void EnqueueAssetsForProcessing(IEnumerable<string> assetGuids)
		{
			foreach (var guid in assetGuids)
				_assetsToProcess.Enqueue(guid);

			if (_processingCts == null)
			{
				ClearFileModifierTimeCache();
				var searcher = new ObjectReferenceSearcher(_indexers);
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

					if (guid != null)
					{
						ProcessAsset(searcher, guid, token);
						await Task.Delay(1, token);
					}
				}
			}
			finally
			{
				if (_processingCts?.Token == token)
				{
					_processingCts?.Dispose();
					_processingCts = null;
				}
			}
		}

		public void CancelProcessing()
		{
			_processingCts?.Cancel();
			_processingCts?.Dispose();
			_processingCts = null;
			_assetsToProcess.Clear();
		}

		private void SaveCache()
		{
			try
			{
				using (var stream = File.Open(CacheFilePath, FileMode.Create))
				using (var writer = new BinaryWriter(stream))
				{
					writer.Write(DateTime.Now.Ticks);

					writer.Write(_assetCache.Count);
					foreach (var kvp in _assetCache)
					{
						writer.Write(kvp.Key);
						writer.Write(kvp.Value.Count);

						foreach (var indexerKvp in kvp.Value)
						{
							writer.Write(indexerKvp.Key);
							writer.Write(indexerKvp.Value.Count);

							foreach (var referenceKvp in indexerKvp.Value)
							{
								writer.Write(referenceKvp.Key);
								writer.Write(referenceKvp.Value.Count);
								foreach (var path in referenceKvp.Value)
								{
									writer.Write(path);
								}
							}
						}
					}

					writer.Write(_assetHashMap.Count);
					foreach (var kvp in _assetHashMap)
					{
						writer.Write(kvp.Key);
						writer.Write(kvp.Value);
					}
				}

				CacheSaveEvent?.Invoke();
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to save Asset Scout cache: {e.Message}");
			}
		}

		private void LoadCache()
		{
			DeleteLegacyCache();

			if (!File.Exists(CacheFilePath))
				return;

			try
			{
				using (var stream = File.OpenRead(CacheFilePath))
				using (var reader = new BinaryReader(stream))
				{
					var lastRebuildTicks = reader.ReadInt64();

					_assetCache.Clear();
					var entryCount = reader.ReadInt32();
					for (var i = 0; i < entryCount; i++)
					{
						var targetGuid = reader.ReadString();
						var indexerCount = reader.ReadInt32();
						var assetEntry = new Dictionary<string, Dictionary<string, List<string>>>(indexerCount);
						_assetCache[targetGuid] = assetEntry;

						for (var p = 0; p < indexerCount; p++)
						{
							var indexerId = reader.ReadString();
							var refCount = reader.ReadInt32();
							var indexerDict = new Dictionary<string, List<string>>(refCount);
							assetEntry[indexerId] = indexerDict;

							for (var r = 0; r < refCount; r++)
							{
								var sourceGuid = reader.ReadString();
								var pathCount = reader.ReadInt32();
								var paths = new List<string>(pathCount);
								for (var s = 0; s < pathCount; s++)
								{
									paths.Add(reader.ReadString());
								}
								indexerDict[sourceGuid] = paths;
							}
						}
					}

					_assetHashMap.Clear();
					var hashCount = reader.ReadInt32();
					for (var i = 0; i < hashCount; i++)
					{
						var guid = reader.ReadString();
						var hash = reader.ReadInt64();
						_assetHashMap[guid] = hash;
					}

					RebuildForwardIndex();

					if (lastRebuildTicks >= DateTime.MinValue.Ticks && lastRebuildTicks <= DateTime.MaxValue.Ticks)
					{
						_lastRebuildTime = new DateTime(lastRebuildTicks);
					}
					else
					{
						_lastRebuildTime = DateTime.Now;
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to load Asset Scout cache: {e.Message}");
				_assetCache.Clear();
				_forwardIndex.Clear();
				_assetHashMap.Clear();
			}
		}

		private static void DeleteLegacyCache()
		{
			if (File.Exists(LegacyCacheFilePath))
			{
				File.Delete(LegacyCacheFilePath);
			}
		}

		private void RebuildForwardIndex()
		{
			_forwardIndex.Clear();

			foreach (var targetEntry in _assetCache)
			{
				var targetGuid = targetEntry.Key;

				foreach (var indexerEntry in targetEntry.Value)
				{
					var indexerId = indexerEntry.Key;

					foreach (var sourceGuid in indexerEntry.Value.Keys)
					{
						AddForwardReference(sourceGuid, indexerId, targetGuid);
					}
				}
			}
		}

		private readonly Dictionary<string, long> _fileModifierTimeCache = new();

		private void ClearFileModifierTimeCache()
		{
			_fileModifierTimeCache.Clear();
		}

		private long GetFileModifierTime(string filePath)
		{
			if (_fileModifierTimeCache.TryGetValue(filePath, out var time))
				return time;

			time = File.GetLastWriteTime(filePath).Ticks;
			_fileModifierTimeCache[filePath] = time;
			return time;
		}

		private long CalculateAssetHash(string assetPath) => GetFileModifierTime(assetPath);
	}
}
