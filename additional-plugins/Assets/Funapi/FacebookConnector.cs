// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

using Facebook.Unity;
using MiniJSON;

namespace Fun
{
    public class FacebookConnector : SocialNetwork
    {
        #region public implementation
        public override void Init (params object[] param)
        {
            DebugUtils.DebugLog("FacebookConnector.Init called.");
            if (!FB.IsInitialized) {
                // Initialize the Facebook SDK
                FB.Init(OnInitCb, OnHideCb);
            } else {
                // Already initialized, signal an app activation App Event
                if (Application.isMobilePlatform)
                    FB.ActivateApp();
            }
        }

        public void LogInWithRead (List<string> perms)
        {
            DebugUtils.DebugLog("Request facebook login with read.");
            FB.LogInWithReadPermissions(perms, OnLoginCb);
        }

        public void LogInWithPublish (List<string> perms)
        {
            DebugUtils.DebugLog("Request facebook login with publish.");
            FB.LogInWithPublishPermissions(perms, OnLoginCb);
        }

        public void Logout()
        {
            FB.LogOut();
        }

        public override void PostWithImage (string message, byte[] image)
        {
            StartCoroutine(PostWithImageEnumerator(message, image));
        }

        public override void PostWithScreenshot (string message)
        {
            StartCoroutine(PostWithScreenshotEnumerator(message));
        }

        public bool auto_request_picture
        {
            set { auto_request_picture_ = value; }
        }

        public void RequestFriendList (int limit)
        {
            string query = string.Format("me?fields=friends.limit({0}).fields(id,name,picture.width(128).height(128))", limit);
            DebugUtils.Log("Facebook request: {0}", query);

            // Reqests friend list
            FB.API(query, HttpMethod.GET, OnFriendListCb);
        }

        public void RequestInviteList (int limit)
        {
            string query = string.Format("me?fields=invitable_friends.limit({0}).fields(id,name,picture.width(128).height(128))", limit);
            DebugUtils.Log("Facebook request: {0}", query);

            // Reqests friend list
            FB.API(query, HttpMethod.GET, OnInviteListCb);
        }

        // start: index at which the range starts.
        public void RequestFriendPictures (int start, int count)
        {
            if (friends_.Count <= 0) {
                DebugUtils.LogWarning("There's no friend list. You should call 'RequestFriendList' first.");
                return;
            }

            List<UserInfo> list = GetRangeOfList(friends_, start, count);
            if (list == null) {
                DebugUtils.LogWarning("Invalid range of friend list. list:{0} start:{1} count:{2}",
                                      friends_.Count, start, count);
                return;
            }

            StartCoroutine(RequestPictureList(list));
        }

        // start: index at which the range starts.
        public void RequestInvitePictures (int start, int count)
        {
            if (invite_friends_.Count <= 0) {
                DebugUtils.LogWarning("There's no friend list. You should call 'RequestInviteList' first.");
                return;
            }

            List<UserInfo> list = GetRangeOfList(invite_friends_, start, count);
            if (list == null) {
                DebugUtils.LogWarning("Invalid range of invite list. list:{0} start:{1} count:{2}",
                                      invite_friends_.Count, start, count);
                return;
            }

            StartCoroutine(RequestPictureList(list));
        }
        #endregion

        #region internal implementation
        private List<UserInfo> GetRangeOfList (List<UserInfo> list, int start, int count)
        {
            if (start < 0 || start >= list.Count ||
                count <= 0 || start + count > list.Count)
                return null;

            return list.GetRange(start, count);
        }

