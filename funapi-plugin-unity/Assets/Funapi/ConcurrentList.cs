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


    public sealed class ConcurrentList<T> where T : IConcurrentItem
    {
        public string Add (T item)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            lock (lock_)
            {
                pending_.Add(item);
            }

            return item.name;
        }

        public bool Remove (T item)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            lock (lock_)
            {
                if (list_.Contains(item))
                {
                    item.isDone = true;
                    return true;
                }

                return pending_.Remove(item);
            }
        }

        public bool Remove (string name)
        {
            lock (lock_)
            {
                List<T> list = list_.FindAll(predicate(name));
                if (list.Count > 0)
                {
                    if (list.Count > 1)
                        FunDebug.LogWarning("There are too many items with the same name as '{0}'.", name);

                    list.ForEach(t => { t.isDone = true; });
                    return true;
                }

                int count = pending_.RemoveAll(predicate(name));
                if (count > 0)
                {
                    if (count > 1)
                        FunDebug.LogWarning("There are too many items with the same name as '{0}'.", name);
                    return true;
                }
            }

            return false;
        }

        public void Update (float delta_time)
        {
            lock (lock_)
            {
                // Adds from pending list
                if (pending_.Count > 0)
                {
                    list_.AddRange(pending_);
                    pending_.Clear();
                }

                // Updates item
                if (list_.Count > 0)
                {
                    list_.RemoveAll(t => { return t.isDone; });

                    foreach (T item in list_)
                    {
                        item.Update(delta_time);
                    }
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                pending_.Clear();
                list_.ForEach(t => { t.isDone = true; });
            }
        }

        public void ForEach (Action<T> action)
        {
            lock (lock_)
            {
                if (list_.Count > 0)
                    list_.ForEach(action);
            }
        }

        public bool Exists (string name)
        {
            lock (lock_)
            {
                if (list_.Exists(predicate(name)))
                    return true;

                if (pending_.Exists(predicate(name)))
                    return true;
            }

            return false;
        }

        static Predicate<T> predicate (string name)
        {
            return t => { return t.name == name; };
        }


        // Member variables.
        object lock_ = new object();
        List<T> list_ = new List<T>();
        List<T> pending_ = new List<T>();
    }
}
