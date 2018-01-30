// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// protobuf
using funapi.service.multicast_message;


public partial class Tester : MonoBehaviour
{
    public abstract class Base
    {
        public abstract IEnumerator Start ();

        public void OnFinished ()
        {
            if (FinishedCallback != null)
                FinishedCallback();
        }

        public event Action FinishedCallback;
    }


    void Awake ()
    {
        FunapiEncryptor.public_key = encryptionPublicKey;
        //FunapiCompression.PerformanceTest();

        option_ = optionPopup.GetComponent<UIOption>();
        option_.Init();

        buttons_["create"] = GameObject.Find("ButtonCreateSession").GetComponent<Button>();
        buttons_["close"] = GameObject.Find("ButtonCloseSession").GetComponent<Button>();
        buttons_["session"] = GameObject.Find("ButtonSessionTest").GetComponent<Button>();
        buttons_["multicast"] = GameObject.Find("ButtonMulticastTest").GetComponent<Button>();
        buttons_["chatting"] = GameObject.Find("ButtonChattingTest").GetComponent<Button>();

        setButtonState(false);
    }

    public void OnToggleSession (GameObject panel)
    {
        if (panel != null)
            panel.SetActive(!panel.activeSelf);
    }

    public void OnToggleOthers (GameObject panel)
    {
        if (panel != null)
            panel.SetActive(!panel.activeSelf);
    }

    public void OnCreateSession ()
    {
        dashLog("Start Test");
        buttons_["create"].interactable = false;

        createSession();
    }

    public void OnCloseSession ()
    {
        closeSession();
    }

    public void OnSessionTest ()
    {
        dashLog("Session Test");
        buttons_["session"].interactable = false;

        StartCoroutine(tester_.Start());
    }

    void onSessionTestFinished ()
    {
        buttons_["session"].interactable = true;
    }

    public void OnMulticastTest ()
    {
        dashLog("Multicast Test");
        buttons_["multicast"].interactable = false;

        Multicast multicast = new Multicast(session_);
        multicast.FinishedCallback += delegate() {
            multicast = null;
            buttons_["multicast"].interactable = true;
        };

        StartCoroutine(multicast.Start());
    }

    public void OnChattingTest ()
    {
        dashLog("Chatting Test");
        buttons_["chatting"].interactable = false;

        Chatting chatting = new Chatting(session_);
        chatting.FinishedCallback += delegate() {
            chatting = null;
            buttons_["chatting"].interactable = true;
        };

        StartCoroutine(chatting.Start());
    }

    public void OnAnnounceTest ()
    {
        dashLog("Announce Test");

        Announce announce = new Announce();
        announce.FinishedCallback += delegate() {
            announce = null;
        };

        announce.Start(option_.serverAddress);
    }

    public void OnDownloadTest ()
    {
        dashLog("Download Test");

        Download download = new Download();
        download.FinishedCallback += delegate() {
            download = null;
        };

        download.Start(option_.serverAddress);
    }

    public void OnDebugLogTest ()
    {
        if (FunDebug.GetLogLength() <= 0)
        {
            FunDebug.Log("There are no logs or you should define 'ENABLE_SAVE_LOG' symbol.");
            return;
        }

        FunDebug.SaveLogs();
    }

    public void OnOption ()
    {
        if (optionPopup != null)
            optionPopup.SetActive(true);
    }

    public void OnClearLogs ()
    {
        logPanel.Clear();
    }


    void createSession ()
    {
        if (session_ == null || option_.bChanged)
        {
            // Create session
            SessionOption session_option = new SessionOption();
            session_option.sessionReliability = option_.sessionReliability;
            session_option.sendSessionIdOnlyOnce = option_.sendSessionIdOnlyOnce;

            session_ = FunapiSession.Create(option_.serverAddress, session_option);
            session_.SessionEventCallback += onSessionEvent;
            session_.TransportEventCallback += onTransportEvent;

            option_.bChanged = false;

            // Create tester
            tester_ = new Session(session_);
            tester_.FinishedCallback += onSessionTestFinished;
        }

        if (option_.connectTcp)
            tryConnect(TransportProtocol.kTcp);

        if (option_.connectUdp)
            tryConnect(TransportProtocol.kUdp);

        if (option_.connectHttp)
            tryConnect(TransportProtocol.kHttp);
    }

    void closeSession ()
    {
        if (session_ != null)
        {
            session_.Stop();
        }

        buttons_["create"].interactable = true;
        setButtonState(false);
    }

