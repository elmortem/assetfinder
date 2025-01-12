using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetFinder
{
    public class AssetReference
    {
        public Object Asset { get; }
        public List<string> Paths { get; }

        public AssetReference(Object asset)
        {
            Asset = asset;
            Paths = new List<string>();
        }

        public void AddPath(string path)
        {
            if (!Paths.Contains(path))
            {
                Paths.Add(path);
            }
        }
    }
}
