// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.


using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// protobuf
using plugin_messages;
using funapi.network.fun_message;


public class Test2 : MonoBehaviour
{
    void Awake ()
    {
        Transform transform = GameObject.Find("Options").transform;
        Dropdown[] dnlist = transform.GetComponentsInChildren<Dropdown>();
        Dictionary<string, Dropdown> drops = new Dictionary<string, Dropdown>();
        foreach (Dropdown d in dnlist)
            drops[d.name] = d;

        protocol1 = new OptionProtocol(drops["Protocol1"]);
        encoding1 = new OptionEncoding(drops["Encoding1"]);
        protocol2 = new OptionProtocol(drops["Protocol2"]);
        encoding2 = new OptionEncoding(drops["Encoding2"]);
        protocol3 = new OptionProtocol(drops["Protocol3"]);
        encoding3 = new OptionEncoding(drops["Encoding3"]);
    }

    public void OnButtonConnect ()
    {
        if (session != null)
        {
            FunapiSession.Destroy(session);
            session = null;
        }

        // Create session
        SessionOption option = new SessionOption();
        option.sessionReliability = false;
        option.sendSessionIdOnlyOnce = false;

        session = FunapiSession.Create(address.text, option);
        session.SessionEventCallback += onSessionEvent;
        session.TransportEventCallback += onTransportEvent;
        session.TransportErrorCallback += onTransportError;
        session.ReceivedMessageCallback += onReceivedMessage;

        if (protocol1.type != TransportProtocol.kDefault)
            session.Connect(protocol1.type, encoding1.type, ushort.Parse(port1.text));

        if (protocol2.type != TransportProtocol.kDefault)
            session.Connect(protocol2.type, encoding2.type, ushort.Parse(port2.text));

        if (protocol3.type != TransportProtocol.kDefault)
            session.Connect(protocol3.type, encoding3.type, ushort.Parse(port3.text));
    }

    public void OnButtonSendEcho ()
    {
        sendEcho(TransportProtocol.kTcp);
        sendEcho(TransportProtocol.kUdp);
        sendEcho(TransportProtocol.kHttp);
        sendEcho(TransportProtocol.kWebsocket);
    }

    public void OnButtonStop ()
    {
        if (session != null && session.Connected)
            session.Stop();
    }


    void sendEcho (TransportProtocol protocol)
    {
        FunapiSession.Transport transport = session.GetTransport(protocol);
        if (transport == null)
            return;

        if (transport.encoding == FunEncoding.kJson)
        {
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["message"] = string.Format("[{0}] hello", transport.str_protocol);
            session.SendMessage("echo", message, protocol);
        }
        else if (transport.encoding == FunEncoding.kProtobuf)
        {
            PbufEchoMessage echo = new PbufEchoMessage();
            echo.msg = string.Format("[{0}] hello", transport.str_protocol);
            FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
            session.SendMessage("pbuf_echo", message, protocol);
        }
    }

    void onSessionEvent (SessionEventType type, string sessionid)
    {
        if (type == SessionEventType.kConnected)
        {
            // All transports are connected.
        }
        else if (type == SessionEventType.kStopped)
        {
            // All transports are stopped.
        }
    }

    void onTransportEvent (TransportProtocol protocol, TransportEventType type)
    {
        if (type == TransportEventType.kStarted)
        {
            // The transport is connected.
        }
        else if (type == TransportEventType.kStopped)
        {
            // The transport is stopped.
        }
        else if (type == TransportEventType.kReconnecting)
        {
            // The transport has started to reconnect.
        }
    }

    void onTransportError (TransportProtocol protocol, TransportError error)
    {
        if (error.type == TransportError.Type.kDisconnected)
        {
            // If the connection is lost due to external factors, trys to reconnect.
            //session.Connect(info.protocol);
        }
        else
        {
            // If any other error occurs, terminates the session connection.
            // You can try to reconnect for these errors,
            // but the reconnect may fail if the cause of the error is not resolved.

            session.Stop();
        }
    }

    void onReceivedMessage (string type, object message)
    {
        if (type == "echo")
        {
            FunDebug.Assert(message is Dictionary<string, object>);
            Dictionary<string, object> json = message as Dictionary<string, object>;
            FunDebug.Log("Received an echo message: {0}", json["message"]);
        }
        else if (type == "pbuf_echo")
        {
            FunMessage msg = message as FunMessage;
            PbufEchoMessage echo = FunapiMessage.GetMessage<PbufEchoMessage>(msg, MessageType.pbuf_echo);
            FunDebug.Log("Received an echo message: {0}", echo.msg);
        }
    }


    public InputField address;
    public InputField port1;
    public InputField port2;
    public InputField port3;

    OptionProtocol protocol1;
    OptionEncoding encoding1;
    OptionProtocol protocol2;
    OptionEncoding encoding2;
    OptionProtocol protocol3;
    OptionEncoding encoding3;

    FunapiSession session = null;
}
