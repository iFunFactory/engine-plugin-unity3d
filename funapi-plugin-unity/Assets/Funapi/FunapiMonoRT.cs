// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

#if NO_UNITY
using System.Threading;


namespace Fun
{
    public partial class FunapiMono
    {
        static FunapiMono instance
        {
            get
            {
                if (instance_ == null)
                {
                    instance_ = new FunapiMono();
                    instance_.onStart();
                }

                return instance_;
            }
        }

        public static void Stop ()
        {
            instance.onStop();
        }

        ~FunapiMono()
        {
            onStop();
        }

        void onStart ()
        {
            thread_ = new Thread(new ThreadStart(onUpdate));
            thread_.Start();
        }

        void onStop ()
        {
            if (thread_ == null || running_ == false)
                return;

            running_ = false;

            thread_.Join();
            thread_ = null;
        }

        void onUpdate ()
        {
            while (running_)
            {
                Update();
                Thread.Sleep(33);
            }
        }


        static FunapiMono instance_ = null;

        Thread thread_ = null;
        bool running_ = true;
    }
}
#endif
