// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;


namespace Fun
{
    public class FunapiTimerList
    {
        public void Add (FunapiTimer timer)
        {
            if (timer == null)
                return;

            if (list_.Exists(timer.name))
                list_.Remove(timer.name);

            list_.Add(timer);
            FunDebug.DebugLog1("[Timer] {0}", timer.ToString());
        }

        public void Remove (string name)
        {
            if (list_.Remove(name))
                FunDebug.DebugLog1("[Timer] '{0}' timer deleted.", name);
        }

        public void Clear ()
        {
            list_.Clear();
        }

        public void Update (float deltaTime)
        {
            list_.Update(deltaTime);
        }


        ConcurrentList<FunapiTimer> list_ = new ConcurrentList<FunapiTimer>();
    }


    public class FunapiTimer : IConcurrentItem
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

        public virtual void Update (float deltaTime)
        {
            if (isDone)
                return;

            if (wait_time_ > 0f)
            {
                wait_time_ -= deltaTime;
                if (wait_time_ > 0f)
                    return;
            }

            callback_();

            isDone = true;
        }

        public string name { get { return name_; } }

        public bool isDone { get; set; }

        public override string ToString ()
        {
            return string.Format("'{0}' timer. delay:{1}", name_, wait_time_);
        }


        // Member variables.
        protected string name_;
        protected float wait_time_ = 0f;
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

        public override void Update (float deltaTime)
        {
            if (isDone)
                return;

            if (wait_time_ > 0f)
            {
                wait_time_ -= deltaTime;
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

        public override void Update (float deltaTime)
        {
            if (isDone)
                return;

            if (wait_time_ > 0f)
            {
                wait_time_ -= deltaTime;
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
