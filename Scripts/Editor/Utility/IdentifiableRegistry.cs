#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NCDK.Editor
{
    /// <summary>
    /// Centralized registry for all IIdentifiableObject instances in the project.
    /// Uses chunked background processing to avoid Editor lag.
    /// Provides progress feedback via the Progress API.
    /// </summary>
    [InitializeOnLoad]
    public static class IdentifiableRegistry
    {
        static Dictionary<SerializableGuid, List<UnityEngine.Object>> _sceneRegistry;
        static Dictionary<SerializableGuid, List<UnityEngine.Object>> _assetRegistry;
        
        // Background indexing state
        static Queue<string> _prefabQueue = new Queue<string>();
        static Dictionary<SerializableGuid, List<UnityEngine.Object>> _workingAssetRegistry;
        static int _progressTaskId = -1;
        static int _totalPrefabs;
        
        static bool _sceneDirty = true;
        static bool _assetsDirty = true;
        static double _lastRebuildTime;
        static int _lastLoggedCount = -1;

        const double REBUILD_THROTTLE_SECONDS = 0.5;
        const int PREFABS_PER_FRAME = 20; // Adjust for performance vs speed

        public static bool IsIndexingAssets => _prefabQueue.Count > 0;

        static IdentifiableRegistry()
        {
            _sceneRegistry = new Dictionary<SerializableGuid, List<UnityEngine.Object>>();
            _assetRegistry = new Dictionary<SerializableGuid, List<UnityEngine.Object>>();

            IdentifiableEditorHooks.GetAllObjects = GetAllObjects;
            IdentifiableEditorHooks.GetObjectPath = GetObjectPath;
            
            EditorApplication.hierarchyChanged += MarkDirty;
            EditorSceneManager.sceneOpened += (scene, mode) => MarkDirty();
            EditorSceneManager.sceneClosed += (scene) => MarkDirty();
            EditorApplication.playModeStateChanged += (state) => MarkDirty();
            
            EditorApplication.update += OnEditorUpdate;
        }

        static void OnEditorUpdate()
        {
            // Handle async prefab indexing
            if (_prefabQueue.Count > 0)
            {
                ProcessPrefabBatch();
                return;
            }

            // Handle throttled dirty checks
            if ((_sceneDirty || _assetsDirty) && (EditorApplication.timeSinceStartup - _lastRebuildTime) > REBUILD_THROTTLE_SECONDS)
            {
                Rebuild();
            }
        }

        public static void MarkDirty() => _sceneDirty = true;
        public static void MarkAssetsDirty() => _assetsDirty = true;

        public static void ForceRebuild()
        {
            _sceneDirty = true;
            _assetsDirty = true;
            Rebuild();
        }

        static void Rebuild()
        {
            _lastRebuildTime = EditorApplication.timeSinceStartup;

            if (_sceneDirty)
            {
                _sceneRegistry.Clear();
                IndexSceneObjects();
                _sceneDirty = false;
            }

            if (_assetsDirty && !IsIndexingAssets)
            {
                StartAssetIndexing();
                _assetsDirty = false;
            }
        }

        static void IndexSceneObjects()
        {
            // FindObjectsByType is global, not per scene: calling it inside the loop scans the whole
            // project once per open scene and registers every object N times. Scan once, then bucket
            // by the scene each object actually belongs to.
            var sceneObjects = UnityEngine.Object.FindObjectsByType<IdentifiableMonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (var obj in sceneObjects)
            {
                if (obj == null || obj.ID == SerializableGuid.Empty) continue;

                Scene scene = obj.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue;

                AddToRegistry(_sceneRegistry, obj.ID, obj);
            }
        }

        static void StartAssetIndexing()
        {
            _workingAssetRegistry = new Dictionary<SerializableGuid, List<UnityEngine.Object>>();
            _prefabQueue.Clear();

            // 1. ScriptableObjects are fast, index them immediately in the working registry
            string[] soGuids = AssetDatabase.FindAssets("t:IdentifiableScriptableObject");
            foreach (string assetGuid in soGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = AssetDatabase.LoadAssetAtPath<IdentifiableScriptableObject>(path);
                if (asset != null && asset.ID != SerializableGuid.Empty)
                {
                    AddToRegistry(_workingAssetRegistry, asset.ID, asset);
                }
            }

            // 2. Queue up prefabs for chunked processing
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                _prefabQueue.Enqueue(AssetDatabase.GUIDToAssetPath(guid));
            }

            _totalPrefabs = _prefabQueue.Count;
            
            if (_totalPrefabs > 0)
            {
                _progressTaskId = Progress.Start("Indexing Identifiables", "Scanning prefabs for GUIDs...", Progress.Options.None);
            }
            else
            {
                // No prefabs, finish immediately
                _assetRegistry = _workingAssetRegistry;
                _workingAssetRegistry = null;
            }
        }

        static void ProcessPrefabBatch()
        {
            int processedCount = 0;
            while (processedCount < PREFABS_PER_FRAME && _prefabQueue.Count > 0)
            {
                string path = _prefabQueue.Dequeue();
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    var components = prefab.GetComponentsInChildren<IdentifiableMonoBehaviour>(true);
                    foreach (var comp in components)
                    {
                        if (comp != null && comp.ID != SerializableGuid.Empty)
                        {
                            AddToRegistry(_workingAssetRegistry, comp.ID, comp);
                        }
                    }
                }
                processedCount++;
            }

            // Update Progress
            if (_progressTaskId != -1)
            {
                float progress = 1f - (float)_prefabQueue.Count / _totalPrefabs;
                Progress.Report(_progressTaskId, progress, $"Indexed {_totalPrefabs - _prefabQueue.Count}/{_totalPrefabs} prefabs");
            }

            // Check if finished
            if (_prefabQueue.Count == 0)
            {
                _assetRegistry = _workingAssetRegistry;
                _workingAssetRegistry = null;
                
                if (_progressTaskId != -1)
                {
                    Progress.Remove(_progressTaskId);
                    _progressTaskId = -1;
                }

                // Only report a change: an identical count means nothing new was indexed, and logging
                // it on every import turned the Console into noise.
                int count = GetRegisteredCount();
                if (count != _lastLoggedCount)
                {
                    _lastLoggedCount = count;
                    Debug.Log($"[IdentifiableRegistry] Asset indexing complete. Total Identifiables: {count}");
                }
            }
        }

        static void AddToRegistry(Dictionary<SerializableGuid, List<UnityEngine.Object>> registry, SerializableGuid guid, UnityEngine.Object obj)
        {
            if (!registry.ContainsKey(guid))
            {
                registry[guid] = new List<UnityEngine.Object>();
            }
            
            if (!registry[guid].Contains(obj))
            {
                registry[guid].Add(obj);
            }
        }

        public static bool TryGetObject(SerializableGuid guid, out UnityEngine.Object obj)
        {
            // Never rebuild here: this is called from OnGUI (the reference drawer repaints constantly),
            // and a synchronous scene scan inside a repaint is what made the Inspector stall. The
            // throttled EditorApplication.update pass owns rebuilding; until it runs, serve the cache.
            obj = null;
            if (guid == SerializableGuid.Empty) return false;

            if (_sceneRegistry.TryGetValue(guid, out var sceneList) && sceneList.Count > 0)
            {
                obj = sceneList.FirstOrDefault(o => o != null);
                if (obj != null) return true;
            }

            if (_assetRegistry.TryGetValue(guid, out var assetList) && assetList.Count > 0)
            {
                obj = assetList.FirstOrDefault(o => o != null);
                return obj != null;
            }

            return false;
        }

        public static List<UnityEngine.Object> GetAllObjects(SerializableGuid guid)
        {
            List<UnityEngine.Object> combined = new List<UnityEngine.Object>();
            
            if (_sceneRegistry.TryGetValue(guid, out var sceneList))
            {
                combined.AddRange(sceneList.Where(o => o != null));
            }
            
            if (_assetRegistry.TryGetValue(guid, out var assetList))
            {
                combined.AddRange(assetList.Where(o => o != null));
            }

            return combined.Distinct().ToList();
        }

        public static Dictionary<SerializableGuid, List<UnityEngine.Object>> GetAllRegistered()
        {
            // Same rule as TryGetObject: serve the cache, let the throttled update pass rebuild it.
            // The lists are copied, never aliased: assigning the registry's own list back into the
            // result would make every later AddRange mutate the cache.
            var combined = new Dictionary<SerializableGuid, List<UnityEngine.Object>>();

            foreach (var kvp in _sceneRegistry)
            {
                combined[kvp.Key] = new List<UnityEngine.Object>(kvp.Value);
            }

            foreach (var kvp in _assetRegistry)
            {
                if (combined.TryGetValue(kvp.Key, out var list))
                {
                    list.AddRange(kvp.Value);
                    combined[kvp.Key] = list.Distinct().ToList();
                }
                else
                {
                    combined[kvp.Key] = new List<UnityEngine.Object>(kvp.Value);
                }
            }

            return combined;
        }

        public static bool CheckForDuplicates(bool logErrors = true)
        {
            // If currently indexing assets, we might want to wait or use partial data.
            // For Build validation, we should ensure indexing is finished.
            if (IsIndexingAssets)
            {
                Debug.LogWarning("[IdentifiableRegistry] CheckForDuplicates called while indexing is in progress. Result might be incomplete.");
            }

            var combined = GetAllRegistered();
            bool foundDuplicates = false;

            foreach (var kvp in combined)
            {
                var validObjects = kvp.Value.Where(o => o != null).ToList();
                
                if (validObjects.Count > 1)
                {
                    foundDuplicates = true;

                    if (logErrors)
                    {
                        string objectNames = string.Join(", ", validObjects.Select(o => GetObjectPath(o)));
                        Debug.LogError($"[IdentifiableRegistry] Duplicate GUID detected: {kvp.Key}\nFound in: {objectNames}");
                        
                        foreach (var obj in validObjects)
                        {
                            Debug.LogError($"  - {GetObjectPath(obj)}", obj);
                        }
                    }
                }
            }

            return foundDuplicates;
        }

        public static string GetObjectPath(UnityEngine.Object obj)
        {
            if (obj == null) return "null";

            if (obj is Component comp)
            {
                if (comp.gameObject.scene.IsValid())
                {
                    return $"{comp.gameObject.scene.name}/{GetGameObjectPath(comp.gameObject)}";
                }
                else
                {
                    string assetPath = AssetDatabase.GetAssetPath(comp);
                    return string.IsNullOrEmpty(assetPath) ? comp.name : assetPath;
                }
            }

            if (obj is GameObject go)
            {
                if (go.scene.IsValid())
                {
                    return $"{go.scene.name}/{GetGameObjectPath(go)}";
                }
                else
                {
                    string assetPath = AssetDatabase.GetAssetPath(go);
                    return string.IsNullOrEmpty(assetPath) ? go.name : assetPath;
                }
            }

            string path = AssetDatabase.GetAssetPath(obj);
            return string.IsNullOrEmpty(path) ? obj.name : path;
        }

        static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }

        public static int GetRegisteredCount()
        {
            return _sceneRegistry.Sum(kvp => kvp.Value.Count(o => o != null)) + 
                   _assetRegistry.Sum(kvp => kvp.Value.Count(o => o != null));
        }
    }

    public class IdentifiableAssetPostprocessor : AssetPostprocessor
    {
        // Assets that are rewritten by tooling on a timer and never contain an Identifiable. Reindexing
        // for them is pure waste, and the NavMesh bake rewrites its data on every pass.
        static readonly string[] IgnoredFolders =
        {
            "Assets/_game/Data/NavMesh/",
        };

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string str in importedAssets.Concat(deletedAssets).Concat(movedAssets))
            {
                if (!str.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    !str.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsIgnored(str)) continue;

                IdentifiableRegistry.MarkAssetsDirty();
                return;
            }
        }

        static bool IsIgnored(string path)
        {
            foreach (string folder in IgnoredFolders)
            {
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
#endif
