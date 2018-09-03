// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Facebook.Unity;
using MiniJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


namespace Fun
{
    public class FacebookConnector : SocialNetwork
    {
        public void Init ()
        {
            FunDebug.DebugLog1("FacebookConnector.Init called.");

            if (!FB.IsInitialized)
            {
                // Initialize the Facebook SDK
                FB.Init(OnInitCb, OnHideCb);
            }
            else
            {
                // Already initialized, signal an app activation App Event
                if (Application.isMobilePlatform)
                    FB.ActivateApp();
            }
        }

        public void LogInWithRead (List<string> perms)
        {
            FunDebug.Log("Request facebook login with read.");
            FB.LogInWithReadPermissions(perms, OnLoginCb);
        }

        public void LogInWithPublish (List<string> perms)
        {
            FunDebug.Log("Request facebook login with publish.");
            FB.LogInWithPublishPermissions(perms, OnLoginCb);
        }

        public void Logout ()
        {
            FB.LogOut();
        }

        [System.Obsolete("This is an obsolete method. This method is no longer used because the 'publish_actions' permission has been removed.")]
        public override void PostWithImage (string message, byte[] image)
        {
        }

        [System.Obsolete("This is an obsolete method. This method is no longer used because the 'publish_actions' permission has been removed.")]
        public override void PostWithScreenshot (string message)
        {
        }

        public bool IsLoggedIn
        {
            get { return FB.IsLoggedIn; }
        }

        public bool AutoDownloadPicture
        {
            set { auto_request_picture_ = value; }
        }

        public void RequestFriendList (int limit)
        {
            string query = string.Format("me?fields=friends.limit({0})" +
                                         ".fields(id,name,picture.width(128).height(128))", limit);
            FunDebug.Log("Facebook request: {0}", query);

            // Reqests friend list
            FB.API(query, HttpMethod.GET, OnFriendListCb);
        }

        [System.Obsolete("This is an obsolete method. This method is no longer used because the 'invitable_friends' has been removed.")]
        public void RequestInviteList (int limit)
        {
        }


        // Callback-related functions
        void OnInitCb ()
        {
            if (FB.IsInitialized)
            {
                // Signal an app activation App Event
                if (Application.isMobilePlatform)
                    FB.ActivateApp();

                if (FB.IsLoggedIn)
                {
                    FunDebug.Log("Already logged in.");
                    OnEventNotify(SNResultCode.kLoggedIn);
                }
                else
                {
                    OnEventNotify(SNResultCode.kInitialized);
                }
            }
            else
            {
                Debug.LogWarning("Failed to Initialize the Facebook SDK");
            }
        }

        void OnHideCb (bool isGameShown)
        {
            FunDebug.DebugLog1("isGameShown: {0}", isGameShown);
        }

        void OnLoginCb (ILoginResult result)
        {
            if (result.Error != null)
            {
                FunDebug.LogError(result.Error);
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else if (!FB.IsLoggedIn)
            {
                FunDebug.Log("User cancelled login.");
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else
            {
                FunDebug.Log("Facebook login succeeded!");

                // AccessToken class will have session details
                var aToken = Facebook.Unity.AccessToken.CurrentAccessToken;

                // Print current access token's granted permissions
                StringBuilder perms = new StringBuilder();
                perms.Append("Permissions: ");
                foreach (string perm in aToken.Permissions)
                    perms.AppendFormat("\"{0}\", ", perm);
                FunDebug.Log(perms.ToString());

                // Reqests my info and profile picture
                FB.API("me?fields=id,name,picture.width(128).height(128)", HttpMethod.GET, OnMyProfileCb);

                OnEventNotify(SNResultCode.kLoggedIn);
            }
        }

        void OnMyProfileCb (IGraphResult result)
        {
            FunDebug.DebugLog1("FacebookConnector.OnMyProfileCb called.");
            if (result.Error != null)
            {
                FunDebug.LogError(result.Error);
                OnEventNotify(SNResultCode.kError);
                return;
            }

            try
            {
                Dictionary<string, object> json = Json.Deserialize(result.RawResult) as Dictionary<string, object>;
                if (json == null)
                {
                    FunDebug.LogError("OnMyProfileCb - json is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                // my profile
                my_info_.id = json["id"] as string;
                my_info_.name = json["name"] as string;
                FunDebug.Log("Facebook id: {0}, name: {1}.", my_info_.id, my_info_.name);

                // my picture
                var picture = json["picture"] as Dictionary<string, object>;
                var data = picture["data"] as Dictionary<string, object>;
                my_info_.url = data["url"] as string;
                StartCoroutine(RequestPicture(my_info_));

                OnEventNotify(SNResultCode.kMyProfile);
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in OnMyProfileCb: {0}", e.ToString());
            }
        }

        void OnFriendListCb (IGraphResult result)
        {
            try
            {
                Dictionary<string, object> json = Json.Deserialize(result.RawResult) as Dictionary<string, object>;
                if (json == null)
                {
                    FunDebug.LogError("OnFriendListCb - json is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                // friend list
                object friend_list = null;
                json.TryGetValue("friends", out friend_list);
                if (friend_list == null)
                {
                    FunDebug.LogError("OnInviteListCb - friend_list is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                lock (friend_list_)
                {
                    friend_list_.Clear();

                    List<object> list = ((Dictionary<string, object>)friend_list)["data"] as List<object>;
                    foreach (object item in list)
                    {
                        Dictionary<string, object> info = item as Dictionary<string, object>;
                        Dictionary<string, object> picture = ((Dictionary<string, object>)info["picture"])["data"] as Dictionary<string, object>;

                        UserInfo user = new UserInfo();
                        user.id = info["id"] as string;
                        user.name = info["name"] as string;
                        user.url = picture["url"] as string;

                        friend_list_.Add(user);
                        FunDebug.DebugLog1("> id:{0} name:{1} image:{2}", user.id, user.name, user.url);
                    }
                }

                FunDebug.Log("Succeeded in getting the friend list. count:{0}", friend_list_.Count);
                OnEventNotify(SNResultCode.kFriendList);

                lock (friend_list_)
                {
                    if (auto_request_picture_ && friend_list_.Count > 0)
                        StartCoroutine(RequestPictures(GetFriendList()));
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in OnFriendListCb: {0}", e.ToString());
            }
        }

        // member variables.
        bool auto_request_picture_ = true;
    }
}
