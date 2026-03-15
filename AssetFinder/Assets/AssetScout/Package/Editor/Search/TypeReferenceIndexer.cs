using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AssetScout.Crawlers;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AssetScout.Search
{
	internal class TypeReferenceIndexer : IReferenceIndexer
	{
		public string Id => typeof(TypeReferenceIndexer).FullName;

		private readonly Dictionary<Type, string> _typeToScriptGuidCache = new();
		private readonly HashSet<System.Reflection.Assembly> _projectAssemblies = new();

		private static Dictionary<string, HashSet<Type>> _scriptGuidToTypesCache;
		private static Dictionary<string, HashSet<string>> _assemblyToScriptGuidsCache;
		private static Dictionary<string, HashSet<string>> _typeNameToScriptGuidsCache;
		private static Dictionary<string, HashSet<string>> _typeNameNamespaceToScriptGuidsCache;
		private static HashSet<string> _compiledAssemblyNames;

		public void Reset()
		{
		}

		public void ProcessElement(object element, TraversalContext context, string assetGuid,
			Dictionary<string, HashSet<string>> results)
		{
			if (element == null || context.PropertyInfo != null)
				return;
			
			var type = element.GetType();
			
			ProcessTypeReferences(type, context, assetGuid, results);
		}

		private void ProcessTypeReferences(Type type, TraversalContext context, string assetGuid, 
			Dictionary<string, HashSet<string>> results)
		{
			if (type == null || !ShouldProcessType(type))
				return;
			
			string scriptGuid = GetScriptGuidForType(type);
			if (!string.IsNullOrEmpty(scriptGuid))
			{
				AddReference(results, assetGuid, scriptGuid, $"{context.CurrentPath} (Type: {type.Name})");
			}

			if (type.IsGenericType)
			{
				foreach (var genericArg in type.GetGenericArguments())
				{
					ProcessTypeReferences(genericArg, context, assetGuid, results);
				}
			}
			
			if (type.IsArray)
			{
				ProcessTypeReferences(type.GetElementType(), context, assetGuid, results);
			}
		}

		private bool ShouldProcessType(Type type)
		{
			if (type == null)
				return false;
				
			if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
				return false;
				
			if (type.Namespace != null && (
				type.Namespace.StartsWith("System") || 
				type.Namespace.StartsWith("UnityEngine") || 
				type.Namespace.StartsWith("UnityEditor")))
				return false;
			
			bool isSerializable = type.IsSerializable || typeof(UnityEngine.Object).IsAssignableFrom(type);
			if (!isSerializable)
				return false;
				
			if (_projectAssemblies.Count == 0)
			{
				InitializeProjectAssemblies();
			}
			
			return _projectAssemblies.Contains(type.Assembly);
		}
		
		private void InitializeProjectAssemblies()
		{
			InitializeScriptCaches();

			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (var assembly in assemblies)
			{
				var assemblyName = assembly.GetName().Name;
				if (_compiledAssemblyNames.Contains(assemblyName))
				{
					_projectAssemblies.Add(assembly);
				}
			}
		}
		
		private void InitializeScriptCaches()
		{
			if (_scriptGuidToTypesCache != null)
				return;

			_scriptGuidToTypesCache = new Dictionary<string, HashSet<Type>>();
			_assemblyToScriptGuidsCache = new Dictionary<string, HashSet<string>>();
			_typeNameToScriptGuidsCache = new Dictionary<string, HashSet<string>>();
			_typeNameNamespaceToScriptGuidsCache = new Dictionary<string, HashSet<string>>();
			_compiledAssemblyNames = new HashSet<string>();

			var compiledAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
			var editorAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
			foreach (var asm in compiledAssemblies)
			{
				_compiledAssemblyNames.Add(asm.name);
			}
			foreach (var asm in editorAssemblies)
			{
				_compiledAssemblyNames.Add(asm.name);
			}

			var scriptGuids = AssetDatabase.FindAssets("t:MonoScript");
			foreach (var guid in scriptGuids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

				if (script == null)
					continue;

				var scriptClass = script.GetClass();
				if (scriptClass != null)
				{
					AddTypeToGuidMapping(guid, scriptClass);
				}
				else
				{
					ScanScriptContent(guid, path);
				}
			}
		}

		private void AddTypeToGuidMapping(string guid, Type type)
		{
			if (!_scriptGuidToTypesCache.TryGetValue(guid, out var types))
			{
				types = new HashSet<Type>();
				_scriptGuidToTypesCache[guid] = types;
			}
			types.Add(type);

			var typeName = GetCleanTypeName(type.Name);
			AddToSetDictionary(_typeNameToScriptGuidsCache, typeName, guid);

			var namespaceKey = MakeNamespaceKey(typeName, type.Namespace);
			AddToSetDictionary(_typeNameNamespaceToScriptGuidsCache, namespaceKey, guid);

			var assemblyName = type.Assembly.GetName().Name;
			AddToSetDictionary(_assemblyToScriptGuidsCache, assemblyName, guid);
		}
		
		private void ScanScriptContent(string guid, string path)
		{
			try
			{
				var content = File.ReadAllText(path);

				string namespaceValue = null;
				var namespaceMatch = Regex.Match(content, @"namespace\s+([^\s{;]+)");
				if (namespaceMatch.Success)
				{
					namespaceValue = namespaceMatch.Groups[1].Value;
				}

				var classMatches = Regex.Matches(content, @"(class|struct|enum|interface)\s+([^\s:<{]+)");
				foreach (Match match in classMatches)
				{
					var className = match.Groups[2].Value;

					AddToSetDictionary(_typeNameToScriptGuidsCache, className, guid);

					var namespaceKey = MakeNamespaceKey(className, namespaceValue);
					AddToSetDictionary(_typeNameNamespaceToScriptGuidsCache, namespaceKey, guid);
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"Error scanning file {path}: {ex.Message}");
			}
		}

		private string GetScriptGuidForType(Type type)
		{
			if (_typeToScriptGuidCache.TryGetValue(type, out var cachedGuid))
				return cachedGuid;

			InitializeScriptCaches();

			var guid = FindGuidForType(type);
			_typeToScriptGuidCache[type] = guid;
			return guid;
		}

		private string FindGuidForType(Type type)
		{
			var typeName = GetCleanTypeName(type.Name);
			var namespaceKey = MakeNamespaceKey(typeName, type.Namespace);

			if (_typeNameNamespaceToScriptGuidsCache.TryGetValue(namespaceKey, out var exactGuids))
			{
				if (exactGuids.Count == 1)
				{
					foreach (var g in exactGuids)
						return g;
				}

				var guid = DisambiguateByType(exactGuids, type, typeName);
				if (!string.IsNullOrEmpty(guid))
					return guid;
			}

			if (_typeNameToScriptGuidsCache.TryGetValue(typeName, out var nameGuids))
			{
				var guid = DisambiguateByType(nameGuids, type, typeName);
				if (!string.IsNullOrEmpty(guid))
					return guid;
			}

			var assemblyName = type.Assembly.GetName().Name;
			if (_assemblyToScriptGuidsCache.TryGetValue(assemblyName, out var assemblyGuids))
			{
				var guid = DisambiguateByType(assemblyGuids, type, typeName);
				if (!string.IsNullOrEmpty(guid))
					return guid;
			}

			return string.Empty;
		}

		private string DisambiguateByType(HashSet<string> candidateGuids, Type type, string typeName)
		{
			string fileNameMatch = null;
			string typeMatch = null;
			string firstCandidate = null;

			foreach (var scriptGuid in candidateGuids)
			{
				if (firstCandidate == null)
					firstCandidate = scriptGuid;

				if (_scriptGuidToTypesCache.TryGetValue(scriptGuid, out var types) && types.Contains(type))
				{
					typeMatch = scriptGuid;
					break;
				}

				if (fileNameMatch == null)
				{
					var path = AssetDatabase.GUIDToAssetPath(scriptGuid);
					if (Path.GetFileNameWithoutExtension(path) == typeName)
					{
						fileNameMatch = scriptGuid;
					}
				}
			}

			if (typeMatch != null)
				return typeMatch;

			if (fileNameMatch != null)
				return fileNameMatch;

			if (candidateGuids.Count == 1)
				return firstCandidate;

			return string.Empty;
		}
		
		private static string GetCleanTypeName(string typeName)
		{
			if (string.IsNullOrEmpty(typeName))
				return string.Empty;

			if (typeName.Contains("`"))
			{
				typeName = typeName.Substring(0, typeName.IndexOf("`", StringComparison.Ordinal));
			}

			if (typeName.Contains("+"))
			{
				typeName = typeName.Substring(0, typeName.IndexOf("+", StringComparison.Ordinal));
			}

			return typeName;
		}

		private static string MakeNamespaceKey(string typeName, string namespaceName)
		{
			if (string.IsNullOrEmpty(namespaceName))
				return typeName;
			return namespaceName + "." + typeName;
		}

		private static void AddToSetDictionary(Dictionary<string, HashSet<string>> dict, string key, string value)
		{
			if (!dict.TryGetValue(key, out var set))
			{
				set = new HashSet<string>();
				dict[key] = set;
			}
			set.Add(value);
		}

		public bool ShouldCrawlDeeper(object currentObject, TraversalContext context)
		{
			if (currentObject is UnityEngine.Object unityObject && 
				(context.FieldInfo != null || context.PropertyInfo != null) &&
				AssetDatabase.Contains(unityObject))
			{
				return false;
			}

			return true;
		}

		private void AddReference(Dictionary<string, HashSet<string>> results, string assetGuid, string referencedGuid, string path)
		{
			if (assetGuid == referencedGuid)
				return;

			if (!results.ContainsKey(referencedGuid))
			{
				results[referencedGuid] = new HashSet<string>();
			}

			results[referencedGuid].Add(path);
		}
		
		public static void ResetStaticCaches()
		{
			_scriptGuidToTypesCache = null;
			_assemblyToScriptGuidsCache = null;
			_typeNameToScriptGuidsCache = null;
			_typeNameNamespaceToScriptGuidsCache = null;
			_compiledAssemblyNames = null;
		}
	}
}
