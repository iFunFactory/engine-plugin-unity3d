// Copyright 2017 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;


namespace Fun
{
    public class FunapiDedicatedServer : MonoBehaviour
    {
        private FunapiDedicatedServer () {}

        public static string version { private get; set; }

        public static bool isServer { get; private set; }

        public static bool isActive { get; private set; }

        public static int serverPort { get; private set; }


        public static bool Init ()
        {
            string commandLine = System.Environment.CommandLine;
            if (!commandLine.Contains("-RunDedicatedServer"))
                return true;

            return instance.readCommandLineArgs();
        }

        public static void Start ()
        {
            if (!isServer || isActive)
                return;

            isActive = true;

            instance.onStart();
        }

        public static void Ready ()
        {
            instance.httpPost("ready");
        }

        public static void Stop ()
        {
            isActive = false;
        }

        public static string GetUserDataJsonString (string uid)
        {
            return instance.getUserData(uid);
        }

        public static bool AuthUser (string uid, string token)
        {
            return instance.authUser(uid, token);
        }

        public static void SendJoined (string uid)
        {
            instance.httpPost("joined", instance.jsonStringWithUID(uid));
        }

        public static void SendLeft (string uid)
        {
            instance.httpPost("left", instance.jsonStringWithUID(uid));
        }

        public static void SendCustomCallback (string json_string)
        {
            instance.httpPost("callback", json_string);
        }

        public static void SendResult (string json_string)
        {
            instance.httpPost("result", json_string);
        }


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

        bool readCommandLineArgs ()
        {
            isServer = true;

            Dictionary<string, string> arg_list = new Dictionary<string, string>();
            string [] args = System.Environment.GetCommandLineArgs();
            bool sendVersion = false;

            foreach (string n in args)
            {
                if (n.StartsWith("-") && n.Contains("="))
                {
                    int index = n.IndexOf("=");
                    string key = n.Substring(1, index - 1);
                    string value = n.Substring(index + 1, n.Length - index - 1);
                    arg_list.Add(key, value);
                    FunDebug.DebugLog1("command argument - key:{0} value:{1}", key, value);
                }
                else if (n.Contains(kServerVersion))
                {
                    sendVersion = true;
                }
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
                if (string.IsNullOrEmpty(version))
                {
                    FunDebug.LogWarning("Need to set the dedicated server version.");
                }
                else
                {
                    instance.httpPost(CmdVersion, version, delegate (object obj)
                    {
                        FunDebug.Log("Sent the dedicated server version. ({0})", version);
                    });
                }

                return false;
            }

            if (arg_list.ContainsKey(kHeartbeat))
            {
                if (!float.TryParse(arg_list[kHeartbeat], out heartbeat_seconds_))
                    heartbeat_seconds_ = 0f;
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
                FunDebug.DebugLog2("Send a 'pending_users' request to the manager server.");

                httpPost ("pending_users", delegate (object obj)
                {
                    updateData(obj as Dictionary<string, object>);
                });

                yield return new WaitForSeconds(update_pending_seconds_);
            }
        }

        IEnumerator sendHeartbeat ()
        {
            yield return new WaitForSeconds(heartbeat_seconds_);

            while (isActive)
            {
                FunDebug.DebugLog2("Send a heart beat to the manager server.");

                httpPost("heartbeat");
                yield return new WaitForSeconds(heartbeat_seconds_);
            }
        }


