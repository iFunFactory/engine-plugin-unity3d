// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

//#define ENABLE_DEBUG
//#define ENABLE_SAVE_LOG

#if ENABLE_DEBUG || UNITY_EDITOR || NO_UNITY
#define ENABLE_LOG
#endif

using System;
using System.Diagnostics;
#if ENABLE_SAVE_LOG
using System.IO;
using System.Text;
#endif


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
            UnityEngine.Debug.Log(GetTimeLog(string.Format(message, args)));
        }

        [Conditional("ENABLE_LOG")]
        public static void LogWarning (string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(GetTimeLog(string.Format(message, args)));
        }

        [Conditional("ENABLE_LOG")]
        public static void LogError (string message, params object[] args)
        {
            UnityEngine.Debug.LogError(GetTimeLog(string.Format(message, args)));
        }

        [Conditional("ENABLE_DEBUG")]
        public static void DebugLog (string message, params object[] args)
        {
            UnityEngine.Debug.Log(GetTimeLog(string.Format(message, args)));
        }

        [Conditional("ENABLE_DEBUG")]
        public static void DebugLogWarning (string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(GetTimeLog(string.Format(message, args)));
        }

        [Conditional("ENABLE_DEBUG")]
        public static void DebugLogError (string message, params object[] args)
        {
            UnityEngine.Debug.LogError(GetTimeLog(string.Format(message, args)));
        }
#else
        public static void Log (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(GetTimeLog(string.Format(message, args)));
#endif
        }

        public static void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(GetTimeLog(string.Format(message, args)));
#endif
        }

        public static void LogError (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(GetTimeLog(string.Format(message, args)));
#endif
        }

        public static void DebugLog (string message, params object[] args)
        {
#if ENABLE_DEBUG
            Console.WriteLine(GetTimeLog(string.Format(message, args)));
#endif
        }

        public static void DebugLogWarning (string message, params object[] args)
        {
#if ENABLE_DEBUG
            Console.WriteLine(GetTimeLog(string.Format(message, args)));
#endif
        }

        public static void DebugLogError (string message, params object[] args)
        {
#if ENABLE_DEBUG
            Console.WriteLine(GetTimeLog(string.Format(message, args)));
#endif
        }
#endif

        private static string GetTimeLog (string message)
        {
            string log = string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), message);
#if ENABLE_SAVE_LOG
            if (log_buffer_.Length + log.Length >= kLogBufferMax)
                SaveLogs();

            log_buffer_.Append(log);
            log_buffer_.AppendLine();
#endif
            return log;
        }

#if ENABLE_SAVE_LOG
        public static void SaveLogs ()
        {
            if (log_buffer_.Length <= 0)
                return;

            string path = FunapiUtils.GetLocalDataPath + kLogPath;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            DateTime time = DateTime.Now;
            path += string.Format("Log_{0}-{1:D2}-{2:D2}-{3:D2}-{4:D2}-{5:D2}.txt",
                                  time.Year, time.Month, time.Day,
                                  time.Hour, time.Minute, time.Second);

            FileStream file = File.Open(path, FileMode.Create);
            StreamWriter stream = new StreamWriter(file);
            stream.Write(log_buffer_.ToString().ToCharArray(), 0, log_buffer_.Length);
            stream.Flush();
            stream.Close();

            ClearLogBuffer();
        }

        public static int GetLogLength ()
        {
            return log_buffer_.Length;
        }

        public static string GetLogString ()
        {
            return log_buffer_.ToString();
        }

        public static void ClearLogBuffer ()
        {
            // Reset buffer
            log_buffer_.Length = 0;
        }

        public static void RemoveAllLogFiles ()
        {
            string path = FunapiUtils.GetLocalDataPath + kLogPath;
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }


        private static readonly string kLogPath = "/Logs/";
        private static readonly int kLogBufferMax = 1024 * 1024;
        private static StringBuilder log_buffer_ = new StringBuilder(kLogBufferMax);
#else
        public static void SaveLogs () {}
        public static int GetLogLength() { return 0; }
        public static string GetLogString() { return ""; }
        public static void RemoveAllLogFiles () {}
#endif
    }
}
