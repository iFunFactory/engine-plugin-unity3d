// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.IO;
using System.Collections.Generic;
#if !NO_UNITY
using UnityEngine;
#endif

// Utility classes
namespace Fun
{
    public class FunapiUpdater
    {
        protected void CreateUpdater ()
        {
#if !NO_UNITY
            if (game_object_ != null)
                return;

            game_object_ = new GameObject(GetType().ToString());
            if (game_object_ != null)
            {
                FunapiObject obj = game_object_.AddComponent(typeof(FunapiObject)) as FunapiObject;
                if (obj != null)
                {
                    funapi_object_ = obj;
                    obj.Updater = Update;
                    obj.OnQuit = OnQuit;
                }

                DebugUtils.DebugLog("'{0}' GameObject was created.", game_object_.name);
            }
#else
            funapi_object_ = new FunapiObject();
            funapi_object_.Updater = Update;
            funapi_object_.OnQuit = OnQuit;
#endif
        }

        protected void ReleaseUpdater ()
        {
#if !NO_UNITY
            if (game_object_ == null)
                return;

            DebugUtils.DebugLog("'{0}' GameObject was destroyed", game_object_.name);
            GameObject.Destroy(game_object_);
            game_object_ = null;
#endif
            funapi_object_ = null;
        }

#if NO_UNITY
        public void UpdateFrame ()
        {
            if (funapi_object_ != null)
                funapi_object_.Update();
        }
#endif

        protected virtual bool Update (float deltaTime)
        {
            event_.Update(deltaTime);
            return true;
        }

        protected virtual void OnQuit ()
        {
        }

        protected MonoBehaviour mono
        {
            get { return funapi_object_; }
        }

        protected ThreadSafeEventList event_list
        {
            get { return event_; }
        }


        // For use a MonoBehaviour
        class FunapiObject : MonoBehaviour
        {
#if !NO_UNITY
            void Awake ()
            {
                prev_ticks_ = DateTime.UtcNow.Ticks;
                deltaTime_ = 0.03f;

                DontDestroyOnLoad(gameObject);
            }

            void Update ()
            {
                long now = DateTime.UtcNow.Ticks;
                int milliseconds = (int)((now - prev_ticks_) / 10000);
                deltaTime_ = (float)milliseconds / 1000f;
                prev_ticks_ = now;

                Updater(deltaTime_);
            }

            void OnApplicationQuit()
            {
                OnQuit();
            }
#else
            public void Update ()
            {
                Updater(0.03f);
            }
#endif

            public Func<float, bool> Updater
            {
                set; private get;
            }

            public Action OnQuit
            {
                set; private get;
            }

#if !NO_UNITY
            private long prev_ticks_ = 0;
            private float deltaTime_ = 0f;
#endif
        }

#if !NO_UNITY
        private GameObject game_object_ = null;
#endif
        private FunapiObject funapi_object_ = null;
        private ThreadSafeEventList event_ = new ThreadSafeEventList();
    }


    public class ThreadSafeEventList
    {
        public int Add (Action callback, float start_delay = 0f)
        {
            return AddItem(callback, start_delay);
        }

        public int Add (Action callback, bool repeat, float repeat_time)
        {
            return AddItem(callback, 0f, repeat, repeat_time);
        }

        public int Add (Action callback, float start_delay, bool repeat, float repeat_time)
        {
            return AddItem(callback, start_delay, repeat, repeat_time);
        }

        public void Remove (int key)
        {
            if (key <= 0)
                return;

            lock (lock_)
            {
                if (!original_.ContainsKey(key) && !pending_.ContainsKey(key))
                    return;

                if (pending_.ContainsKey(key))
                {
                    pending_.Remove(key);
                }
                else if (!removing_.Contains(key))
                {
                    removing_.Add(key);
                }
                else
                {
                    return;
                }
            }
        }

        public bool ContainsKey (int key)
        {
            lock (lock_)
            {
                return original_.ContainsKey(key) || pending_.ContainsKey(key);
            }
        }

