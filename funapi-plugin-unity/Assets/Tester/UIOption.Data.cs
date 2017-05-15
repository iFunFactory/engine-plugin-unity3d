// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using UnityEngine;


public partial class UIOption
{
    public class Data
    {
        public void Init ()
        {
            serverAddress = "127.0.0.1";
            sessionReliability = false;
            sequenceValidation = false;
            connectTcp = true;
            tcpPort = 8012;
            tcpEncoding = 0;
            tcpEncryption = 0;
            autoReconnect = true;
            disableNagle = false;
            usePing = false;

            connectUdp = false;
            udpPort = 0;
            udpEncoding = 0;
            udpEncryption = 0;

            connectHttp = true;
            httpPort = 8018;
            httpEncoding = 0;
            httpEncryption = 0;
            HTTPS = false;
            useWWW = false;
        }

        public string serverAddress
        {
            get { return PlayerPrefs.GetString("serverAddress"); }
            set { PlayerPrefs.SetString("serverAddress", value); }
        }

        public bool sessionReliability
        {
            get { return PlayerPrefs.GetInt("sessionReliability") == 1; }
            set { PlayerPrefs.SetInt("sessionReliability", value ? 1 : 0); }
        }

        public bool sequenceValidation
        {
            get { return PlayerPrefs.GetInt("sequenceValidation") == 1; }
            set { PlayerPrefs.SetInt("sequenceValidation", value ? 1 : 0); }
        }

        public bool sendSessionIdOnlyOnce
        {
            get { return PlayerPrefs.GetInt("sendSessionIdOnlyOnce") == 1; }
            set { PlayerPrefs.SetInt("sendSessionIdOnlyOnce", value ? 1 : 0); }
        }

        public bool connectTcp
        {
            get { return PlayerPrefs.GetInt("connectTcp") == 1; }
            set { PlayerPrefs.SetInt("connectTcp", value ? 1 : 0); }
        }

        public int tcpPort
        {
            get { return PlayerPrefs.GetInt("tcpPort"); }
            set { PlayerPrefs.SetInt("tcpPort", value); }
        }

        public int tcpEncoding
        {
            get { return PlayerPrefs.GetInt("tcpEncoding"); }
            set { PlayerPrefs.SetInt("tcpEncoding", value); }
        }

        public int tcpEncryption
        {
            get { return PlayerPrefs.GetInt("tcpEncryption"); }
            set { PlayerPrefs.SetInt("tcpEncryption", value); }
        }

        public bool autoReconnect
        {
            get { return PlayerPrefs.GetInt("autoReconnect") == 1; }
            set { PlayerPrefs.SetInt("autoReconnect", value ? 1 : 0); }
        }

        public bool disableNagle
        {
            get { return PlayerPrefs.GetInt("disableNagle") == 1; }
            set { PlayerPrefs.SetInt("disableNagle", value ? 1 : 0); }
        }

        public bool usePing
        {
            get { return PlayerPrefs.GetInt("usePing") == 1; }
            set { PlayerPrefs.SetInt("usePing", value ? 1 : 0); }
        }

        public bool connectUdp
        {
            get { return PlayerPrefs.GetInt("connectUdp") == 1; }
            set { PlayerPrefs.SetInt("connectUdp", value ? 1 : 0); }
        }

        public int udpPort
        {
            get { return PlayerPrefs.GetInt("udpPort"); }
            set { PlayerPrefs.SetInt("udpPort", value); }
        }

        public int udpEncoding
        {
            get { return PlayerPrefs.GetInt("udpEncoding"); }
            set { PlayerPrefs.SetInt("udpEncoding", value); }
        }

        public int udpEncryption
        {
            get { return PlayerPrefs.GetInt("udpEncryption"); }
            set { PlayerPrefs.SetInt("udpEncryption", value); }
        }

        public bool connectHttp
        {
            get { return PlayerPrefs.GetInt("connectHttp") == 1; }
            set { PlayerPrefs.SetInt("connectHttp", value ? 1 : 0); }
        }

        public int httpPort
        {
            get { return PlayerPrefs.GetInt("httpPort"); }
            set { PlayerPrefs.SetInt("httpPort", value); }
        }

        public int httpEncoding
        {
            get { return PlayerPrefs.GetInt("httpEncoding"); }
            set { PlayerPrefs.SetInt("httpEncoding", value); }
        }

        public int httpEncryption
        {
            get { return PlayerPrefs.GetInt("httpEncryption"); }
            set { PlayerPrefs.SetInt("httpEncryption", value); }
        }

        public bool HTTPS
        {
            get { return PlayerPrefs.GetInt("HTTPS") == 1; }
            set { PlayerPrefs.SetInt("HTTPS", value ? 1 : 0); }
        }

        public bool useWWW
        {
            get { return PlayerPrefs.GetInt("useWWW") == 1; }
            set { PlayerPrefs.SetInt("useWWW", value ? 1 : 0); }
        }
    }
}
