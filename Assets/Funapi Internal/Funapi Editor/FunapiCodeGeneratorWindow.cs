using UnityEditor;
using UnityEngine;


public class FunapiCodeGeneratorWindow : EditorWindow
{
  // project
  string projectName = "JustFun";
  // common
  bool protoFileGenEnabled = true;
  bool jsonFileGenEnabled = true;
  // server
  bool serverCodeGenEnabled = true;
  bool serverCompleteCode = true;
  bool serverPartialCode = false;
  // client
  bool clientLogInOutGenEnabled = true;
  // code gen button rect
  Rect codeGenButtonRect;


  // Add menu item named "My Window" to the Window menu
  [MenuItem("Window/Funapi Code Generator")]
  public static void ShowWindow()
  {
    //Show existing window instance. If one doesn't exist, make one.
    EditorWindow.GetWindow(typeof(FunapiCodeGeneratorWindow));
  }

  void OnGUI()
  {
    // project
    GUILayout.Label("Project Settings", EditorStyles.boldLabel);
    projectName = EditorGUILayout.TextField("Project Name", projectName);
    EditorGUILayout.Space();

    // common
    GUILayout.Label("Common Settings", EditorStyles.boldLabel);
    protoFileGenEnabled = EditorGUILayout.Toggle("Proto File Generate",
                                                protoFileGenEnabled);
    jsonFileGenEnabled = EditorGUILayout.Toggle("Json File Generate",
                                                jsonFileGenEnabled);
    EditorGUILayout.Space();

    // server
    GUILayout.Label("Server Settings", EditorStyles.boldLabel);
    serverCodeGenEnabled = EditorGUILayout.BeginToggleGroup(
        "Check Server Code Generation", serverCodeGenEnabled);
    serverCompleteCode = EditorGUILayout.Toggle("Server Complete Code",
                                                serverCompleteCode);
    serverPartialCode = EditorGUILayout.Toggle("Server Partial Code",
                                                serverPartialCode);
    EditorGUILayout.EndToggleGroup();
    EditorGUILayout.Space();

    // client
    GUILayout.Label("Client Settings", EditorStyles.boldLabel);
    clientLogInOutGenEnabled = EditorGUILayout.Toggle(
        "Client Dummy LogIn/Out Code Generation", clientLogInOutGenEnabled);
    EditorGUILayout.Space();

    // code gen button
    codeGenButtonRect = EditorGUILayout.BeginHorizontal("Button");
    if (GUI.Button(codeGenButtonRect, GUIContent.none)) {
      Debug.Log("Code generate starting...");
      // call code generate
      CodeGenerate();
      // code generate end
      Debug.Log("Code generate end");
    }
    GUILayout.Label("Do Generate!");
    EditorGUILayout.EndHorizontal();
    EditorGUILayout.Space();
  }

  // do generate!!
  private void CodeGenerate() {
    // code moving

    // object create

    // script component attach to object

    // funapi sync object tagging
  }
}
