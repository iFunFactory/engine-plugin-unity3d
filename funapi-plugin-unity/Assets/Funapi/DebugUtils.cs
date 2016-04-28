// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

#if UNITY_EDITOR || NO_UNITY
#define ENABLE_LOG
#endif
//#define ENABLE_DEBUG

using System;
using System.Diagnostics;


namespace Fun
{
    // Utility class
    public class FunDebug
    {
        [Conditional("ENABLE_DEBUG")]
        public static void Assert (bool condition)
        {
            if (!condition)
            {
                throw new Exception();
            }
        }

        [Conditional("ENABLE_DEBUG")]
        public static void Assert (bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

#if !NO_UNITY
        [Conditional("ENABLE_LOG")]
        public static void Log (string message, params object[] args)
        {
            UnityEngine.Debug.Log(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("ENABLE_LOG")]
        public static void LogWarning (string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("ENABLE_LOG")]
        public static void LogError (string message, params object[] args)
        {
            UnityEngine.Debug.LogError(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("ENABLE_DEBUG")]
        public static void DebugLog (string message, params object[] args)
        {
            UnityEngine.Debug.Log(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("ENABLE_DEBUG")]
        public static void DebugLogWarning (string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("ENABLE_DEBUG")]
        public static void DebugLogError (string message, params object[] args)
        {
            UnityEngine.Debug.LogError(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }
#else
        public static void Log (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(string.Format(message, args));
#endif
        }

        public static void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine("Warning: " + string.Format(message, args));
#endif
        }

        public static void LogError (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine("Error: " + string.Format(message, args));
#endif
        }

        public static void DebugLog (string message, params object[] args)
        {
#if ENABLE_DEBUG
            Console.WriteLine(string.Format(message, args));
#endif
        }

        public static void DebugLogWarning (string message, params object[] args)
        {
#if ENABLE_DEBUG
            Console.WriteLine("Warning: " + string.Format(message, args));
#endif
        }

        public static void DebugLogError (string message, params object[] args)
        {
#if ENABLE_DEBUG
            Console.WriteLine("Error: " + string.Format(message, args));
#endif
        }
#endif
    }
}
