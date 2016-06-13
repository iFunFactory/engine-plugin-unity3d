// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
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


    [MenuItem("iFun Plugin/About iFun Plugin", false, 0)]
    static void AboutPlugin ()
    {
        EditorUtility.DisplayDialog("Funapi Plugin", kAboutText, "OK");
    }

    [MenuItem("iFun Plugin/Documentation", false, 0)]
    static void OpenDocs ()
    {
        Application.OpenURL(kDocsUrl);
    }

    [MenuItem("iFun Plugin/Download MozRoots", false, 20)]
    static void DownloadMozRoots ()
    {
        bool ret = Fun.MozRoots.DownloadMozRoots();

        string text = string.Format("Download certificates {0}!!", ret ? "succeeded" : "failed");
        EditorUtility.DisplayDialog("Funapi Plugin", text, "OK");
    }
}
