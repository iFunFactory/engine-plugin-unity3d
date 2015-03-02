// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MiniJSON;

namespace Fun
{
    public class FacebookConnector : SocialNetwork
    {
        #region public implementation
        public override void Init (params object[] param)
        {
            Debug.Log("FacebookConnector Initialization.");
            FB.Init(OnInitComplete, isGameShown =>
            {
                Debug.Log("isGameShown: " + isGameShown);
            });
        }

        public void Login (string scope)
        {
            status_scope_ = scope;

            if (init_ == false)
            {
                try_login_ = true;
                return;
            }

            Debug.Log("Request login facebook.");
            FB.Login(scope, LoginCallback);
        }

        public void Logout()
        {
            FB.Logout();
        }

        public override void PostWithImage (string message, byte[] image)
        {
            StartCoroutine(PostWithImageEnumerator(message, image));
        }

        public override void PostWithScreenshot (string message)
        {
            StartCoroutine(PostWithScreenshotEnumerator(message));
        }
        #endregion

        #region internal implementation
        // Callback-related functions
        void OnInitComplete()
        {
            Debug.Log("OnInitComplete called.");

            init_ = true;

            if (FB.IsLoggedIn)
            {
                Debug.Log("Already logged in.");
                OnEventHandler(SnResultCode.kLoggedIn);
            }
            else if (try_login_)
            {
                Login(status_scope_);
            }
        }

        void LoginCallback (FBResult result)
        {
            if (result.Error != null)
            {
                Debug.LogError(result.Text);
                OnEventHandler(SnResultCode.kLoginFailed);
            }
            else if (!FB.IsLoggedIn)
            {
                Debug.Log("Login cancelled by Player.");
                OnEventHandler(SnResultCode.kLoginFailed);
            }
            else
            {
                Debug.Log("Login was successful!");

                string start_query = "me?fields=id,name"
                    + ",friends.limit(100).fields(id,name,picture.width(128).height(128))"
                    + ",invitable_friends.limit(100).fields(id,name,picture.width(128).height(128))";

                // Reqest player info and profile picture
                FB.API(start_query, Facebook.HttpMethod.GET, StartCallback);
                RequestPicture(FB.UserId, GetPictureURL("me", 128, 128), MyPictureCallback);

                OnEventHandler(SnResultCode.kLoggedIn);
            }
        }

