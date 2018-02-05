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
        public static readonly int kPluginVersion = 246;
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
