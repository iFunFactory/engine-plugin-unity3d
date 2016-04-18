// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;

public class FacebookTest : MonoBehaviour
{
    void Awake ()
    {
        facebook_ = GameObject.Find("SocialNetwork").GetComponent<FacebookConnector>();
        //facebook_.auto_request_picture = false;

        facebook_.OnEventCallback += new SocialNetwork.EventHandler(OnEventHandler);
        facebook_.OnPictureDownloaded += delegate(SocialNetwork.UserInfo user) {
            DebugUtils.Log("{0}'s profile picture.", user.name);
            if (tex_ == null)
                tex_ = user.picture;
        };

        facebook_.Init();
    }

    public void OnGUI ()
    {
        // For debugging
        if (GUI.Button(new Rect(30, 30, 240, 40), "Facebook login"))
        {
            facebook_.LogInWithPublish(new List<string>() {
                "public_profile", "email", "user_friends", "publish_actions"});
        }

        GUI.enabled = logged_in_;
        if (GUI.Button(new Rect(30, 80, 240, 40), "Request friend list"))
        {
            facebook_.RequestFriendList(100);
        }

        if (GUI.Button(new Rect(30, 130, 240, 40), "Request invite list"))
        {
            facebook_.RequestInviteList(100);
        }

        if (GUI.Button(new Rect(30, 180, 240, 40), "Post to feed"))
        {
            facebook_.PostWithScreenshot("plugin test post.");
        }

        // Picture
        if (GUI.Button(new Rect(285, 30, 240, 40), "Show friend's picture (random)"))
        {
            SocialNetwork.UserInfo info = facebook_.FindFriendInfo(Random.Range(0, facebook_.friend_list_count));
            if (info != null && info.picture != null)
            {
                tex_ = info.picture;
                Debug.Log(info.name + "'s picture.");
            }
        }

        if (tex_ != null)
            GUI.DrawTexture(new Rect(285, 80, 128, 128), tex_);
    }

    private void OnEventHandler (SNResultCode result)
    {
        switch (result)
        {
        case SNResultCode.kLoggedIn:
            logged_in_ = true;
            break;

        case SNResultCode.kError:
            DebugUtils.Assert(false);
            break;
        }
    }


    // member variables.
    private FacebookConnector facebook_ = null;
    private bool logged_in_ = false;
    private Texture2D tex_ = null;
}
