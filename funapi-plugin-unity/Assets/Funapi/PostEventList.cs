// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;


namespace Fun
{
    public class PostEventList
    {
        public void Add (Action func, float delay = 0f)
        {
            if (func == null)
                return;

            list_.Add(new Func(func, delay));
        }

        public void Add<T> (Action<T> func, T param, float delay = 0f)
        {
            if (func == null)
                return;

            list_.Add(new Func(delegate { func(param); }, delay));
        }

        public void Clear ()
        {
            list_.Clear();
        }

        public void Update (float deltaTime)
        {
            list_.Update(deltaTime);
        }


        class Func : IConcurrentItem
        {
            public Func (Action func, float delay)
            {
                func_ = func;
                delay_ = delay;
            }

            public void Update (float deltaTime)
            {
                if (isDone)
                    return;

                delay_ -= deltaTime;
                if (delay_ > 0f)
                    return;

                func_();

                isDone = true;
            }

            public string name { get { return "Event"; } }
            public bool isDone { get; set; }

            float delay_ = 0f;
            Action func_ = null;
        }


        ConcurrentList<Func> list_ = new ConcurrentList<Func>();
    }
}
