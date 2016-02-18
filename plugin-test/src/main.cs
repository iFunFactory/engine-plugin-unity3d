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


namespace Tester
{
    class TesterMain
    {
        private const string kServerIp = "127.0.0.1";
        private const int kNumberOfClient = 3;

        private List<Client> list_ = new List<Client>();


        public static void Main ()
        {
            new TesterMain().Start();
        }

        private void Start ()
        {
            WriteTitle("START");

            bool reliability = true;
            for (int i = 0; i < kNumberOfClient; ++i)
            {
                Client client = new Client(i, reliability);
                client.Init(kServerIp);
                list_.Add(client);
            }

            Thread t = new Thread(new ThreadStart(Update));
            t.Start();

            ConnectTest();
            StartStopTest();
            SendReceiveTest();
            PingTest();

            t.Abort();
        }

        private void Update ()
        {
            while (true)
            {
                foreach (Client c in list_)
                {
                    c.Update();
                }

                Thread.Sleep(33);
            }
        }

        private void Connect ()
        {
            foreach (Client c in list_)
            {
                c.Connect();
            }

            while (true)
            {
                bool keep_check = false;
                foreach (Client c in list_)
                {
                    if (c.Connecting && !c.Connected)
                    {
                        keep_check = true;
                        break;
                    }
                }

                Thread.Sleep(100);
                if (!keep_check)
                    break;
            }
        }

        private void Stop ()
        {
            foreach (Client c in list_)
            {
                c.Stop();
            }
        }

        private void SendMessage ()
        {
            foreach (Client c in list_)
            {
                c.SendMessage(TransportProtocol.kTcp, "tcp message");
                c.SendMessage(TransportProtocol.kUdp, "udp message");
                c.SendMessage(TransportProtocol.kHttp, "http message");
            }

            Thread.Sleep(33);
        }

        private void ConnectTest ()
        {
            WriteTitle("CONNECT TEST");

            Connect();
            Stop();
        }

        private void StartStopTest ()
        {
            WriteTitle("START / STOP TEST");

            for (int i = 0; i < 10; ++i)
            {
                Connect();
                SendMessage();
                Thread.Sleep(100);
                Stop();
            }
        }

        private void SendReceiveTest ()
        {
            WriteTitle("SEND / RECEIVE TEST");

            Connect();

            for (int i = 0; i < 100; ++i)
                SendMessage();

            Thread.Sleep(200);
            Stop();
        }

        private void PingTest ()
        {
            WriteTitle("PING TEST");

            foreach (Client c in list_)
                c.SetPing(true);

            Connect();
            Thread.Sleep(5000);
            Stop();

            foreach (Client c in list_)
                c.SetPing(false);
        }

        private void WriteTitle (string message)
        {
            Console.WriteLine("\n---------------------- "
                              + message
                              + " -----------------------");
        }
    }
}
