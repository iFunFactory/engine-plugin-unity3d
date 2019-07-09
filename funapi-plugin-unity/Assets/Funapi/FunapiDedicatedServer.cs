// Copyright 2017 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

#if !NO_UNITY
#if !FUNAPI_DEDICATED_SERVER
#pragma warning disable 67
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Threading;

namespace Fun
{
    public class FunapiDedicatedServer : MonoBehaviour
    {
        static readonly string kInstanceName = "FunDedicatedServer";
        static FunapiDedicatedServer instance_ = null;
        static bool use_old_version_ = false;

        static FunapiDedicatedServer instance
        {
            get
            {
                if (instance_ == null)
                {
                    GameObject obj = GameObject.Find(kInstanceName);
                    if (obj == null)
                    {
                        obj = new GameObject(kInstanceName);
                        obj.AddComponent<FunapiDedicatedServer>();

                        DontDestroyOnLoad(obj);
                    }

                    instance_ = obj.GetComponent<FunapiDedicatedServer>();
                }

                return instance_;
            }
        }

        private FunapiDedicatedServer () {}

        [System.Obsolete("This property is deprecated. Please use 'void Start(string version)' instead. Please note that this property will be removed in June 2019.")]
        public static string version { private get; set; }

        public static bool isServer { get; private set; }

        public static bool isActive { get; private set; }

        public static int serverPort { get; private set; }


#if FUNAPI_DEDICATED_SERVER
        [System.Obsolete("This function is deprecated. Please use 'void Start(string version)' instead. Please note that this funtion will be removed in June 2019.")]
        public static bool Init ()
        {
            use_old_version_ = true;

            string commandLine = System.Environment.CommandLine;
            if (!commandLine.Contains("-RunDedicatedServer"))
                return true;

            return instance.readCommandLineArgs();
        }

        public static void Start (string version)
        {
            string commandLine = System.Environment.CommandLine;
            if (commandLine.Contains("-RunDedicatedServer"))
            {
                if (!instance.setVersion(version) || !instance.readCommandLineArgs())
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.Exit(0);
#else
                    Application.Quit();
#endif
                    return;
                }
                isActive = true;

                instance.onStart();
            }
        }

