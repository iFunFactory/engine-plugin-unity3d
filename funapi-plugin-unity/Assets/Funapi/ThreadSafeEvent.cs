// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using UnityEngine;


namespace Fun
{
    public class ThreadSafeEventList
    {
        public uint Add (Action callback, float start_delay = 0f)
        {
            return addItem(callback, start_delay);
        }

        public uint Add (Action callback, bool repeat, float repeat_time)
        {
            return addItem(callback, 0f, repeat, repeat_time);
        }

        public uint Add (Action callback, float start_delay, bool repeat, float repeat_time)
        {
            return addItem(callback, start_delay, repeat, repeat_time);
        }

        public void Remove (uint key)
        {
            lock (lock_)
            {
                if (all_clear_)
                {
                    if (pending2_.ContainsKey(key))
                        pending2_.Remove(key);
                    return;
                }

                if (list_.ContainsKey(key))
                {
                    if (!expired_.Contains(key))
                        expired_.Add(key);
                }
                else if (pending_.ContainsKey(key))
                {
                    pending_.Remove(key);
                }
            }
        }

        public bool ContainsKey (uint key)
        {
            lock (lock_)
            {
                return list_.ContainsKey(key) || pending_.ContainsKey(key);
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                all_clear_ = true;
            }
        }

        public void Update (float deltaTime)
        {
            lock (lock_)
            {
                checkStatus();

                // Adds pending items
                if (pending_.Count > 0)
                {
                    foreach (KeyValuePair<uint, Item> p in pending_)
                    {
                        list_.Add(p.Key, p.Value);
                    }

                    pending_.Clear();
                }

                // Update routine
                if (list_.Count > 0)
                {
                    foreach (KeyValuePair<uint, Item> p in list_)
                    {
                        if (expired_.Contains(p.Key))
                            continue;

                        Item item = p.Value;
                        if (item.remaining_time > 0f)
                        {
                            item.remaining_time -= deltaTime;
                            if (item.remaining_time > 0f)
                                continue;
                        }

                        if (item.repeat)
                            item.remaining_time = item.repeat_time;
                        else
                            expired_.Add(p.Key);

                        item.callback();
                    }
                }

                checkStatus();
            }
        }


        // Adds a action
        uint addItem (Action callback, float start_delay = 0f, bool repeat = false, float repeat_time = 0f)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            lock (lock_)
            {
                uint key = key_;
                while (ContainsKey(key))
                    ++key;
                key_ = key + 1;

                if (all_clear_)
                    pending2_.Add(key, new Item(callback, start_delay, repeat, repeat_time));
                else
                    pending_.Add(key, new Item(callback, start_delay, repeat, repeat_time));

                return key;
            }
        }

        void checkStatus ()
        {
            lock (lock_)
            {
                if (all_clear_)
                {
                    list_.Clear();
                    pending_.Clear();
                    expired_.Clear();
                    all_clear_ = false;

                    if (pending2_.Count > 0)
                    {
                        Dictionary<uint, Item> tmp = list_;
                        list_ = pending2_;
                        pending2_ = tmp;
                    }
                    return;
                }

                // Removes items from the expired list
                if (expired_.Count > 0)
                {
                    foreach (uint key in expired_)
                    {
                        if (list_.ContainsKey(key))
                        {
                            list_.Remove(key);
                        }
                        else if (pending_.ContainsKey(key))
                        {
                            pending_.Remove(key);
                        }
                    }

                    expired_.Clear();
                }
            }
        }


        class Item
        {
            public bool repeat;
            public float repeat_time;
            public float remaining_time;
            public Action callback;

            public Item (Action callback, float start_delay = 0f,
                         bool repeat = false, float repeat_time = 0f)
            {
                this.callback = callback;
                this.repeat = repeat;
                this.repeat_time = repeat_time;
                this.remaining_time = start_delay;
            }
        }


        // Member variables
        uint key_ = 100;
        bool all_clear_ = false;
        object lock_ = new object();
        Dictionary<uint, Item> list_ = new Dictionary<uint, Item>();
        Dictionary<uint, Item> pending_ = new Dictionary<uint, Item>();
        Dictionary<uint, Item> pending2_ = new Dictionary<uint, Item>();
        List<uint> expired_ = new List<uint>();
    }
}
