using UnityEditor;
using UnityEngine;


[CustomEditor (typeof(CharSyncInspector))]
public class CharSyncInspectorEditor : Editor {

  public override void OnInspectorGUI () {
   CharSyncInspector inspector = (CharSyncInspector)target;
        inspector.syncTransform = EditorGUILayout.Toggle("Sync Transform",
            inspector.syncTransform);
        inspector.syncRotation = EditorGUILayout.Toggle("Sync Rotation",
            inspector.syncRotation);
        inspector.syncAnimation = EditorGUILayout.Toggle("Sync Animation",
            inspector.syncAnimation);

        if (GUI.changed)
            EditorUtility.SetDirty(inspector);
    }
}
