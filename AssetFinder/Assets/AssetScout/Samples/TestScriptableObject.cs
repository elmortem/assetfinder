using UnityEngine;

namespace AssetFinder.Samples
{
	[CreateAssetMenu]
	public class TestScriptableObject : ScriptableObject
	{
		public Material PublicMaterial;
		
		[SerializeField]
		private Material _privateMaterial;
		
		public AnimationClip PublicAnimationClip;
		
		[SerializeField]
		private AnimationClip _privateAnimationClip;
		
		public Sprite[] PublicSprites;
		
		[SerializeField]
		private Sprite[] _privateSprites;
	}
}