        void StartCallback (FBResult result)
        {
            Debug.Log("StartCallback called.");
            if (result.Error != null)
            {
                Debug.LogError(result.Text);
                OnEventHandler(SnResultCode.kError);
                return;
            }

            Debug.Log(">>> " + result.Text);

            try
            {
                Dictionary<string, object> json = Json.Deserialize(result.Text) as Dictionary<string, object>;
                if (json == null)
                {
                    Debug.LogError("StartCallback - json is null.");
                    OnEventHandler(SnResultCode.kError);
                    return;
                }

                // my profile
                DebugUtils.Assert(json["id"] is string);
                DebugUtils.Assert(json["name"] is string);
                my_info_.id = json["id"] as string;
                my_info_.name = json["name"] as string;
                Debug.Log("my name: " + my_info_.name);

                OnEventHandler(SnResultCode.kGetMyInfo);

                // friends list
                object friends = null;
                object invitable_friends = null;
                json.TryGetValue("friends", out friends);
                json.TryGetValue("invitable_friends", out invitable_friends);

                if (friends != null || invitable_friends != null)
                {
                    if (friends != null)
                    {
                        friends_.Clear();

                        List<object> list = ((Dictionary<string, object>)friends)["data"] as List<object>;
                        foreach (object item in list)
                        {
                            Dictionary<string, object> info = item as Dictionary<string, object>;
                            Dictionary<string, object> data = ((Dictionary<string, object>)info["picture"])["data"] as Dictionary<string, object>;
                            DebugUtils.Assert(info["id"] is string);
                            DebugUtils.Assert(info["name"] is string);
                            DebugUtils.Assert(data["url"] is string);

                            UserInfo user = new UserInfo();
                            user.id = info["id"] as string;
                            user.name = info["name"] as string;
                            user.url = data["url"] as string;

                            friends_.Add(user);
                            Debug.Log("> id:" + user.id + " name:" + user.name + " url:" + user.url);
                        }
                    }

                    if (invitable_friends != null)
                    {
                        invite_friends_.Clear();

                        List<object> list = ((Dictionary<string, object>)invitable_friends)["data"] as List<object>;
                        foreach (object item in list)
                        {
                            Dictionary<string, object> info = item as Dictionary<string, object>;
                            Dictionary<string, object> data = ((Dictionary<string, object>)info["picture"])["data"] as Dictionary<string, object>;
                            DebugUtils.Assert(info["id"] is string);
                            DebugUtils.Assert(info["name"] is string);
                            DebugUtils.Assert(data["url"] is string);

                            string url = data["url"] as string;
                            UserInfo user = new UserInfo();
                            user.id = info["id"] as string;
                            user.name = info["name"] as string;
                            user.url = url;

                            invite_friends_.Add(user);
                            Debug.Log(">> id:" + user.id + " name:" + user.name + " url:" + user.url);
                        }
                    }

                    if (friends_.Count > 0)
                        StartCoroutine(UpdateFriendsPictureEnumerator());

                    OnEventHandler(SnResultCode.kGetFriendsList);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Failure in StartCallback: " + e.ToString());
            }
        }


        // Picture-related functions
        private void RequestPicture (string id, string url, RequestPictureCallback callback)
        {
            FB.API(url, Facebook.HttpMethod.GET, result =>
            {
                if (result.Error != null)
                {
                    Debug.LogError(result.Text);
                    return;
                }

                var json = Json.Deserialize(result.Text) as Dictionary<string, object>;
                var picture = json["data"] as Dictionary<string, object>;
                DebugUtils.Assert(picture["url"] is string);

                string imageUrl = picture["url"] as string;
                StartCoroutine(LoadPictureEnumerator(id, imageUrl, callback));
            });
        }

        IEnumerator LoadPictureEnumerator (string id, string url, RequestPictureCallback callback)
        {
            WWW www = new WWW(url);
            yield return www;
            callback(id, www.texture);
        }

        IEnumerator UpdateFriendsPictureEnumerator()
        {
            foreach (UserInfo user in friends_)
            {
                WWW www = new WWW(user.url);
                yield return www;
                user.picture = www.texture;
            }

            foreach (UserInfo user in invite_friends_)
            {
                WWW www = new WWW(user.url);
                yield return www;
                user.picture = www.texture;
            }
        }

        private string GetPictureURL (string facebookID, int? width = null, int? height = null, string type = null)
        {
            string url = facebookID + "/picture";
            string query = width != null ? "&width=" + width.ToString() : "";
            query += height != null ? "&height=" + height.ToString() : "";
            query += type != null ? "&type=" + type : "";
            query += "&redirect=false";

            if (query != "")
                url += ("?g" + query);

            return url;
        }

        void MyPictureCallback (string id, Texture2D texture)
        {
            Debug.Log("MyPictureCallback called.");

            if (texture ==  null)
            {
                Debug.Log("texture is null.");
                return;
            }

            my_info_.picture = texture;
        }



        // Post-related functions
        IEnumerator PostWithImageEnumerator (string message, byte[] image)
        {
            yield return new WaitForEndOfFrame();

            var wwwForm = new WWWForm();
            wwwForm.AddBinaryData("image", image, "image.png");
            wwwForm.AddField("message", message);

            FB.API("me/photos", Facebook.HttpMethod.POST, PostCallback, wwwForm);
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

            FB.API("me/photos", Facebook.HttpMethod.POST, PostCallback, wwwForm);
        }

        void PostCallback (FBResult result)
        {
            Debug.Log("PostCallback called.");
            if (result.Error != null)
            {
                Debug.LogError(result.Text);
                OnEventHandler(SnResultCode.kPostFailed);
                return;
            }

            Debug.Log("result: " + result.Text);
            OnEventHandler(SnResultCode.kPosted);
        }
        #endregion


        // Callback-related delegates.
        private delegate void RequestPictureCallback(string id, Texture2D texture);

       // Member variables.
        private bool init_ = false;
        private bool try_login_ = false;
        private string status_scope_ = "";
    }
}
