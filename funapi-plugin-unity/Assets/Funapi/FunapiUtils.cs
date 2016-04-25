// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.IO;
using System.Collections.Generic;
#if !NO_UNITY
using UnityEngine;
#endif

// Utility classes
namespace Fun
{
    public class FunapiUpdater
    {
        protected void CreateUpdater ()
        {
#if !NO_UNITY
            if (game_object_ != null)
                return;

            game_object_ = new GameObject(GetType().ToString());
            if (game_object_ != null)
            {
                FunapiObject obj = game_object_.AddComponent(typeof(FunapiObject)) as FunapiObject;
                if (obj != null)
                {
                    funapi_object_ = obj;
                    obj.Updater = Update;
                    obj.OnQuit = OnQuit;
                }

                DebugUtils.DebugLog("'{0}' GameObject was created.", game_object_.name);
            }
#else
            funapi_object_ = new FunapiObject();
            funapi_object_.Updater = Update;
            funapi_object_.OnQuit = OnQuit;
#endif
        }

        protected void ReleaseUpdater ()
        {
#if !NO_UNITY
            if (game_object_ == null)
                return;

            DebugUtils.DebugLog("'{0}' GameObject was destroyed", game_object_.name);
            GameObject.Destroy(game_object_);
            game_object_ = null;
#endif
            funapi_object_ = null;
        }

#if NO_UNITY
        public void UpdateFrame ()
        {
            if (funapi_object_ != null)
                funapi_object_.Update();
        }
#endif

        protected virtual bool Update (float deltaTime)
        {
            event_.Update(deltaTime);
            return true;
        }

        protected virtual void OnQuit ()
        {
        }

        protected MonoBehaviour mono
        {
            get { return funapi_object_; }
        }

        protected ThreadSafeEventList event_list
        {
            get { return event_; }
        }


        // For use a MonoBehaviour
        class FunapiObject : MonoBehaviour
        {
#if !NO_UNITY
            void Awake ()
            {
                prev_ticks_ = DateTime.UtcNow.Ticks;
                deltaTime_ = 0.03f;

                DontDestroyOnLoad(gameObject);
            }

            void Update ()
            {
                long now = DateTime.UtcNow.Ticks;
                int milliseconds = (int)((now - prev_ticks_) / 10000);
                deltaTime_ = Math.Min((float)milliseconds / 1000f, kDeltaTimeMax);
                prev_ticks_ = now;

                Updater(deltaTime_);
            }

            void OnApplicationQuit()
            {
                OnQuit();
            }
#else
            public void Update ()
            {
                Updater(0.03f);
            }
#endif

            public Func<float, bool> Updater
            {
                set; private get;
            }

            public Action OnQuit
            {
                set; private get;
            }

#if !NO_UNITY
            private static readonly float kDeltaTimeMax = 0.3f;

            private long prev_ticks_ = 0;
            private float deltaTime_ = 0f;
#endif
        }

#if !NO_UNITY
        private GameObject game_object_ = null;
#endif
        private FunapiObject funapi_object_ = null;
        private ThreadSafeEventList event_ = new ThreadSafeEventList();
    }


    public class ThreadSafeEventList
    {
        public int Add (Action callback, float start_delay = 0f)
        {
            return AddItem(callback, start_delay);
        }

        public int Add (Action callback, bool repeat, float repeat_time)
        {
            return AddItem(callback, 0f, repeat, repeat_time);
        }

        public int Add (Action callback, float start_delay, bool repeat, float repeat_time)
        {
            return AddItem(callback, start_delay, repeat, repeat_time);
        }

        public void Remove (int key)
        {
            if (key <= 0)
                return;

            lock (lock_)
            {
                if (!original_.ContainsKey(key) && !pending_.ContainsKey(key))
                    return;

                if (pending_.ContainsKey(key))
                {
                    pending_.Remove(key);
                }
                else if (!removing_.Contains(key))
                {
                    removing_.Add(key);
                }
                else
                {
                    return;
                }
            }
        }

        public bool ContainsKey (int key)
        {
            lock (lock_)
            {
                return original_.ContainsKey(key) || pending_.ContainsKey(key);
            }
        }

        public int Count
        {
            get
            {
                lock (lock_)
                {
                    return original_.Count + pending_.Count;
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                pending_.Clear();
                is_all_clear_ = true;
            }
        }

        public void Update (float deltaTime)
        {
            if (Count <= 0)
                return;

            lock (lock_)
            {
                if (is_all_clear_)
                {
                    original_.Clear();
                    removing_.Clear();
                    is_all_clear_ = false;
                    return;
                }

                CheckRemoveList();

                // Adds timer
                if (pending_.Count > 0)
                {
                    foreach (KeyValuePair<int, Item> p in pending_)
                    {
                        original_.Add(p.Key, p.Value);
                    }

                    pending_.Clear();
                }

                if (original_.Count <= 0)
                    return;

                foreach (KeyValuePair<int, Item> p in original_)
                {
                    if (removing_.Contains(p.Key))
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
                        removing_.Add(p.Key);

                    item.callback();
                }

                CheckRemoveList();
            }
        }


        // Gets new id
        private int GetNewId ()
        {
            do
            {
                ++next_id_;
                if (next_id_ >= int.MaxValue)
                    next_id_ = 1;
            }
            while (ContainsKey(next_id_));

            return next_id_;
        }

        // Adds a action
        private int AddItem (Action callback, float start_delay = 0f,
                             bool repeat = false, float repeat_time = 0f)
        {
            if (callback == null)
            {
                throw new ArgumentNullException ("callback");
            }

            lock (lock_)
            {
                int key = GetNewId();
                pending_.Add(key, new Item(callback, start_delay, repeat, repeat_time));
                return key;
            }
        }

        // Removes actions from remove list
        private void CheckRemoveList ()
        {
            lock (lock_)
            {
                if (removing_.Count <= 0)
                    return;

                foreach (int key in removing_)
                {
                    if (original_.ContainsKey(key))
                    {
                        original_.Remove(key);
                    }
                    else if (pending_.ContainsKey(key))
                    {
                        pending_.Remove(key);
                    }
                }

                removing_.Clear();
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


        // member variables.
        private static int next_id_ = 0;
        private object lock_ = new object();
        private Dictionary<int, Item> original_ = new Dictionary<int, Item>();
        private Dictionary<int, Item> pending_ = new Dictionary<int, Item>();
        private List<int> removing_ = new List<int>();
        private bool is_all_clear_ = false;
    }


    internal class FunapiUtils
    {
        // Gets local path
        public static string GetLocalDataPath
        {
            get
            {
                if (path_ == null)
                {
#if !NO_UNITY
                    if (Application.platform == RuntimePlatform.Android ||
                        Application.platform == RuntimePlatform.IPhonePlayer)
                    {
                        path_ = Application.persistentDataPath;
                    }
                    else if (Application.platform == RuntimePlatform.OSXEditor ||
                             Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        string path = Application.dataPath;
                        path_ = path.Substring(0, path.LastIndexOf('/')) + "/Data";
                    }
                    else
                    {
                        path_ = Application.dataPath;
                    }
#else
                    path_ = "";
#endif
                }

                return path_;
            }
        }

        private static string path_ = null;
    }


#if NO_UNITY
    public class MonoBehaviour
    {
        public void StartCoroutine (Action func)
        {
            func();
        }
    }
#endif
}
