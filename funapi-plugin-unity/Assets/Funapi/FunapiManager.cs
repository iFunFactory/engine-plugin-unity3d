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

        public void AddEvent (Action action)
        {
            if (action == null)
            {
                Debug.Log("FunapiManager.AddEvent - action is null.");
                return;
            }

            lock (event_lock_)
            {
                event_queue_.Enqueue(action);
            }
        }

        void Update()
        {
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
        }


        // Singleton-releated static variables
        private static readonly string kInstanceName = "Funapi Manager";
        private static FunapiManager instance_ = null;

        // Action event-releated member variables
        private object event_lock_ = new object();
        private Queue<Action> event_queue_ = new Queue<Action>();
    }
}
