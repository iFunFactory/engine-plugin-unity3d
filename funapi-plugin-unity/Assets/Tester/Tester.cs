// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
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
    public string serverAddress = "127.0.0.1";

    public bool sessionReliability = false;
    public bool sequenceValidation = false;
    public FunEncoding encoding = FunEncoding.kJson;
    public int sendingCount = 10;

    [Header("TCP Option")]
    public EncryptionType tcpEncryption = EncryptionType.kDefaultEncryption;
    public bool autoReconnect = false;
    public bool disableNagle = false;
    public bool usePing = false;

    [Header("UDP Option")]
    public EncryptionType udpEncryption = EncryptionType.kDefaultEncryption;

    [Header("HTTP Option")]
    public EncryptionType httpEncryption = EncryptionType.kDefaultEncryption;
    public bool useWWW = false;

    public abstract class Base
    {
        public abstract IEnumerator Start (params object[] param);

        public void OnFinished ()
        {
            if (FinishedCallback != null)
                FinishedCallback();

            FunDebug.Log("---------- Finished ----------");
        }

        public event Action FinishedCallback;
    }


    void Awake ()
    {
        GameObject.Find("ServerIP").GetComponent<Text>().text = serverAddress;

        buttons_["create"] = GameObject.Find("ButtonCreateSession").GetComponent<Button>();
        buttons_["close"] = GameObject.Find("ButtonCloseSession").GetComponent<Button>();
        buttons_["session"] = GameObject.Find("ButtonSessionTest").GetComponent<Button>();
        buttons_["multicast"] = GameObject.Find("ButtonMulticastTest").GetComponent<Button>();
        buttons_["chatting"] = GameObject.Find("ButtonChattingTest").GetComponent<Button>();

        setButtonState(false);
    }

    public void OnCreateSession ()
    {
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
        session_test_ = true;

        Session session = new Session();
        session.FinishedCallback += delegate() {
            session = null;
            session_test_ = false;
            buttons_["session"].interactable = true;
        };

        StartCoroutine(session.Start(session_, encoding, sendingCount));
    }

    public void OnMulticastTest ()
    {
        dashLog("Multicast Test");
        buttons_["multicast"].interactable = false;

        Multicast multicast = new Multicast();
        multicast.FinishedCallback += delegate() {
            multicast = null;
            buttons_["multicast"].interactable = true;
        };

        StartCoroutine(multicast.Start(session_, encoding, sendingCount));
    }

    public void OnChattingTest ()
    {
        dashLog("Chatting Test");
        buttons_["chatting"].interactable = false;

        Chatting chatting = new Chatting();
        chatting.FinishedCallback += delegate() {
            chatting = null;
            buttons_["chatting"].interactable = true;
        };

        StartCoroutine(chatting.Start(session_, encoding, sendingCount));
    }

    public void OnAnnounceTest ()
    {
        dashLog("Announce Test");

        Announce announce = new Announce();
        announce.FinishedCallback += delegate() {
            announce = null;
            dashLog("Finished");
        };

        announce.Start(serverAddress);
    }

    public void OnDownloadTest ()
    {
        dashLog("Download Test");

        Download download = new Download();
        download.FinishedCallback += delegate() {
            download = null;
            dashLog("Finished");
        };

        download.Start(serverAddress);
    }

    public void OnDebugLogTest ()
    {
        if (FunDebug.GetLogLength() <= 0)
        {
            FunDebug.Log("There are no logs or you should turn on 'ENABLE_SAVE_LOG' define.");
            return;
        }

        FunDebug.SaveLogs();
    }


    void createSession ()
    {
        if (session_ != null)
            return;

        session_ = FunapiSession.Create(serverAddress, sessionReliability);
        session_.SessionEventCallback += onSessionEvent;
        session_.TransportEventCallback += onTransportEvent;

        tryConnect(TransportProtocol.kTcp);
        tryConnect(TransportProtocol.kUdp);
        tryConnect(TransportProtocol.kHttp);
    }

    void closeSession ()
    {
        if (session_ != null)
        {
            session_.Stop();
            session_ = null;
        }

        buttons_["create"].interactable = true;
        setButtonState(false);
    }

    void tryConnect (TransportProtocol protocol)
    {
        TransportOption option = makeOption(protocol);
        ushort port = getPort(protocol, encoding);

        session_.Connect(protocol, encoding, port, option);
    }

    TransportOption makeOption (TransportProtocol protocol)
    {
        TransportOption option = null;

        if (protocol == TransportProtocol.kTcp)
        {
            TcpTransportOption tcp_option = new TcpTransportOption();
            tcp_option.Encryption = tcpEncryption;
            tcp_option.AutoReconnect = autoReconnect;
            tcp_option.DisableNagle = disableNagle;

            if (usePing)
                tcp_option.SetPing(1, 20, true);

            option = tcp_option;
        }
        else if (protocol == TransportProtocol.kUdp)
        {
            option = new TransportOption();
            option.Encryption = udpEncryption;
        }
        else if (protocol == TransportProtocol.kHttp)
        {
            HttpTransportOption http_option = new HttpTransportOption();
            http_option.Encryption = httpEncryption;
            http_option.UseWWW = useWWW;

            option = http_option;
        }

        option.ConnectionTimeout = 10f;
        option.SequenceValidation = sequenceValidation;

        return option;
    }

    ushort getPort (TransportProtocol protocol, FunEncoding encoding)
    {
        ushort port = 0;
        if (protocol == TransportProtocol.kTcp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8012 : 8022);
        else if (protocol == TransportProtocol.kUdp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8013 : 8023);
        else if (protocol == TransportProtocol.kHttp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8018 : 8028);

        return port;
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
            if (!session_test_)
                closeSession();
        }
    }

    void onTransportEvent (TransportProtocol protocol, TransportEventType type)
    {
    }

    static void onMulticastChannelList (FunEncoding encoding, object channel_list)
    {
        if (encoding == FunEncoding.kJson)
        {
            List<object> list = channel_list as List<object>;
            if (list.Count <= 0) {
                FunDebug.Log("[Channel List] There are no channels.");
                return;
            }

            StringBuilder data = new StringBuilder();
            data.Append("[Channel List]\n");
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
                FunDebug.Log("[Channel List] There are no channels.");
                return;
            }

            StringBuilder data = new StringBuilder();
            data.Append("[Channel List]\n");
            foreach (FunMulticastChannelListMessage info in list)
            {
                data.AppendFormat("name:{0} members:{1}", info.channel_name, info.num_members);
                data.AppendLine();
            }
            FunDebug.Log(data.ToString());
        }
    }


    FunapiSession session_ = null;
    bool session_test_ = false;
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
