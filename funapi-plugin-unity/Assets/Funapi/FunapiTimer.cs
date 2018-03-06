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
        public void Add (FunapiTimer timer, bool removeOld = false)
        {
            if (timer == null)
                return;

            if (removeOld && list_.Exists(timer.name))
                list_.Remove(timer.name);

            list_.Add(timer);

            if (debug != null)
                debug.DebugLog1("[Timer] {0}", timer.ToString());
        }

        public bool Remove (FunapiTimer timer)
        {
            if (list_.Remove(timer))
            {
                if (debug != null)
                    debug.DebugLog1("[Timer] '{0}' timer deleted.", timer.name);
                return true;
            }

            return false;
        }

        public bool Remove (string name)
        {
            if (list_.Remove(name))
            {
                if (debug != null)
                    debug.DebugLog1("[Timer] '{0}' timer deleted.", name);
                return true;
            }

            return false;
        }

        public void Clear ()
        {
            list_.Clear();

            if (debug != null)
                debug.DebugLog1("[Timer] all timers were deleted.");
        }

        public void Update (float deltaTime)
        {
            list_.Update(deltaTime);
        }

        public FunDebugLog debug { private get; set; }


        ConcurrentList<FunapiTimer> list_ = new ConcurrentList<FunapiTimer>();
    }


    public class FunapiTimer : IConcurrentItem
    {
        public FunapiTimer (string name, float start_delay, Action callback)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("timer name");

            if (callback == null && !GetType().Equals(typeof(FunapiLoopTimer)))
                throw new ArgumentNullException("timer callback");

            name_ = name;
            start_delay_ = start_delay;
            callback_ = callback;
        }

        public virtual void Update (float deltaTime)
        {
            if (isDone)
                return;

            elapsed_ += deltaTime;
            if (elapsed_ < start_delay_)
                return;

            callback_();

            isDone = true;
        }

        public string name { get { return name_; } }

        public bool isDone { get; set; }

        public override string ToString ()
        {
            return string.Format("'{0}' timer. delay: {1}s", name_, start_delay_);
        }


        // Member variables.
        protected string name_;
        protected float start_delay_ = 0f;
        protected float elapsed_ = 0f;
        protected Action callback_ = null;
    }


    public class FunapiLoopTimer : FunapiTimer
    {
        public FunapiLoopTimer (string name, float interval, Action<float> callback)
            : this(name, 0f, interval, callback)
        {
        }

        public FunapiLoopTimer (string name, float start_delay,
                                float interval, Action<float> callback)
            : base(name, start_delay, null)
        {
            if (callback == null)
                throw new ArgumentNullException("loop timer callback");

            interval_ = interval;
            callback_ = callback;
        }

        public override void Update (float deltaTime)
        {
            if (isDone)
                return;

            elapsed_ += deltaTime;

            if (start_delay_ > 0f)
            {
                if (elapsed_ < start_delay_)
                    return;

                callback_(elapsed_ - start_delay_);

                start_delay_ = 0f;
                elapsed_ = 0f;
                return;
            }

            if (elapsed_ < interval_)
                return;

            callback_(elapsed_);

            elapsed_ = 0f;
        }

        public override string ToString ()
        {
            return string.Format("'{0}' loop timer. delay: {1}s interval: {2}s",
                                 name_, start_delay_, interval_);
        }


        float interval_ = 0f;
        new Action<float> callback_ = null;
    }


    public class FunapiTimeoutTimer : FunapiTimer
    {
        public FunapiTimeoutTimer (string name, float timeout, Action callback)
            : base(name, 0f, callback)
        {
            timeout_ = timeout;
            onUpdate = onDefaultUpdate;
        }

        // It will be loop timer
        public FunapiTimeoutTimer (string name, float interval, Action<float> loopCallback,
                                   float timeout, Action callback)
            : base(name, 0f, callback)
        {
            timeout_ = timeout;
            interval_ = interval;
            loop_callback_ = loopCallback;
            onUpdate = onLoopUpdate;
        }

        public void Reset ()
        {
            elapsed_ = 0f;
        }

        public override void Update (float deltaTime)
        {
            onUpdate(deltaTime);
        }

        void onDefaultUpdate (float deltaTime)
        {
            if (isDone)
                return;

            elapsed_ += deltaTime;
            if (elapsed_ < timeout_)
                return;

            callback_();

            isDone = true;
        }

        void onLoopUpdate (float deltaTime)
        {
            if (isDone)
                return;

            elapsed_ += deltaTime;
            if (elapsed_ >= timeout_)
            {
                callback_();
                isDone = true;
                return;
            }

            elapsed_loop_ += deltaTime;
            if (elapsed_loop_ < interval_)
                return;

            loop_callback_(elapsed_loop_);

            elapsed_loop_ = 0f;
        }

        public override string ToString ()
        {
            if (loop_callback_ != null)
                return string.Format("'{0}' timeout loop timer. interval: {1}s timeout: {2}s",
                                     name_, interval_, timeout_);
            else
                return string.Format("'{0}' timeout timer. timeout: {1}s", name_, timeout_);
        }


        float timeout_ = 0f;
        float interval_ = 0f;
        float elapsed_loop_ = 0f;
        Action<float> onUpdate;
        Action<float> loop_callback_;
    }


    // Call function at intervals of exponent 2
    public class FunapiExponentTimer : FunapiTimer
    {
        public FunapiExponentTimer (string name, float limit, Action callback)
            : base(name, 0f, callback)
        {
            limit_ = limit;
        }

        public override void Update (float deltaTime)
        {
            if (isDone)
                return;

            elapsed_ += deltaTime;
            if (elapsed_ < exp_time_)
                return;

            elapsed_ = 0f;

            if (exp_time_ < limit_)
            {
                exp_time_ *= 2f;
                if (exp_time_ > limit_)
                    exp_time_ = limit_;
            }

            callback_();
        }

        public override string ToString ()
        {
            return string.Format("'{0}' exponent timer. limit: {1}s", name_, limit_);
        }


        float limit_ = 0f;
        float exp_time_ = 1f;
    }
}
