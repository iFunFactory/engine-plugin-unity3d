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


    public sealed class ConcurrentSimpleList<T>
    {
        public void Add (T item)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            lock (lock_)
            {
                pending_.Add(item);
            }
        }

        public bool Remove (T item)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            lock (lock_)
            {
                if (list_.Contains(item))
                {
                    remove_.Add(item);
                    return true;
                }

                return pending_.Remove(item);
            }
        }

        public void Update ()
        {
            lock (lock_)
            {
                // Adds from pending list
                if (pending_.Count > 0)
                {
                    list_.AddRange(pending_);
                    pending_.Clear();
                }

                // Removes items
                if (remove_.Count > 0)
                {
                    foreach (T item in remove_)
                    {
                        if (list_.Contains(item))
                            list_.Remove(item);
                    }

                    remove_.Clear();
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                remove_.AddRange(list_);
                pending_.Clear();
            }
        }

        public List<T> List
        {
            get { lock (lock_) { return list_; } }
        }

        public bool Contains (T item)
        {
            lock (lock_) { return list_.Contains(item); }
        }

        public int Count
        {
            get { lock (lock_) { return list_.Count + pending_.Count; } }
        }


        // Member variables.
        object lock_ = new object();
        List<T> list_ = new List<T>();
        List<T> pending_ = new List<T>();
        List<T> remove_ = new List<T>();
    }


    public class ConcurrentDictionary<Key, Value>
    {
        public void Add (Key key, Value value)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            lock (lock_)
            {
                pending_.Add(key, value);
            }
        }

        public bool Remove (Key key)
        {
            if (key == null)
                throw new ArgumentNullException("key");

            lock (lock_)
            {
                if (list_.ContainsKey(key))
                {
                    remove_.Add(key);
                    return true;
                }

                return pending_.Remove(key);
            }
        }

        public void Update ()
        {
            lock (lock_)
            {
                // Adds from pending list
                if (pending_.Count > 0)
                {
                    foreach (var item in pending_)
                    {
                        list_.Add(item.Key, item.Value);
                    }
                    pending_.Clear();
                }

                // Removes items
                if (remove_.Count > 0)
                {
                    foreach (Key key in remove_)
                    {
                        if (list_.ContainsKey(key))
                            list_.Remove(key);
                    }

                    remove_.Clear();
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                remove_.AddRange(list_.Keys);
                pending_.Clear();
            }
        }

        public Dictionary<Key, Value> Container
        {
            get { lock (lock_) { return list_; } }
        }

        public bool ContainsKey (Key key)
        {
            lock (lock_)
            {
                return list_.ContainsKey(key);
            }
        }

        public Value GetValue (Key key)
        {
            lock (lock_) { return list_[key]; }
        }

        public int Count
        {
            get { lock (lock_) { return list_.Count + pending_.Count; } }
        }


        // Member variables.
        object lock_ = new object();
        Dictionary<Key, Value> list_ = new Dictionary<Key, Value>();
        Dictionary<Key, Value> pending_ = new Dictionary<Key, Value>();
        List<Key> remove_ = new List<Key>();
    }
}
