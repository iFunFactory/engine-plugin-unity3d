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

        updateButtonState();
    }

    void updateButtonState ()
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
        handler_.Disconnect();

        updateButtonState();
    }

    public void OnSendMessage ()
    {
        handler_.SendEchoMessage();
    }


    void tryConnect (TransportProtocol protocol)
    {
        FunDebug.Log("-------- Connect --------");

        FunapiTransport transport = null;
        if (network_ == null || network_.SessionReliability != with_session_reliability_.isOn)
        {
            handler_ = new TestNetwork();
            network_ = handler_.CreateNetwork(with_session_reliability_.isOn);

            // If you want to have a sequence number with your messages, set the 'SequenceNumberValidation' to true.
            // The server must also set the same value with this option.
            //network_.SequenceNumberValidation = true;

            // If you set this option and there is no response from the server, it disconnects from the server.
            // So this option is recommended for use with the ping.
            //network.ResponseTimeout = 10f;

            network_.StoppedAllTransportCallback += onStoppedAllTransport;

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
            transport.StartedCallback += onTransportStarted;

            if (protocol == TransportProtocol.kTcp)
            {
                // If you want to use the ping of client side. Set 'EnablePing' to true.
                // If you want to use the ping of server side. You don't have to anything.
                //transport.EnablePing = true;

                // You can turn the nagle option on/off. The default value is false.
                //((FunapiTcpTransport)transport).DisableNagle = true;
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                // If you want to use the UnityEngine.WWW for the HTTP transport,
                // or if you have trouble with the HttpWebRequest class.
                // (The HttpWebRequest may have blocking in the Unity Editor Windows version)
                // Set 'UseWWW' to true and then HTTP transport will use the WWW instead of the HttpWebRequest.
                //((FunapiHttpTransport)transport).UseWWW = true;
            }

            // You can add an extra server address.
            // If you want to add a server address for the HTTP transport, use HostHttp.
            //transport.AddServerList(new List<HostAddr>{
            //    new HostAddr("127.0.0.1", 8012), new HostAddr("127.0.0.1", 8012),
            //    new HostAddr("127.0.0.1", 8013), new HostAddr("127.0.0.1", 8018)
            //});
            //transport.AddServerList(new List<HostHttp>{
            //    new HostHttp("your.address", 8018, false), new HostHttp("your.address", 8018, false)
            //});
        }

        // If you prefer using specific Json implementation rather than Dictionary,
        // you need to register Json accessors to handle the Json implementation before the FunapiNetwork.Start().
        //FunapiMessage.JsonHelper = new YourJsonAccessorClass

        network_.Start();

        updateButtonState();
    }

    void onTransportStarted (TransportProtocol protocol)
    {
        updateButtonState();
    }

    void onStoppedAllTransport()
    {
        if (!network_.SessionReliability)
            network_ = null;

        updateButtonState();
    }


    // Please change this address to your server.
    const string kServerIp = "127.0.0.1";

    // Member variables.
    TestNetwork handler_ = null;
    FunapiNetwork network_ = null;

    // UI buttons
    Button button_start_;
    Toggle with_protobuf_;
    Toggle with_session_reliability_;
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