    void tryConnect (TransportProtocol protocol)
    {
        FunEncoding encoding = getEncoding(protocol, option_);
        TransportOption option = makeOption(protocol);
        ushort port = getPort(protocol);

        session_.Connect(protocol, encoding, port, option);
    }

    TransportOption makeOption (TransportProtocol protocol)
    {
        TransportOption option = null;

        if (protocol == TransportProtocol.kTcp)
        {
            TcpTransportOption tcp_option = new TcpTransportOption();
            tcp_option.Encryption = option_.tcpEncryption;
            tcp_option.AutoReconnect = option_.autoReconnect;
            tcp_option.DisableNagle = option_.disableNagle;

            if (option_.usePing)
                tcp_option.SetPing(1, 20, true);

            option = tcp_option;
        }
        else if (protocol == TransportProtocol.kUdp)
        {
            option = new TransportOption();
            option.Encryption = option_.udpEncryption;
        }
        else if (protocol == TransportProtocol.kHttp)
        {
            HttpTransportOption http_option = new HttpTransportOption();
            http_option.Encryption = option_.httpEncryption;
            http_option.HTTPS = option_.HTTPS;
            http_option.UseWWW = option_.useWWW;

            option = http_option;
        }

        if (protocol != TransportProtocol.kUdp)
            option.SequenceValidation = option_.sequenceValidation;

        option.ConnectionTimeout = 10f;

        return option;
    }

    static FunEncoding getEncoding(TransportProtocol protocol, UIOption option)
    {
        if (protocol == TransportProtocol.kTcp)
            return option.tcpEncoding;
        else if (protocol == TransportProtocol.kUdp)
            return option.udpEncoding;
        else if (protocol == TransportProtocol.kHttp)
            return option.httpEncoding;

        return FunEncoding.kJson;
    }

    ushort getPort (TransportProtocol protocol)
    {
        if (protocol == TransportProtocol.kTcp)
            return option_.tcpPort;
        else if (protocol == TransportProtocol.kUdp)
            return option_.udpPort;
        else if (protocol == TransportProtocol.kHttp)
            return option_.httpPort;

        return 0;
    }

    void dashLog (string text)
    {
        FunDebug.Log(string.Format("---------- {0} ----------", text));
    }

    void setButtonState (bool enable)
    {
        buttons_["close"].interactable = enable;
        buttons_["session"].interactable = enable;
        buttons_["multicast"].interactable = enable;
        buttons_["chatting"].interactable = enable;
    }

    void onSessionEvent (SessionEventType type, string sessionid)
    {
        if (type == SessionEventType.kOpened)
        {
            setButtonState(true);
        }
        else if (type == SessionEventType.kStopped)
        {
        }
    }

    void onTransportEvent (TransportProtocol protocol, TransportEventType type)
    {
        if (type == TransportEventType.kStarted)
        {
            if (!buttons_["close"].IsInteractable())
                setButtonState(true);
        }
    }

    static void onMulticastChannelList (FunEncoding encoding, object channel_list)
    {
        if (encoding == FunEncoding.kJson)
        {
            List<object> list = channel_list as List<object>;
            if (list.Count <= 0) {
                FunDebug.Log("Multicast - There are no channels.");
                return;
            }

            StringBuilder data = new StringBuilder();
            data.Append("Multicast - channel list\n");
            foreach (Dictionary<string, object> info in list)
            {
                data.AppendFormat("name:{0} members:{1}", info["_name"], info["_members"]);
                data.AppendLine();
            }
            FunDebug.Log(data.ToString());
        }
        else
        {
            List<FunMulticastChannelListMessage> list = channel_list as List<FunMulticastChannelListMessage>;
            if (list.Count <= 0) {
                FunDebug.Log("Multicast There are no channels.");
                return;
            }

            StringBuilder data = new StringBuilder();
            data.Append("Multicast - channel list\n");
            foreach (FunMulticastChannelListMessage info in list)
            {
                data.AppendFormat("name:{0} members:{1}", info.channel_name, info.num_members);
                data.AppendLine();
            }
            FunDebug.Log(data.ToString());
        }
    }


    static readonly int sendingCount = 1;
    static readonly string encryptionPublicKey = "0b8504a9c1108584f4f0a631ead8dd548c0101287b91736566e13ead3f008f5d";

    FunapiSession session_ = null;

    Session tester_ = null;

    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
    UIOption option_;

    public GameObject optionPopup;
    public UILogs logPanel;
}
