// Copyright (C) 2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

using MiniJSON;


namespace Fun
{
    public class TwitterConnector : SocialNetwork
    {
        public void Awake ()
        {
            // this array should be filled before you can use EncryptedPlayerPrefs :
            EncryptedPlayerPrefs.keys=new string[5];
            EncryptedPlayerPrefs.keys[0]="AFsT0m8Q";
            EncryptedPlayerPrefs.keys[1]="WKvhyubv";
            EncryptedPlayerPrefs.keys[2]="kOg6mN9l";
            EncryptedPlayerPrefs.keys[3]="Ed3ri5U4";
            EncryptedPlayerPrefs.keys[4]="GyVHft3j";

            OnEventCallback += new SocialNetwork.EventHandler(OnEventHandler);
        }

        #region public implementation
        public override void Init(params object[] param)
        {
            Debug.Log("TwitterConnector Initialization.");
            DebugUtils.Assert(param[0] is string);
            DebugUtils.Assert(param[1] is string);

            oauth_handler_ = new OAuthHandler(param[0] as string, param[1] as string);

            string oauth_token = EncryptedPlayerPrefs.GetString("oauth_token");
            string oauth_token_secret = EncryptedPlayerPrefs.GetString("oauth_token_secret");
            if (!string.IsNullOrEmpty(oauth_token) && !string.IsNullOrEmpty(oauth_token_secret))
            {
                oauth_handler_.AddParameter("oauth_token", oauth_token);
                oauth_handler_.AddParameter("oauth_token_secret", oauth_token_secret);

                my_info_.id = EncryptedPlayerPrefs.GetString("user_id");
                my_info_.name = EncryptedPlayerPrefs.GetString("screen_name");

                Debug.Log("Already logged in.");
                OnEventNotify(SNResultCode.kLoggedIn);
            }
        }

        public bool auto_request_picture
        {
            set { auto_request_picture_ = value; }
        }

        public void Login()
        {
            Debug.Log("Request token.");
            StartCoroutine(GetRequestToken());
        }

        public void RequestAccess (string oauth_verifier)
        {
            StartCoroutine(GetAccessToken(oauth_verifier));
        }

        public void Logout()
        {
        }

        public void RequestFriendList (int limit)
        {
            limit = Mathf.Max(Mathf.Min(0, limit), kFriendLimitMax);
            if (limit <= 0)
                return;

            StartCoroutine(RequestFriendIds(limit));
        }

        public override void Post (string message)
        {
            Debug.Log("Post tweet. message: " + message);
            StartCoroutine(PostTweet(message));
        }
        #endregion


        #region internal implementation
        private void OnEventHandler (SNResultCode result)
        {
            switch (result)
            {
            case SNResultCode.kLoggedIn:
                StartCoroutine(RequestMyInfo());
                break;
            }
        }

        // Coroutine-related functions
        IEnumerator GetRequestToken ()
        {
            // form data
            WWWForm form = new WWWForm();
            form.AddField("oauth_callback", "oob");

            // header
            oauth_handler_.Clear();
            oauth_handler_.AddParameter("oauth_callback", "oob");

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = oauth_handler_.GenerateHeader(kRequestUrl, "POST");

            // request
            WWW web = new WWW(kRequestUrl, form.data, headers);
            yield return web;

            if (!string.IsNullOrEmpty(web.error))
            {
                Debug.Log(string.Format("GetRequestToken - failed. {0}, {1}", web.error, web.text));
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else
            {
                string token = Regex.Match(web.text, @"oauth_token=([^&]+)").Groups[1].Value;
                string secret = Regex.Match(web.text, @"oauth_token_secret=([^&]+)").Groups[1].Value;

                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(secret))
                {
                    Debug.Log(string.Format("GetRequestToken - succeeded.\n  token: {0}\n  secret: {1}",
                                            token, secret));

                    request_token_ = token;

                    string url = string.Format(kOAuthUrl, token);
                    Application.OpenURL(url);
                }
                else
                {
                    Debug.Log("GetRequestToken - failed. " + web.text);
                    OnEventNotify(SNResultCode.kLoginFailed);
                }
            }
        }

