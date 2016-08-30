// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class FunapiSessionTest : MonoBehaviour
{
    void Awake ()
    {
        GameObject.Find("ServerIP").GetComponent<Text>().text = kServerIp;
        with_session_reliability_ = GameObject.Find("ToggleSR").GetComponent<Toggle>();
        with_protobuf_ = GameObject.Find("ToggleProtobuf").GetComponent<Toggle>();

        buttons_["connect_tcp"] = GameObject.Find("ButtonTCP").GetComponent<Button>();
        buttons_["connect_udp"] = GameObject.Find("ButtonUDP").GetComponent<Button>();
        buttons_["connect_http"] = GameObject.Find("ButtonHTTP").GetComponent<Button>();
        buttons_["disconnect"] = GameObject.Find("ButtonSendMessage").GetComponent<Button>();
        buttons_["send"] = GameObject.Find("ButtonDisconnect").GetComponent<Button>();

        // If you prefer using specific Json implementation rather than Dictionary,
        // you need to register Json accessors to handle the Json implementation before the FunapiSession.Connect().
        //FunapiMessage.JsonHelper = new YourJsonAccessorClass;

        updateButtonState();
    }

    void updateButtonState ()
    {
        bool enable = session_ == null || !session_.Started;
        buttons_["connect_tcp"].interactable = enable;
        buttons_["connect_udp"].interactable = enable;
        buttons_["connect_http"].interactable = enable;

        enable = session_ != null && session_.Connected;
        buttons_["disconnect"].interactable = enable;
        buttons_["send"].interactable = enable;
    }

    public void OnConnectTCP ()
    {
        tryConnect(TransportProtocol.kTcp);
    }

    public void OnConnectUDP ()
    {
        tryConnect(TransportProtocol.kUdp);
    }

    public void OnConnectHTTP ()
    {
        tryConnect(TransportProtocol.kHttp);
    }

    public void OnDisconnect ()
    {
        if (!session_.Connected)
        {
            FunDebug.Log("You should connect first.");
            return;
        }

        session_.Close();
        session_ = null;

        updateButtonState();
    }

    public void OnSendMessage ()
    {
        if (message_helper_ != null)
            message_helper_.SendEchoMessage();
    }


    void tryConnect (TransportProtocol protocol)
    {
        FunDebug.Log("-------- Connect --------");

        FunEncoding encoding = with_protobuf_.isOn ? FunEncoding.kProtobuf : FunEncoding.kJson;

        session_ = FunapiSession.Create(kServerIp, with_session_reliability_.isOn);
        message_helper_ = new MessageHelper(session_, encoding);

        session_.SessionEventCallback += onSessionEvent;
        session_.TransportEventCallback += onTransportEvent;
        session_.TransportErrorCallback += onTransportError;

        ushort port = getPort(protocol, encoding);
        TransportOption option = makeOption(protocol);
        session_.Connect(protocol, encoding, port, option);

        updateButtonState();
    }

    TransportOption makeOption (TransportProtocol protocol)
    {
        TransportOption option = null;

        if (protocol == TransportProtocol.kTcp)
        {
            TcpTransportOption tcp_option = new TcpTransportOption();
            //tcp_option.AutoReconnect = true;

            // If you want to use the ping of client side. Call 'SetPing' function.
            // If you want to use the ping of server side. You don't have to anything.
            //tcp_option.SetPing(3, 20, true);

            // You can turn the nagle option on/off. The default value is false.
            //tcp_option.DisableNagle = true;

            option = tcp_option;
        }
        else if (protocol == TransportProtocol.kHttp)
        {
            HttpTransportOption http_option = new HttpTransportOption();

            // If you want to use the UnityEngine.WWW for the HTTP transport,
            // or if you have trouble with the HttpWebRequest class.
            // (The HttpWebRequest may have blocking in the Unity Editor Windows version)
            // Set 'use_www' to true and then HTTP transport will use the WWW instead of the HttpWebRequest.
            //http_option.UseWWW = true;

            option = http_option;
        }
        else
        {
            option = new TransportOption();
        }

        // If you want to have a sequence number with your messages, set the 'sequence_validation' to true.
        // The server must also set the same value with this option.
        //option.SequenceValidation = true;

        // If you want to use encryption, set the encryption type.
        // Please set the same encryption type as the encryption type of the server.
        // TCP protocol can use both the encryption types.
        //option.Encryption = EncryptionType.kIFunEngine1Encryption;
        //
        // In the case of UDP and HTTP, you can use only the kIFunEngine2Encryption type.
        //option.Encryption = EncryptionType.kIFunEngine2Encryption;

        // Connection timeout.
        option.ConnectionTimeout = 10f;

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

    void onSessionEvent (SessionEventType type, string session_id)
    {
        updateButtonState();
    }

    void onTransportEvent (TransportProtocol protocol, TransportEventType type)
    {
        if (session_ != null && !session_.Started)
            session_ = null;

        updateButtonState();
    }

    void onTransportError (TransportProtocol protocol, TransportError error)
    {
        updateButtonState();
    }


    // Please change this address to your server.
    const string kServerIp = "127.0.0.1";

    // Member variables.
    FunapiSession session_ = null;
    MessageHelper message_helper_ = null;

    // UI buttons
    Toggle with_protobuf_;
    Toggle with_session_reliability_;
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
