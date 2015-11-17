// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

#define DEBUG
//#define DEBUG_LOG

using System;
using System.Diagnostics;


namespace Fun
{
    // Utility class
    public class DebugUtils
    {
        [Conditional("DEBUG")]
        public static void Assert (bool condition)
        {
            if (!condition)
            {
                throw new Exception();
            }
        }

        [Conditional("DEBUG")]
        public static void Assert (bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

#if !NO_UNITY
        public static void Log (string message, params object[] args)
        {
            UnityEngine.Debug.Log(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        public static void LogWarning (string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        public static void LogError (string message, params object[] args)
        {
            UnityEngine.Debug.LogError(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("DEBUG_LOG")]
        public static void DebugLog (string message, params object[] args)
        {
            UnityEngine.Debug.Log(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("DEBUG_LOG")]
        public static void DebugLogWarning (string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }

        [Conditional("DEBUG_LOG")]
        public static void DebugLogError (string message, params object[] args)
        {
            UnityEngine.Debug.LogError(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(message, args)));
        }
#else
        public static void Log (string message, params object[] args)
        {
            Console.WriteLine(string.Format(message, args));
        }

        public static void LogWarning (string message, params object[] args)
        {
            Console.WriteLine("Warning: " + string.Format(message, args));
        }

        public static void LogError (string message, params object[] args)
        {
            Console.WriteLine("Error: " + string.Format(message, args));
        }

        [Conditional("DEBUG_LOG")]
        public static void DebugLog (string message, params object[] args)
        {
            Console.WriteLine(string.Format(message, args));
        }

        [Conditional("DEBUG_LOG")]
        public static void DebugLogWarning (string message, params object[] args)
        {
            Console.WriteLine("Warning: " + string.Format(message, args));
        }

        [Conditional("DEBUG_LOG")]
        public static void DebugLogError (string message, params object[] args)
        {
            Console.WriteLine("Error: " + string.Format(message, args));
        }
#endif
    }


#if NO_UNITY
    class Debug
    {
        public static void Log(object message)
        {
            Console.WriteLine(message);
        }

        public static void LogWarning(object message)
        {
            Console.WriteLine("Warning: " + message);
        }

        public static void LogError(object message)
        {
            Console.WriteLine("Error: " + message);
        }
    }
#endif
}