        IEnumerator GetAccessToken (string oauth_verifier)
        {
            // header
            oauth_handler_.Clear();
            oauth_handler_.AddParameter("oauth_token", request_token_);
            oauth_handler_.AddParameter("oauth_verifier", oauth_verifier);

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = oauth_handler_.GenerateHeader(kAccessUrl, "POST");

            WWW web = new WWW(kAccessUrl, null, headers);
            yield return web;

            if (!string.IsNullOrEmpty(web.error))
            {
                Debug.Log(string.Format("GetAccessToken - failed. {0}, {1}", web.error, web.text));
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else
            {
                my_info_.id = Regex.Match(web.text, @"user_id=([^&]+)").Groups[1].Value;
                my_info_.name = Regex.Match(web.text, @"screen_name=([^&]+)").Groups[1].Value;
                string token = Regex.Match(web.text, @"oauth_token=([^&]+)").Groups[1].Value;
                string secret = Regex.Match(web.text, @"oauth_token_secret=([^&]+)").Groups[1].Value;

                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(secret) &&
                    !string.IsNullOrEmpty(my_info_.id) && !string.IsNullOrEmpty(my_info_.name))
                {
                    Debug.Log("GetAccessToken - succeeded.");
                    Debug.Log(string.Format("id: {0}\nname: {1}\ntoken: {2}\ntoken_secret: {3}",
                                            my_info_.id, my_info_.name, token, secret));

                    oauth_handler_.AddParameter("oauth_token", token);
                    oauth_handler_.AddParameter("oauth_token_secret", secret);

                    EncryptedPlayerPrefs.SetString("user_id", my_info_.id);
                    EncryptedPlayerPrefs.SetString("screen_name", my_info_.name);
                    EncryptedPlayerPrefs.SetString("oauth_token", token);
                    EncryptedPlayerPrefs.SetString("oauth_token_secret", secret);

                    OnEventNotify(SNResultCode.kLoggedIn);
                }
                else
                {
                    Debug.Log("GetAccessToken - failed. " + web.text);
                    OnEventNotify(SNResultCode.kLoginFailed);
                }
            }
        }

        IEnumerator RequestMyInfo ()
        {
            oauth_handler_.Clear();
            oauth_handler_.AddParameter("user_id", my_id);

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = oauth_handler_.GenerateHeader(kShowUrl, "GET");

            string url = string.Format("{0}?user_id={1}", kShowUrl, my_id);
            WWW web = new WWW(url.ToString(), null, headers);
            yield return web;

            if (!string.IsNullOrEmpty(web.error))
            {
                Debug.Log(string.Format("RequestMyInfo - failed. {0}", web.error));
                OnEventNotify(SNResultCode.kError);
            }
            else
            {
                Dictionary<string, object> data = Json.Deserialize(web.text) as Dictionary<string, object>;
                string img_url = data["profile_image_url"] as string;
                my_info_.url = img_url.Replace("normal", "bigger");
                StartCoroutine(RequestPicture(my_info_));

                OnEventNotify(SNResultCode.kMyProfile);
            }
        }

        IEnumerator RequestFriendIds (int limit)
        {
            oauth_handler_.Clear();

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = oauth_handler_.GenerateHeader(kFriendsIdsUrl, "GET");

            WWW web = new WWW(kFriendsIdsUrl, null, headers);
            yield return web;

            if (!string.IsNullOrEmpty(web.error))
            {
                Debug.Log(string.Format("RequestFriendIds - failed. {0}", web.error));
                OnEventNotify(SNResultCode.kError);
            }
            else
            {
                Dictionary<string, object> data = Json.Deserialize(web.text) as Dictionary<string, object>;
                List<object> list = data["ids"] as List<object>;

                StringBuilder ids = new StringBuilder();
                int idx = 0, count = Mathf.Min(list.Count, limit);
                foreach (object id in list)
                {
                    ids.AppendFormat("{0},", Convert.ToUInt32(id));
                    if (++idx >= count)
                        break;
                }

                if (ids.Length > 0)
                    StartCoroutine(RequestFriendsInfo(ids.ToString()));
            }
        }