        [System.Obsolete("This function is deprecated. Please use 'void Start(string version)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void Start ()
        {
            if (!isServer || isActive)
                return;

            isActive = true;

            instance.onStart();
        }

        [System.Obsolete("This function is deprecated. Please use 'void Ready(Action<object> callback)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void Ready ()
        {
            instance.httpPost("ready");
        }

        public static void SendReady (Action<int, string> callback)
        {
            instance.httpPost("ready", null, callback);
        }

        [System.Obsolete("This function is deprecated. Please note that this funtion will be removed in June 2019.")]
        public static void Stop ()
        {
            isActive = false;
        }

        public static string GetUserDataJsonString (string uid)
        {
            return instance.getUserData(uid);
        }

        public static string GetMatchDataJsonString ()
        {
            return instance.getMatchData();
        }

        public static bool AuthUser (string uid, string token)
        {
            return instance.authUser(uid, token);
        }

        [System.Obsolete("This function is deprecated. Please use 'void SendJoined(string uid, Action<object> callback)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void SendJoined (string uid)
        {
            instance.httpPost("joined", instance.jsonStringWithUID(uid));
        }

        [System.Obsolete("This function is deprecated. Please use 'void SendLeft(string uid, Action<object> callback)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void SendLeft (string uid)
        {
            instance.httpPost("left", instance.jsonStringWithUID(uid));
        }

        [System.Obsolete("This function is deprecated. Please use 'void SendCustomCallback (string json_string, Action<object> callback)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void SendCustomCallback (string json_string)
        {
            instance.httpPost("callback", json_string);
        }

        [System.Obsolete("This function is deprecated. Please use 'void SendResult (string json_string, Action<object> callback)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void SendResult (string json_string)
        {
            instance.httpPost("result", json_string);
        }

        [System.Obsolete("This function is deprecated. Please use 'void SendGameState (string json_string, Action<object> callback)' instead. Please note that this funtion will be removed in June 2019.")]
        public static void SendGameState (string json_string)
        {
            instance.httpPost("state", json_string);
        }

        public static void SendJoined (string uid, Action<int, string> callback)
        {
            instance.httpPost("joined", instance.jsonStringWithUID(uid), null, callback);
        }

        public static void SendLeft (string uid, Action<int, string> callback)
        {
            instance.httpPost("left", instance.jsonStringWithUID(uid), null, callback);
        }

        public static void SendCustomCallback (string json_string, Action<int, string> callback)
        {
            instance.httpPost("callback", json_string, null, callback);
        }

        public static void SendResult (string json_string, Action<int, string> callback)
        {
            instance.httpPost("result", json_string, null, callback);
        }

        public static void SendGameState (string json_string, Action<int, string> callback)
        {
            instance.httpPost("state", json_string, null, callback);
        }

        bool setVersion(string version)
        {
            Regex regex = new Regex(@"^[0-9]+[.][0-9]+[.][0-9]+[.][0-9]+$");
            if(!regex.IsMatch(version))
            {
                FunDebug.LogWarning("The dedicated server version must be 'x.x.x.x' format.");
                return false;
            }

            Dictionary<string, object> json = new Dictionary<string, object>();
            json["version"] = version;

            version_ = json_helper_.Serialize(json);

            return true;
        }

        bool readCommandLineArgs ()
        {
            isServer = true;

            Dictionary<string, string> arg_list = new Dictionary<string, string>();
            string [] args = System.Environment.GetCommandLineArgs();
            bool sendVersion = false;

            foreach (string n in args)
            {
                bool not_user_arg = n.Contains(kServerVersion) || n.Contains(kManagerServer) ||
                                    n.Contains(kHeartbeat) || n.Contains(kPort) ||
                                    n.Contains(kMatchId) || n.Contains("RunDedicatedServer") ||
                                    n.Contains("batchmode") || n.Contains("nographics");
                if (!not_user_arg)
                {
                    user_cmd_options_.Add(n);
                }

                if (n.StartsWith("-") && n.Contains("="))
                {
                    int index = n.IndexOf("=");
                    string key = n.Substring(1, index - 1);
                    string value = n.Substring(index + 1, n.Length - index - 1);
                    arg_list.Add(key, value);
                    FunDebug.Log("Commandline argument - key:{0} value:{1}", key, value);
                }
                else if (n.Contains(kServerVersion))
                {
                    sendVersion = true;
                }
            }

            user_cmd_options_.RemoveAt(0); // Remove running path.
            foreach (string cmd in user_cmd_options_)
            {
                FunDebug.Log("User command : {0}", cmd);
            }

            if (arg_list.ContainsKey(kManagerServer))
            {
                server_url_ = string.Format("http://{0}/", arg_list[kManagerServer]);
                server_url_with_match_id_ = string.Format("{0}match/{1}/", server_url_, arg_list[kMatchId]);
            }
            else
            {
                FunDebug.LogError("'{0}' parameter is required.", kManagerServer);
                return false;
            }

            if (sendVersion)
            {
                instance.httpPostSync(CmdVersion, use_old_version_ ? version : version_, delegate (object obj)
                {
                    FunDebug.Log("Dedicated Server - Sent the version. ({0})", version_);
                });

                return false;
            }

            if (arg_list.ContainsKey(kHeartbeat))
            {
                if (!float.TryParse(arg_list[kHeartbeat], out heartbeat_seconds_))
                    heartbeat_seconds_ = 0f;

                if (heartbeat_seconds_ == 0f)
                {
                    FunDebug.LogWarning("'{0}' value must be greater than zero and lesser than 60 seconds.", kHeartbeat);
                    return false;
                }
            }

            if (arg_list.ContainsKey(kPort))
            {
                int port = 0;
                if (int.TryParse(arg_list[kPort], out port))
                    serverPort = port;
            }

            return true;
        }

        IEnumerator updatePendingUsers ()
        {
            yield return new WaitForSeconds(update_pending_seconds_);

            while (isActive)
            {
                FunDebug.Log("Send a 'pending_users' request to host manager.");

                httpPost ("pending_users", delegate (object obj)
                {
                    if (obj != null)
                    {
                        updateData(obj as Dictionary<string, object>);
                    }
                });

                yield return new WaitForSeconds(update_pending_seconds_);
            }
        }

        IEnumerator sendHeartbeat ()
        {
            yield return new WaitForSeconds(heartbeat_seconds_);

            while (isActive)
            {
                FunDebug.LogDebug("Send a heart beat to host manager.");

                httpPost("heartbeat");
                yield return new WaitForSeconds(heartbeat_seconds_);
            }
        }


        void onStart ()
        {
            httpGet ("", delegate (object obj)
            {
                if (obj != null)
                {
                    updateData(obj as Dictionary<string, object>);
                }

                if (StartCallback != null)
                {
                    StartCallback();
                }
            });

            if (heartbeat_seconds_ > 0)
            {
                StartCoroutine(sendHeartbeat());
            }

            StartCoroutine(updatePendingUsers());
        }

        bool authUser (string uid, string token)
        {
            lock (lock_user_data_)
            {
                if (users_.ContainsKey(uid))
                {
                    if (users_[uid] == token)
                        return true;
                }
            }

            return false;
        }

        string getUserData (string uid)
        {
            lock (lock_user_data_)
            {
                if (user_data_.ContainsKey(uid))
                    return user_data_[uid];
            }

            return "";
        }

        string getMatchData ()
        {
            lock (lock_match_data)
            {
                return match_data_;
            }
        }

        string jsonStringWithUID (string uid)
        {
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["uid"] = uid;

            return json_helper_.Serialize(json);
        }

        void updateData (Dictionary<string, object> data)
        {
            if (data.ContainsKey("match_data"))
            {
                lock (lock_match_data)
                {
                    List<object> match_data = data["match_data"] as List<object>;
                    if (match_data == null)
                    {
                        match_data = new List<object>();
                        match_data.Add(data["match_data"]);
                    }
                    for (int i = 0; i < match_data.Count; ++i)
                    {
                        string json_string;
                        if (match_data[i] is string)
                        {
                            json_string = match_data[i] as string;
                        }
                        else
                        {
                            json_string = json_helper_.Serialize(match_data[i]);
                        }

                        if (json_string.Length > 0)
                        {
                            if (match_data_ != json_string)
                            {
                                match_data_ = json_string;

                                if (MatchDataCallback != null)
                                {
                                    MatchDataCallback(json_string);
                                }
                            }
                        }
                    }
                }
            }

            if (data.ContainsKey("users"))
            {
                lock (lock_user_data_)
                {
                    List<object> users = data["users"] as List<object>;

                    List<object> user_data = null;
                    if (data.ContainsKey("user_data"))
                        user_data = data["user_data"] as List<object>;

                    for (int i = 0; i < users.Count; ++i)
                    {
                        Dictionary<string, object> user = users[i] as Dictionary<string, object>;

                        string uid = "";
                        string token = "";

                        if (user.ContainsKey("uid"))
                            uid = user["uid"] as string;

                        if (user.ContainsKey("token"))
                            token = user["token"] as string;

                        FunDebug.Log("Update user info - uid:{0} token:{1}", uid, token);

                        if (uid.Length > 0 && token.Length > 0)
                            users_.Add(uid, token);

                        if (user_data != null && i < user_data.Count)
                        {
                            string json_string;
                            if (user_data[i] is string)
                                json_string = user_data[i] as string;
                            else
                                json_string = json_helper_.Serialize(user_data[i]);

                            if (uid.Length > 0 && json_string.Length > 0)
                            {
                                if (!user_data_.ContainsKey(uid) || json_string != user_data_[uid])
                                {
                                    user_data_[uid] = json_string;

                                    if (UserDataCallback != null)
                                        UserDataCallback(uid, json_string);
                                }
                            }
                        }
                    }
                }
            }
        }


        bool needMatchId (string command)
        {
            return command != CmdVersion;
        }

        void httpGet (string command, Action<object> callback = null)
        {
            webRequest("GET", command, "", callback, null);
        }

        void httpPost (string command, Action<object> callback = null, Action<int, string> user_callback = null)
        {
            webRequest("POST", command, "", callback, user_callback);
        }

        void httpPost (string command, string data, Action<object> callback = null, Action<int, string> user_callback = null)
        {
            webRequest("POST", command, data, callback, user_callback);
        }

        void httpPostSync (string command, string data, Action<object> callback = null) // for send version only
        {
            string url = server_url_ + command;

            // Request
            HttpWebRequest web_request = (HttpWebRequest)WebRequest.Create(url);
            web_request.Method = "POST";
            web_request.ContentType = "application/octet-stream";

            Request request = new Request();
            request.web_request = web_request;
            request.callback = callback;
            request.command = command;

           try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
                ArraySegment<byte> body = new ArraySegment<byte>(bytes);
                web_request.ContentLength = body.Count;
                request.body = body;

                Stream stream = web_request.GetRequestStream();
                stream.Write(request.body.Array, 0 , request.body.Count);
                stream.Close();

                request.web_response = (HttpWebResponse)web_request.GetResponse();
                request.web_request = null;

                if (request.web_response.StatusCode == HttpStatusCode.OK)
                {
                    request.read_stream = request.web_response.GetResponseStream();
                    StreamReader sr = new StreamReader(request.read_stream);

                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(sr.ReadToEnd());
                    request.body = new ArraySegment<byte>(buffer);

                    webRequestCallback(request);

                    request.read_stream.Close();
                    request.web_response.Close();
                }
                else
                {
                    FunDebug.LogError("Host manager response failed. status:{0}",
                                      request.web_response.StatusDescription);
                }
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if (we != null && we.Status == WebExceptionStatus.ConnectFailure)
                {
                    onRequestFailed(request);
                }
                else if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                    (e is ObjectDisposedException || e is NullReferenceException))
                {
                    FunDebug.LogDebug("Dedicated Server - httpPostServerVersionSync operation has been cancelled.");
                }
            }
        }

        void webRequest (string method, string command, string data, Action<object> callback, Action<int, string> user_callback)
        {
            if (!isActive && needMatchId(command))
                return;

            string url = (needMatchId(command) ? server_url_with_match_id_ : server_url_) + command;
            FunDebug.Log("FunapiDedicatedServer.webRequest called.\n  {0} {1}", method, url);

            // Request
            HttpWebRequest web_request = (HttpWebRequest)WebRequest.Create(url);
            web_request.Method = method;
            web_request.ContentType = "application/octet-stream";

            Request request = new Request();
            request.web_request = web_request;
            request.callback = callback;
            request.user_callback = user_callback;
            request.command = command;

            if (method == "POST")
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
                ArraySegment<byte> body = new ArraySegment<byte>(bytes);
                web_request.ContentLength = body.Count;
                request.body = body;

                web_request.BeginGetRequestStream(new AsyncCallback(requestStreamCb), request);
            }
            else
            {
                IAsyncResult result = web_request.BeginGetResponse(new AsyncCallback(responseCb), request);
                ThreadPool.RegisterWaitForSingleObject(
                        result.AsyncWaitHandle, new WaitOrTimerCallback(TimeoutCallback), request,
                        heartbeat_seconds_ == 0 ? default_timeout_ : (int)(heartbeat_seconds_ / 2f) * 1000, true);
            }
        }

        void requestStreamCb (IAsyncResult ar)
        {
            try
            {
                Request request = (Request)ar.AsyncState;
                Stream stream = request.web_request.EndGetRequestStream(ar);

                ArraySegment<byte> body = request.body;
                if (body.Count > 0)
                    stream.Write(body.Array, 0, body.Count);
                stream.Close();

                IAsyncResult result = request.web_request.BeginGetResponse(new AsyncCallback(responseCb), request);
                ThreadPool.RegisterWaitForSingleObject(
                        result.AsyncWaitHandle, new WaitOrTimerCallback(TimeoutCallback), request,
                        heartbeat_seconds_ == 0 ? default_timeout_ : (int)(heartbeat_seconds_ / 2f) * 1000, true);
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if (we != null && we.Status == WebExceptionStatus.ConnectFailure)
                {
                    onRequestFailed((Request)ar.AsyncState);
                }
                else if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                    (e is ObjectDisposedException || e is NullReferenceException))
                {
                    // When Stop is called HttpWebRequest.EndGetRequestStream may return an Exception
                    FunDebug.LogDebug("Dedicated Server - Request operation has been cancelled.");
                }
            }
        }

        void responseCb (IAsyncResult ar)
        {
            try
            {
                Request request = (Request)ar.AsyncState;
                if (request.was_aborted)
                {
                    FunDebug.Log("Dedicated Server - Response callback. Request aborted.");
                    return;
                }

                request.web_response = (HttpWebResponse)request.web_request.EndGetResponse(ar);
                request.web_request = null;

                if (request.web_response.StatusCode == HttpStatusCode.OK)
                {
                    byte[] header = request.web_response.Headers.ToByteArray();
                    string str_header = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
                    string[] lines = str_header.Replace("\r", "").Split('\n');

                    int length = 0;

                    foreach (string n in lines)
                    {
                        if (n.Length > 0)
                        {
                            string[] tuple = n.Split(kHeaderSeparator, StringSplitOptions.RemoveEmptyEntries);
                            string key = tuple[0].ToLower();
                            if (key == "content-length" && tuple.Length >= 2)
                            {
                                length = Convert.ToInt32(tuple[1]);
                                break;
                            }
                        }
                    }

                    byte[] buffer = new byte[length];
                    request.body = new ArraySegment<byte>(buffer);

                    request.read_stream = request.web_response.GetResponseStream();
                    request.read_stream.BeginRead(buffer, 0, length, new AsyncCallback(readCb), request);
                }
                else
                {
                    FunDebug.LogError("Host manager response failed. status:{0}",
                                      request.web_response.StatusDescription);
                }
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if (we != null && we.Status == WebExceptionStatus.ConnectFailure)
                {
                    onRequestFailed((Request)ar.AsyncState);
                }
                else if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                    (e is ObjectDisposedException || e is NullReferenceException))
                {
                    // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                    FunDebug.LogDebug("Dedicated server request operation has been cancelled.");
                }
            }
        }

        void readCb (IAsyncResult ar)
        {
            try
            {
                Request request = (Request)ar.AsyncState;
                int nRead = request.read_stream.EndRead(ar);
                if (nRead > 0)
                {
                    request.read_offset += nRead;
                    request.read_stream.BeginRead(request.body.Array, request.read_offset,
                                                  request.body.Count - request.read_offset,
                                                  new AsyncCallback(readCb), request);
                }
                else
                {
                    if (request.web_response == null)
                    {
                        FunDebug.LogError("Host manager response failed.");
                        return;
                    }

                    webRequestCallback(request);

                    request.read_stream.Close();
                    request.web_response.Close();
                }
            }
            catch (Exception e)
            {
                if (e is ObjectDisposedException || e is NullReferenceException)
                {
                    FunDebug.LogDebug("Dedicated server request operation has been cancelled.");
                }
            }
        }

        void webRequestCallback(Request request)
        {
            int error_code = -1;
            string error_desc = "";
            object data = null;

            if (request.failed)
            {
                error_code = 28;
                error_desc = "Connection timed out / Connection refused";
            }
            else
            {
                bool invalid = false;
                string invalid_message = "";

                string str = System.Text.Encoding.UTF8.GetString(request.body.Array, 0, request.body.Count);
                object obj = json_helper_.Deserialize(str);

                Dictionary<string, object> body = obj as Dictionary<string, object>;

                if (body.ContainsKey("status"))
                {
                    if ((body["status"] as string).ToLower() == "ok")
                    {
                        error_code = 0;
                        error_desc = "";
                        data = body.ContainsKey("data") ? body["data"] : body;
                    }
                    else
                    {
                        error_code = -1;
                        error_desc = body.ContainsKey("error") ? body["error"] as string : "";
                        data = body;
                    }
                }
                else if (body.ContainsKey("error"))
                {
                    Dictionary<string, object> err_data = body["error"] as Dictionary<string, object>;
                    if (err_data.ContainsKey("code"))
                    {
                        error_code = Convert.ToInt32(err_data["code"]);
                    }
                    else
                    {
                        invalid = true;
                        invalid_message = "Dedicated Server - Response data doesn't have \"error.code\" attribute.";
                    }

                    if (error_code == 0)
                    {
                        if (body.ContainsKey("data"))
                        {
                            error_desc = "";
                            data = body["data"];
                        }
                        else
                        {
                            invalid = true;
                            invalid_message = "Dedicated Server - Response data doesn't have \"data\" attribute.";
                        }
                    }
                    else
                    {
                        error_desc = err_data.ContainsKey("desc") ? err_data["desc"] as string : "";
                    }
                }
                else
                {
                    invalid = true;
                    invalid_message = "Dedicated Server - Response data doesn't have essential attribute.";
                }

                if (invalid)
                {
                    FunDebug.LogWarning(invalid_message);
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.Exit(0);
#else
                    Application.Quit();
#endif
                    return;
                }
            }

            if (request.callback != null)
            {
                if ( error_code == 0)
                {
                    request.callback(data);
                }
                else
                {
                    FunDebug.LogWarning("Request failed. error : {0}", error_desc);
                }
            }
            if (request.user_callback != null)
            {
                request.user_callback(error_code, error_desc);
            }
        }

        void TimeoutCallback(object state, bool timeout)
        {
            if (timeout)
            {
                Request request = (Request)state;
                if (request.web_request != null)
                {
                    request.web_request.Abort();
                }

                onRequestFailed(request);
            }
        }

        void onRequestFailed(Request request)
        {
            request.failed = true;

            if (request.command == CmdVersion)
            {
                FunDebug.LogWarning("Failed to check a dedicated server version. Please check the host manager.");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit();
#endif
                return;
            }
            else if (request.command == "heartbeat")
            {
                FunDebug.LogWarning("Failed to request heartbeat.");
                heartbeat_timeout_count_++;
                if (heartbeat_timeout_count_ >= heartbeat_retry_threshold_)
                {
                    if (DisconnectedCallback != null && isActive)
                    {
                        DisconnectedCallback();
                        isActive = false;
                    }
                }
            }
            else
            {
                webRequestCallback(request);
            }
        }

        static public List<string> user_cmd_options
        {
            get
            {
                return instance.user_cmd_options_;
            }
        }

        class Request
        {
            public string command = null;
            public HttpWebRequest web_request = null;
            public HttpWebResponse web_response = null;
            public Stream read_stream = null;
            public bool was_aborted = false;
            public ArraySegment<byte> body;
            public int read_offset = 0;
            public Action<object> callback = null;
            public Action<int, string> user_callback = null;
            public bool failed = false;
        }

        static readonly string kServerVersion = "FunapiVersion";
        static readonly string kManagerServer = "FunapiManagerServer";
        static readonly string kMatchId = "FunapiMatchID";
        static readonly string kHeartbeat = "FunapiHeartbeat";
        static readonly string kPort = "port";
        static readonly string CmdVersion = "server/version";

        static readonly string[] kHeaderSeparator = { ":", "\n" };

        object lock_user_data_ = new object();
        Dictionary<string, string> users_ = new Dictionary<string, string>();
        Dictionary<string, string> user_data_ = new Dictionary<string, string>();
        object lock_match_data = new object();
        string match_data_ = "";
        List<string> user_cmd_options_ = new List<string>();
        static JsonAccessor json_helper_ = new DictionaryJsonAccessor();

        string server_url_ = "";
        string server_url_with_match_id_ = "";
        float heartbeat_seconds_ = 0f;
        const int heartbeat_retry_threshold_ = 2;
        int heartbeat_timeout_count_ = 0;
        const int default_timeout_ = 10000; // HttpWebRequest timeout default value
        float update_pending_seconds_ = 5f;
        string version_ = null;
#else
        public static bool Init() { return true; }

        public static void Start() { }

        public static void Start (string version) { }

        public static void Ready() { }

        public static void SendReady (Action<int, string> callback) { }

        public static void Stop() { }

        public static void SendJoined(string uid) { }

        public static void SendJoined(string uid, Action<int, string> callback) { }

        public static void SendLeft(string uid) { }

        public static void SendLeft(string uid, Action<int, string> callback) { }

        public static void SendCustomCallback(string json_string) { }

        public static void SendCustomCallback(string json_string, Action<int, string> callback) { }

        public static void SendResult(string json_string) { }

        public static void SendResult(string json_string, Action<int, string> callback) { }

        public static void SendGameState(string json_string) { }

        public static void SendGameState(string json_string, Action<int, string> callback) { }

        public static string GetUserDataJsonString(string uid) { return ""; }

        public static string GetMatchDataJsonString() { return ""; }

        public static bool AuthUser(string uid, string token) { return false; }
#endif

        public static event Action StartCallback;
        public static event Action<string, string> UserDataCallback;   // uid, json string
        public static event Action<string> MatchDataCallback;          // json string
        public static event Action DisconnectedCallback;
    }
}
#endif