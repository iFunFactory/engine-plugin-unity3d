// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Fun
{
    // Utility class
    public class FunapiUtils
    {
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
    }


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
                Debug.Log(String.Format("Can't find a config file. path: {0}", path));
                return;
            }

            string text = File.ReadAllText(path);
            DebugUtils.Log(String.Format("{0} >> {1}", path, text));

            Dictionary<string, object> json = Json.Deserialize(text) as Dictionary<string, object>;
            if (json == null)
            {
                Debug.Log("Deserialize json failed. json: " + text);
                return;
            }

            data_ = json;
        }

        public static FunapiTransport CreateTransport (TransportProtocol protocol,
                                                       FunEncoding encoding = FunEncoding.kJson)
        {
            if (data_ == null)
            {
                Debug.Log("There's no config data. You should call FunapiConfig.Load first.");
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
                Debug.Log(String.Format("CreateTransport - Invalid protocol. protocol: {0}", protocol));
                return null;
            }

            string str_ip = String.Format("{0}_server_ip", str_protocol);
            string str_port = String.Format("{0}_server_port", str_protocol);
            if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
            {
                Debug.Log(String.Format("CreateTransport - Can't find values with '{0}'", str_protocol));
                return null;
            }

            string hostname_or_ip = data_[str_ip] as string;
            UInt16 port = Convert.ToUInt16(data_[str_port]);
            if (hostname_or_ip.Length <= 0 || port == 0)
            {
                Debug.Log(String.Format("CreateTransport - Invalid value. ip:{0} port:{1} encoding:{2}",
                                        hostname_or_ip, port, encoding));
                return null;
            }

            if (protocol == TransportProtocol.kTcp)
            {
                FunapiTcpTransport transport = new FunapiTcpTransport(hostname_or_ip, port, encoding);

                if (data_.ContainsKey("disable_nagle"))
                    transport.DisableNagle = (bool)data_["disable_nagle"];

                return transport;
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                return new FunapiUdpTransport(hostname_or_ip, port, encoding);
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                bool with_https = false;
                if (data_.ContainsKey("http_with_secure"))
                    with_https = (bool)data_["http_with_secure"];

                return new FunapiHttpTransport(hostname_or_ip, port, with_https, encoding);
            }

            return null;
        }

        public static FunapiMulticastClient CreateMulticasting (FunEncoding encoding, bool session_reliability)
        {
            if (data_ == null)
            {
                Debug.Log("There's no config data. You should call FunapiConfig.Load first.");
                return null;
            }

            string str_ip = "multicast_server_ip";
            string str_port = "multicast_server_port";
            if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
            {
                Debug.Log("CreateMulticasting - Can't find values for multicasting");
                return null;
            }

            string hostname_or_ip = data_[str_ip] as string;
            UInt16 port = Convert.ToUInt16(data_[str_port]);
            if (hostname_or_ip.Length <= 0 || port == 0)
            {
                Debug.Log(String.Format("CreateMulticasting - Invalid value. ip:{0} port:{1} encoding:{2}",
                                        hostname_or_ip, port, encoding));
                return null;
            }

            FunapiMulticastClient multicast = new FunapiMulticastClient(encoding);
            multicast.Connect(hostname_or_ip, port, session_reliability);

            return multicast;
        }

        public static FunapiHttpDownloader CreateDownloader (string target_path)
        {
            if (data_ == null)
            {
                Debug.Log("There's no config data. You should call FunapiConfig.Load first.");
                return null;
            }

            string str_ip = "download_server_ip";
            string str_port = "download_server_port";
            if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
            {
                Debug.Log("CreateDownloader - Can't find values for downloader");
                return null;
            }

            string hostname_or_ip = data_[str_ip] as string;
            UInt16 port = Convert.ToUInt16(data_[str_port]);
            if (hostname_or_ip.Length <= 0 || port == 0)
            {
                Debug.Log(String.Format("CreateDownloader - Invalid value. ip:{0} port:{1}",
                                        hostname_or_ip, port));
                return null;
            }

            FunapiHttpDownloader downloader = new FunapiHttpDownloader(target_path);
            downloader.StartDownload(string.Format("http://{0}:{1}", hostname_or_ip, port));

            return downloader;
        }

        public static string GetAnnouncementUrl ()
        {
            if (data_ == null)
            {
                Debug.Log("There's no config data. You should call FunapiConfig.Load first.");
                return null;
            }

            string str_ip = "announcement_server_ip";
            string str_port = "announcement_server_port";
            if (!data_.ContainsKey(str_ip) || !data_.ContainsKey(str_port))
            {
                Debug.Log("CreateDownloader - Can't find values for announcement");
                return null;
            }

            string hostname_or_ip = data_[str_ip] as string;
            UInt16 port = Convert.ToUInt16(data_[str_port]);
            if (hostname_or_ip.Length <= 0 || port == 0)
            {
                Debug.Log(String.Format("CreateDownloader - Invalid value. ip:{0} port:{1}",
                                        hostname_or_ip, port));
                return null;
            }

            bool with_https = false;
            if (data_.ContainsKey("announcement_with_secure"))
                with_https = (bool)data_["announcement_with_secure"];

            return string.Format("{0}://{1}:{2}", with_https ? "https" : "http", hostname_or_ip, port);
        }


        // static member variables
        private static Dictionary<string, object> data_ = null;
    }
}
