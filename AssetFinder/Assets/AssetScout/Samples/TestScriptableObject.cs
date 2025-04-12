using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AssetScout.Samples
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
		
		public DoubleTypeProvider DoubleTypeProvider;
		
		public AssetReferenceGameObject Addressable_GameObject;
		public AssetReferenceSprite Addressable_Sprite;
	}
}