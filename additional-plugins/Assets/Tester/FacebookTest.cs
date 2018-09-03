// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using funapi.network.fun_message;
using plugin_messages;

public class FacebookTest : MonoBehaviour
{
    void Awake ()
    {
        session_ = FunapiSession.Create(server_ip_);
        session_.SessionEventCallback += OnSessionEvent;
        session_.ReceivedMessageCallback += OnReceive;

        session_.Connect(protocol_, encoding_, port_);

        // Initialize facebook
        facebook_ = GameObject.Find("SocialNetwork").GetComponent<FacebookConnector>();

        // If you don't want to download profile photos with a friend list,
        // set this value to false. The default value is ture.
        //facebook_.AutoDownloadPicture = false;

        facebook_.OnEventCallback += new SocialNetwork.EventHandler(OnEventHandler);
        facebook_.OnPictureDownloaded += delegate(SocialNetwork.UserInfo user)
        {
            if (!logged_in_)
            {
                return;
            }

            if (image_ != null && user.picture != null)
            {
                image_.texture = user.picture;
            }
        };

        facebook_.Init();

        // Initialize UI
        login_button_ = GameObject.Find("BtnLogin").GetComponent<Button>();
        image_ = GameObject.Find("imgProfile").GetComponent<RawImage>();

        login_button_.interactable = false;
        setButtonState(false);
    }

    public void OnLogin ()
    {
        if (facebook_.IsLoggedIn)
            return;

        facebook_.LogInWithRead(new List<string>()
        {
            "public_profile", "email", "user_friends"
        });
    }

    public void OnFriendList ()
    {
        facebook_.RequestFriendList(100);
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

    void OnSessionEvent (SessionEventType type, string sessionId)
    {
        if (type == SessionEventType.kConnected)
        {
            login_button_.interactable = true;
        }
        else if (type == SessionEventType.kStopped)
        {
            if (facebook_.IsLoggedIn)
            {
                facebook_.Logout();
                logged_in_ = false;
            }
        }
    }

    void OnReceive (string type, object obj)
    {
        FunEncoding encoding = session_.GetEncoding();

        string result = "";
        if (type == "fb_authentication")
        {
            if (encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> message = obj as Dictionary<string, object>;
                result = message["result"].ToString();
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = obj as FunMessage;
                PbufAnotherMessage message = FunapiMessage.GetMessage<PbufAnotherMessage>(msg, MessageType.pbuf_another);
                result = message.msg;
            }

            if (result == "ok")
            {
                logged_in_ = true;

                if (image_ != null && facebook_.MyPicture != null)
                {
                    image_.texture = facebook_.MyPicture;
                }

                setButtonState(true);
            }
            else
            {
                FunDebug.Log("facebook login authenticatiion failed.");

                facebook_.Logout();
            }
        }
    }

    void OnEventHandler (SNResultCode result)
    {
        FunDebug.DebugLog1("EVENT: Facebook ({0})", result);

        switch (result)
        {
        case SNResultCode.kLoggedIn:
        {
            var token = Facebook.Unity.AccessToken.CurrentAccessToken;
            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["facebook_uid"] = token.UserId;
                message["facebook_access_token"] = token.TokenString;

                session_.SendMessage("login", message);
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FacebookLoginMessage login = new FacebookLoginMessage();
                login.facebook_uid = token.UserId;
                login.facebook_access_token = token.TokenString;

                FunMessage message = FunapiMessage.CreateFunMessage(login, MessageType.facebook_login);
                session_.SendMessage("facebook_login", message);
            }
        }
            break;

        case SNResultCode.kError:
            FunDebug.Assert(false);
            break;
        }
    }

    void setButtonState (bool enable)
    {
        if (other_buttons_.Count <= 0)
        {
            // Gets buttons
            other_buttons_["friends"] = GameObject.Find("BtnFriends").GetComponent<Button>();
            other_buttons_["picture"] = GameObject.Find("BtnPicture").GetComponent<Button>();
        }

        foreach (Button btn in other_buttons_.Values)
        {
            btn.interactable = enable;
        }
    }


    // member variables.
    FacebookConnector facebook_ = null;
    bool logged_in_ = false;

    RawImage image_ = null;
    Button login_button_ = null;
    Dictionary<string, Button> other_buttons_ = new Dictionary<string, Button>();

    string server_ip_ = "127.0.0.1";
    TransportProtocol protocol_ = TransportProtocol.kTcp;
    FunEncoding encoding_ = FunEncoding.kProtobuf;
    ushort port_ = 6013;
    FunapiSession session_ = null;
}
