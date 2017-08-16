// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class FacebookTest : MonoBehaviour
{
    void Awake ()
    {
        // Initialize facebook
        facebook_ = GameObject.Find("SocialNetwork").GetComponent<FacebookConnector>();

        // If you don't want to download profile photos with a friend list,
        // set this value to false. The default value is ture.
        //facebook_.AutoDownloadPicture = false;

        facebook_.OnEventCallback += new SocialNetwork.EventHandler(OnEventHandler);
        facebook_.OnPictureDownloaded += delegate(SocialNetwork.UserInfo user)
        {
            if (image_ != null && user.picture != null)
                image_.texture = user.picture;
        };

        facebook_.Init();

        // Initialize UI
        image_ = GameObject.Find("imgProfile").GetComponent<RawImage>();
        setButtonState(false);
    }

    public void OnLogin ()
    {
        if (facebook_.IsLoggedIn)
            return;

        facebook_.LogInWithPublish(new List<string>()
        {
            "public_profile", "email", "user_friends", "publish_actions"
        });
    }

    public void OnFriendList ()
    {
        facebook_.RequestFriendList(100);
    }

    public void OnInviteList ()
    {
        facebook_.RequestInviteList(100);
    }

    public void OnPostToFeed ()
    {
        facebook_.PostWithScreenshot("Test post for funapi plugin.");
    }

    public void OnRandomPicture ()
    {
        SocialNetwork.UserInfo info = facebook_.FindFriend(Random.Range(0, facebook_.FriendListCount));
        if (info != null && info.picture != null)
        {
            if (image_ != null)
                image_.texture = info.picture;

            FunDebug.Log("Sets {0}'s picture.", info.name);
        }
    }

    void OnEventHandler (SNResultCode result)
    {
        FunDebug.DebugLog1("EVENT: Facebook ({0})", result);

        switch (result)
        {
        case SNResultCode.kLoggedIn:
            setButtonState(true);
            break;

        case SNResultCode.kError:
            FunDebug.Assert(false);
            break;
        }
    }

    void setButtonState (bool enable)
    {
        if (buttons_.Count <= 0)
        {
            // Gets buttons
            buttons_["friends"] = GameObject.Find("BtnFriends").GetComponent<Button>();
            buttons_["invite"] = GameObject.Find("BtnInvite").GetComponent<Button>();
            buttons_["post"] = GameObject.Find("BtnPost").GetComponent<Button>();
            buttons_["picture"] = GameObject.Find("BtnPicture").GetComponent<Button>();
        }

        foreach (Button btn in buttons_.Values)
        {
            btn.interactable = enable;
        }
    }


    // member variables.
    FacebookConnector facebook_ = null;

    RawImage image_ = null;
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