        public int Count
        {
            get
            {
                lock (lock_)
                {
                    return original_.Count + pending_.Count;
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                pending_.Clear();
                is_all_clear_ = true;
            }
        }

        public void Update (float deltaTime)
        {
            if (Count <= 0)
                return;

            lock (lock_)
            {
                if (is_all_clear_)
                {
                    original_.Clear();
                    removing_.Clear();
                    is_all_clear_ = false;
                    return;
                }

                CheckRemoveList();

                // Adds timer
                if (pending_.Count > 0)
                {
                    foreach (KeyValuePair<int, Item> p in pending_)
                    {
                        original_.Add(p.Key, p.Value);
                    }

                    pending_.Clear();
                }

                if (original_.Count <= 0)
                    return;

                foreach (KeyValuePair<int, Item> p in original_)
                {
                    if (removing_.Contains(p.Key))
                        continue;

                    Item item = p.Value;
                    if (item.remaining_time > 0f)
                    {
                        item.remaining_time -= deltaTime;
                        if (item.remaining_time > 0f)
                            continue;
                    }

                    if (item.repeat)
                        item.remaining_time = item.repeat_time;
                    else
                        removing_.Add(p.Key);

                    item.callback();
                }

                CheckRemoveList();
            }
        }


        // Gets new id
        private int GetNewId ()
        {
            do
            {
                ++next_id_;
                if (next_id_ >= int.MaxValue)
                    next_id_ = 1;
            }
            while (ContainsKey(next_id_));

            return next_id_;
        }

        // Adds a action
        private int AddItem (Action callback, float start_delay = 0f,
                             bool repeat = false, float repeat_time = 0f)
        {
            if (callback == null)
            {
                throw new ArgumentNullException ("callback");
            }

            lock (lock_)
            {
                int key = GetNewId();
                pending_.Add(key, new Item(callback, start_delay, repeat, repeat_time));
                return key;
            }
        }

        // Removes actions from remove list
        private void CheckRemoveList ()
        {
            lock (lock_)
            {
                if (removing_.Count <= 0)
                    return;

                foreach (int key in removing_)
                {
                    if (original_.ContainsKey(key))
                    {
                        original_.Remove(key);
                    }
                    else if (pending_.ContainsKey(key))
                    {
                        pending_.Remove(key);
                    }
                }

                removing_.Clear();
            }
        }


        class Item
        {
            public bool repeat;
            public float repeat_time;
            public float remaining_time;
            public Action callback;

            public Item (Action callback, float start_delay = 0f,
                         bool repeat = false, float repeat_time = 0f)
            {
                this.callback = callback;
                this.repeat = repeat;
                this.repeat_time = repeat_time;
                this.remaining_time = start_delay;
            }
        }


        // member variables.
        private static int next_id_ = 0;
        private object lock_ = new object();
        private Dictionary<int, Item> original_ = new Dictionary<int, Item>();
        private Dictionary<int, Item> pending_ = new Dictionary<int, Item>();
        private List<int> removing_ = new List<int>();
        private bool is_all_clear_ = false;
    }


    internal class FunapiUtils
    {
        // Gets local path
        public static string GetLocalDataPath
        {
            get
            {
                if (path_ == null)
                {
#if !NO_UNITY
                    if (Application.platform == RuntimePlatform.IPhonePlayer)
                    {
                        string path = Application.dataPath.Substring(0, Application.dataPath.Length - 5); // Strip "/Data" from path
                        path = path.Substring(0, path.LastIndexOf('/'));
                        path_ = path + "/Documents";
                    }
                    else if (Application.platform == RuntimePlatform.Android)
                    {
                        path_ = Application.persistentDataPath;
                    }
                    else
                    {
                        string path = Application.dataPath;
                        path_ = path.Substring(0, path.LastIndexOf('/'));
                    }
#else
                    path_ = "";
#endif
                }

                return path_;
            }
        }

        private static string path_ = null;
    }


    // Config file handler
    public class FunapiConfig
    {
        public static bool IsValid
        {
            get { return data_ != null; }
        }

        public static void Load (string path)
        {
            if (!File.Exists(path))
            {
                DebugUtils.Log("Can't find a config file. path: {0}", path);
                return;
            }

            string text = File.ReadAllText(path);
            DebugUtils.DebugLog("{0} >> {1}", path, text);

            Dictionary<string, object> json = Json.Deserialize(text) as Dictionary<string, object>;
            if (json == null)
            {
                DebugUtils.Log("Deserialize json failed. json: {0}", text);
                return;
            }

            data_ = json;
        }

        public static FunapiTransport CreateTransport (TransportProtocol protocol,
                                                       FunEncoding encoding = FunEncoding.kJson,
                                                       EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            if (data_ == null)
            {
                DebugUtils.Log("There's no config data. You should call FunapiConfig.Load first.");
                return null;
            }

            string str_protocol;
            if (protocol == TransportProtocol.kTcp)
                str_protocol = "tcp";
            else if (protocol == TransportProtocol.kUdp)
                str_protocol = "udp";
            else if (protocol == TransportProtocol.kHttp)
                str_protocol = "http";
            else
            {
                DebugUtils.Log("CreateTransport - Invalid protocol. protocol: {0}", protocol);
                return null;
            }

            string str_ip = string.Format("{0}_server_ip", str_protocol);
            string str_port = string.Format("{0}_server_port", str_protocol);
            if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
            {
                DebugUtils.Log("CreateTransport - Can't find values with '{0}'", str_protocol);
                return null;
            }

            string hostname_or_ip = data_[str_ip] as string;
            UInt16 port = Convert.ToUInt16(data_[str_port]);
            if (hostname_or_ip.Length <= 0 || port == 0)
            {
                DebugUtils.Log("CreateTransport - Invalid value. ip:{0} port:{1} encoding:{2}",
                               hostname_or_ip, port, encoding);
                return null;
            }

            if (protocol == TransportProtocol.kTcp)
            {
                FunapiTcpTransport transport = new FunapiTcpTransport(hostname_or_ip, port, encoding);

                if (data_.ContainsKey("disable_nagle"))
                    transport.DisableNagle = (bool)data_["disable_nagle"];

                if (encryption != EncryptionType.kDefaultEncryption)
                    transport.SetEncryption(encryption);

                return transport;
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                FunapiUdpTransport transport = new FunapiUdpTransport(hostname_or_ip, port, encoding);

                if (encryption != EncryptionType.kDefaultEncryption)
                    transport.SetEncryption(encryption);

                return transport;
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                bool with_https = false;
                if (data_.ContainsKey("http_with_secure"))
                    with_https = (bool)data_["http_with_secure"];

                FunapiHttpTransport transport = new FunapiHttpTransport(hostname_or_ip, port, with_https, encoding);

                if (encryption != EncryptionType.kDefaultEncryption)
                    transport.SetEncryption(encryption);

                return transport;
            }

            return null;
        }

