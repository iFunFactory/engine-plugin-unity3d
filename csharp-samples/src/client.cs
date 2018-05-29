// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System;
using System.Collections.Generic;

// protobuf
using plugin_messages;
using funapi.network.fun_message;


namespace Tester
{
    class Client
    {
        public Client (int id)
        {
            client_id = id;
        }

        public void Connect (TransportProtocol protocol, FunEncoding encoding)
        {
            if (session == null)
            {
                SessionOption option = new SessionOption();
                option.sessionReliability = false;
                option.sendSessionIdOnlyOnce = false;

                session = FunapiSession.Create(address, option);
                session.SessionEventCallback += onSessionEvent;
                session.TransportEventCallback += onTransportEvent;
                session.TransportErrorCallback += onTransportError;
                session.ReceivedMessageCallback += onReceivedMessage;
            }

            session.Connect(protocol, encoding, getPort(protocol, encoding));
        }

        public void Stop ()
        {
            if (session != null && session.Connected)
                session.Stop();
        }

        public bool Connected
        {
            get { return session != null && session.Connected; }
        }

        public void SendEchoMessageWithCount (TransportProtocol protocol, int count)
        {
            for (int i = 0; i < count; ++i)
                SendEchoMessage(protocol);
        }

        public void SendEchoMessage (TransportProtocol protocol)
        {
            FunapiSession.Transport transport = session.GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.LogWarning("SendEchoMessage - transport is null.");
                return;
            }

            if (transport.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = string.Format("{0} echo message", transport.str_protocol);
                session.SendMessage("echo", message, protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = string.Format("{0} echo message", transport.str_protocol);
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session.SendMessage("pbuf_echo", message, protocol);
            }
        }

        void onReceivedMessage (string type, object message)
        {
            if (type == "echo")
            {
                Dictionary<string, object> json = message as Dictionary<string, object>;
                FunDebug.Log("[{0}] received: {1}", client_id, json["message"] as string);
            }
            else if (type == "pbuf_echo")
            {
                FunMessage msg = message as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_echo);
                if (obj == null)
                    return;

                PbufEchoMessage echo = obj as PbufEchoMessage;
                FunDebug.Log("[{0}] received: {1}", client_id, echo.msg);
            }
        }

        void onSessionEvent (SessionEventType type, string sessionid)
        {
            if (type == SessionEventType.kConnected)
            {
                if (ConnectedCallback != null)
                    ConnectedCallback(this);
            }
            else if (type == SessionEventType.kStopped)
            {
                if (StoppedCallback != null)
                    StoppedCallback(this);
            }
        }

        void onTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
        }

        void onTransportError (TransportProtocol protocol, TransportError error)
        {
            session.Stop(protocol);
        }


        ushort getPort (TransportProtocol protocol, FunEncoding encoding)
        {
            ushort port = 0;
            if (protocol == TransportProtocol.kTcp)
                port = (ushort)(encoding == FunEncoding.kJson ? 8011 : 8017);
            else if (protocol == TransportProtocol.kUdp)
                port = (ushort)(encoding == FunEncoding.kJson ? 8012 : 8018);
            else if (protocol == TransportProtocol.kHttp)
                port = (ushort)(encoding == FunEncoding.kJson ? 8013 : 8019);

            return port;
        }



        public static string address { private get; set; }

        public static event Action<Client> ConnectedCallback;
        public static event Action<Client> StoppedCallback;


        int client_id = 0;
        FunapiSession session = null;
    }
}
