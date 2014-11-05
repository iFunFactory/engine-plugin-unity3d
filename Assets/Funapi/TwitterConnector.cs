// Copyright (C) 2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Fun;

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
            OnLoggedIn();
        }
    }

    public override void Login()
    {
        Debug.Log("Request token.");
        StartCoroutine(GetRequestToken());
    }

    public void RequestAccess (string oauth_verifier)
    {
        StartCoroutine(GetAccessToken(oauth_verifier));
    }

    public override void Logout()
    {
    }

    public override void Post (string message)
    {
        Debug.Log("Post tweet. message: " + message);
        StartCoroutine(PostTweet(message));
    }
    #endregion


    #region internal implementation
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
        }
        else
        {
            string token = Regex.Match(web.text, @"oauth_token=([^&]+)").Groups[1].Value;
            string secret = Regex.Match(web.text, @"oauth_token_secret=([^&]+)").Groups[1].Value;

            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(secret))
            {
                Debug.Log(string.Format("GetRequestToken - succeeded.\n\ttoken: {0}\n\tsecret: {1}",
                                        token, secret));

                request_token_ = token;

                string url = string.Format(kOAuthUrl, token);
                Application.OpenURL(url);
            }
            else
            {
                Debug.Log("GetRequestToken - failed. " + web.text);
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
                Debug.Log("GetAccessToken - succeeded.\n");
                Debug.Log(string.Format("\tid: {0}\n\tname: {1}\n\ttoken: {2}\n\ttoken_secret: {3}",
                                        my_info_.id, my_info_.name, token, secret));

                oauth_handler_.AddParameter("oauth_token", token);
                oauth_handler_.AddParameter("oauth_token_secret", secret);

                EncryptedPlayerPrefs.SetString("user_id", my_info_.id);
                EncryptedPlayerPrefs.SetString("screen_name", my_info_.name);
                EncryptedPlayerPrefs.SetString("oauth_token", token);
                EncryptedPlayerPrefs.SetString("oauth_token_secret", secret);

                OnLoggedIn();
            }
            else
            {
                Debug.Log("GetAccessToken - failed. " + web.text);
            }
        }
    }

    IEnumerator PostTweet (string message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > 140)
        {
            Debug.Log("PostTweet - message is empty or too long. message: " + message);
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
            }
            else
            {
                string error = Regex.Match(web.text, @"<error>([^&]+)</error>").Groups[1].Value;
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.Log("PostTweet - failed. {0}" + error);
                }
                else
                {
                    Debug.Log("PostTweet - succeeeded.");
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
    private readonly string kPostUrl = "https://api.twitter.com/1.1/statuses/update.json";

    // Member variables.
    private OAuthHandler oauth_handler_;
    private string request_token_ = "";
    #endregion
}