        public static void GetDownloaderUrl (out string url)
        {
            url = "";

            if (data_ == null)
            {
                DebugUtils.Log("There's no config data. You should call FunapiConfig.Load first.");
                return;
            }

            string str_ip = "download_server_ip";
            string str_port = "download_server_port";
            if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
            {
                DebugUtils.Log("CreateDownloader - Can't find values for downloader.");
                return;
            }

            string hostname_or_ip = data_[str_ip] as string;
            UInt16 port = Convert.ToUInt16(data_[str_port]);
            if (hostname_or_ip.Length <= 0 || port == 0)
            {
                DebugUtils.Log("CreateDownloader - Invalid value. ip:{0} port:{1}",
                               hostname_or_ip, port);
                return;
            }

            url = string.Format("http://{0}:{1}", hostname_or_ip, port);
        }

        public static string AnnouncementUrl
        {
            get
            {
                if (announcement_url_ == null)
                {
                    if (data_ == null)
                    {
                        DebugUtils.Log("There's no config data. You should call FunapiConfig.Load first.");
                        return "";
                    }

                    string str_ip = "announcement_server_ip";
                    string str_port = "announcement_server_port";
                    if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
                    {
                        DebugUtils.Log("AnnouncementUrl - Can't find values for announcement.");
                        return "";
                    }

                    string hostname_or_ip = data_[str_ip] as string;
                    UInt16 port = Convert.ToUInt16(data_[str_port]);
                    if (hostname_or_ip.Length <= 0 || port == 0)
                    {
                        DebugUtils.Log("CreateDownloader - Invalid value. ip:{0} port:{1}",
                                       hostname_or_ip, port);
                        return "";
                    }

                    bool with_https = false;
                    if (data_.ContainsKey("announcement_with_secure"))
                        with_https = (bool)data_["announcement_with_secure"];

                    announcement_url_ = string.Format("{0}://{1}:{2}", with_https ? "https" : "http", hostname_or_ip, port);
                    DebugUtils.Log("Announcement url : {0}", announcement_url_);
                }

                return announcement_url_;
            }
        }

        public static int PingInterval
        {
            get
            {
                if (ping_interval_ < 0)
                {
                    if (data_ == null)
                    {
                        DebugUtils.Log("There's no config data. You should call FunapiConfig.Load first.");
                        ping_interval_ = 0;
                        return 0;
                    }

                    string str_interval = "ping_interval_second";
                    if (!data_.ContainsKey(str_interval))
                    {
                        DebugUtils.Log("GetPingInterval - Can't find a interval value of ping.");
                        ping_interval_ = 0;
                        return 0;
                    }

                    ping_interval_ = Convert.ToInt32(data_[str_interval]);
                }

                return ping_interval_;
            }
        }

        public static float PingTimeoutSeconds
        {
            get
            {
                if (ping_timeout_seconds_ < 0f)
                {
                    if (data_ == null)
                    {
                        DebugUtils.Log("There's no config data. You should call FunapiConfig.Load first.");
                        ping_timeout_seconds_ = 0f;
                        return 0f;
                    }

                    string str_ping_timeout = "ping_timeout_second";
                    if (!data_.ContainsKey(str_ping_timeout))
                    {
                        DebugUtils.Log("PingTimeoutSeconds - Can't find a timeout value of ping.");
                        ping_timeout_seconds_ = 0f;
                        return 0f;
                    }

                    ping_timeout_seconds_ = Convert.ToInt32(data_[str_ping_timeout]);
                }

                return ping_timeout_seconds_;
            }
        }


        // static member variables
        private static Dictionary<string, object> data_ = null;
        private static string announcement_url_ = null;
        private static int ping_interval_ = -1;
        private static float ping_timeout_seconds_ = -1f;
    }

#if NO_UNITY
    public class MonoBehaviour
    {
        public void StartCoroutine (Action func)
        {
            func();
        }
    }
#endif
}
