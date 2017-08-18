using Fun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace test_winform
{
    static class Program
    {
        /// <summary>
        /// 해당 응용 프로그램의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            /*
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            */

            new Test();
        }
    }

    class Test
    {
        const int kClientMax = 1;
        const string kServerIp = "10.10.0.98";

        SessionOption option = new SessionOption();
        List<Client> list_ = new List<Client>();

        public Test ()
        {
            FunapiEncryptor.public_key = "0b8504a9c1108584f4f0a631ead8dd548c0101287b91736566e13ead3f008f5d";

            writeTitle("START");
            FunDebug.Log("Client count is {0}.", kClientMax);

            option.sessionReliability = true;
            option.sendSessionIdOnlyOnce = true;

            for (int i = 0; i < kClientMax; ++i)
            {
                Client client = new Client(i, kServerIp);
                list_.Add(client);
            }

            Thread t = new Thread(new ThreadStart(onUpdate));
            t.Start();

            foreach (Client c in list_)
            {
                c.Connect(option);
                Thread.Sleep(10);
            }

            while (waitForConnect())
            {
                Thread.Sleep(500);
            }
            writeTitle("All sessions are connected.");

            foreach (Client c in list_)
            {
                c.SendMessage();
                Thread.Sleep(10);
            }

            while (waitForStop())
            {
                FunDebug.LogWarning("wait for stop...");
                Thread.Sleep(500);
            }

            t.Abort();
            writeTitle("FINISHED");

            FunDebug.SaveLogs();
        }

        void onUpdate()
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

        bool waitForConnect()
        {
            foreach (Client c in list_)
            {
                if (!c.Connected)
                    return true;
            }

            return false;
        }

        bool waitForStop()
        {
            foreach (Client c in list_)
            {
                if (c.Connected)
                    return true;
            }

            return false;
        }

        void writeTitle(string message)
        {
            Console.WriteLine("");
            Console.WriteLine("---------------------- "
                              + message
                              + " -----------------------");
            Console.WriteLine("");
        }
    }
}
