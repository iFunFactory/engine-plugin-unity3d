// Copyright 2013 iFunFactory Inc. All Rights Reserved.
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
            address = "127.0.0.1";
            port = 8012;
            protocol = 0;
            encoding = 0;
        }

        public string address
        {
            get { return PlayerPrefs.GetString("address"); }
            set { PlayerPrefs.SetString("address", value); }
        }

        public int port
        {
            get { return PlayerPrefs.GetInt("port"); }
            set { PlayerPrefs.SetInt("port", value); }
        }

        public int protocol
        {
            get { return PlayerPrefs.GetInt("protocol"); }
            set { PlayerPrefs.SetInt("protocol", value); }
        }

        public int encoding
        {
            get { return PlayerPrefs.GetInt("encoding"); }
            set { PlayerPrefs.SetInt("encoding", value); }
        }
    }
}
