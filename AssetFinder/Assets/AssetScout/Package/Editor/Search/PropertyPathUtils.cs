namespace AssetFinder
{
    public static class PropertyPathUtils
    {
        public static string GetGameObjectPath(UnityEngine.GameObject gameObject)
        {
            var path = gameObject.name;
            var parent = gameObject.transform.parent;
            
            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }
            
            return path;
        }
    }
}
