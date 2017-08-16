// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;


namespace Fun
{
    public class OAuthHandler
    {
        public OAuthHandler (string consumer_key, string consumer_secret)
        {
            params_[kOAuthVersion] = "1.0";
            params_[kOAuthConsumerKey] = consumer_key;
            params_[kOAuthConsumerSecret] = consumer_secret;
            params_[kOAuthSignatureMethod] = "HMAC-SHA1";
            params_[kOAuthTokenSecret] = "";
        }

        public OAuthHandler (string consumer_key, string consumer_secret,
                             string token, string token_secret)
            : this(consumer_key, consumer_secret)
        {
            params_[kOAuthToken] = token;
            params_[kOAuthTokenSecret] = token_secret;
        }

        public void AddParameter (string key, string value)
        {
            params_[key] = value;
        }

        public void Clear ()
        {
            // removes added parameters
            List<string> remove = new List<string>();
            foreach (var item in params_)
            {
                if (!oauth_parameters.Contains(item.Key) && !except_parameters.Contains(item.Key))
                    remove.Add(item.Key);
            }

            foreach (string key in remove)
            {
                params_.Remove(key);
            }
        }

        public string GenerateHeader (string url, string method)
        {
            resetNew();

            // create a signature
            var parameters = (from p in params_
                              where !except_parameters.Contains(p.Key)
                              orderby p.Key, p.Value
                              select p);

            string param_string = "";
            foreach (var item in parameters)
            {
                if (param_string.Length > 0)
                    param_string += "&";

                param_string += string.Format("{0}={1}",
                                              urlEncode(item.Key), urlEncode(item.Value));
            }

            string base_string = string.Format("{0}&{1}&{2}",
                                               urlEncode(method), urlEncode(url),
                                               urlEncode(param_string));

            string key_string = string.Format("{0}&{1}",
                                              urlEncode(params_[kOAuthConsumerSecret]),
                                              urlEncode(params_[kOAuthTokenSecret]));

            HMACSHA1 hmacsha1 = new HMACSHA1(Encoding.UTF8.GetBytes(key_string));
            byte[] bytes = hmacsha1.ComputeHash(Encoding.UTF8.GetBytes(base_string));
            string signature = Convert.ToBase64String(bytes);

            // oauth string
            parameters = (from p in params_
                         where oauth_parameters.Contains(p.Key)
                         orderby p.Key, urlEncode(p.Value)
                         select p);

            string header = "OAuth ";
            foreach (var item in parameters)
            {
                if (item.Key == kOAuthVersion)
                    header += string.Format("{0}=\"{1}\"", item.Key, item.Value);
                else
                    header += string.Format("{0}=\"{1}\",", item.Key, item.Value);

                if (item.Key == kOAuthNonce)
                    header += string.Format("{0}=\"{1}\",", kOAuthSignature, urlEncode(signature));
            }

            return header;
        }

        void resetNew ()
        {
            // nonce
            params_[kOAuthNonce] = System.Guid.NewGuid().ToString().Replace("-","");

            // timestamp
            TimeSpan time = System.DateTime.UtcNow - epoch_time_;
            params_[kOAuthTimeStamp] = Convert.ToInt64(time.TotalSeconds).ToString();
        }

        string urlEncode (string value)
        {
            string encode_string = "";

            foreach (char c in value)
            {
                if (kUnreservedChars.IndexOf(c) != -1)
                    encode_string += c;
                else
                {
                    // char is 2bytes (Unicode)
                    foreach (byte n in Encoding.UTF8.GetBytes(c.ToString()))
                    {
                        encode_string += string.Format("%{0:X2}", n);
                    }
                }
            }

            return encode_string;
        }


        // Parameter-related constants
        static readonly string kOAuthVersion = "oauth_version";
        static readonly string kOAuthNonce = "oauth_nonce";
        static readonly string kOAuthTimeStamp = "oauth_timestamp";
        static readonly string kOAuthSignatureMethod = "oauth_signature_method";
        static readonly string kOAuthConsumerKey = "oauth_consumer_key";
        static readonly string kOAuthConsumerSecret = "oauth_consumer_secret";
        static readonly string kOAuthToken = "oauth_token";
        static readonly string kOAuthTokenSecret = "oauth_token_secret";
        static readonly string kOAuthVerifier = "oauth_verifier";
        static readonly string kOAuthSignature = "oauth_signature";
        static readonly string kUnreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

        static readonly DateTime epoch_time_ = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        static readonly string[] except_parameters = new[]
        {
            kOAuthConsumerSecret,
            kOAuthTokenSecret,
            kOAuthSignature
        };

        static readonly string[] oauth_parameters = new[]
        {
            kOAuthConsumerKey,
            kOAuthNonce,
            kOAuthSignatureMethod,
            kOAuthTimeStamp,
            kOAuthToken,
            kOAuthVerifier,
            kOAuthVersion
        };

        Dictionary<string, string> params_ = new Dictionary<string, string>();
    }
}
