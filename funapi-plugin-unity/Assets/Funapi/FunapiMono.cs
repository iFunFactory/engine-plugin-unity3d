// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections;
using System.Collections.Generic;
#if !NO_UNITY
using UnityEngine;
#endif


namespace Fun
{
#if !NO_UNITY
    public partial class FunapiMono : MonoBehaviour
    {
        static FunapiMono instance
        {
            get
            {
                if (instance_ == null)
                {
                    if (is_expired_)
                        return null;

                    instance_ =  FindObjectOfType(typeof(FunapiMono)) as FunapiMono;
                    if (instance_ == null)
                    {
                        GameObject obj = new GameObject("FunapiMono");
                        instance_ = obj.AddComponent(typeof(FunapiMono)) as FunapiMono;
                    }
                }

                return instance_;
            }
        }

        void Awake()
        {
            DontDestroyOnLoad(this);
        }

        void OnApplicationPause (bool isPaused)
        {
            listener_.ForEach(delegate (Listener listener) {
                listener.OnPause(isPaused);
            });
        }

        void OnApplicationQuit()
        {
            listener_.ForEach(delegate (Listener listener) {
                listener.OnQuit();
            });

            instance_ = null;
            is_expired_ = true;
        }


        static FunapiMono instance_ = null;
        static bool is_expired_ = false;
    }
#endif


    public sealed partial class FunapiMono
    {
        private FunapiMono() {}

        void Update ()
        {
            // gets delta time
            long now = DateTime.UtcNow.Ticks;
            float delta_time = (now - prev_ticks_) / 10000000f;
            prev_ticks_ = now;

            listener_.Update(delta_time);
        }


        // Member variables.
        long prev_ticks_ = DateTime.UtcNow.Ticks;
        ConcurrentList<Listener> listener_ = new ConcurrentList<Listener>();
    }


    // Mono Listener
    public partial class FunapiMono
    {
        public abstract class Listener : IConcurrentItem
        {
            // MonoBehaviour-related functions.
            protected void setMonoListener ()
            {
                instance.listener_.Add(this);
                is_active_ = true;
            }

            protected void releaseMonoListener ()
            {
                is_active_ = false;
                routines_.Clear();

                instance.listener_.Remove(this);
            }

            public void StartCoroutine (IEnumerator func)
            {
                if (!is_active_)
                    return;

                routines_.Add(new Func(func));
            }

            public void Update (float deltaTime)
            {
                if (!is_active_)
                    return;

                // Updates routines
                routines_.Update(deltaTime);
                post_event_.Update(deltaTime);

                OnUpdate(deltaTime);
            }

            public virtual void OnUpdate (float deltaTime) {}

            public virtual void OnPause (bool isPaused) {}

            public virtual void OnQuit () {}

            public abstract string name { get; }

            public bool isDone { get; set; }

            protected PostEventList event_ { get { return post_event_; } }


            class Func : IConcurrentItem
            {
                public Func (IEnumerator func)
                {
                    list_.Push(func);

                    moveNext();
                }

                public void Update (float deltaTime)
                {
                    if (isDone)
                        return;

                    if (list_.Count > 0)
                    {
                        moveNext();
                    }
                    else
                    {
                        isDone = true;
                    }
                }

                void moveNext ()
                {
                    IEnumerator func = list_.Peek();

                    if (!func.MoveNext())
                    {
                        list_.Pop();
                        return;
                    }

                    if (func.Current != null)
                    {
                        list_.Push((IEnumerator)func.Current);
                    }
                }

                public string name { get { return "Coroutine"; } }
                public bool isDone { get; set; }

                Stack<IEnumerator> list_ = new Stack<IEnumerator>();
            }


            // Member variables.
            bool is_active_ = false;

            // list of coroutine
            PostEventList post_event_ = new PostEventList();
            ConcurrentList<Func> routines_ = new ConcurrentList<Func>();
        }
    }


    //
    // The following class was created by referring to Unity's CustomYieldInstruction class.
    // And also rest of the other classes.
    //
    public abstract class YieldIndication : IEnumerator
    {
        public object Current { get { return (object)null; } }
        public bool MoveNext () { return keepWaiting; }
        public void Reset () {}

        // Indicates if coroutine should be kept suspended.
        public abstract bool keepWaiting { get; }
    }


    public sealed class SleepForSeconds : YieldIndication
    {
        public SleepForSeconds (float delay)
        {
            end_ticks_ = DateTime.UtcNow.Ticks + (long)(delay * 10000000);
        }

        public override bool keepWaiting
        {
            get { return end_ticks_ > DateTime.UtcNow.Ticks; }
        }

        long end_ticks_ = 0;
    }
}
