using UnityEditor;
using UnityEngine;


[CustomEditor (typeof(PrefabSyncInspector))]
public class PrefabSyncInspectorEditor : Editor {

  public override void OnInspectorGUI () {
   PrefabSyncInspector inspector = (PrefabSyncInspector)target;
        inspector.syncTransform = EditorGUILayout.Toggle("Sync Transform",
            inspector.syncTransform);
        inspector.syncRotation = EditorGUILayout.Toggle("Sync Rotation",
            inspector.syncRotation);
        inspector.syncCount = EditorGUILayout.Toggle("Sync Count",
            inspector.syncCount);

        if (GUI.changed)
            EditorUtility.SetDirty(inspector);
    }
}
