using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Search;
using UnityEditor;
using UnityEngine;
//using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace AssetScout.Cache
{
	public class AssetCache
	{
		private const string CacheFilePath = "Library/AssetScoutCache.json";

		public static AssetCache Instance { get; } = new();

		public event Action CacheSaveEvent;

		private List<IReferenceProcessor> _processors;
		private readonly Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> _assetCache = new();
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
				{
					_assetCache.Clear();
					_assetHashMap.Clear();
				}

				//Profiler.BeginSample("FindAssets");
				//var allAssets = AssetDatabase.FindAssets("", new[] { "Assets" });
				var allAssets = AssetDatabase.FindAssets(
					"t:GameObject t:ScriptableObject t:Material t:SceneAsset t:SpriteAtlas", new[] { "Assets" });
				//var allAssets = AssetDatabase.FindAssets("t:GameObject Test", new[] { "Assets" });
				//var allAssets = AssetDatabase.FindAssets("t:Scene Sample", new[] { "Assets" });

				//allAssets = allAssets.Where(guid => AssetUtility.IsBaseAsset(AssetDatabase.GUIDToAssetPath(guid)))
				//	.ToArray();
				//Profiler.EndSample();

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
			var searcher = new ObjectReferenceSearcher(_processors);

			var batchSize = Mathf.Max(10, Mathf.Min(100, Mathf.FloorToInt(Mathf.Sqrt(assets.Count))));
			Debug.Log($"Batch size: {batchSize}");

			for (var i = 0; i < assets.Count; i += batchSize)
			{
				if (token.IsCancellationRequested)
					return;

				var currentBatch = assets.Skip(i).Take(batchSize).ToArray();

				//Profiler.BeginSample("ProcessAssets.Batch");
				foreach (var guid in currentBatch)
				{
					//Profiler.BeginSample("ProcessAssets.ProcessAsset");
					ProcessAsset(searcher, guid, token);
					//Profiler.EndSample();
				}
				
				//Resources.UnloadUnusedAssets(); // so slow

				onProgress?.Invoke(currentBatch.Length);
				//Profiler.EndSample();
			}
		}

		public Dictionary<string, HashSet<string>> FindReferences(string key, string processorId = null)
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

			if (!string.IsNullOrEmpty(processorId))
			{
				if (entry.TryGetValue(processorId, out var processorData))
				{
					foreach (var reference in processorData)
					{
						results[reference.Key] = new HashSet<string>(reference.Value);
					}
				}
			}
			else
			{
				foreach (var processor in entry)
				{
					foreach (var reference in processor.Value)
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

				//Profiler.BeginSample("ProcessAsset.FindReferencePaths");
				var referenceResults = new Dictionary<string, Dictionary<string, HashSet<string>>>();
				searcher.FindReferencePaths(asset, referenceResults, cancellationToken);
				//Profiler.EndSample();

				//Profiler.BeginSample("ProcessAsset.ApplyDependencies");
				
				foreach (var processorEntry in referenceResults)
				{
					var processorId = processorEntry.Key;
					var references = processorEntry.Value;
					
					foreach (var referenceEntry in references)
					{
						var targetGuid = referenceEntry.Key;
						var paths = referenceEntry.Value;
						
						if (!_assetCache.TryGetValue(targetGuid, out var targetEntry))
						{
							targetEntry = new Dictionary<string, Dictionary<string, List<string>>>();
							_assetCache[targetGuid] = targetEntry;
						}
						
						if (!targetEntry.TryGetValue(processorId, out var processorDict))
						{
							processorDict = new Dictionary<string, List<string>>();
							targetEntry[processorId] = processorDict;
						}
						
						processorDict[assetGuid] = new List<string>(paths);
					}
				}
				
				//Profiler.EndSample();
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
			try
			{
				var container = new CacheContainer
				{
					LastRebuildTime = DateTime.Now.Ticks,
					Entries = new List<SerializedEntry>(),
					AssetHashes = new List<SerializedAssetHash>()
				};

				foreach (var kvp in _assetCache)
				{
					var entry = new SerializedEntry
					{
						Guid = kvp.Key,
						ProcessorGroups = new List<SerializedReferencesGroup>()
					};

					foreach (var processorKvp in kvp.Value)
					{
						var group = new SerializedReferencesGroup
						{
							ProcessorId = processorKvp.Key,
							References = new List<SerializedReference>()
						};

						foreach (var referenceKvp in processorKvp.Value)
						{
							group.References.Add(new SerializedReference
							{
								TargetGuid = referenceKvp.Key,
								Paths = referenceKvp.Value
							});
						}

						entry.ProcessorGroups.Add(group);
					}

					container.Entries.Add(entry);
				}

				foreach (var kvp in _assetHashMap)
				{
					container.AssetHashes.Add(new SerializedAssetHash
					{
						Guid = kvp.Key,
						Hash = kvp.Value
					});
				}

				var json = JsonUtility.ToJson(container, true);
				File.WriteAllText(CacheFilePath, json);

				CacheSaveEvent?.Invoke();
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to save AssetFinder cache: {e.Message}");
			}
		}

		private void LoadCache()
		{
			if (!File.Exists(CacheFilePath))
				return;

			try
			{
				var json = File.ReadAllText(CacheFilePath);
				var container = JsonUtility.FromJson<CacheContainer>(json);

				_assetCache.Clear();
				foreach (var entry in container.Entries) 
				{
					var assetEntry = new Dictionary<string, Dictionary<string, List<string>>>();
					_assetCache[entry.Guid] = assetEntry;

					if (entry.ProcessorGroups == null || entry.ProcessorGroups.Count == 0)
					{
						var defaultProcessorId = typeof(DefaultReferenceProcessor).FullName;
						
						var oldReferences = entry.GetType().GetField("References")?.GetValue(entry) as List<SerializedReference>;
						if (oldReferences != null && oldReferences.Count > 0)
						{
							var processorDict = new Dictionary<string, List<string>>();
							assetEntry[defaultProcessorId] = processorDict;

							foreach (var reference in oldReferences)
							{
								processorDict[reference.TargetGuid] = reference.Paths;
							}
						}
					}
					else
					{
						foreach (var group in entry.ProcessorGroups)
						{
							var processorDict = new Dictionary<string, List<string>>();
							assetEntry[group.ProcessorId] = processorDict;

							foreach (var reference in group.References)
							{
								processorDict[reference.TargetGuid] = reference.Paths;
							}
						}
					}
				}

				_assetHashMap.Clear();
				if (container.AssetHashes != null)
				{
					foreach (var hash in container.AssetHashes)
					{
						_assetHashMap[hash.Guid] = hash.Hash;
					}
				}

				try
				{
					if (container.LastRebuildTime >= DateTime.MinValue.Ticks && container.LastRebuildTime <= DateTime.MaxValue.Ticks)
					{
						_lastRebuildTime = new DateTime(container.LastRebuildTime);
					}
					else
					{
						_lastRebuildTime = DateTime.Now;
						Debug.LogError($"Invalid LastRebuildTime value in cache: {container.LastRebuildTime}. Using current time instead.");
					}
				}
				catch (Exception timeEx)
				{
					_lastRebuildTime = DateTime.Now;
					Debug.LogError($"Failed to parse LastRebuildTime: {timeEx.Message}. Using current time instead.");
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to load AssetFinder cache: {e.Message}");
				_assetCache.Clear();
				_assetHashMap.Clear();
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

		private long CalculateAssetHash(string assetPath)
		{
			var lastModified = GetFileModifierTime(assetPath);
			var dependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath);
			
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + dependencyHash.GetHashCode();
				hash = hash * 31 + lastModified.GetHashCode();
				return hash;
			}
		}

		[Serializable]
		private class CacheContainer
		{
			public long LastRebuildTime;
			public List<SerializedEntry> Entries = new();
			public List<SerializedAssetHash> AssetHashes = new();
		}

		[Serializable]
		private class SerializedEntry
		{
			public string Guid;
			public List<SerializedReferencesGroup> ProcessorGroups = new();
		}

		[Serializable]
		private class SerializedReferencesGroup
		{
			public string ProcessorId;
			public List<SerializedReference> References = new();
		}
		
		[Serializable]
		private class SerializedReference
		{
			public string TargetGuid;
			public List<string> Paths = new();
		}

		[Serializable]
		private class SerializedAssetHash
		{
			public string Guid;
			public long Hash;
		}
	}
}