// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine;


class TestBase : YieldIndication
{
    public TestBase ()
    {
        updater = new GameObject("TestUpdater").AddComponent<Updater>();
    }

    public override bool keepWaiting
    {
        get
        {
            if (isFinished)
                GameObject.Destroy(updater.gameObject);

            return !isFinished;
        }
    }

    protected void startCoroutine (IEnumerator func)
    {
        updater.StartCoroutine(func);
    }

    protected void setTestTimeout (float seconds)
    {
        updater.StartCoroutine(onTestTimedOut(seconds));
    }

    protected void setTestTimeoutWithoutFail (float seconds)
    {
        updater.StartCoroutine(onTestTimedOut(seconds, false));
    }

    IEnumerator onTestTimedOut (float seconds, bool with_fail = true)
    {
        yield return new SleepForSeconds(seconds);

        onTestFinished();
        if (with_fail)
            Assert.Fail("'{0}' Test has timed out.", GetType().ToString());
    }

    protected virtual void onTestFinished ()
    {
        isFinished = true;
    }

    public FunapiTimerList timer { get { return updater.timer; } }

    class Updater : MonoBehaviour
    {
        void Update ()
        {
            if (timer_ != null)
            {
                // gets delta time
                long now = DateTime.UtcNow.Ticks;
                float delta = (now - prev_) / 10000000f;
                prev_ = now;

                timer_.Update(delta);
            }
        }

        public FunapiTimerList timer
        {
            get
            {
                if (timer_ == null)
                {
                    timer_ = new FunapiTimerList();
                    prev_ = DateTime.UtcNow.Ticks;
                }

                return timer_;
            }
        }

        long prev_;
        public FunapiTimerList timer_ = null;
    }


    protected bool isFinished = false;

    Updater updater = null;
}
