// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System;
using System.Collections.Generic;
using System.Threading;

// protobuf
using plugin_messages;
using funapi.network.fun_message;


namespace Tester
{
    class Client
    {
        public Client (int id)
        {
            client_id_ = id;
        }

        public void Connect (TransportProtocol protocol, FunEncoding encoding)
        {
            if (session_ == null)
            {
                SessionOption option = new SessionOption();
                option.sessionReliability = false;
                option.sendSessionIdOnlyOnce = false;

                session_ = FunapiSession.Create(address, option);
                session_.SessionEventCallback += onSessionEvent;
                session_.TransportEventCallback += onTransportEvent;
                session_.TransportErrorCallback += onTransportError;
                session_.ReceivedMessageCallback += onReceivedMessage;
            }

            session_.Connect(protocol, encoding, getPort(protocol, encoding));
        }

        public void Stop ()
        {
            if (session_ != null && session_.Connected)
                session_.Stop();
        }

        public int id
        {
            get { return client_id_; }
        }

        public bool Connected
        {
            get { return session_ != null && session_.Connected; }
        }

        public bool IsDone
        {
            get { return is_done_; }
        }

        public void SendEchoMessageWithCount (TransportProtocol protocol, int count)
        {
            sending_count_ = count;

            for (int i = 0; i < count; ++i)
            {
                sendEchoMessage(protocol);
            }
        }

        void sendEchoMessage (TransportProtocol protocol)
        {
            FunapiSession.Transport transport = session_.GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.LogWarning("sendEchoMessage - transport is null.");
                return;
            }

            ++echo_id_;

            if (transport.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = string.Format("[{0}] echo message ({1})", client_id_, echo_id_);
                session_.SendMessage("echo", message, protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = string.Format("[{0}] echo message ({1})", client_id_, echo_id_);
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session_.SendMessage("pbuf_echo", message, protocol);
            }
        }

        void onReceivedMessage (string type, object message)
        {
            if (type == "echo" || type == "pbuf_echo")
            {
                --sending_count_;
                if (sending_count_ <= 0)
                    is_done_ = true;
            }

            if (type == "echo")
            {
                Dictionary<string, object> json = message as Dictionary<string, object>;
                FunDebug.Log("[{0}] received: {1} (left:{2})", client_id_, json["message"] as string, sending_count_);
            }
            else if (type == "pbuf_echo")
            {
                FunMessage msg = message as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_echo);
                if (obj == null)
                    return;

                PbufEchoMessage echo = obj as PbufEchoMessage;
                FunDebug.Log("[{0}] received: {1} (left:{2})", client_id_, echo.msg, sending_count_);
            }
        }

        void onSessionEvent (SessionEventType type, string sessionid)
        {
            if (type == SessionEventType.kStopped || type == SessionEventType.kClosed)
                is_done_ = true;
        }

        void onTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            if (type == TransportEventType.kStopped)
                is_done_ = true;
        }

        void onTransportError (TransportProtocol protocol, TransportError error)
        {
            is_done_ = true;
            session_.Stop(protocol);
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
            else if (protocol == TransportProtocol.kWebsocket)
                port = (ushort)(encoding == FunEncoding.kJson ? 8019 : 8029);

            return port;
        }



        public static string address { private get; set; }

        int client_id_ = 0;
        int echo_id_ = 0;
        int sending_count_ = 0;
        bool is_done_ = false;

        FunapiSession session_ = null;
    }
}
