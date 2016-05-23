// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class FunapiNetworkTest : MonoBehaviour
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

        UpdateButtonState();
    }

    void UpdateButtonState ()
    {
        bool enable = network_ == null || !network_.Started;
        buttons_["connect_tcp"].interactable = enable;
        buttons_["connect_udp"].interactable = enable;
        buttons_["connect_http"].interactable = enable;

        enable = network_ != null && network_.Connected;
        buttons_["disconnect"].interactable = enable;
        buttons_["send"].interactable = enable;
    }

    public void OnConnectTCP ()
    {
        Connect(TransportProtocol.kTcp);
    }

    public void OnConnectUDP ()
    {
        Connect(TransportProtocol.kUdp);
    }

    public void OnConnectHTTP ()
    {
        Connect(TransportProtocol.kHttp);
    }

    public void OnDisconnect ()
    {
        handler_.Disconnect();

        if (!network_.SessionReliability)
            network_ = null;

        UpdateButtonState();
    }

    public void OnSendMessage ()
    {
        handler_.SendEchoMessage();
    }


    void Connect (TransportProtocol protocol)
    {
        FunDebug.Log("-------- Connect --------");

        FunapiTransport transport = null;
        if (network_ == null || network_.SessionReliability != with_session_reliability_.isOn)
        {
            handler_ = new TestNetwork();
            network_ = handler_.CreateNetwork(with_session_reliability_.isOn);

            // If you want to have sequence number with your messages, Set 'SequenceNumberValidation' to true.
            // The server also must set the same value with this option.
            //network.SequenceNumberValidation = true;

            // If you set this option, If there is no response from the server, It disconnects from the server.
            // So this option is recommended for use with the ping.
            //network.ResponseTimeout = 10f;

            network_.StoppedAllTransportCallback += OnStoppedAllTransport;

            FunEncoding encoding = with_protobuf_.isOn ? FunEncoding.kProtobuf : FunEncoding.kJson;
            transport = handler_.AddTransport(protocol, kServerIp, encoding);
        }
        else
        {
            if (!network_.HasTransport(protocol))
            {
                FunEncoding encoding = with_protobuf_.isOn ? FunEncoding.kProtobuf : FunEncoding.kJson;
                transport = handler_.AddTransport(protocol, kServerIp, encoding);
            }

            network_.SetDefaultProtocol(protocol);
        }

        if (network_ == null)
        {
            FunDebug.Log("Failed to create the network instance.");
            return;
        }

        if (transport != null)
        {
            transport.StartedCallback += new TransportEventHandler(OnTransportStarted);

            if (protocol == TransportProtocol.kTcp)
            {
                // If you want to use the ping of client side. Set 'EnablePing' to true.
                // If you want to use the ping of server side. You don't have to anything.
                //transport.EnablePing = true;

                // You can turn the nagle option on/off. The default value is false.
                //transport.DisableNagle = true;
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                // If you want to use the UnityEngine.WWW for the HTTP transport,
                // or you have trouble with the HttpWebRequest class.
                // (The HttpWebRequest may have blocking in the Unity Editor Windows version)
                // Set 'UseWWW' to true and then HTTP transport will use the WWW instead of the HttpWebRequest.
                //((FunapiHttpTransport)transport).UseWWW = true;
            }

            // If you want to use encryption, Set encryption type.
            // Please set the same encryption type as the encryption type of server.
            // TCP protocol can use both of two encryption types.
            //transport.SetEncryption(EncryptionType.kIFunEngine1Encryption);
            //
            // In the case of UDP and HTTP can use only kIFunEngine2Encryption type.
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);

            // If you prefer use specific Json implementation other than Dictionary,
            // you need to register json accessors to handle the Json implementation before FunapiNetwork::Start().
            //transport.JsonHelper = new YourJsonAccessorClass

            // You can add a extra server address.
            // If you want to add a server address for HTTP transport, use HostHttp.
            //transport.AddServerList(new List<HostAddr>{
            //    new HostAddr("127.0.0.1", 8012), new HostAddr("127.0.0.1", 8012),
            //    new HostAddr("127.0.0.1", 8013), new HostAddr("127.0.0.1", 8018)
            //});
            //transport.AddServerList(new List<HostHttp>{
            //    new HostHttp("your.address", 8018, false), new HostHttp("your.address", 8018, false)
            //});
        }

        network_.Start();

        UpdateButtonState();
    }

    void OnTransportStarted (TransportProtocol protocol)
    {
        UpdateButtonState();
    }

    void OnStoppedAllTransport()
    {
        UpdateButtonState();
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";

    // member variables.
    private Toggle with_protobuf_;
    private Toggle with_session_reliability_;

    private TestNetwork handler_ = null;
    private FunapiNetwork network_ = null;

    private Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
