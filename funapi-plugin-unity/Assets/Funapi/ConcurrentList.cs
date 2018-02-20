// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;


namespace Fun
{
    public interface IConcurrentItem
    {
        void Update (float deltaTime);

        string name { get; }
        bool isDone { get; set; }
    }


    class ConcurrentList<T> where T : IConcurrentItem
    {
        public string Add (T item)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            lock (lock_)
            {
                pending_list_.Add(item);
            }

            return item.name;
        }

        public bool Remove (T item)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            lock (lock_)
            {
                if (primary_list_.Contains(item))
                {
                    item.isDone = true;
                    return true;
                }

                return pending_list_.Remove(item);
            }
        }

        public bool Remove (string name)
        {
            lock (lock_)
            {
                List<T> list = primary_list_.FindAll(predicate(name));
                if (list.Count > 0)
                {
                    if (list.Count > 1)
                        FunDebug.LogWarning("There are too many items with the same name as '{0}'.", name);

                    list.ForEach(t => { t.isDone = true; });
                    return true;
                }

                int count = pending_list_.RemoveAll(predicate(name));
                if (count > 0)
                {
                    if (count > 1)
                        FunDebug.LogWarning("There are too many items with the same name as '{0}'.", name);
                    return true;
                }
            }

            return false;
        }

        public bool Exists (string name)
        {
            lock (lock_)
            {
                if (primary_list_.Exists(predicate(name)))
                    return true;

                if (pending_list_.Exists(predicate(name)))
                    return true;
            }

            return false;
        }

        public void Clear ()
        {
            lock (lock_)
            {
                pending_list_.Clear();
                primary_list_.ForEach(t => { t.isDone = true; });
            }
        }

        public void Update (float delta_time)
        {
            lock (lock_)
            {
                // adds from pending list
                if (pending_list_.Count > 0)
                {
                    primary_list_.AddRange(pending_list_);
                    pending_list_.Clear();
                }

                // updates item
                if (primary_list_.Count > 0)
                {
                    primary_list_.RemoveAll(t => { return t.isDone; });

                    foreach (T item in primary_list_)
                    {
                        item.Update(delta_time);
                    }
                }
            }
        }

        public void ForEach (Action<T> action)
        {
            lock (lock_)
            {
                if (primary_list_.Count > 0)
                    primary_list_.ForEach(action);
            }
        }

        static Predicate<T> predicate (string name)
        {
            return t => { return t.name == name; };
        }


        // Member variables.
        object lock_ = new object();
        List<T> primary_list_ = new List<T>();
        List<T> pending_list_ = new List<T>();
    }
}
