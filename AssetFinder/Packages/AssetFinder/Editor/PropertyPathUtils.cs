using System.Collections.Generic;
using UnityEditor;

namespace AssetFinder
{
    public static class PropertyPathUtils
    {
        public static string GetFullPropertyPath(SerializedProperty property)
        {
            var elements = new List<string>();
            var propertyPath = property.propertyPath;
            var pathParts = propertyPath.Split('.');

            for (int i = 0; i < pathParts.Length; i++)
            {
                var part = pathParts[i];
                
                if (part == "Array" && i + 1 < pathParts.Length && pathParts[i + 1].StartsWith("data["))
                {
                    var arrayIndex = pathParts[i + 1].Substring(5).TrimEnd(']');
                    elements.Add($"{elements[elements.Count - 1]}[{arrayIndex}]");
                    elements.RemoveAt(elements.Count - 1);
                    i++;
                    continue;
                }
                
                if (part.Contains("k__BackingField"))
                {
                    elements.Add(part.Split('>')[0].TrimStart('<'));
                    continue;
                }

                elements.Add(part);
            }

            return string.Join(".", elements);
        }

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
