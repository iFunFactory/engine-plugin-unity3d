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

        twitter_.OnEventCallback += new SocialNetwork.EventHandler(OnEventHandler);
        twitter_.OnPictureDownloaded += delegate(SocialNetwork.UserInfo user) {
            DebugUtils.Log("{0}'s profile picture.", user.name);
            if (tex_ == null)
                tex_ = user.picture;
        };

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

        GUI.Label(new Rect(30, 70, 240, 22), "pin code : ");

        GUIStyle style = new GUIStyle(GUI.skin.textField);
        style.fontSize = 16;
        style.alignment = TextAnchor.MiddleCenter;
        oauth_verifier_ = GUI.TextField(new Rect(30, 93, 80, 25), oauth_verifier_, style);

        GUI.enabled = oauth_verifier_.Length > 0;
        if (GUI.Button(new Rect(120, 80, 150, 40), "Access token"))
        {
            twitter_.RequestAccess(oauth_verifier_);
        }

        GUI.enabled = logged_in_;
        if (GUI.Button(new Rect(30, 140, 240, 40), "Get friends list"))
        {
            twitter_.RequestFriendList(100);
        }

        if (GUI.Button(new Rect(30, 190, 240, 40), "Tweet post"))
        {
            twitter_.Post("Funapi plugin test~");
        }

        if (GUI.Button(new Rect(290, 30, 240, 40), "Show friend's picture (random)"))
        {
            SocialNetwork.UserInfo info = twitter_.FindFriendInfo(Random.Range(0, twitter_.friend_list_count));
            if (info != null && info.picture != null)
            {
                tex_ = info.picture;
                Debug.Log(info.name + "'s picture.");
            }
        }

        if (tex_ != null)
            GUI.DrawTexture(new Rect(295, 80, 73, 73), tex_);
    }

    private void OnEventHandler (SNResultCode result)
    {
        switch (result)
        {
        case SNResultCode.kLoggedIn:
            logged_in_ = true;
            Debug.Log("Logged in. MyId: " + twitter_.my_id);
            break;

        case SNResultCode.kError:
            DebugUtils.Assert(false);
            break;
        }
    }


    // member variables.
    private TwitterConnector twitter_ = null;
    private string oauth_verifier_ = "";
    private bool logged_in_ = false;
    private Texture2D tex_ = null;
}
