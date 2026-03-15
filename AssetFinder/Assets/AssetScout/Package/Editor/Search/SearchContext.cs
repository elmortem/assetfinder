using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace AssetScout.Search
{
	public class SearchContext
	{
		public Object Target { get; set; }
#if UNITY_EDITOR
		public PrefabStage PrefabStage { get; set; }
#endif
		public Scene ActiveScene { get; set; }
		public Dictionary<string, object> Extras { get; } = new();
	}
}
