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

		private readonly Dictionary<Type, string> _typeToScriptGuidCache = new();
		private readonly HashSet<System.Reflection.Assembly> _projectAssemblies = new();
		
		private static Dictionary<string, MonoScript> _monoScriptsCache;
		private static Dictionary<string, HashSet<string>> _directoryToScriptsCache;
		private static Dictionary<string, HashSet<Type>> _scriptGuidToTypesCache;
		private static Dictionary<string, HashSet<string>> _assemblyToScriptGuidsCache;
		private static Dictionary<string, HashSet<string>> _typeNameToScriptGuidsCache;
		private static HashSet<string> _typesFoundByRegex = new();

		public void Reset()
		{
		}

		public string DrawGUI(string searchKey, bool active)
		{
			return searchKey;
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
			if (_monoScriptsCache != null)
				return;
			
			_monoScriptsCache = new Dictionary<string, MonoScript>();
			_directoryToScriptsCache = new Dictionary<string, HashSet<string>>();
			_scriptGuidToTypesCache = new Dictionary<string, HashSet<Type>>();
			_assemblyToScriptGuidsCache = new Dictionary<string, HashSet<string>>();
			_typeNameToScriptGuidsCache = new Dictionary<string, HashSet<string>>();
			_typesFoundByRegex = new HashSet<string>();
			
			var scriptGuids = AssetDatabase.FindAssets("t:MonoScript");
			foreach (var guid in scriptGuids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
				
				if (script != null)
				{
					_monoScriptsCache[guid] = script;
					
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
					
					var scriptClass = script.GetClass();
					if (scriptClass != null)
					{
						if (!_scriptGuidToTypesCache.TryGetValue(guid, out var types))
						{
							types = new HashSet<Type>();
							_scriptGuidToTypesCache[guid] = types;
						}
						types.Add(scriptClass);
						
						var typeName = GetCleanTypeName(scriptClass.Name);
						if (!_typeNameToScriptGuidsCache.TryGetValue(typeName, out var typeScripts))
						{
							typeScripts = new HashSet<string>();
							_typeNameToScriptGuidsCache[typeName] = typeScripts;
						}
						typeScripts.Add(guid);
						
						var assemblyName = scriptClass.Assembly.GetName().Name;
						if (!_assemblyToScriptGuidsCache.TryGetValue(assemblyName, out var assemblyScripts))
						{
							assemblyScripts = new HashSet<string>();
							_assemblyToScriptGuidsCache[assemblyName] = assemblyScripts;
						}
						assemblyScripts.Add(guid);
					}
					else
					{
						ScanScriptContent(guid, path);
					}
				}
			}
		}
		
		private void ScanScriptContent(string guid, string path)
		{
			try
			{
				var content = File.ReadAllText(path);
				var fileName = Path.GetFileNameWithoutExtension(path);
				
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
					
					if (!_typeNameToScriptGuidsCache.TryGetValue(className, out var typeScripts))
					{
						typeScripts = new HashSet<string>();
						_typeNameToScriptGuidsCache[className] = typeScripts;
					}
					typeScripts.Add(guid);
					
					if (className == fileName)
					{
						_typesFoundByRegex.Add(className);
					}
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
				
			string guid = string.Empty;
			bool usedRegex = false;
			
			var typeName = GetCleanTypeName(type.Name);
			if (_typeNameToScriptGuidsCache.TryGetValue(typeName, out var scriptGuids))
			{
				foreach (var scriptGuid in scriptGuids)
				{
					var path = AssetDatabase.GUIDToAssetPath(scriptGuid);
					
					if (Path.GetFileNameWithoutExtension(path) == typeName)
					{
						guid = scriptGuid;
						break;
					}
					
					if (_scriptGuidToTypesCache.TryGetValue(scriptGuid, out var types) && types.Contains(type))
					{
						guid = scriptGuid;
						break;
					}
				}
			}
			
			if (string.IsNullOrEmpty(guid) && 
				(typeof(ScriptableObject).IsAssignableFrom(type) || typeof(MonoBehaviour).IsAssignableFrom(type)))
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
				var assemblyName = type.Assembly.GetName().Name;
				if (_assemblyToScriptGuidsCache.TryGetValue(assemblyName, out var assemblyScripts))
				{
					foreach (var scriptGuid in assemblyScripts)
					{
						var path = AssetDatabase.GUIDToAssetPath(scriptGuid);
						
						if (Path.GetFileNameWithoutExtension(path) == typeName)
						{
							guid = scriptGuid;
							break;
						}
						
						if (_scriptGuidToTypesCache.TryGetValue(scriptGuid, out var types) && types.Contains(type))
						{
							guid = scriptGuid;
							break;
						}
					}
				}
			}
			
			if (string.IsNullOrEmpty(guid))
			{
				usedRegex = true;
				var assemblyName = type.Assembly.GetName().Name;
				var compiledAssemblies = CompilationPipeline.GetAssemblies();
				
				foreach (var compiledAssembly in compiledAssemblies)
				{
					if (compiledAssembly.name == assemblyName)
					{
						foreach (var sourceFile in compiledAssembly.sourceFiles)
						{
							var directory = Path.GetDirectoryName(sourceFile);
							if (!string.IsNullOrEmpty(directory) && _directoryToScriptsCache.TryGetValue(directory, out var directoryScripts))
							{
								foreach (var scriptGuid in directoryScripts)
								{
									var path = AssetDatabase.GUIDToAssetPath(scriptGuid);
									
									if (Path.GetFileNameWithoutExtension(path) == typeName)
									{
										guid = scriptGuid;
										break;
									}
									
									if (string.IsNullOrEmpty(guid))
									{
										if (_scriptGuidToTypesCache.TryGetValue(scriptGuid, out var types))
										{
											if (types.Contains(type))
											{
												guid = scriptGuid;
												break;
											}
										}
										else
										{
											try
											{
												var content = File.ReadAllText(path);
												var pattern = $@"(class|struct|enum|interface)\s+{Regex.Escape(typeName)}(\s|:|<|{{)";
												if (Regex.IsMatch(content, pattern))
												{
													if (type.Namespace == null || content.Contains($"namespace {type.Namespace}"))
													{
														guid = scriptGuid;
														
														if (!_scriptGuidToTypesCache.TryGetValue(scriptGuid, out var scriptTypes))
														{
															scriptTypes = new HashSet<Type>();
															_scriptGuidToTypesCache[scriptGuid] = scriptTypes;
														}
														scriptTypes.Add(type);
														
														break;
													}
												}
											}
											catch (Exception ex)
											{
												Debug.LogWarning($"Error scanning file {path}: {ex.Message}");
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
			
			if (usedRegex && !string.IsNullOrEmpty(guid))
			{
				var typeFullName = type.FullName ?? type.Name;
				_typesFoundByRegex.Add(typeFullName);
				Debug.Log($"Type found by regex: {typeFullName}");
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
		
		public static void LogRegexFoundTypes()
		{
			if (_typesFoundByRegex != null && _typesFoundByRegex.Count > 0)
			{
				Debug.Log($"Total types found by regex: {_typesFoundByRegex.Count}");
				foreach (var typeName in _typesFoundByRegex)
				{
					Debug.Log($"Type found by regex: {typeName}");
				}
			}
			else
			{
				Debug.Log("No types were found using regex");
			}
		}
	}
}
