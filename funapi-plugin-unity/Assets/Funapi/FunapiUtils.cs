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

namespace Fun
{
    // Utility class
    internal class FunapiUtils
    {
#if !NO_UNITY
        // Gets local path
        public static string GetLocalDataPath
        {
            get
            {
                if (path_ == null)
                {
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
                }

                return path_;
            }
        }

        private static string path_ = null;
#endif
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


    // Timer
    internal class FunapiTimer
    {
        public void AddTimer (string name, float delay, bool loop, EventHandler callback, object param = null)
        {
            AddTimer(name, 0f, delay, loop, callback, param);
        }

        public void AddTimer (string name, float start, EventHandler callback, object param = null)
        {
            AddTimer(name, start, 0f, false, callback, param);
        }

        public void AddTimer (string name, float start, float delay, bool loop, EventHandler callback, object param = null)
        {
            if (callback == null)
            {
                DebugUtils.LogWarning("AddTimer - '{0}' timer's callback is null.", name);
                return;
            }

            if (pending_list_.ContainsKey(name))
            {
                DebugUtils.LogWarning("AddTimer - '{0}' timer already exists.", name);
                return;
            }

            if (timer_list_.ContainsKey(name))
            {
                if (!is_all_clear_ && !remove_list_.Contains(name))
                {
                    DebugUtils.LogWarning("AddTimer - '{0}' timer already exists.", name);
                    return;
                }
            }

            pending_list_.Add(name, new Event(name, start, delay, loop, callback, param));
            DebugUtils.DebugLog("AddTimer - '{0}' timer added.", name);
        }

        public void KillTimer (string name)
        {
            if (!timer_list_.ContainsKey(name) && !pending_list_.ContainsKey(name))
                return;

            if (pending_list_.ContainsKey(name))
            {
                pending_list_.Remove(name);
            }
            else if (!remove_list_.Contains(name))
            {
                remove_list_.Add(name);
            }
            else
            {
                return;
            }

            DebugUtils.DebugLog("KillTimer - '{0}' timer removed.", name);
        }

        public bool ContainTimer (string name)
        {
            return timer_list_.ContainsKey(name) || pending_list_.ContainsKey(name);
        }

        public void Clear ()
        {
            is_all_clear_ = true;
        }

        public void Update ()
        {
            if (is_all_clear_)
            {
                timer_list_.Clear();
                remove_list_.Clear();
                is_all_clear_ = false;
                return;
            }

            CheckRemoveList();

            // Adds timer
            if (pending_list_.Count > 0)
            {
                foreach (Event e in pending_list_.Values)
                {
                    timer_list_.Add(e.name, e);
                }

                pending_list_.Clear();
            }

            if (timer_list_.Count <= 0)
                return;

            // Updates timer
            float delta = FunapiManager.deltaTime;
            foreach (Event e in timer_list_.Values)
            {
                e.remaining -= delta;
                if (e.remaining <= 0f)
                {
                    if (remove_list_.Contains(e.name))
                        continue;

                    if (e.loop)
                        e.remaining = e.interval;
                    else
                        remove_list_.Add(e.name);

                    e.callback(e.param);
                }
            }

            CheckRemoveList();
        }

        // Removes timer
        private void CheckRemoveList ()
        {
            if (remove_list_.Count <= 0)
                return;

            foreach (string name in remove_list_)
            {
                if (timer_list_.ContainsKey(name))
                {
                    timer_list_.Remove(name);
                }
                else if (pending_list_.ContainsKey(name))
                {
                    pending_list_.Remove(name);
                }
            }

            remove_list_.Clear();
        }


        public delegate void EventHandler (object param);

        class Event
        {
            public string name;
            public bool loop;
            public float remaining;
            public float interval;
            public EventHandler callback;
            public object param;

            public Event (string name, float start, float delay, bool loop, EventHandler callback, object param)
            {
                this.name = name;
                this.loop = loop;
                this.interval = delay;
                this.remaining = start;
                this.callback = callback;
                this.param = param;
            }
        }


        private Dictionary<string, Event> timer_list_ = new Dictionary<string, Event>();
        private Dictionary<string, Event> pending_list_ = new Dictionary<string, Event>();
        private List<string> remove_list_ = new List<string>();
        private bool is_all_clear_ = false;
    }
}