        void onStart ()
        {
            httpGet ("", delegate (object obj)
            {
                Dictionary<string, object> body = obj as Dictionary<string, object>;
                updateData(body["data"] as Dictionary<string, object>);
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

        string jsonStringWithUID (string uid)
        {
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["uid"] = uid;

            return FunapiMessage.JsonHelper.Serialize(json);
        }

        void updateData (Dictionary<string, object> data)
        {
            if (data.ContainsKey("match_data"))
            {
                string json_string;
                if (data["match_data"] is string)
                    json_string = data["match_data"] as string;
                else
                    json_string = FunapiMessage.JsonHelper.Serialize(data["match_data"]);

                if (json_string.Length > 0)
                {
                    if (MatchDataCallback != null)
                        MatchDataCallback(json_string);
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

                        FunDebug.DebugLog1("user - uid:{0} token:{1}", uid, token);

                        if (uid.Length > 0 && token.Length > 0)
                            users_.Add(uid, token);

                        if (user_data != null && i < user_data.Count)
                        {
                            string json_string;
                            if (user_data[i] is string)
                                json_string = user_data[i] as string;
                            else
                                json_string = FunapiMessage.JsonHelper.Serialize(user_data[i]);

                            if (uid.Length > 0 && json_string.Length > 0)
                            {
                                if (!user_data_.ContainsKey(uid) || json_string != user_data_[uid])
                                {
                                    user_data_[uid] = json_string;
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
            webRequest("GET", command, "", callback);
        }

        void httpPost (string command, Action<object> callback = null)
        {
            webRequest("POST", command, "", callback);
        }

        void httpPost (string command, string data, Action<object> callback = null)
        {
            webRequest("POST", command, data, callback);
        }

        void webRequest (string method, string command, string data, Action<object> callback)
        {
            if (!isActive && needMatchId(command))
                return;

            string url = (needMatchId(command) ? server_url_with_match_id_ : server_url_) + command;
            FunDebug.DebugLog1("FunapiDedicatedServer.webRequest called.\n  {0} {1}", method, url);

            // Request
            HttpWebRequest web_request = (HttpWebRequest)WebRequest.Create(url);
            web_request.Method = method;
            web_request.ContentType = "application/octet-stream";

            Request request = new Request();
            request.web_request = web_request;
            request.callback = callback;

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
                web_request.BeginGetResponse(new AsyncCallback(responseCb), request);
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

                request.web_request.BeginGetResponse(new AsyncCallback(responseCb), request);
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                    (e is ObjectDisposedException || e is NullReferenceException))
                {
                    // When Stop is called HttpWebRequest.EndGetRequestStream may return a Exception
                    FunDebug.DebugLog1("Dedicated server request operation has been cancelled.");
                    return;
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
                    FunDebug.Log("Dedicated manager server response callback - request aborted.");
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
                    FunDebug.LogError("Dedicated manager server response failed. status:{0}",
                                      request.web_response.StatusDescription);
                }
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                    (e is ObjectDisposedException || e is NullReferenceException))
                {
                    // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                    FunDebug.DebugLog1("Dedicated server request operation has been cancelled.");
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
                        FunDebug.LogError("Dedicated manager server response failed.");
                        return;
                    }

                    if (request.callback != null)
                    {
                        string str = System.Text.Encoding.UTF8.GetString(request.body.Array, 0, request.body.Count);
                        object body = FunapiMessage.JsonHelper.Deserialize(str);

                        request.callback(body);
                    }

                    request.read_stream.Close();
                    request.web_response.Close();
                }
            }
            catch (Exception e)
            {
                if (e is ObjectDisposedException || e is NullReferenceException)
                {
                    FunDebug.DebugLog1("Dedicated server request operation has been cancelled.");
                }
            }
        }


        class Request
        {
            public HttpWebRequest web_request = null;
            public HttpWebResponse web_response = null;
            public Stream read_stream = null;
            public bool was_aborted = false;
            public ArraySegment<byte> body;
            public int read_offset = 0;
            public Action<object> callback = null;
        }

        public delegate void UserDataHandler (string uid, string json_string);
        public delegate void MatchDataHandler (string json_string);

        public static event UserDataHandler UserDataCallback;
        public static event MatchDataHandler MatchDataCallback;

        static readonly string kInstanceName = "Fun.DedicatedServer";
        static readonly string kServerVersion = "FunapiVersion";
        static readonly string kManagerServer = "FunapiManagerServer";
        static readonly string kMatchId = "FunapiMatchID";
        static readonly string kHeartbeat = "FunapiHeartbeat";
        static readonly string kPort = "port";
        static readonly string CmdVersion = "server/version";

        static readonly string[] kHeaderSeparator = { ":", "\n" };

        static FunapiDedicatedServer instance_ = null;

        object lock_user_data_ = new object();
        Dictionary<string, string> users_ = new Dictionary<string, string>();
        Dictionary<string, string> user_data_ = new Dictionary<string, string>();

        string server_url_ = "";
        string server_url_with_match_id_ = "";
        float heartbeat_seconds_ = 0f;
        float update_pending_seconds_ = 5f;
    }
}
