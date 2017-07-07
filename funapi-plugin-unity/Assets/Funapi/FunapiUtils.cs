// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
#if NO_UNITY
using System.Threading;
#else
using UnityEngine;
#endif

// Utility classes
namespace Fun
{
    // Funapi plugin version
    public class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 216;
    }


    public class FunapiUpdater : FunDebugLog
    {
        public FunapiUpdater ()
        {
            setDebugObject(this);
        }

        protected void createUpdater ()
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
                    obj.Updater = onUpdate;
                    obj.OnQuit = onQuit;
                }

                DebugLog1("CreateUpdater - '{0}' was created.", game_object_.name);
            }
#else
            if (funapi_object_ != null)
                return;

            funapi_object_ = new FunapiObject();
            funapi_object_.Updater = onUpdate;
            funapi_object_.OnQuit = onQuit;
#endif
        }

        protected void releaseUpdater ()
        {
#if !NO_UNITY
            if (game_object_ == null)
                return;

            DebugLog1("ReleaseUpdater - '{0}' was destroyed", game_object_.name);
            GameObject.Destroy(game_object_);
            game_object_ = null;
            funapi_object_ = null;
#endif
        }

#if NO_UNITY
        public void updateFrame ()
        {
            if (funapi_object_ != null)
                funapi_object_.Update();
        }
#endif

        protected virtual bool onUpdate (float deltaTime)
        {
            event_.Update(deltaTime);
            return true;
        }

        protected virtual void onQuit ()
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

            void OnApplicationQuit ()
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
                private get; set;
            }

            public Action OnQuit
            {
                private get; set;
            }

#if !NO_UNITY
            static readonly float kDeltaTimeMax = 0.3f;

            long prev_ticks_ = 0;
            float deltaTime_ = 0f;
#endif
        }

#if !NO_UNITY
        GameObject game_object_ = null;
#endif
        FunapiObject funapi_object_ = null;
        ThreadSafeEventList event_ = new ThreadSafeEventList();
    }


    public class ThreadSafeEventList
    {
        public int Add (Action callback, float start_delay = 0f)
        {
            return addItem(callback, start_delay);
        }

        public int Add (Action callback, bool repeat, float repeat_time)
        {
            return addItem(callback, 0f, repeat, repeat_time);
        }

        public int Add (Action callback, float start_delay, bool repeat, float repeat_time)
        {
            return addItem(callback, start_delay, repeat, repeat_time);
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

                checkRemoveList();

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

                checkRemoveList();
            }
        }


        // Gets new id
        int getNewId ()
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
        int addItem (Action callback, float start_delay = 0f, bool repeat = false, float repeat_time = 0f)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            lock (lock_)
            {
                int key = getNewId();
                pending_.Add(key, new Item(callback, start_delay, repeat, repeat_time));
                return key;
            }
        }

        // Removes actions from remove list
        void checkRemoveList ()
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


        static int next_id_ = 0;

        // member variables.
        object lock_ = new object();
        Dictionary<int, Item> original_ = new Dictionary<int, Item>();
        Dictionary<int, Item> pending_ = new Dictionary<int, Item>();
        List<int> removing_ = new List<int>();
        bool is_all_clear_ = false;
    }


    public class FunapiUtils
    {
        public static string BytesToHex (byte[] array)
        {
            string hex = "";
            foreach (byte n in array)
                hex += n.ToString("x2");

            return hex;
        }

        public static byte[] HexToBytes (string hex)
        {
            byte[] array = new byte[hex.Length / 2];
            for (int i = 0; i < array.Length; ++i)
                array[i] = (byte)Convert.ToByte(hex.Substring(i * 2, 2), 16);

            return array;
        }

        public static bool EqualsBytes (byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        // Gets assets path
        public static string GetAssetsPath
        {
            get
            {
#if !NO_UNITY
                if (Application.platform == RuntimePlatform.OSXEditor ||
                    Application.platform == RuntimePlatform.WindowsEditor)
                {
                    return Application.dataPath;
                }
#endif

                return "";
            }
        }

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

        static string path_ = null;
    }


#if NO_UNITY
    public class MonoBehaviour
    {
        public void StartCoroutine (Action func)
        {
            Thread t = new Thread(new ThreadStart(func));
            t.Start();
        }
    }
#endif
}
