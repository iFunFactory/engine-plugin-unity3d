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
        public Client (int id, bool session_reliability)
        {
            id_ = id;

            network_ = new FunapiNetwork(session_reliability);
            network_.OnSessionInitiated += OnSessionInitiated;
            network_.StoppedAllTransportCallback += OnStoppedAllTransport;

            network_.RegisterHandler("echo", this.OnEcho);
            network_.RegisterHandler("pbuf_echo", this.OnEchoWithProtobuf);
        }

        public void Init (string addr)
        {
            if (network_ == null)
                return;

            List<TransportProtocol> protocols = new List<TransportProtocol>() {
                TransportProtocol.kTcp, TransportProtocol.kUdp, TransportProtocol.kHttp };

            List<FunEncoding> encodings = new List<FunEncoding>() {
                FunEncoding.kProtobuf, FunEncoding.kJson, FunEncoding.kJson };

            for (int i = 0; i < 3; ++i)
            {
                AddTransport(protocols[i], encodings[i], addr);
            }
        }

        public void Connect ()
        {
            if (network_ == null)
                return;

            connecting_ = true;
            message_number_ = 0;

            network_.Start();
        }

        public void Stop ()
        {
            if (network_ != null)
                network_.Stop();
        }

        public bool Connecting
        {
            get { return connecting_; }
        }

        public bool Connected
        {
            get
            {
                if (network_ == null)
                    return false;

                return network_.Connected;
            }
        }

        public void Update ()
        {
            if (network_ != null)
                network_.UpdateFrame();
        }

        public void SetPing (bool enable)
        {
            if (network_ == null || !network_.HasTransport(TransportProtocol.kTcp))
                return;

            network_.EnablePing = enable;
        }

        public void SetEncryption (TransportProtocol protocol, EncryptionType enc)
        {
            FunapiTransport transport = network_.GetTransport(protocol);
            if (transport == null)
                return;

            ((FunapiEncryptedTransport)transport).SetEncryption(enc);
        }

        private void AddTransport (TransportProtocol protocol, FunEncoding type, string addr)
        {
            FunapiTransport transport = null;
            UInt16 port = 0;

            if (protocol == TransportProtocol.kTcp)
            {
                port = (ushort)(type == FunEncoding.kJson ? 8012 : 8022);
                FunapiTcpTransport tcp = new FunapiTcpTransport(addr, port, type);
                tcp.PingIntervalSeconds = 1;
                tcp.PingTimeoutSeconds = 3;
                tcp.AutoReconnect = true;
                transport = tcp;
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                port = (ushort)(type == FunEncoding.kJson ? 8013 : 8023);
                FunapiUdpTransport udp = new FunapiUdpTransport(addr, port, type);
                transport = udp;
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                port = (ushort)(type == FunEncoding.kJson ? 8018 : 8028);
                FunapiHttpTransport http = new FunapiHttpTransport(addr, port, false, type);
                transport = http;
            }

            if (transport == null)
                return;

            transport.ConnectTimeout = 3f;

            network_.AttachTransport(transport);
        }

        public void SendMessage (TransportProtocol protocol, string message)
        {
            FunEncoding encoding = network_.GetEncoding(protocol);
            if (encoding == FunEncoding.kNone)
            {
                Console.WriteLine("You should attach {0} transport first.", protocol);
                return;
            }

            if (encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> echo = new Dictionary<string, object>();
                echo["message"] = message;
                network_.SendMessage("echo", echo,
                                     EncryptionType.kDefaultEncryption, protocol);
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = message;
                FunMessage fmsg = network_.CreateFunMessage(echo, MessageType.pbuf_echo);
                network_.SendMessage(MessageType.pbuf_echo, fmsg,
                                     EncryptionType.kDefaultEncryption, protocol);
            }
        }

        private void OnEcho (string msg_type, object body)
        {
            Dictionary<string, object> json = body as Dictionary<string, object>;
            string strJson = Json.Serialize(json);
            Console.WriteLine("[{0}:{2}] {1}", id_, strJson, ++message_number_);
        }

        private void OnEchoWithProtobuf (string msg_type, object body)
        {
            FunMessage msg = body as FunMessage;
            object obj = network_.GetMessage(msg, MessageType.pbuf_echo);
            if (obj == null)
                return;

            PbufEchoMessage echo = obj as PbufEchoMessage;
            Console.WriteLine("[{0}:{2}] {1}", id_, echo.msg, ++message_number_);
        }

        private void OnSessionInitiated (string session_id)
        {
        }

        private void OnStoppedAllTransport ()
        {
            connecting_ = false;
        }


        private int id_ = -1;
        private bool connecting_ = false;
        private int message_number_ = 0;

        private FunapiNetwork network_ = null;
    }
}
