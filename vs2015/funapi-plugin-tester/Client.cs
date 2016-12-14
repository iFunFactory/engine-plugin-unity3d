// vim: fileencoding=utf-8 expandtab tabstop=4 softtabstop=4 shiftwidth=4
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System.Collections.Generic;

// protobuf
using plugin_messages;
using funapi.network.fun_message;


namespace funapi_plugin_tester
{
    class Client
    {
        public Client (int id, string server_ip)
        {
            id_ = id;
            server_ip_ = server_ip;
        }

        public void Connect (bool session_reliability)
        {
            message_number_ = 0;

            if (session_ == null)
            {
                session_ = FunapiSession.Create(server_ip_, session_reliability);
                session_.SessionEventCallback += onSessionEvent;
                session_.TransportEventCallback += onTransportEvent;
                session_.TransportErrorCallback += onTransportError;
                session_.ReceivedMessageCallback += onReceivedMessage;

                for (int i = 0; i < 3; ++i)
                {
                    TransportOption option = new TransportOption();
                    if (protocols[i] == TransportProtocol.kTcp)
                    {
                        TcpTransportOption tcp_option = new TcpTransportOption();
                        tcp_option.EnablePing = true;
                        tcp_option.PingIntervalSeconds = 1;
                        tcp_option.PingTimeoutSeconds = 3;
                        option = tcp_option;
                    }
                    else if (protocols[i] == TransportProtocol.kUdp)
                    {
                        option = new TransportOption();
                    }
                    else if (protocols[i] == TransportProtocol.kHttp)
                    {
                        option = new HttpTransportOption();
                    }

                    option.ConnectionTimeout = 3f;

                    //if (protocols[i] == TransportProtocol.kTcp)
                    //    option.Encryption = EncryptionType.kIFunEngine1Encryption;
                    //else
                    //    option.Encryption = EncryptionType.kIFunEngine2Encryption;

                    ushort port = getPort(protocols[i], encodings[i]);
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
                session_.Stop();
        }

        public bool Connected
        {
            get { return connected_; }
        }

        public bool HasUnsentMessages
        {
            get { return session_.HasUnsentMessages; }
        }

        public void Update ()
        {
            if (session_ != null)
                session_.updateFrame();
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
            if (type == SessionEventType.kStopped)
                connected_ = false;
        }

        void onTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            if (type == TransportEventType.kStarted)
            {
                if (!connected_)
                    connected_ = true;
            }
        }

        void onTransportError (TransportProtocol protocol, TransportError error)
        {
        }

        void onReceivedMessage (string type, object message)
        {
            if (type == "echo")
            {
                Dictionary<string, object> json = message as Dictionary<string, object>;
                string echo_msg = json["message"] as string;
                FunDebug.Log("[{0}:{2}] {1}", id_, echo_msg, ++message_number_);
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


        // Protocol constants.
        static readonly List<TransportProtocol> protocols = new List<TransportProtocol>() {
            TransportProtocol.kTcp, TransportProtocol.kUdp, TransportProtocol.kHttp };

        // Encoding constants.
        static readonly List<FunEncoding> encodings = new List<FunEncoding>() {
            FunEncoding.kProtobuf, FunEncoding.kJson, FunEncoding.kJson };


        // Member variables.
        int id_ = -1;
        string server_ip_;
        bool connected_ = false;
        int message_number_ = 0;

        FunapiSession session_ = null;
    }
}
