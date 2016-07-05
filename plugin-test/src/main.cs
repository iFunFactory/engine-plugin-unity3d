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
        const int max_client = 3;
        const bool reliability = true;
        const string kServerIp = "127.0.0.1";

        List<Client> list_ = new List<Client>();


        public static void Main ()
        {
            new TesterMain().Start();
        }

        void Start ()
        {
            WriteTitle("START");
            FunDebug.Log("Client count is {0}.", max_client);

            for (int i = 0; i < max_client; ++i)
            {
                Client client = new Client(i, kServerIp);
                list_.Add(client);
            }

            Thread t = new Thread(new ThreadStart(Update));
            t.Start();

            ConnectTest();
            StartStopTest();
            SendReceiveTest();

            t.Abort();
        }

        void Update ()
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

        void Connect ()
        {
            foreach (Client c in list_)
            {
                c.Connect(reliability);
            }

            while (true)
            {
                bool keep_check = false;
                foreach (Client c in list_)
                {
                    if (!c.Connected)
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

        void Stop ()
        {
            foreach (Client c in list_)
            {
                c.Stop();
            }

            Thread.Sleep(100);
        }

        void SendMessage ()
        {
            foreach (Client c in list_)
            {
                c.SendMessage(TransportProtocol.kTcp, "tcp message");
                c.SendMessage(TransportProtocol.kUdp, "udp message");
                c.SendMessage(TransportProtocol.kHttp, "http message");
            }

            Thread.Sleep(33);
        }

        void ConnectTest ()
        {
            WriteTitle("CONNECT TEST");

            for (int i = 0; i < 10; ++i)
            {
                Connect();
                Stop();
            }
        }

        void StartStopTest ()
        {
            WriteTitle("START / STOP TEST");

            for (int i = 0; i < 10; ++i)
            {
                Connect();
                SendMessage();
                Thread.Sleep(200);
                Stop();
            }
        }

        void SendReceiveTest ()
        {
            WriteTitle("SEND / RECEIVE TEST");

            Connect();

            for (int i = 0; i < 100; ++i)
                SendMessage();

            Thread.Sleep(200);
            Stop();
        }

        void WriteTitle (string message)
        {
            Console.WriteLine("\n---------------------- "
                              + message
                              + " -----------------------");
        }
    }
}
