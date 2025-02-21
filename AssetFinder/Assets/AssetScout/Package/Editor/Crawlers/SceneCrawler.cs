using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetScout.Crawlers
{
	public class SceneCrawler : IAssetCrawler
	{
		public bool CanCrawl(object currentObject) => currentObject is SceneAsset;

		public IEnumerable<TraversalContext> GetChildren(object currentObject, TraversalContext parentContext)
		{
			if (currentObject is SceneAsset sceneAsset && sceneAsset != null)
			{
				if (Thread.CurrentThread.ManagedThreadId != 1)
				{
					Debug.LogError("Scene operations must be done on main thread");
					yield break;
				}

				var opened = false;
				var scene = SceneManager.GetSceneByPath(AssetDatabase.GetAssetPath(sceneAsset));
				if (!scene.IsValid())
				{
					scene = EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(sceneAsset),
						OpenSceneMode.Additive);
				}
				else
				{
					opened = true;
				}

				if (scene.isLoaded)
				{
					var rootGameObjects = scene.GetRootGameObjects();
					foreach (var rootGameObject in rootGameObjects)
					{
						yield return parentContext.CreateChildContext(rootGameObject,
							$"{parentContext.CurrentPath}/{rootGameObject.name}");
					}
					if (!opened && !Application.isPlaying && SceneManager.sceneCount > 1)
					{
						EditorSceneManager.CloseScene(scene, true);
					}
				}
			}
		}
	}
}