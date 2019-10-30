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
        const int kTestCount = 10;
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

            int testCount = 0;
            while (testCount < kTestCount)
            {
                ++testCount;

                for (int i = 1; i <= kClientMax; ++i)
                {
                    Thread t = new Thread(new ThreadStart(onTest));
                    t.IsBackground = true;
                    threads_.Add(t);

                    t.Start();
                }

                foreach (Thread t in threads_)
                {
                    t.Join();
                }

                threads_.Clear();
                Thread.Sleep(1000);

                writeTitle("Test Set '" + testCount + "' has been finished.");
            }

            FunapiMono.Stop();

            writeTitle("FINISHED");

            Process.GetCurrentProcess().Kill();
        }

        void onTest ()
        {
            Client client = new Client(++clinet_id_);

            client.Connect(TransportProtocol.kTcp, FunEncoding.kProtobuf);
            while (!client.Connected)
                Thread.Sleep(10);

            client.SendEchoMessageWithCount(TransportProtocol.kTcp, 1000);
            while (!client.IsDone)
                Thread.Sleep(10);

            client.Stop();
            client = null;
        }

        void writeTitle (string message)
        {
            Console.WriteLine("");
            Console.WriteLine("---------------------- "
                              + message
                              + " -----------------------");
            Console.WriteLine("");
        }


        int clinet_id_ = 0;
        List<Thread> threads_ = new List<Thread>();
    }
}
