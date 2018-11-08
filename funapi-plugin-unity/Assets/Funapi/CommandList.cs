// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections.Generic;


namespace Fun
{
    public interface ICommand
    {
        void Excute ();

        string name { get; }
        bool canExcute { get; set; }
        bool keepWaiting { get; }
    }


    public sealed class CommandList
    {
        public bool Add (ICommand cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("command");

            cmd.canExcute = true;

            lock (pending_lock_)
            {
                pending_.Add(cmd);
            }

            return true;
        }

        public void Update ()
        {
            lock (lock_)
            {
                if (clear_)
                {
                    if (list_.Count > 0)
                    {
                        list_.Clear();
                    }

                    clear_ = false;
                }

                lock (pending_lock_)
                {
                    // Adds from pending list
                    if (pending_.Count > 0)
                    {
                        list_.AddRange(pending_);
                        pending_.Clear();
                    }
                }

                // Excutes commands
                while (list_.Count > 0)
                {
                    ICommand cmd = list_[0];

                    if (cmd.canExcute)
                    {
                        cmd.canExcute = false;
                        cmd.Excute();
                    }

                    if (cmd.keepWaiting)
                        break;

                    list_.RemoveAt(0);
                }
            }
        }

        public void Clear ()
        {
            clear_ = true;
        }


        object lock_ = new object();
        object pending_lock_ = new object();
        List<ICommand> list_ = new List<ICommand>();
        List<ICommand> pending_ = new List<ICommand>();
        bool clear_ = false;
    }
}