        IEnumerator RequestFriendsInfo (string ids)
        {
            oauth_handler_.Clear();
            oauth_handler_.AddParameter("user_id", ids);

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = oauth_handler_.GenerateHeader(kLookupUrl, "GET");

            string url = string.Format("{0}?user_id={1}", kLookupUrl, ids);
            WWW web = new WWW(url.ToString(), null, headers);
            yield return web;

            if (!string.IsNullOrEmpty(web.error))
            {
                Debug.Log(string.Format("RequestUserInfo - failed. {0}", web.error));
                OnEventNotify(SNResultCode.kError);
            }
            else
            {
                List<object> list = Json.Deserialize(web.text) as List<object>;
                if (list.Count <= 0)
                {
                    DebugUtils.Log("RequestUserInfo - Invalid list data. List size is 0.");
                    yield break;
                }
                else
                {
                    foreach (Dictionary<string, object> node in list)
                    {
                        UserInfo user = new UserInfo();
                        user.id = node["id"] as string;
                        user.name = node["screen_name"] as string;

                        url = node["profile_image_url"] as string;
                        user.url = url.Replace("_normal", "_bigger");

                        friends_.Add(user);
                    }

                    DebugUtils.Log("Succeeded in getting the friend list. count:{0}", friends_.Count);
                    OnEventNotify(SNResultCode.kFriendList);

                    if (auto_request_picture_ && friends_.Count > 0)
                        StartCoroutine(RequestPictureList(friends_));
                }
            }
        }

        IEnumerator PostTweet (string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length > 140)
            {
                Debug.Log("PostTweet - message is empty or too long. message: " + message);
                OnEventNotify(SNResultCode.kPostFailed);
            }
            else
            {
                // message
                WWWForm form = new WWWForm();
                form.AddField("status", message);

                // header
                oauth_handler_.Clear();
                oauth_handler_.AddParameter("status", message);

                var headers = new Dictionary<string, string>();
                headers["Authorization"] = oauth_handler_.GenerateHeader(kPostUrl, "POST");

                // request
                WWW web = new WWW(kPostUrl, form.data, headers);
                yield return web;

                if (!string.IsNullOrEmpty(web.error))
                {
                    Debug.Log(string.Format("PostTweet - failed. {0}, {1}", web.error, web.text));
                    OnEventNotify(SNResultCode.kPostFailed);
                }
                else
                {
                    string error = Regex.Match(web.text, @"<error>([^&]+)</error>").Groups[1].Value;
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.Log("PostTweet - failed." + error);
                        OnEventNotify(SNResultCode.kPostFailed);
                    }
                    else
                    {
                        Debug.Log("PostTweet - succeeeded.");
                        OnEventNotify(SNResultCode.kPosted);
                    }
                }
            }
        }
        #endregion


        #region member variables
        // Twitter APIs for OAuth process
        private readonly string kRequestUrl = "https://api.twitter.com/oauth/request_token";
        private readonly string kOAuthUrl = "https://api.twitter.com/oauth/authenticate?oauth_token={0}";
        private readonly string kAccessUrl = "https://api.twitter.com/oauth/access_token";
        private readonly string kShowUrl = "https://api.twitter.com/1.1/users/show.json";
        private readonly string kFriendsIdsUrl = "https://api.twitter.com/1.1/friends/ids.json";
        private readonly string kLookupUrl = "https://api.twitter.com/1.1/users/lookup.json";
        private readonly string kPostUrl = "https://api.twitter.com/1.1/statuses/update.json";

        private readonly int kFriendLimitMax = 100;

        // Member variables.
        private OAuthHandler oauth_handler_;
        private string request_token_ = "";
        private bool auto_request_picture_ = true;
        #endregion
    }
}
