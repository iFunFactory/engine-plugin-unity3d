// vim: fileencoding=utf-8 expandtab tabstop=4 softtabstop=4 shiftwidth=4
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Threading;

using Fun;
using MiniJSON;
using plugin_messages;
using funapi.network.fun_message;


namespace Tester
{
    class Client
    {
        public Client (int id, string server_addr)
        {
            id_ = id;
            server_addr_ = server_addr;
        }

        public void Connect (bool session_reliability)
        {
            message_number_ = 0;

            if (session_ == null)
            {
                session_ = FunapiSession.Create(server_addr_, session_reliability);
                session_.SessionEventCallback += OnSessionEvent;
                session_.TransportEventCallback += OnTransportEvent;
                session_.TransportErrorCallback += OnTransportError;
                session_.ReceivedMessageCallback += OnReceivedMessage;

                for (int i = 0; i < 3; ++i)
                {
                    TransportOption option = new TransportOption();
                    if (protocols[i] == TransportProtocol.kTcp)
                    {
                        TcpTransportOption tcp_option = new TcpTransportOption();
                        tcp_option.AutoReconnect = true;
                        tcp_option.EnablePing = true;
                        tcp_option.PingIntervalSeconds = 1;
                        tcp_option.PingTimeoutSeconds = 3;
                        option = tcp_option;
                    }
                    else
                    {
                        option = new TransportOption();
                    }
                    option.ConnectionTimeout = 3f;

                    ushort port = GetPort(protocols[i], encodings[i]);
                    session_.Connect(protocols[i], encodings[i], port, option);
                }
            }
            else
            {
                for (int i = 0; i < 3; ++i)
                {
                    session_.Connect(protocols[i]);
                }
            }
        }

        public void Stop ()
        {
            if (session_ != null)
                session_.Close();
        }

        public bool Connected
        {
            get { return connected_; }
        }

        public void Update ()
        {
            if (session_ != null)
                session_.UpdateFrame();
        }


        ushort GetPort (TransportProtocol protocol, FunEncoding encoding)
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

        public void SendMessage (TransportProtocol protocol, string message)
        {
            if (protocol == TransportProtocol.kTcp)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = message;
                FunMessage fmsg = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session_.SendMessage(MessageType.pbuf_echo, fmsg, protocol);
            }
            else
            {
                Dictionary<string, object> echo = new Dictionary<string, object>();
                echo["message"] = message;
                session_.SendMessage("echo", echo, protocol);
            }
        }

        void OnSessionEvent (SessionEventType type, string session_id)
        {
            FunDebug.Log("[EVENT] Session - {0}.", type);
        }

        void OnTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            FunDebug.Log("[EVENT] {0} transport - {1}.",
                         protocol.ToString().Substring(1).ToUpper(), type);

            if (protocol == TransportProtocol.kTcp && type == TransportEventType.kStarted)
            {
                connected_ = true;
            }
            else
            {
                if (!session_.Started)
                    connected_ = false;
            }
        }

        void OnTransportError (TransportProtocol protocol, TransportError error)
        {
            FunDebug.Log("[ERROR] {0} transport - {1}\n{2}.",
                         protocol.ToString().Substring(1).ToUpper(), error.type, error.message);
        }

        void OnReceivedMessage (string type, object message)
        {
            if (type == "echo")
            {
                Dictionary<string, object> json = message as Dictionary<string, object>;
                string strJson = Json.Serialize(json);
                FunDebug.Log("[{0}:{2}] {1}", id_, strJson, ++message_number_);
            }
            else if (type == "pbuf_echo")
            {
                FunMessage msg = message as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_echo);
                if (obj == null)
                    return;

                PbufEchoMessage echo = obj as PbufEchoMessage;
                FunDebug.Log("[{0}:{2}] {1}", id_, echo.msg, ++message_number_);
            }
        }


        static List<TransportProtocol> protocols = new List<TransportProtocol>() {
            TransportProtocol.kTcp, TransportProtocol.kUdp, TransportProtocol.kHttp };

        static List<FunEncoding> encodings = new List<FunEncoding>() {
            FunEncoding.kProtobuf, FunEncoding.kJson, FunEncoding.kJson };

        int id_ = -1;
        string server_addr_;
        bool connected_ = false;
        int message_number_ = 0;

        FunapiSession session_ = null;
    }
}
