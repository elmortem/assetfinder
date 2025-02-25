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
using UnityEngine.Profiling;
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

		public void RebuildCache(bool force = false, Action<int, int> onProgress = null,
			CancellationToken cancellationToken = default)
		{
			if (_processors == null || _processors.Count <= 0)
			{
				Debug.LogError("Preprocessors not set");
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
					_assetCache.Clear();

				Profiler.BeginSample("FindAssets");
				//var allAssets = AssetDatabase.FindAssets("", new[] { "Assets" });
				var allAssets = AssetDatabase.FindAssets(
					"t:GameObject t:ScriptableObject t:Material t:SceneAsset t:SpriteAtlas", new[] { "Assets" });
				//var allAssets = AssetDatabase.FindAssets("t:GameObject Test", new[] { "Assets" });
				//var allAssets = AssetDatabase.FindAssets("t:Scene Sample", new[] { "Assets" });

				//allAssets = allAssets.Where(guid => AssetUtility.IsBaseAsset(AssetDatabase.GUIDToAssetPath(guid)))
				//	.ToArray();
				Profiler.EndSample();

				var assetsToProcess = new List<string>();

				if (!force)
				{
					foreach (var guid in allAssets)
					{
						var assetPath = AssetDatabase.GUIDToAssetPath(guid);
						var lastModified = GetFileModifierTime(assetPath);

						if (!_assetCache.ContainsKey(guid) || _assetCache[guid].LastModified != lastModified)
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
			var searcher = new ObjectReferenceSearcher(_processors);

			var batchSize = Mathf.Max(10, Mathf.Min(100, Mathf.FloorToInt(Mathf.Sqrt(assets.Count))));
			Debug.Log($"Batch size: {batchSize}");

			for (var i = 0; i < assets.Count; i += batchSize)
			{
				if (token.IsCancellationRequested)
					return;

				var currentBatch = assets.Skip(i).Take(batchSize).ToArray();

				Profiler.BeginSample("ProcessAssets.Batch");
				foreach (var guid in currentBatch)
				{
					Profiler.BeginSample("ProcessAssets.ProcessAsset");
					ProcessAsset(searcher, guid, token);
					Profiler.EndSample();
				}

				onProgress?.Invoke(currentBatch.Length);
				Profiler.EndSample();
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

				Profiler.BeginSample("ProcessAsset.FindReferencePaths");
				var referenceResults = searcher.FindReferencePaths(asset, cancellationToken);
				Profiler.EndSample();

				Profiler.BeginSample("ProcessAsset.ApplyDependencies");
				if (!_assetCache.TryGetValue(assetGuid, out var currentAssetEntry))
				{
					currentAssetEntry = new SerializedCacheEntry
					{
						Guid = assetGuid,
						LastModified = GetFileModifierTime(assetPath),
						References = new List<SerializedReference>()
					};
					_assetCache[assetGuid] = currentAssetEntry;
				}

				currentAssetEntry.LastModified = GetFileModifierTime(assetPath);

				foreach (var kvp in referenceResults)
				{
					var targetGuid = kvp.Key;
					var paths = kvp.Value;

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
				
				Profiler.EndSample();
			}
			finally
			{
				_processingCount--;
			}
		}

		public async void EnqueueAssetsForProcessing(IEnumerable<string> assetGuids)
		{
			foreach (var guid in assetGuids)
				_assetsToProcess.Enqueue(guid);

			if (_processingCts == null)
			{
				ClearFileModifierTimeCache();
				var searcher = new ObjectReferenceSearcher(_processors);
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