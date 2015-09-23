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
#if NO_UNITY
    class Debug
    {
        public static void Log(object message) {
            Console.WriteLine(message);
        }

        public static void LogWarning(object message) {
            Console.WriteLine("Warning: " + message);
        }

        public static void LogError(object message) {
            Console.WriteLine("Error: " + message);
        }
    }

    class Time
    {
        public const float deltaTime = 0.3f; // dummy delta time
    }
#endif


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

#if NO_UNITY
        [Conditional("DEBUG_LOG")]
        public static void Log (object message)
        {
            Console.WriteLine(message);
        }

        [Conditional("DEBUG_LOG")]
        public static void LogError (object message)
        {
            Console.WriteLine("Error: " + message);
        }

        [Conditional("DEBUG_LOG")]
        public static void LogWarning (object message)
        {
            Console.WriteLine("Warning: " + message);
        }
#else
        [Conditional("DEBUG_LOG")]
        public static void Log (object message)
        {
            UnityEngine.Debug.Log(message);
        }

        [Conditional("DEBUG_LOG")]
        public static void Log (object message, UnityEngine.Object context)
        {
            UnityEngine.Debug.Log(message, context);
        }

        [Conditional("DEBUG_LOG")]
        public static void LogError (object message)
        {
            UnityEngine.Debug.LogError(message);
        }

        [Conditional("DEBUG_LOG")]
        public static void LogError (object message, UnityEngine.Object context)
        {
            UnityEngine.Debug.LogError(message, context);
        }

        [Conditional("DEBUG_LOG")]
        public static void LogWarning (object message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        [Conditional("DEBUG_LOG")]
        public static void LogWarning (object message, UnityEngine.Object context)
        {
            UnityEngine.Debug.LogWarning(message, context);
        }
#endif
    }
}
