using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
using AssetScout.Crawlers;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AssetScout.Search
{
	internal class TypeReferenceProcessor : IReferenceProcessor
	{
		public string Id => typeof(TypeReferenceProcessor).FullName;

		private MonoScript _targetAsset;
		private string _searchKey;
		private readonly Dictionary<Type, string> _typeToScriptGuidCache = new();
		private readonly HashSet<System.Reflection.Assembly> _projectAssemblies = new();
		
		private static Dictionary<string, string> _allScriptsCache;
		private static Dictionary<string, MonoScript> _monoScriptsCache;
		private static Dictionary<string, HashSet<string>> _namespaceToScriptsCache;
		private static Dictionary<string, HashSet<string>> _classNameToScriptsCache;
		private static Dictionary<string, HashSet<string>> _directoryToScriptsCache;

		public string DrawGUI(string searchKey, bool active)
		{
			if (_targetAsset == null && !string.IsNullOrEmpty(searchKey))
			{
				_targetAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(searchKey));
				_searchKey = searchKey;
			}

			var newAsset = (MonoScript)EditorGUILayout.ObjectField(_targetAsset, typeof(MonoScript), false);
			if (newAsset != _targetAsset)
			{
				_targetAsset = newAsset;
				_searchKey = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_targetAsset));
			}

			return _searchKey;
		}

		public void ProcessElement(object element, TraversalContext context, string assetGuid,
			Dictionary<string, HashSet<string>> results)
		{
			if (element == null)
				return;
			
			var type = element.GetType();
			ProcessTypeReferences(type, context, assetGuid, results);
			
			if (context.FieldInfo != null && type != context.FieldInfo.FieldType)
			{
				ProcessTypeReferences(context.FieldInfo.FieldType, context, assetGuid, results);
			}
			else if (context.PropertyInfo != null && type != context.PropertyInfo.PropertyType)
			{
				ProcessTypeReferences(context.PropertyInfo.PropertyType, context, assetGuid, results);
			}
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
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			var compiledAssemblies = CompilationPipeline.GetAssemblies();
			var compiledAssemblyNames = new HashSet<string>();
			
			foreach (var compiledAssembly in compiledAssemblies)
			{
				compiledAssemblyNames.Add(compiledAssembly.name);
			}
			
			foreach (var assembly in assemblies)
			{
				var assemblyName = assembly.GetName().Name;
				if (compiledAssemblyNames.Contains(assemblyName) || 
					(!assemblyName.StartsWith("Unity") && 
					!assemblyName.StartsWith("System") && 
					!assemblyName.StartsWith("mscorlib") &&
					!assemblyName.StartsWith("netstandard")))
				{
					_projectAssemblies.Add(assembly);
				}
			}
		}
		
		private void InitializeScriptCaches()
		{
			if (_allScriptsCache != null)
				return;
				
			_allScriptsCache = new Dictionary<string, string>();
			_monoScriptsCache = new Dictionary<string, MonoScript>();
			_namespaceToScriptsCache = new Dictionary<string, HashSet<string>>();
			_classNameToScriptsCache = new Dictionary<string, HashSet<string>>();
			_directoryToScriptsCache = new Dictionary<string, HashSet<string>>();
			
			var scriptGuids = AssetDatabase.FindAssets("t:MonoScript");
			foreach (var guid in scriptGuids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
				
				if (script != null)
				{
					_monoScriptsCache[guid] = script;
					
					// Кешируем директории скриптов
					var directory = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(directory))
					{
						if (!_directoryToScriptsCache.TryGetValue(directory, out var scripts))
						{
							scripts = new HashSet<string>();
							_directoryToScriptsCache[directory] = scripts;
						}
						scripts.Add(guid);
					}
				}
			}
		}
		
		private void CacheScriptContent(string guid)
		{
			if (_allScriptsCache.ContainsKey(guid))
				return;
				
			var path = AssetDatabase.GUIDToAssetPath(guid);
			try
			{
				var content = File.ReadAllText(path);
				_allScriptsCache[guid] = content;
				
				var namespaceMatch = Regex.Match(content, @"namespace\s+([^\s{;]+)");
				if (namespaceMatch.Success)
				{
					var ns = namespaceMatch.Groups[1].Value;
					if (!_namespaceToScriptsCache.ContainsKey(ns))
					{
						_namespaceToScriptsCache[ns] = new HashSet<string>();
					}
					_namespaceToScriptsCache[ns].Add(guid);
				}
				
				var classMatches = Regex.Matches(content, @"(class|struct|enum|interface)\s+([^\s:<{]+)");
				foreach (Match match in classMatches)
				{
					var className = match.Groups[2].Value;
					if (!_classNameToScriptsCache.ContainsKey(className))
					{
						_classNameToScriptsCache[className] = new HashSet<string>();
					}
					_classNameToScriptsCache[className].Add(guid);
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"Error reading file {path}: {ex.Message}");
			}
		}

		private string GetScriptGuidForType(Type type)
		{
			if (_typeToScriptGuidCache.TryGetValue(type, out var cachedGuid))
				return cachedGuid;
				
			InitializeScriptCaches();
				
			string guid = string.Empty;
			
			// Метод 1: Прямой поиск для MonoBehaviour и ScriptableObject
			if (typeof(ScriptableObject).IsAssignableFrom(type) || typeof(MonoBehaviour).IsAssignableFrom(type))
			{
				foreach (var kvp in _monoScriptsCache)
				{
					if (kvp.Value.GetClass() == type)
					{
						guid = kvp.Key;
						break;
					}
				}
			}
			
			if (string.IsNullOrEmpty(guid))
			{
				var typeName = GetCleanTypeName(type.Name);
				var scriptGuids = AssetDatabase.FindAssets($"{typeName} t:MonoScript");
				foreach (var scriptGuid in scriptGuids)
				{
					var path = AssetDatabase.GUIDToAssetPath(scriptGuid);
					
					if (Path.GetFileNameWithoutExtension(path) == typeName)
					{
						CacheScriptContent(scriptGuid);
						if (_allScriptsCache.TryGetValue(scriptGuid, out var content))
						{
							var pattern = $@"(class|struct|enum|interface)\s+{Regex.Escape(typeName)}(\s|:|<|{{)";
							if (Regex.IsMatch(content, pattern))
							{
								if (type.Namespace == null || content.Contains($"namespace {type.Namespace}"))
								{
									guid = scriptGuid;
									break;
								}
							}
						}
					}
				}
			}
			
			if (string.IsNullOrEmpty(guid))
			{
				var typeName = GetCleanTypeName(type.Name);
				
				if (_classNameToScriptsCache.TryGetValue(typeName, out var scriptGuids))
				{
					foreach (var scriptGuid in scriptGuids)
					{
						var path = AssetDatabase.GUIDToAssetPath(scriptGuid);
						
						if (Path.GetFileNameWithoutExtension(path) == typeName)
						{
							guid = scriptGuid;
							break;
						}
						
						if (string.IsNullOrEmpty(guid))
						{
							CacheScriptContent(scriptGuid);
							if (_allScriptsCache.TryGetValue(scriptGuid, out var content))
							{
								var pattern = $@"(class|struct|enum|interface)\s+{Regex.Escape(typeName)}(\s|:|<|{{)";
								if (Regex.IsMatch(content, pattern))
								{
									if (type.Namespace != null && 
										(content.Contains($"namespace {type.Namespace}") || 
										content.Contains($"namespace {type.Namespace.Split('.')[0]}")))
									{
										guid = scriptGuid;
										break;
									}
								}
							}
						}
					}
				}
			}
			
			if (string.IsNullOrEmpty(guid) && type.Namespace != null)
			{
				if (_namespaceToScriptsCache.TryGetValue(type.Namespace, out var scriptGuids))
				{
					var typeName = GetCleanTypeName(type.Name);
					foreach (var scriptGuid in scriptGuids)
					{
						CacheScriptContent(scriptGuid);
						if (_allScriptsCache.TryGetValue(scriptGuid, out var content))
						{
							var pattern = $@"(class|struct|enum|interface)\s+{Regex.Escape(typeName)}(\s|:|<|{{)";
							if (Regex.IsMatch(content, pattern))
							{
								guid = scriptGuid;
								break;
							}
						}
					}
				}
				
				if (string.IsNullOrEmpty(guid) && type.Namespace.Contains("."))
				{
					var parentNamespace = type.Namespace.Split('.')[0];
					if (_namespaceToScriptsCache.TryGetValue(parentNamespace, out scriptGuids))
					{
						var typeName = GetCleanTypeName(type.Name);
						foreach (var scriptGuid in scriptGuids)
						{
							CacheScriptContent(scriptGuid);
							if (_allScriptsCache.TryGetValue(scriptGuid, out var content))
							{
								if (content.Contains($"namespace {type.Namespace}"))
								{
									var pattern = $@"(class|struct|enum|interface)\s+{Regex.Escape(typeName)}(\s|:|<|{{)";
									if (Regex.IsMatch(content, pattern))
									{
										guid = scriptGuid;
										break;
									}
								}
							}
						}
					}
				}
			}
			
			if (string.IsNullOrEmpty(guid))
			{
				var assemblyName = type.Assembly.GetName().Name;
				var compiledAssemblies = CompilationPipeline.GetAssemblies();
				
				foreach (var compiledAssembly in compiledAssemblies)
				{
					if (compiledAssembly.name == assemblyName)
					{
						var typeName = GetCleanTypeName(type.Name);
						
						foreach (var sourceFile in compiledAssembly.sourceFiles)
						{
							var directory = Path.GetDirectoryName(sourceFile);
							if (!string.IsNullOrEmpty(directory) && _directoryToScriptsCache.TryGetValue(directory, out var scriptGuids))
							{
								foreach (var scriptGuid in scriptGuids)
								{
									CacheScriptContent(scriptGuid);
									if (_allScriptsCache.TryGetValue(scriptGuid, out var content))
									{
										var pattern = $@"(class|struct|enum|interface)\s+{Regex.Escape(typeName)}(\s|:|<|{{)";
										if (Regex.IsMatch(content, pattern))
										{
											if (type.Namespace == null || content.Contains($"namespace {type.Namespace}"))
											{
												guid = scriptGuid;
												break;
											}
										}
									}
								}
								
								if (!string.IsNullOrEmpty(guid))
									break;
							}
						}
						
						if (!string.IsNullOrEmpty(guid))
							break;
					}
				}
			}
			
			_typeToScriptGuidCache[type] = guid;
			return guid;
		}
		
		private string GetCleanTypeName(string typeName)
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
	}
}
