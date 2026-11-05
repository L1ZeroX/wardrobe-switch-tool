#if RISV4_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YagihataItems.RadialInventorySystemV4
{
    [JsonObject]
    public class GUIDPathPair<T> : System.IEquatable<GUIDPathPair<T>> where T : Object
    {
        [JsonProperty] public string ObjectGUID { get; set; }
        [JsonProperty] private string ObjectPath { get; set; }
        private ObjectPathStateType _objectPathState;
        [JsonConverter(typeof(StringEnumConverter))] [JsonProperty] public ObjectPathStateType ObjectPathState { get { return _objectPathState; } private set { _objectPathState = value; } }
        private T objectCache = null;
        public GUIDPathPair(ObjectPathStateType type)
        {
            _objectPathState = type;
        }
        public GUIDPathPair()
        {
            _objectPathState = ObjectPathStateType.Asset;
        }
        public GUIDPathPair(ObjectPathStateType type, T initializeObject, GameObject parentObject = null)
        {
            _objectPathState = type;
            SetObject(initializeObject, parentObject);
        }
        public GUIDPathPair(T initializeObject, GameObject parentObject = null)
        {
            _objectPathState = ObjectPathStateType.Asset;
            SetObject(initializeObject, parentObject);
        }
        public string GetFormattedPath()
        {
            return string.IsNullOrEmpty(ObjectPath) ? "" : UnityWebRequest.UnEscapeURL(ObjectPath);
        }
        public void SetFormattedPath(string path)
        {
            objectCache = null;
            if (ObjectPathState == ObjectPathStateType.Asset)
                ObjectPath = path;
            else if (ObjectPathState == ObjectPathStateType.Scene)
                ObjectPath = string.Join("/", path.Split('/').Select(UnityWebRequest.EscapeURL));
            else if (ObjectPathState == ObjectPathStateType.RelativeFromObject)
                ObjectPath = string.Join("/", path.Split('/').Select(UnityWebRequest.EscapeURL));
        }
        private void SetObjectPath(T targetObject, GameObject parentObject = null)
        {
            if(targetObject != null)
            {
                if (ObjectPathState == ObjectPathStateType.Asset)
                    ObjectPath = AssetDatabase.GetAssetPath(targetObject);
                else if (ObjectPathState == ObjectPathStateType.Scene)
                {
                    if (targetObject is GameObject)
                        ObjectPath = (targetObject as GameObject).GetObjectPath();
                    else if(targetObject is MonoBehaviour)
                        ObjectPath = (targetObject as MonoBehaviour).gameObject.GetObjectPath();
                }
                else if (ObjectPathState == ObjectPathStateType.RelativeFromObject)
                {
                    if (targetObject is GameObject)
                        ObjectPath = (targetObject as GameObject).GetRelativePath(parentObject, true);
                    else if (targetObject is MonoBehaviour)
                        ObjectPath = (targetObject as MonoBehaviour).gameObject.GetRelativePath(parentObject, true);
                }
            }
        }
        public T GetObject(GameObject parentObject = null)
        {
            if (objectCache != null)
            {
                if (ObjectPathState != ObjectPathStateType.RelativeFromObject || parentObject != null)
                    SetObjectPath(objectCache, parentObject);
                return objectCache;
            }
            if (string.IsNullOrEmpty(ObjectPath)) return null;
            if (ObjectPathState == ObjectPathStateType.Asset)
            {
                string path = AssetDatabase.GUIDToAssetPath(ObjectGUID);
                objectCache = AssetDatabase.LoadAssetAtPath<T>(string.IsNullOrEmpty(path) ? ObjectPath : path);
                return objectCache;
            }
            GameObject node = parentObject;
            var segments = ObjectPath.Split('/');
            int begin = 0;
            if (ObjectPathState == ObjectPathStateType.Scene)
            {
                var candidates = new List<GameObject>();
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (scene.isLoaded && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene) && scene.name != "___NDMF Preview___") candidates.AddRange(scene.GetRootGameObjects().Where(o => o.name == UnityWebRequest.UnEscapeURL(segments[0])));
                }
                if (candidates.Count != 1) return null;
                node = candidates[0];
                begin = 1;
            }
            for (int i = begin; i < segments.Length && node != null; i++)
                node = node.transform.Find(UnityWebRequest.UnEscapeURL(segments[i]))?.gameObject;
            if (node is T) objectCache = node as T;
            else if (node != null && typeof(Component).IsAssignableFrom(typeof(T))) objectCache = node.GetComponent(typeof(T)) as T;
            return objectCache;
        }
        public T ForceReload(GameObject parentObject = null)
        {
            objectCache = null;
            return GetObject(parentObject);
        }
        public void SetObject(T targetObject, GameObject parentObject = null)
        {
            if (targetObject == null)
            {
                ObjectPath = "";
                ObjectGUID = "";
            }
            else if(ObjectPathState == ObjectPathStateType.Asset)
            {
                ObjectPath = AssetDatabase.GetAssetPath(targetObject);
                ObjectGUID = AssetDatabase.AssetPathToGUID(ObjectPath);
            }
            else if(ObjectPathState == ObjectPathStateType.Scene)
            {
                GameObject gameObject = null;
                if(targetObject is GameObject)
                    gameObject = targetObject as GameObject;
                else if(targetObject is MonoBehaviour)
                {
                    gameObject = (targetObject as MonoBehaviour).gameObject;
                }
                ObjectPath = gameObject.GetObjectPath();
                ObjectGUID = gameObject.GetInstanceID().ToString();
            }
            else if(ObjectPathState == ObjectPathStateType.RelativeFromObject)
            {
                GameObject gameObject = null;
                if (targetObject is GameObject)
                    gameObject = targetObject as GameObject;
                else if (targetObject is MonoBehaviour)
                {
                    gameObject = (targetObject as MonoBehaviour).gameObject;
                }
                ObjectPath = gameObject.GetRelativePath(parentObject, true);
                ObjectGUID = gameObject.GetInstanceID().ToString();
            }
            else
            {
                ObjectPath = "";
                ObjectGUID = "";
            }
            objectCache = targetObject;
        }

        public override bool Equals(object obj)
        {
            return this == obj;

        }

        public override int GetHashCode()
        {
            int hashCode = -1314013034;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(ObjectGUID);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(ObjectPath);
            hashCode = hashCode * -1521134295 + ObjectPathState.GetHashCode();
            return hashCode;
        }

        public bool Equals(GUIDPathPair<T> other)
        {
            return this == other;
        }

        public static bool operator ==(GUIDPathPair<T> a, object valueB)
        {
            // 同一のインスタンスを参照している場合は true
            if (System.Object.ReferenceEquals(a, valueB))
            {
                return true;
            }
            // どちらか片方でも null なら false
            if (((object)a == null) || ((object)valueB == null))
            {
                return false;
            }
            if (valueB is GUIDPathPair<T>)
            {
                var b = (GUIDPathPair<T>)valueB;
                if (a.ObjectGUID == b.ObjectGUID)
                    return true;
                else if (a.ObjectPath == b.ObjectPath)
                    return true;
            }
            else if(valueB is T)
            {
                var b = (T)valueB;
                if (a.GetObject() == b)
                    return true;
                else
                {
                    if (a.ObjectPathState == ObjectPathStateType.Asset)
                    {
                        var objectPath = AssetDatabase.GetAssetPath(b);
                        var objectGUID = AssetDatabase.AssetPathToGUID(objectPath);
                        if (a.ObjectGUID == objectGUID)
                            return true;
                        else if (a.ObjectPath == objectPath)
                            return true;
                    }
                    else if (a.ObjectPathState == ObjectPathStateType.Scene)
                    {
                        GameObject gameObject = null;
                        if (b is GameObject)
                            gameObject = b as GameObject;
                        else if (b is MonoBehaviour)
                        {
                            gameObject = (b as MonoBehaviour).gameObject;
                        }
                        var objectPath = gameObject.GetObjectPath();
                        var objectGUID = gameObject.GetInstanceID().ToString();
                        if (a.ObjectGUID == objectGUID)
                            return true;
                        else if (a.ObjectPath == objectPath)
                            return true;
                    }
                    else if (a.ObjectPathState == ObjectPathStateType.RelativeFromObject)
                    {
                        GameObject gameObject = null;
                        if (b is GameObject)
                            gameObject = b as GameObject;
                        else if (b is MonoBehaviour)
                        {
                            gameObject = (b as MonoBehaviour).gameObject;
                        }
                        var objectPath = gameObject.GetObjectPath();
                        var objectGUID = gameObject.GetInstanceID().ToString();
                        if (a.ObjectGUID == objectGUID)
                            return true;
                        else if (objectPath.Contains(a.ObjectPath))
                            return true;
                    }
                }
            }
            return false;
        }
        public static bool operator !=(GUIDPathPair<T> a, object b)
        {
            return !(a == b);
        }
    }
    public enum ObjectPathStateType
    {
        Asset,
        Scene,
        RelativeFromObject
    }
}
#endif
