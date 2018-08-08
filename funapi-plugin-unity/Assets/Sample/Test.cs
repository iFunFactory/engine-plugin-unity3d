// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.


using Fun;
using System.Collections.Generic;
using UnityEngine;

// protobuf
using plugin_messages;
using funapi.network.fun_message;


public class Test : MonoBehaviour
{
    public void OnButtonConnect ()
    {
        if (session == null || address_changed)
        {
            createSession();
        }

        TransportOption option = null;
        if (info.protocol == TransportProtocol.kTcp)
        {
            TcpTransportOption tcp = new TcpTransportOption();

            // If this option is set to true,
            // it will automatically try to reconnect within the Transport when it is disconnected.
            tcp.AutoReconnect = false;
            option = tcp;
        }

        session.Connect(info.protocol, info.encoding, info.port, option);
    }

    public void OnButtonSendEcho ()
    {
        if (session == null)
            return;

        FunapiSession.Transport transport = session.GetTransport(info.protocol);
        if (transport == null)
        {
            FunDebug.LogWarning("sendEchoMessage - transport is null.");
            return;
        }

        if (transport.encoding == FunEncoding.kJson)
        {
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["message"] = string.Format("[{0}] hello", transport.str_protocol);
            session.SendMessage("echo", message, info.protocol);
        }
        else if (transport.encoding == FunEncoding.kProtobuf)
        {
            PbufEchoMessage echo = new PbufEchoMessage();
            echo.msg = string.Format("[{0}] hello", transport.str_protocol);
            FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
            session.SendMessage("pbuf_echo", message, info.protocol);
        }
    }

    public void OnButtonStop ()
    {
        if (session != null && session.Connected)
            session.Stop();
    }

    public void OnButtonDisconnect ()
    {
        if (session == null || !session.Connected)
            return;

        FunapiSession.Transport transport = session.GetTransport(info.protocol);
        if (transport != null)
            transport.ForcedDisconnect();
    }

    public void OnClearLogs ()
    {
        logs.Clear();
    }

    public void OnChangedAddress (string text)
    {
        address_changed = true;
    }


    void createSession ()
    {
        // Create session
        SessionOption option = new SessionOption();
        option.sessionReliability = false;
        option.sendSessionIdOnlyOnce = false;

        session = FunapiSession.Create(info.address, option);
        session.SessionEventCallback += onSessionEvent;
        session.TransportEventCallback += onTransportEvent;
        session.TransportErrorCallback += onTransportError;
        session.ReceivedMessageCallback += onReceivedMessage;

        address_changed = false;
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
            session = null;
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
            if (session != null)
                session.Connect(info.protocol);
        }
        else
        {
            // If any other error occurs, terminates the session connection.
            // You can try to reconnect for these errors,
            // but the reconnect may fail if the cause of the error is not resolved.

            if (session != null)
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



    public UIOption info;
    public UILogs logs;

    FunapiSession session = null;
    bool address_changed = false;
}
