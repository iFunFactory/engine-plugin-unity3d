// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using UnityEngine;


namespace Fun
{
    public class FunapiTimer
    {
        public FunapiTimer (string name, Action callback)
            : this(name, 0f, callback)
        {
        }

        public FunapiTimer (string name, float start_delay, Action callback)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("timer name");

            if (callback == null)
                throw new ArgumentNullException("timer callback");

            name_ = name;
            callback_ = callback;
            wait_time_ = start_delay;
        }

        public virtual void Update (float delta_time)
        {
            if (is_done_)
                return;

            if (wait_time_ > 0f)
            {
                wait_time_ -= delta_time;
                if (wait_time_ > 0f)
                    return;
            }

            is_done_ = true;

            callback_();
        }

        public void Kill ()
        {
            is_done_ = true;
        }

        public override string ToString ()
        {
            return string.Format("'{0}' timer. delay:{1}", name_, wait_time_);
        }

        public string Name { get { return name_; } }

        public bool IsDone { get { return is_done_; } }


        // Member variables.
        protected string name_;
        protected float wait_time_ = 0f;
        protected bool is_done_ = false;
        protected Action callback_ = null;
    }


    public class FunapiLoopTimer : FunapiTimer
    {
        public FunapiLoopTimer (string name, float interval, Action callback)
            : this(name, 0f, interval, callback)
        {
        }

        public FunapiLoopTimer (string name, float start_delay, float interval, Action callback)
            : base(name, start_delay, callback)
        {
            interval_ = interval;
        }

        public override void Update (float delta_time)
        {
            if (is_done_)
                return;

            if (wait_time_ > 0f)
            {
                wait_time_ -= delta_time;
                if (wait_time_ > 0f)
                    return;
            }

            wait_time_ = interval_;

            callback_();
        }

        public override string ToString ()
        {
            return string.Format("'{0}' loop timer. delay:{1} interval:{2}",
                                 name_, wait_time_, interval_);
        }


        float interval_ = 0f;
    }


    // Call function at intervals of exponent 2
    public class FunapiExponentTimer : FunapiTimer
    {
        public FunapiExponentTimer (string name, float limit, Action callback)
            : base(name, 1f, callback)
        {
            limit_ = limit;
        }

        public override void Update (float delta_time)
        {
            if (is_done_)
                return;

            if (wait_time_ > 0f)
            {
                wait_time_ -= delta_time;
                if (wait_time_ > 0f)
                    return;
            }

            if (exp_time_ < limit_)
            {
                exp_time_ *= 2f;
                if (exp_time_ > limit_)
                    exp_time_ = limit_;
            }

            wait_time_ = exp_time_;

            callback_();
        }

        public override string ToString ()
        {
            return string.Format("'{0}' exponent timer. limit:{1}", name_, limit_);
        }


        float limit_ = 0f;
        float exp_time_ = 1f;
    }
}
