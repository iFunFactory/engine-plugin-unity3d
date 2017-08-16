// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class TwitterTest : MonoBehaviour
{
    public void Awake ()
    {
        // Initialize twitter
        twitter_ = GameObject.Find("SocialNetwork").GetComponent<TwitterConnector>();

        // If you don't want to download profile photos with a friend list,
        // set this value to false. The default value is ture.
        //twitter_.AutoDownloadPicture = false;

        twitter_.OnEventCallback += new SocialNetwork.EventHandler(OnEventHandler);
        twitter_.OnPictureDownloaded += delegate(SocialNetwork.UserInfo user)
        {
            if (image_ != null && user.picture != null)
                image_.texture = user.picture;
        };

        // Please pass consumer key and consumer secret of the Twitter apps
        twitter_.Init("4RnU4YDXmu8vmwKW5Lgpej3Xc",
                      "voDDAoaTNXj8VjuWRDhfrnCpa9pnVgpRhBJuKwjJpkg62dtEhd");

        // Initialize UI
        image_ = GameObject.Find("ImgProfile").GetComponent<RawImage>();
        pin_code_ = GameObject.Find("InputPinCode").GetComponent<InputField>();

        if (!twitter_.IsLoggedIn)
            setButtonState(false);
    }

    public void OnStartOAuth ()
    {
        if (twitter_.IsLoggedIn)
            return;

        twitter_.Login();
    }

    public void OnAccessToken ()
    {
        if (twitter_.IsLoggedIn)
            return;

        twitter_.RequestAccess(pin_code_.text);
    }

    public void OnFriendList ()
    {
        twitter_.RequestFriendList(100);
    }

    public void OnTweetPost ()
    {
        twitter_.Post("Test post for funapi plugin.");
    }

    public void OnRandomPicture ()
    {
        SocialNetwork.UserInfo info = twitter_.FindFriend(Random.Range(0, twitter_.FriendListCount));
        if (info != null && info.picture != null)
        {
            if (image_ != null)
                image_.texture = info.picture;

            FunDebug.Log("Sets {0}'s picture.", info.name);
        }
    }


    void OnEventHandler (SNResultCode result)
    {
        FunDebug.DebugLog1("EVENT: Twitter ({0})", result);

        switch (result)
        {
        case SNResultCode.kLoggedIn:
            FunDebug.Log("Twitter Id: {0}", twitter_.MyId);
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
            buttons_["post"] = GameObject.Find("BtnPost").GetComponent<Button>();
            buttons_["picture"] = GameObject.Find("BtnPicture").GetComponent<Button>();
        }

        foreach (Button btn in buttons_.Values)
        {
            btn.interactable = enable;
        }
    }


    // member variables.
    TwitterConnector twitter_ = null;

    RawImage image_ = null;
    InputField pin_code_ = null;
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
