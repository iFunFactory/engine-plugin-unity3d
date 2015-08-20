// vim: fileencoding=utf-8 expandtab tabstop=4 softtabstop=4 shiftwidth=4
//
// Copyright (C) 2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using Fun;
using MiniJSON;
using funapi.network.fun_message;
using test_messages;


namespace Tester
{
    class Client
    {
        public Client(int index)
        {
            index_ = index;
        }

        public void Connect (string address)
        {
            FunapiTcpTransport tcp_transport = new FunapiTcpTransport(address, 8022, FunEncoding.kProtobuf);
            FunapiHttpTransport http_transport = new FunapiHttpTransport(address, 8018, false, FunEncoding.kJson);

            network_ = new FunapiNetwork(true);
            network_.AttachTransport(tcp_transport);
            network_.AttachTransport(http_transport);

            network_.RegisterHandlerWithProtocol("pbuf_echo", TransportProtocol.kTcp, this.OnTcpEcho);
            network_.RegisterHandlerWithProtocol("echo", TransportProtocol.kHttp, this.OnHttpEcho);

            network_.Start();
        }

        public void Update()
        {
            network_.Update();
        }

        public void Pause()
        {
            if (!network_.Connected)
                return;

            Console.WriteLine(DateTime.Now.ToString() + " pause");
            network_.Stop(false);
        }

        public void Resume()
        {
            if (network_.Connected)
                return;

            Console.WriteLine(DateTime.Now.ToString() + " continue");
            network_.Start();
        }

        public bool IsRunning
        {
            get { return network_.Connected; }
        }

        public int Index
        {
            get { return index_; }
        }

        public int Pending
        {
            get { return pending_count_; }
        }

        public bool Finished
        {
            get { return tcp_count_ >= kMaxMessageCount && http_count_ >= kMaxMessageCount; }
        }

        public void SendEcho()
        {
            if (tcp_count_ < kMaxMessageCount)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = string.Format("{0} message from tcp {1}", tcp_count_, index_.ToString());
                FunMessage msg = network_.CreateFunMessage(echo, MessageType.pbuf_echo);
                network_.SendMessage(MessageType.pbuf_echo, msg);
            }

            if (http_count_ < kMaxMessageCount)
            {
                Dictionary<string, object> msg = new Dictionary<string, object>();
                msg["message"] = string.Format("{0} message from http {1}", http_count_, index_.ToString());
                network_.SendMessage("echo", msg);
             }

            ++pending_count_;
        }

        private void OnTcpEcho (string msg_type, object body)
        {
            DebugUtils.Assert(body is FunMessage);
            //FunMessage msg = body as FunMessage;
            //PbufEchoMessage _msg = network_.GetMessage(msg, MessageType.pbuf_echo) as PbufEchoMessage;

            --pending_count_;
            ++tcp_count_;
            Console.WriteLine(string.Format("client [{0}] - {1} tcp messages received.", index_, tcp_count_));
        }

        private void OnHttpEcho (string msg_type, object body)
        {
            DebugUtils.Assert(body is Dictionary<string, object>);
            //string strJson = Json.Serialize(body as Dictionary<string, object>);

            ++http_count_;
            Console.WriteLine(string.Format("client [{0}] - {1} http messages received.", index_, http_count_));
        }


        private static readonly int kMaxMessageCount = 100;

        private FunapiNetwork network_;
        private int index_;
        private int tcp_count_ = 0;
        private int http_count_ = 0;
        private int pending_count_ = 0;
    }


    class TesterMain
    {
        public static void Main()
        {
            int i, N = 1000;
            Client[] clients = new Client[N];
            for (i = 0; i < N; ++i)
            {
                clients[i] = new Client(i);
                clients[i].Connect("127.0.0.1");
                Thread.Sleep(1);
            }

            Thread.Sleep(1000);

            while (true)
            {
                bool live = false;

                foreach (Client cli in clients)
                {
                    if (cli.Finished)
                        continue;

                    cli.Update();

                    if (cli.IsRunning)
                    {
                        cli.Pause();
                        Thread.Sleep(1);
                    }
                    else
                    {
                        cli.Resume();
                        cli.SendEcho();
                    }

                    if (!live)
                        live = true;
                }

                if (!live)
                   break;

                for (i = 0; i < 3; ++i)
                {
                    foreach (Client cli in clients)
                    {
                        cli.Update();
                    }

                    Thread.Sleep(5);
                }
            }
        }
    }
}
