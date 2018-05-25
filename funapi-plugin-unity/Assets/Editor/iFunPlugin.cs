// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using UnityEditor;
using UnityEngine;


public class iFunPlugin : ScriptableObject
{
    const string kAboutText = "This helps you use the Funapi Plugin.\n\n" +
                              "If you have any trouble then please contact us.\n" +
                              "funapi-support@ifunfactory.com";
    const string kDocsUrl = "http://www.ifunfactory.com/engine/documents/reference/ko/client-plugin.html";


    [MenuItem("iFun Plugin/Export Package", false, 10)]
    static void MakeExportPackage ()
    {
        makePackage();
    }

    [MenuItem("iFun Plugin/Download Root Certificates", false, 20)]
    static void DownloadMozRoots ()
    {
        bool ret = Fun.TrustManager.DownloadMozRoots();

        string text = string.Format("Download certificates {0}!!", ret ? "succeeded" : "failed");
        EditorUtility.DisplayDialog("Funapi Plugin", text, "OK");
    }

    [MenuItem("iFun Plugin/Documentation", false, 100)]
    static void OpenDocs ()
    {
        Application.OpenURL(kDocsUrl);
    }

    [MenuItem("iFun Plugin/About iFun Plugin", false, 100)]
    static void AboutPlugin ()
    {
        EditorUtility.DisplayDialog("Funapi Plugin", kAboutText, "OK");
    }


    static void makePackage ()
    {
        string output = string.Format("funapi-plugin-unity-v{0}.unitypackage", Fun.FunapiVersion.kPluginVersion);
        string[] assets = new string[] { "Assets/Editor", "Assets/Funapi", "Assets/Plugins",
                                         "Assets/Resources", "Assets/Tester", "Assets/protobuf-net.dll",
                                         "Assets/FunMessageSerializer.dll", "Assets/messages.dll"};

        AssetDatabase.ExportPackage(assets, "../" + output, ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
        EditorUtility.DisplayDialog("Funapi Plugin", string.Format("Package creation success!\n({0})", output), "OK");
    }
}
