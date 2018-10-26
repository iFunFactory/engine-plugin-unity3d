// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;


namespace Fun
{
    public class ResponseTimeout
    {
        public bool Add (int msg_type, float waiting_time)
        {
            lock (lock_)
            {
                if (int_list_.ContainsKey(msg_type))
                {
                    if (debug != null)
                        debug.LogWarning("'{0}' response timeout type is already added. Ignored.", msg_type);
                    return false;
                }

                int_list_[msg_type] = new ResponseInfo<int>(msg_type, waiting_time);

                if (debug != null)
                    debug.LogDebug("Response timeout type added - '{0}' ({1}s)", msg_type, waiting_time);
            }

            return true;
        }

        public bool Add (string msg_type, float waiting_time)
        {
            if (msg_type == null || msg_type.Length <= 0)
                return false;

            lock (lock_)
            {
                if (str_list_.ContainsKey(msg_type))
                {
                    if (debug != null)
                        debug.LogWarning("'{0}' response timeout type is already added. Ignored.", msg_type);
                    return false;
                }

                str_list_[msg_type] = new ResponseInfo<string>(msg_type, waiting_time);

                if (debug != null)
                    debug.LogDebug("Response timeout type added - '{0}' ({1}s)", msg_type, waiting_time);
            }

            return true;
        }

        public bool Remove (int msg_type)
        {
            lock (lock_)
            {
                if (int_list_.ContainsKey(msg_type))
                {
                    if (debug != null)
                        debug.LogDebug("Response timeout type removed - {0}", msg_type);

                    int_list_.Remove(msg_type);
                    return true;
                }
            }

            return false;
        }

        public bool Remove (string msg_type)
        {
            lock (lock_)
            {
                if (str_list_.ContainsKey(msg_type))
                {
                    if (debug != null)
                        debug.LogDebug("Response timeout type removed - {0}", msg_type);

                    str_list_.Remove(msg_type);
                    return true;
                }
            }

            return false;
        }

        public void Update (float deltaTime)
        {
            updateList<int>(ref int_list_, deltaTime);
            updateList<string>(ref str_list_, deltaTime);
        }

        void updateList<T> (ref Dictionary<T, ResponseInfo<T>> list, float deltaTime)
        {
            Dictionary<T, ResponseInfo<T>> temp_list = null;

            lock (lock_)
            {
                if (list.Count <= 0)
                    return;

                temp_list = list;
                list = new Dictionary<T, ResponseInfo<T>>();
            }

            List<T> remove_list = new List<T>();
            foreach (ResponseInfo<T> item in temp_list.Values)
            {
                item.wait_time -= deltaTime;
                if (item.wait_time <= 0f)
                {
                    debug.LogWarning("'{0}' message waiting time has been exceeded.", item.type);
                    remove_list.Add(item.type);

                    onTimeoutCallback<T>(item.type);
                }
            }

            if (remove_list.Count > 0)
            {
                foreach (T key in remove_list)
                {
                    temp_list.Remove(key);
                }
            }

            if (temp_list.Count > 0)
            {
                lock (lock_)
                {
                    if (list.Count <= 0)
                    {
                        list = temp_list;
                    }
                    else
                    {
                        foreach (var item in temp_list)
                        {
                            list[item.Key] = item.Value;
                        }
                    }
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                int_list_.Clear();
                str_list_.Clear();
            }
        }

        public void SetCallbackHandler<T> (Action<T> func)
        {
            if (typeof(T) == typeof(int))
            {
                int_callback_ = func as Action<int>;
            }
            else if (typeof(T) == typeof(string))
            {
                str_callback_ = func as Action<string>;
            }
            else
            {
                if (debug != null)
                    debug.LogWarning("Response Timeout is not support '{0}' type.", typeof(T));
            }
        }

        void onTimeoutCallback<T> (T type)
        {
            if (typeof(T) == typeof(int))
            {
                if (int_callback_ != null)
                    int_callback_(Convert.ToInt32(type));
            }
            else if (typeof(T) == typeof(string))
            {
                if (str_callback_ != null)
                    str_callback_(type as string);
            }
            else
            {
                if (debug != null)
                    debug.LogWarning("Response Timeout is not support '{0}' type.", typeof(T));
            }
        }

        public FunDebugLog debugLog
        {
            set { debug = value; }
        }


        class ResponseInfo<T>
        {
            public ResponseInfo (T type, float wait_time)
            {
                this.type = type;
                this.wait_time = wait_time;
            }

            public T type = default(T);
            public float wait_time = 0f;
        }


        object lock_ = new object();
        Action<int> int_callback_;
        Action<string> str_callback_;
        FunDebugLog debug = null;

        Dictionary<int, ResponseInfo<int>> int_list_ = new Dictionary<int, ResponseInfo<int>>();
        Dictionary<string, ResponseInfo<string>> str_list_ = new Dictionary<string, ResponseInfo<string>>();
    }
}
