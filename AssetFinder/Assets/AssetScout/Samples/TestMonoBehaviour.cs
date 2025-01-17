using UnityEngine;

namespace AssetFinder.Samples
{
    public class TestMonoBehaviour : MonoBehaviour
    {
        public Material PublicMaterial;

        [SerializeField]
        private Material _privateMaterial;
        
        public ScriptableObject PublicScriptableObject;
        
        [SerializeField]
        private ScriptableObject _privateScriptableObject;
    }
}
