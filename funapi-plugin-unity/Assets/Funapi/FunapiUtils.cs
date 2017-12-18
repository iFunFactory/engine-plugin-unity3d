// Copyright 2013 iFunFactory Inc. All Rights Reserved.
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
        public static readonly int kPluginVersion = 240;
    }


    public class FunapiUpdater : FunDebugLog
    {
        public FunapiUpdater ()
        {
            setDebugObject(this);

#if NO_UNITY
            funapi_object_ = new FunapiObject();
            funapi_object_.Updater = onUpdate;
            funapi_object_.OnPause = onPaused;
            funapi_object_.OnQuit = onQuit;
#endif
        }

        protected void createUpdater ()
        {
#if !NO_UNITY
            lock (lock_)
            {
                if (game_object_ != null)
                    return;

                game_object_ = new GameObject(GetType().ToString());
                if (game_object_ != null)
                {
                    FunapiObject obj = game_object_.AddComponent(typeof(FunapiObject)) as FunapiObject;
                    if (obj != null)
                    {
                        funapi_object_ = obj;
                        funapi_object_.Updater = onUpdate;
                        funapi_object_.OnPause = onPaused;
                        funapi_object_.OnQuit = onQuit;
                    }

                    DebugLog1("CreateUpdater - '{0}' was created.", game_object_.name);
                }
            }
#endif
        }

        protected void releaseUpdater ()
        {
#if !NO_UNITY
            event_.Add(() => {
                lock (lock_)
                {
                    if (game_object_ == null)
                        return;

                    DebugLog1("ReleaseUpdater - '{0}' was destroyed", game_object_.name);
                    GameObject.Destroy(game_object_);
                    game_object_ = null;
                    funapi_object_ = null;
                }
            });
#endif
        }

#if NO_UNITY
        public void updateFrame ()
        {
            lock (lock_)
            {
                if (funapi_object_ != null)
                    funapi_object_.Update();
            }
        }
#endif

        protected virtual bool onUpdate (float deltaTime)
        {
            event_.Update(deltaTime);
            return true;
        }

        protected virtual void onPaused (bool paused) {}

        protected virtual void onQuit () {}


        // Properties
        protected MonoBehaviour mono
        {
            get { lock (lock_) { return funapi_object_; } }
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

            void OnApplicationPause (bool isPaused)
            {
                OnPause(isPaused);
            }

            void OnApplicationQuit ()
            {
                OnQuit();
            }
#endif

            public void Update ()
            {
                long now = DateTime.UtcNow.Ticks;
                int milliseconds = (int)((now - prev_ticks_) / 10000);
                deltaTime_ = Math.Min((float)milliseconds / 1000f, kDeltaTimeMax);
                prev_ticks_ = now;

                Updater(deltaTime_);
            }

            public Func<float, bool> Updater { private get; set; }

            public Action<bool> OnPause { private get; set; }

            public Action OnQuit { private get; set; }


            // Member variables
            static readonly float kDeltaTimeMax = 0.3f;

            long prev_ticks_ = 0;
            float deltaTime_ = 0f;
        }


        // Member variables
        object lock_ = new object();
#if !NO_UNITY
        GameObject game_object_ = null;
#endif
        FunapiObject funapi_object_ = null;
        ThreadSafeEventList event_ = new ThreadSafeEventList();
    }


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
