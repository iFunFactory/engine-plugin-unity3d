// Copyright (C) 2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;

public class TwitterTester : MonoBehaviour
{
    public void Awake()
    {
        twitter_ = GameObject.Find("SocialNetwork").GetComponent<TwitterConnector>();
        twitter_.EventCallback += new SnEventHandler(OnEventHandler);

        // Please pass consumer key and consumer secret of the Twitter apps
        twitter_.Init("4RnU4YDXmu8vmwKW5Lgpej3Xc",
                      "voDDAoaTNXj8VjuWRDhfrnCpa9pnVgpRhBJuKwjJpkg62dtEhd");
    }

    public void OnGUI()
    {
        // For debugging
        GUI.enabled = !logged_in_;
        if (GUI.Button(new Rect(30, 30, 240, 40), "Start OAuth"))
        {
            twitter_.Login();
        }

        GUI.Label(new Rect(30, 80, 240, 22), "pin code : ");

        GUIStyle style = new GUIStyle(GUI.skin.textField);
        style.fontSize = 16;
        style.alignment = TextAnchor.MiddleCenter;
        oauth_verifier_ = GUI.TextField(new Rect(30, 103, 80, 25), oauth_verifier_, style);

        GUI.enabled = oauth_verifier_.Length > 0;
        if (GUI.Button(new Rect(120, 90, 150, 40), "Access token"))
        {
            twitter_.RequestAccess(oauth_verifier_);
        }

        GUI.enabled = logged_in_;
        if (GUI.Button(new Rect(30, 150, 240, 40), "Tweet post"))
        {
            twitter_.Post("Funapi plugin test~");
        }
    }

    private void OnEventHandler (SnResultCode result)
    {
        switch (result)
        {
        case SnResultCode.kLoggedIn:
            logged_in_ = true;
            Debug.Log("Logged in. MyId: " + twitter_.MyId);
            break;

        case SnResultCode.kError:
            DebugUtils.Assert(false);
            break;
        }
    }


    // member variables.
    private TwitterConnector twitter_ = null;
    private string oauth_verifier_ = "";
    private bool logged_in_ = false;
}
