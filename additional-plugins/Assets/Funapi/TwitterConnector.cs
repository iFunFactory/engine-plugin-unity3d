// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;


namespace Fun
{
    public class TwitterConnector : SocialNetwork
    {
        public void Init (string consumer_key, string consumer_secret)
        {
            FunDebug.DebugLog1("TwitterConnector.Init called.");

            OnEventCallback += new EventHandler(OnEventHandler);
            oauth_handler_ = new OAuthHandler(consumer_key, consumer_secret);

            string oauth_token = PlayerPrefs.GetString("oauth_token");
            string oauth_token_secret = PlayerPrefs.GetString("oauth_token_secret");
            if (!string.IsNullOrEmpty(oauth_token) && !string.IsNullOrEmpty(oauth_token_secret))
            {
                oauth_handler_.AddParameter("oauth_token", oauth_token);
                oauth_handler_.AddParameter("oauth_token_secret", oauth_token_secret);

                my_info_.id = PlayerPrefs.GetString("user_id");
                my_info_.name = PlayerPrefs.GetString("screen_name");

                FunDebug.Log("Logged in Twitter using saved token.");
                OnEventNotify(SNResultCode.kLoggedIn);
            }
            else
            {
                OnEventNotify(SNResultCode.kInitialized);
            }
        }

        public bool IsLoggedIn
        {
            get { return logged_in_; }
        }

        public bool AutoDownloadPicture
        {
            set { auto_request_picture_ = value; }
        }

        public void Login()
        {
            FunDebug.DebugLog1("Request Twitter access token.");
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
            FunDebug.DebugLog1("Post tweet. message: {0}", message);
            StartCoroutine(PostTweet(message));
        }


        void OnEventHandler (SNResultCode result)
        {
            switch (result)
            {
            case SNResultCode.kLoggedIn:
                logged_in_ = true;
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
                FunDebug.LogError("Failure in GetRequestToken: {0}\n{1}", web.error, web.text);
                OnEventNotify(SNResultCode.kLoginFailed);
            }
            else
            {
                string token = Regex.Match(web.text, @"oauth_token=([^&]+)").Groups[1].Value;
                string secret = Regex.Match(web.text, @"oauth_token_secret=([^&]+)").Groups[1].Value;

                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(secret))
                {
                    FunDebug.Log("Succeeded in getting access token of Twitter.\n" +
                                 "token: {0}\nsecret: {1}", token, secret);

                    request_token_ = token;

                    string url = string.Format(kOAuthUrl, token);
                    Application.OpenURL(url);
                }
                else
                {
                    FunDebug.LogError("Failure in GetRequestToken: {0}", web.text);
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
                FunDebug.LogError("Failure in GetAccessToken: {0}\n{1}", web.error, web.text);
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
                    FunDebug.Log("Succeeded in getting my information of Twitter.\n" +
                                 "id: {0}\nname: {1}\ntoken: {2}\ntoken_secret: {3}",
                                 my_info_.id, my_info_.name, token, secret);

                    oauth_handler_.AddParameter("oauth_token", token);
                    oauth_handler_.AddParameter("oauth_token_secret", secret);

                    PlayerPrefs.SetString("user_id", my_info_.id);
                    PlayerPrefs.SetString("screen_name", my_info_.name);
                    PlayerPrefs.SetString("oauth_token", token);
                    PlayerPrefs.SetString("oauth_token_secret", secret);

                    OnEventNotify(SNResultCode.kLoggedIn);
                }
                else
                {
                    FunDebug.LogError("Failure in GetAccessToken: {0}", web.text);
                    OnEventNotify(SNResultCode.kLoginFailed);
                }
            }
        }

        IEnumerator RequestMyInfo ()
        {
            oauth_handler_.Clear();
            oauth_handler_.AddParameter("user_id", MyId);

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = oauth_handler_.GenerateHeader(kShowUrl, "GET");

            string url = string.Format("{0}?user_id={1}", kShowUrl, MyId);
            WWW web = new WWW(url.ToString(), null, headers);
            yield return web;

            if (!string.IsNullOrEmpty(web.error))
            {
                FunDebug.LogError("Failure in RequestMyInfo: {0}", web.error);
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
                FunDebug.LogError("Failure in RequestFriendIds: {0}", web.error);
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
                FunDebug.LogError("Failure in RequestUserInfo: {0}", web.error);
                OnEventNotify(SNResultCode.kError);
            }
            else
            {
                List<object> list = Json.Deserialize(web.text) as List<object>;
                if (list.Count <= 0)
                {
                    FunDebug.LogError("RequestUserInfo - Invalid list data. List size is 0.");
                    yield break;
                }
                else
                {
                    lock (friend_list_)
                    {
                        friend_list_.Clear();

                        foreach (Dictionary<string, object> node in list)
                        {
                            UserInfo user = new UserInfo();
                            user.id = node["id_str"] as string;
                            user.name = node["screen_name"] as string;

                            url = node["profile_image_url"] as string;
                            user.url = url.Replace("_normal", "_bigger");

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
            }
        }

        IEnumerator PostTweet (string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length > 140)
            {
                FunDebug.LogError("PostTweet - message is empty or too long.\nmessage: {0}", message);
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
                    FunDebug.LogError("Failure in PostTweet: {0}\n{1}", web.error, web.text);
                    OnEventNotify(SNResultCode.kPostFailed);
                }
                else
                {
                    string error = Regex.Match(web.text, @"<error>([^&]+)</error>").Groups[1].Value;
                    if (!string.IsNullOrEmpty(error))
                    {
                        FunDebug.LogError("Failure in PostTweet(Regex.Match): {0}", error);
                        OnEventNotify(SNResultCode.kPostFailed);
                    }
                    else
                    {
                        FunDebug.Log("Post tweet succeeeded.");
                        OnEventNotify(SNResultCode.kPosted);
                    }
                }
            }
        }


        // Twitter APIs for OAuth process
        static readonly string kRequestUrl = "https://api.twitter.com/oauth/request_token";
        static readonly string kOAuthUrl = "https://api.twitter.com/oauth/authenticate?oauth_token={0}";
        static readonly string kAccessUrl = "https://api.twitter.com/oauth/access_token";
        static readonly string kShowUrl = "https://api.twitter.com/1.1/users/show.json";
        static readonly string kFriendsIdsUrl = "https://api.twitter.com/1.1/friends/ids.json";
        static readonly string kLookupUrl = "https://api.twitter.com/1.1/users/lookup.json";
        static readonly string kPostUrl = "https://api.twitter.com/1.1/statuses/update.json";

        static readonly int kFriendLimitMax = 100;

        // Member variables.
        OAuthHandler oauth_handler_;
        string request_token_ = "";
        bool auto_request_picture_ = true;
        bool logged_in_ = false;
    }
}
