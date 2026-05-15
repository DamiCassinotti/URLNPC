#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class RemoveMissingScripts
{
    [MenuItem("Tools/Remove Missing Scripts In Selection (recursive)")]
    public static void RemoveInSelection()
    {
        int totalRemoved = 0;
        int objectsChanged = 0;
        foreach (GameObject root in Selection.gameObjects)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (removed > 0)
                {
                    totalRemoved += removed;
                    objectsChanged++;
                    EditorUtility.SetDirty(t.gameObject);
                }
            }
            // If the selection is a prefab asset, save it.
            string path = AssetDatabase.GetAssetPath(root);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.SaveAssets();
        }
        Debug.Log($"[RemoveMissingScripts] Removed {totalRemoved} missing-script component(s) across {objectsChanged} GameObject(s).");
    }
}
#endif
