// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;

public class FacebookTester : MonoBehaviour
{
    public void OnGUI()
    {
        // For debugging
        if (GUI.Button(new Rect(30, 30, 240, 40), "Facebook login"))
        {
            facebook_ = GameObject.Find("SocialNetwork").GetComponent<FacebookConnector>();
            facebook_.EventCallback += new SnEventHandler(OnEventHandler);

            facebook_.Init();
            facebook_.Login("user_friends,publish_actions");
        }

        GUI.enabled = logged_in_;
        if (GUI.Button(new Rect(30, 80, 240, 40), "Show friend's picture (random)"))
        {
            SocialNetwork.UserInfo info = facebook_.FindFriendInfo(Random.Range(0, facebook_.FriendsCount));
            if (info != null && info.picture != null)
            {
                tex_ = info.picture;
                Debug.Log(info.name + "'s picture.");
            }
        }

        if (GUI.Button(new Rect(30, 130, 240, 40), "Post to feed"))
        {
            facebook_.PostWithScreenshot("Funapi plugin test post.");
        }

        if (tex_ != null)
            GUI.DrawTexture(new Rect(285, 30, 128, 128), tex_);
    }

    private void OnEventHandler (SnResultCode result)
    {
        switch (result)
        {
        case SnResultCode.kLoggedIn:
            logged_in_ = true;
            Debug.Log("Logged in. MyId: " + facebook_.MyId);
            break;

        case SnResultCode.kError:
            DebugUtils.Assert(false);
            break;
        }
    }


    // member variables.
    private FacebookConnector facebook_ = null;
    private bool logged_in_ = false;
    private Texture2D tex_ = null;
}
