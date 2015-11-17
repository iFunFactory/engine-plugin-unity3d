// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using UnityEngine;


namespace Fun
{
    public class FunapiManager : MonoBehaviour
    {
        public static FunapiManager instance
        {
            get
            {
                if (instance_ == null)
                {
                    GameObject obj = GameObject.Find(kInstanceName);
                    if (obj == null)
                        obj = new GameObject(kInstanceName);

                    instance_ = obj.AddComponent(typeof(FunapiManager)) as FunapiManager;
                }

                return instance_;
            }
        }

        public FunapiManager ()
        {
            prev_ticks_ = DateTime.UtcNow.Ticks;
            deltaTime_ = 0.03f;
        }

        public FunapiManager Create ()
        {
            return instance;
        }

        public void AddEvent (Action action)
        {
            if (action == null)
            {
                DebugUtils.Log("FunapiManager.AddEvent - action is null.");
                return;
            }

            lock (event_lock_)
            {
                event_queue_.Enqueue(action);
            }
        }

        void Update()
        {
            // Gets delta time
            long now = DateTime.UtcNow.Ticks;
            int milliseconds = (int)((now - prev_ticks_) / 10000);
            deltaTime_ = (float)milliseconds / 1000f;
            prev_ticks_ = now;

            // Event queue
            if (event_queue_.Count <= 0)
                return;

            Queue<Action> queue = null;

            lock (event_lock_)
            {
                queue = event_queue_;
                event_queue_ = new Queue<Action>();
            }

            foreach (Action action in queue)
            {
                action();
            }
            queue = null;
        }

        public static float deltaTime
        {
            get { return deltaTime_; }
        }


        // Singleton-releated static variables
        private static readonly string kInstanceName = "Funapi Manager";
        private static FunapiManager instance_ = null;

        // Action event-releated member variables
        private object event_lock_ = new object();
        private Queue<Action> event_queue_ = new Queue<Action>();

        // Delta time
        private long prev_ticks_ = 0;
        private static float deltaTime_ = 0f;
    }
}
