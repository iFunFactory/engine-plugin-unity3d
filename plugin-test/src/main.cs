// vim: fileencoding=utf-8 expandtab tabstop=4 softtabstop=4 shiftwidth=4
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections.Generic;
using System.Threading;


namespace Tester
{
    class TesterMain
    {
        const int kClientMax = 10;
        const string kServerIp = "127.0.0.1";

        SessionOption option = new SessionOption();
        List<Client> list_ = new List<Client>();


        public static void Main ()
        {
            new TesterMain().start();
        }


        void start ()
        {
            writeTitle("START");
            FunDebug.Log("Client count is {0}.", kClientMax);

            option.sessionReliability = true;
            option.sendSessionIdOnlyOnce = false;

            for (int i = 0; i < kClientMax; ++i)
            {
                Client client = new Client(i, kServerIp);
                list_.Add(client);
            }

            Thread t = new Thread(new ThreadStart(onUpdate));
            t.Start();

            testConnect();
            testStartStop();
            testSendReceive();

            t.Abort();
            writeTitle("FINISH");
        }

        void onUpdate ()
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

        void connect ()
        {
            foreach (Client c in list_)
            {
                c.Connect(option);
            }

            while (waitForConnect())
            {
                Thread.Sleep(100);
            }
        }

        void stop ()
        {
            while (waitForSend())
            {
                Thread.Sleep(100);
            }
            Thread.Sleep(100);

            foreach (Client c in list_)
            {
                c.Stop();
            }

            while (waitForStop())
            {
                Thread.Sleep(100);
            }
        }

        bool waitForConnect ()
        {
            foreach (Client c in list_)
            {
                if (!c.Connected)
                    return true;
            }

            return false;
        }

        bool waitForStop ()
        {
            foreach (Client c in list_)
            {
                if (c.Connected)
                    return true;
            }

            return false;
        }

        bool waitForSend ()
        {
            foreach (Client c in list_)
            {
                if (c.HasUnsentMessages)
                    return true;
            }

            return false;
        }

        void sendMessage ()
        {
            foreach (Client c in list_)
            {
                c.SendMessage(TransportProtocol.kTcp, "tcp message");
                c.SendMessage(TransportProtocol.kUdp, "udp message");
                c.SendMessage(TransportProtocol.kHttp, "http message");
            }

            Thread.Sleep(33);
        }

        void testConnect ()
        {
            writeTitle("CONNECT TEST");

            for (int i = 0; i < 10; ++i)
            {
                connect();
                stop();
            }
        }

        void testStartStop ()
        {
            writeTitle("START / STOP TEST");

            for (int i = 0; i < 10; ++i)
            {
                connect();
                sendMessage();
                stop();
            }
        }

        void testSendReceive ()
        {
            writeTitle("SEND / RECEIVE TEST");

            connect();

            for (int i = 0; i < 10; ++i)
                sendMessage();

            stop();
        }

        void writeTitle (string message)
        {
            Console.WriteLine("");
            Console.WriteLine("---------------------- "
                              + message
                              + " -----------------------");
        }
    }
}
