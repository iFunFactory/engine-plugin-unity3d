// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;


namespace Tester
{
    class TesterMain
    {
        const int kClientMax = 100;
        const string kServerIp = "127.0.0.1";


        public static void Main ()
        {
            Client.address = kServerIp;

            // You can specify the size of the console window from the Console menu.
            // Also you can resize it here.
            //Console.WindowWidth = 180;
            //Console.WindowHeight = 50;
            //Console.BufferHeight = 2000;

            new TesterMain().start();
        }


        void start ()
        {
            writeTitle("START");

            FunDebug.Log("Client count is {0}.\n", kClientMax);

            Client.ConnectedCallback += onConnected;
            Client.StoppedCallback += onStopped;

            for (int i = 1; i <= kClientMax; ++i)
            {
                Client client = new Client(i);
                list.Add(client);
            }

            foreach (Client c in list)
            {
                c.Connect(TransportProtocol.kTcp, FunEncoding.kJson);
                Thread.Sleep(10);
            }

            while (!didAllConnect())
            {
                Thread.Sleep(500);
            }

            foreach (Client c in list)
            {
                c.Stop();
                Thread.Sleep(10);
            }

            while (!didAllStop())
            {
                Thread.Sleep(500);
            }
            Thread.Sleep(500);

            FunapiMono.Stop();
            writeTitle("FINISHED");

            Process.GetCurrentProcess().Kill();
        }

        void onConnected (Client client)
        {
            client.SendEchoMessageWithCount(TransportProtocol.kTcp, 5);
        }

        void onStopped (Client client)
        {
        }

        bool didAllConnect()
        {
            foreach (Client c in list)
            {
                if (!c.Connected)
                    return false;
            }

            return true;
        }

        bool didAllStop()
        {
            foreach (Client c in list)
            {
                if (c.Connected)
                    return false;
            }

            return true;
        }


        void writeTitle (string message)
        {
            Console.WriteLine("");
            Console.WriteLine("---------------------- "
                              + message
                              + " -----------------------");
            Console.WriteLine("");
        }


        List<Client> list = new List<Client>();
    }
}
