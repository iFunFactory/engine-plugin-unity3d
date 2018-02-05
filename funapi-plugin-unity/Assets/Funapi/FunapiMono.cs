// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Fun
{
    public class FunapiMono : MonoBehaviour
    {
        private static FunapiMono instance_ = null;

        private FunapiMono() {}

        public static FunapiMono Instance
        {
            get
            {
                if (instance_ == null)
                {
                    instance_ =  FindObjectOfType(typeof(FunapiMono)) as FunapiMono;
                    if (instance_ == null)
                    {
                        GameObject obj = new GameObject("FunapiMono");
                        instance_ = obj.AddComponent(typeof(FunapiMono)) as FunapiMono;
                        Debug.Log ("FunapiMono was generated automaticly.");
                    }
                }

                return instance_;
            }
        }

        void Awake()
        {
            DontDestroyOnLoad(this);
        }

        void OnApplicationQuit()
        {
            instance_ = null;
        }

        void Update ()
        {
            // gets delta time
            long now = DateTime.UtcNow.Ticks;
            float delta_time = (now - prev_ticks_) / 10000000f;
            prev_ticks_ = now;

            updateTimer(delta_time);
        }


        // Timer-related functions.
        public static string AddTimer (FunapiTimer timer)
        {
            return Instance.addTimer(timer);
        }

        public static void RemoveTimer (string name)
        {
            Instance.removeTimer(name);
        }

        string addTimer (FunapiTimer timer)
        {
            if (timer == null)
                throw new ArgumentNullException("timer");

            lock (timer_lock_)
            {
                pending_list_.Add(timer);
                FunDebug.DebugLog3("[Timer] Adds {0}", timer.ToString());
            }

            return timer.Name;
        }

        void removeTimer (string name)
        {
            lock (timer_lock_)
            {
                FunapiTimer timer = primary_list_.Find(t => { return t.Name == name; });
                if (timer != null)
                {
                    timer.Kill();
                    FunDebug.DebugLog3("[Timer] Removes '{0}' timer.", name);
                    return;
                }

                int count = pending_list_.RemoveAll(t => { return t.Name == name; });
                if (count > 0)
                    FunDebug.DebugLog3("[Timer] Removes '{0}' timer. ({1})", name, count);
            }
        }

        void updateTimer (float delta_time)
        {
            lock (timer_lock_)
            {
                // adds from pending list
                if (pending_list_.Count > 0)
                {
                    primary_list_.AddRange(pending_list_);
                    pending_list_.Clear();
                }

                // updates timer
                if (primary_list_.Count > 0)
                {
                    primary_list_.RemoveAll(t => { return t.IsDone; });

                    foreach (FunapiTimer timer in primary_list_)
                    {
                        timer.Update(delta_time);
                    }
                }
            }
        }


        // Timer-related variables.
        long prev_ticks_ = DateTime.UtcNow.Ticks;
        object timer_lock_ = new object();
        List<FunapiTimer> primary_list_ = new List<FunapiTimer>();
        List<FunapiTimer> pending_list_ = new List<FunapiTimer>();
    }
}
