using UnityEditor;
using UnityEngine;


[CustomEditor (typeof(BasicSyncInspector))]
public class BasicSyncInspectorEditor : Editor {

  public override void OnInspectorGUI () {
   BasicSyncInspector inspector = (BasicSyncInspector)target;
        inspector.syncTransform = EditorGUILayout.Toggle("Sync Transform",
            inspector.syncTransform);
        inspector.syncRotation = EditorGUILayout.Toggle("Sync Rotation",
            inspector.syncRotation);
        if (GUI.changed)
            EditorUtility.SetDirty(inspector);
    }
}