        // Callback-related functions
        void OnInitCb ()
        {
            if (FB.IsInitialized)
            {
                // Signal an app activation App Event
                if (Application.isMobilePlatform)
                    FB.ActivateApp();

                if (FB.IsLoggedIn) {
                    DebugUtils.DebugLog("Already logged in.");
                    OnEventNotify(SNResultCode.kLoggedIn);
                }
                else {
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
            DebugUtils.DebugLog("isGameShown: {0}", isGameShown);
        }

        void OnLoginCb (ILoginResult result)
        {
            if (result.Error != null)
            {
                DebugUtils.DebugLogError(result.Error);
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else if (!FB.IsLoggedIn)
            {
                DebugUtils.DebugLog("User cancelled login.");
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else
            {
                DebugUtils.DebugLog("Login successful!");

                // AccessToken class will have session details
                var aToken = Facebook.Unity.AccessToken.CurrentAccessToken;

                // Print current access token's granted permissions
                StringBuilder perms = new StringBuilder();
                perms.Append("Permissions: ");
                foreach (string perm in aToken.Permissions)
                    perms.AppendFormat("\"{0}\", ", perm);
                DebugUtils.Log(perms.ToString());

                // Reqests my info and profile picture
                FB.API("me?fields=id,name,picture.width(128).height(128)", HttpMethod.GET, OnMyProfileCb);

                OnEventNotify(SNResultCode.kLoggedIn);
            }
        }

        void OnMyProfileCb (IGraphResult result)
        {
            DebugUtils.DebugLog("OnMyProfileCb called.");
            if (result.Error != null)
            {
                DebugUtils.DebugLogError(result.Error);
                OnEventNotify(SNResultCode.kError);
                return;
            }

            try
            {
                Dictionary<string, object> json = Json.Deserialize(result.RawResult) as Dictionary<string, object>;
                if (json == null)
                {
                    DebugUtils.DebugLogError("OnMyProfileCb - json is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                // my profile
                my_info_.id = json["id"] as string;
                my_info_.name = json["name"] as string;
                DebugUtils.Log("Facebook id: {0}, name: {1}.", my_info_.id, my_info_.name);

                // my picture
                var picture = json["picture"] as Dictionary<string, object>;
                var data = picture["data"] as Dictionary<string, object>;
                my_info_.url = data["url"] as string;
                StartCoroutine(RequestPicture(my_info_));

                OnEventNotify(SNResultCode.kMyProfile);
            }
            catch (Exception e)
            {
                DebugUtils.DebugLogError("Failure in OnMyProfileCb: " + e.ToString());
            }
        }

        void OnFriendListCb (IGraphResult result)
        {
            try
            {
                Dictionary<string, object> json = Json.Deserialize(result.RawResult) as Dictionary<string, object>;
                if (json == null)
                {
                    DebugUtils.DebugLogError("OnFriendListCb - json is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                // friend list
                object friend_list = null;
                json.TryGetValue("friends", out friend_list);
                if (friend_list == null) {
                    DebugUtils.DebugLogError("OnInviteListCb - friend_list is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                friends_.Clear();

                List<object> list = ((Dictionary<string, object>)friend_list)["data"] as List<object>;
                foreach (object item in list)
                {
                    Dictionary<string, object> info = item as Dictionary<string, object>;
                    Dictionary<string, object> picture = ((Dictionary<string, object>)info["picture"])["data"] as Dictionary<string, object>;

                    UserInfo user = new UserInfo();
                    user.id = info["id"] as string;
                    user.name = info["name"] as string;
                    user.url = picture["url"] as string;

                    friends_.Add(user);
                    DebugUtils.DebugLog("> id:{0} name:{1} url:{2}", user.id, user.name, user.url);
                }

                DebugUtils.Log("Succeeded in getting the friend list. count:{0}", friends_.Count);
                OnEventNotify(SNResultCode.kFriendList);

                if (auto_request_picture_ && friends_.Count > 0)
                    StartCoroutine(RequestPictureList(friends_));
            }
            catch (Exception e)
            {
                DebugUtils.DebugLogError("Failure in OnFriendListCb: " + e.ToString());
            }
        }

        void OnInviteListCb (IGraphResult result)
        {
            try
            {
                Dictionary<string, object> json = Json.Deserialize(result.RawResult) as Dictionary<string, object>;
                if (json == null) {
                    DebugUtils.DebugLogError("OnInviteListCb - json is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                object invitable_friends = null;
                json.TryGetValue("invitable_friends", out invitable_friends);
                if (invitable_friends == null) {
                    DebugUtils.DebugLogError("OnInviteListCb - invitable_friends is null.");
                    OnEventNotify(SNResultCode.kError);
                    return;
                }

                invite_friends_.Clear();

                List<object> list = ((Dictionary<string, object>)invitable_friends)["data"] as List<object>;
                foreach (object item in list)
                {
                    Dictionary<string, object> info = item as Dictionary<string, object>;
                    Dictionary<string, object> picture = ((Dictionary<string, object>)info["picture"])["data"] as Dictionary<string, object>;

                    string url = picture["url"] as string;
                    UserInfo user = new UserInfo();
                    user.id = info["id"] as string;
                    user.name = info["name"] as string;
                    user.url = url;

                    invite_friends_.Add(user);
                    DebugUtils.DebugLog(">> id:{0} name:{1} url:{2}", user.id, user.name, user.url);
                }

                DebugUtils.Log("Succeeded in getting the invite list.");
                OnEventNotify(SNResultCode.kInviteList);

                if (auto_request_picture_ && invite_friends_.Count > 0)
                    StartCoroutine(RequestPictureList(invite_friends_));
            }
            catch (Exception e)
            {
                DebugUtils.DebugLogError("Failure in OnInviteListCb: " + e.ToString());
            }
        }


        // Post-related functions
        IEnumerator PostWithImageEnumerator (string message, byte[] image)
        {
            yield return new WaitForEndOfFrame();

            var wwwForm = new WWWForm();
            wwwForm.AddBinaryData("image", image, "image.png");
            wwwForm.AddField("message", message);

            FB.API("me/photos", HttpMethod.POST, PostCallback, wwwForm);
        }

        IEnumerator PostWithScreenshotEnumerator (string message)
        {
            yield return new WaitForEndOfFrame();

            var width = Screen.width;
            var height = Screen.height;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            byte[] screenshot = tex.EncodeToPNG();

            var wwwForm = new WWWForm();
            wwwForm.AddBinaryData("image", screenshot, "screenshot.png");
            wwwForm.AddField("message", message);

            FB.API("me/photos", HttpMethod.POST, PostCallback, wwwForm);
        }

        void PostCallback (IGraphResult result)
        {
            DebugUtils.DebugLog("PostCallback called.");
            if (result.Error != null)
            {
                DebugUtils.DebugLogError(result.Error);
                OnEventNotify(SNResultCode.kPostFailed);
                return;
            }

            DebugUtils.Log("Post successful!");
            OnEventNotify(SNResultCode.kPosted);
        }
        #endregion


        // member variables.
        private bool auto_request_picture_ = true;
    }
}
