// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

//#define ENABLE_SAVE_LOG
//#define ENABLE_OUTPUT

#if UNITY_EDITOR || NO_UNITY
#define ENABLE_LOG
#define LOG_LEVEL_1
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
        public static void Assert (bool condition)
        {
            if (!condition)
            {
                throw new Exception();
            }
        }

        public static void Assert (bool condition, string message)
        {
            if (!condition)
            {
                LogError(message);
                throw new Exception(message);
            }
        }

        public static void Assert (bool condition, string message, params object[] args)
        {
            if (!condition)
            {
                string error = string.Format(message, args);
                LogError(error);
                throw new Exception(error);
            }
        }

#if !NO_UNITY
        public static void Log (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
            string text = getTimeLog("I", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback(text);
#endif
#endif
        }

        public static void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
            string text = getTimeLog("W", string.Format(message, args));
            UnityEngine.Debug.LogWarning(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback(text);
#endif
#endif
        }

        public static void LogError (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
            string text = getTimeLog("E", string.Format(message, args));
            UnityEngine.Debug.LogError(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback(text);
#endif
#endif
        }

        public static void DebugLog1 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_2 || LOG_LEVEL_3)
            string text = getTimeLog("D", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback(text);
#endif
#endif
        }

        public static void DebugLog2 (string message, params object[] args)
        {
#if ENABLE_LOG && LOG_LEVEL_3
            string text = getTimeLog("D", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback(text);
#endif
#endif
        }
#else
        public static void Log (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
            Console.WriteLine(getTimeLog("I", string.Format(message, args)));
#endif
        }

        public static void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
            Console.WriteLine(getTimeLog("W", string.Format(message, args)));
#endif
        }

        public static void LogError (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
            Console.WriteLine(getTimeLog("E", string.Format(message, args)));
#endif
        }

        public static void DebugLog1 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_2 || LOG_LEVEL_3)
            Console.WriteLine(getTimeLog("D", string.Format(message, args)));
#endif
        }

        public static void DebugLog2 (string message, params object[] args)
        {
#if ENABLE_LOG && LOG_LEVEL_3
            Console.WriteLine(getTimeLog("D", string.Format(message, args)));
#endif
        }
#endif

        static string getTimeLog (string type, string message)
        {
            string log = string.Format("{0}[{1}] {2}", type, DateTime.Now.ToLongTimeString(), message);
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

            Log("Logs are saved.\n{0}", path);

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


        const string kLogPath = "/Logs/";
        const int kLogBufferMax = 1024 * 1024;
        static StringBuilder log_buffer_ = new StringBuilder(kLogBufferMax);
#else
        public static void SaveLogs () {}
        public static int GetLogLength() { return 0; }
        public static string GetLogString() { return ""; }
        public static void RemoveAllLogFiles () {}
#endif


#if ENABLE_OUTPUT
        public delegate void OutputListener (string message);
        public static event OutputListener OutputCallback;
#endif
    }


    public class FunDebugLog
    {
        protected void setDebugObject (object obj)
        {
#if ENABLE_LOG && LOG_LEVEL_3
            string str = string.Format("{0:X}", obj.GetHashCode());
            hash_ = str.Length < 6 ? str : str.Substring(0, 6);
#endif
        }

        protected void Log (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
#if LOG_LEVEL_3
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.Log(message, args);
#endif
        }

        protected void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
#if LOG_LEVEL_3
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.LogWarning(message, args);
#endif
        }

        protected void LogError (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_1 || LOG_LEVEL_2 || LOG_LEVEL_3)
#if LOG_LEVEL_3
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.LogError(message, args);
#endif
        }

        protected void DebugLog1 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_2 || LOG_LEVEL_3)
#if LOG_LEVEL_3
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.DebugLog1(message, args);
#endif
        }

        protected void DebugLog2 (string message, params object[] args)
        {
#if ENABLE_LOG && LOG_LEVEL_3
            message = string.Format("[{0}] {1}", hash_, message);
            FunDebug.DebugLog2(message, args);
#endif
        }


#if ENABLE_LOG && LOG_LEVEL_3
        string hash_ = "";
#endif
    }
}
