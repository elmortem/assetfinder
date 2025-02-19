#if PACKAGE_ADDRESSABLES
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetScout.Crawlers;
using AssetScout.Search;
using UnityEngine;
using UnityEditor;
using UnityEngine.AddressableAssets;

namespace AssetScout.Addressables
{
    internal class AddressablesReferenceProcessor : IReferenceProcessor
    {
        public string ProcessorId => typeof(AddressablesReferenceProcessor).FullName;

        public string DrawGUI(string searchKey, bool active)
        {
            return searchKey;
        }

        public async Task ProcessElement(object element, TraversalContext context, string assetGuid, Dictionary<string, List<string>> result, CancellationToken cancellationToken)
        {

            if (elementInfo.Field?.FieldType == typeof(AssetReference))
            {
                var assetReference = elementInfo.Value as AssetReference;
                if (assetReference != null && !string.IsNullOrEmpty(assetReference.AssetGUID))
                {
                    AddReference(result, assetReference.AssetGUID, elementInfo.Path);
                }
            }
        }

        public bool ShouldCrawlDeeper(object currentObject, TraversalContext context)
        {
            if (currentObject is AssetReference) 
                return false;
            
            return true;
        }
        
        private void AddReference(Dictionary<string, List<string>> result, string guid, string path)
        {
            if (!result.ContainsKey(guid))
                result[guid] = new List<string>();

            result[guid].Add(path);
        }
    }
}
#endif