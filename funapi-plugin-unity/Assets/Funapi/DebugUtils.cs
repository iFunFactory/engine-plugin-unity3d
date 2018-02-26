// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

//#define ENABLE_SAVE_LOG
//#define ENABLE_OUTPUT

#if UNITY_EDITOR || NO_UNITY
#define ENABLE_LOG
// LOG_LEVEL_1 ~ LOG_LEVEL_4
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
#if ENABLE_LOG
            string text = getTimeLog("I", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback("I", text);
#endif
#endif
        }

        public static void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG
            string text = getTimeLog("W", string.Format(message, args));
            UnityEngine.Debug.LogWarning(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback("W", text);
#endif
#endif
        }

        public static void LogError (string message, params object[] args)
        {
#if ENABLE_LOG
            string text = getTimeLog("E", string.Format(message, args));
            UnityEngine.Debug.LogError(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback("E", text);
#endif
#endif
        }

        public static void DebugLog1 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_2 || LOG_LEVEL_3 || LOG_LEVEL_4)
            string text = getTimeLog("D", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback("D", text);
#endif
#endif
        }

        public static void DebugLog2 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_3 || LOG_LEVEL_4)
            string text = getTimeLog("D", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback("D", text);
#endif
#endif
        }

        public static void DebugLog3 (string message, params object[] args)
        {
#if ENABLE_LOG && LOG_LEVEL_4
            string text = getTimeLog("D", string.Format(message, args));
            UnityEngine.Debug.Log(text);

#if ENABLE_OUTPUT
            if (OutputCallback != null)
                OutputCallback("D", text);
#endif
#endif
        }
#else
        public static void Log (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(getTimeLog("I", string.Format(message, args)));
#endif
        }

        public static void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(getTimeLog("W", string.Format(message, args)));
#endif
        }

        public static void LogError (string message, params object[] args)
        {
#if ENABLE_LOG
            Console.WriteLine(getTimeLog("E", string.Format(message, args)));
#endif
        }

        public static void DebugLog1 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_2 || LOG_LEVEL_3 || LOG_LEVEL_4)
            Console.WriteLine(getTimeLog("D", string.Format(message, args)));
#endif
        }

        public static void DebugLog2 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_3 || LOG_LEVEL_4)
            Console.WriteLine(getTimeLog("D", string.Format(message, args)));
#endif
        }

        public static void DebugLog3 (string message, params object[] args)
        {
#if ENABLE_LOG && LOG_LEVEL_4
            Console.WriteLine(getTimeLog("D", string.Format(message, args)));
#endif
        }
#endif

        static string getTimeLog (string type, string message)
        {
            string log = string.Format("{0}[{1}] {2}", type, DateTime.Now.ToLongTimeString(), message);
#if ENABLE_SAVE_LOG
            lock (buffer_lock_)
            {
                if ((log_buffer_.Length + log.Length) >= kLogBufferMax)
                    SaveLogs();

                log_buffer_.Append(log);
                log_buffer_.AppendLine();
            }
#endif
            return log;
        }

#if ENABLE_SAVE_LOG
        static string getSavePath ()
        {
            string path = FunapiUtils.GetLocalDataPath;
            if (path.Length > 0 && path[path.Length - 1] != '/')
                path += "/";
            path += kLogPath;

            return path;
        }

        public static void SaveLogs ()
        {
            lock (buffer_lock_)
            {
                if (log_buffer_.Length <= 0)
                    return;

                string path = getSavePath();
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                DateTime time = DateTime.Now;
                path += string.Format("Log_{0}-{1:D2}-{2:D2}-{3:D2}-{4:D2}-{5:D2}.txt",
                                      time.Year, time.Month, time.Day,
                                      time.Hour, time.Minute, time.Second);
#if NO_UNITY
                Console.WriteLine(string.Format("Logs are saved. {0}", path));
#else
                UnityEngine.Debug.Log(string.Format("Logs are saved. {0}", path));
#endif

                FileStream file = File.Open(path, FileMode.Create);
                StreamWriter stream = new StreamWriter(file);
                stream.Write(log_buffer_.ToString().ToCharArray(), 0, log_buffer_.Length);
                stream.Flush();
                stream.Close();

                ClearLogBuffer();
            }
        }

        public static int GetLogLength ()
        {
            lock (buffer_lock_)
            {
                return log_buffer_.Length;
            }
        }

        public static string GetLogString ()
        {
            lock (buffer_lock_)
            {
                return log_buffer_.ToString();
            }
        }

        public static void ClearLogBuffer ()
        {
            // Reset buffer
            lock (buffer_lock_)
            {
                log_buffer_.Length = 0;
            }
        }

        public static void RemoveAllLogFiles ()
        {
            string path = getSavePath();
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }


        const string kLogPath = "Logs/";
        const int kLogBufferMax = 1024 * 1024;

        static object buffer_lock_ = new object();
        static StringBuilder log_buffer_ = new StringBuilder(kLogBufferMax);
#else
        public static void SaveLogs () {}
        public static int GetLogLength() { return 0; }
        public static string GetLogString() { return ""; }
        public static void RemoveAllLogFiles () {}
#endif

#if ENABLE_OUTPUT
        public delegate void OutputListener (string type, string message);
        public static event OutputListener OutputCallback;
#endif
    }


    public sealed class FunDebugLog
    {
        public void SetDebugObject (object obj)
        {
#if ENABLE_LOG && (LOG_LEVEL_3 || LOG_LEVEL_4)
            string str = string.Format("{0:X}", obj.GetHashCode());
            hash_ = str.Length < 6 ? str : str.Substring(0, 6);
#endif
        }

        public void Log (string message, params object[] args)
        {
#if ENABLE_LOG
#if LOG_LEVEL_3 || LOG_LEVEL_4
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.Log(message, args);
#endif
        }

        public void LogWarning (string message, params object[] args)
        {
#if ENABLE_LOG
#if LOG_LEVEL_3 || LOG_LEVEL_4
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.LogWarning(message, args);
#endif
        }

        public void LogError (string message, params object[] args)
        {
#if ENABLE_LOG
#if LOG_LEVEL_3 || LOG_LEVEL_4
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.LogError(message, args);
#endif
        }

        public void DebugLog1 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_2 || LOG_LEVEL_3 || LOG_LEVEL_4)
#if LOG_LEVEL_3 || LOG_LEVEL_4
            message = string.Format("[{0}] {1}", hash_, message);
#endif
            FunDebug.DebugLog1(message, args);
#endif
        }

        public void DebugLog2 (string message, params object[] args)
        {
#if ENABLE_LOG && (LOG_LEVEL_3 || LOG_LEVEL_4)
            message = string.Format("[{0}] {1}", hash_, message);
            FunDebug.DebugLog2(message, args);
#endif
        }

        public void DebugLog3 (string message, params object[] args)
        {
#if ENABLE_LOG && LOG_LEVEL_4
            message = string.Format("[{0}] {1}", hash_, message);
            FunDebug.DebugLog3(message, args);
#endif
        }


#if ENABLE_LOG && (LOG_LEVEL_3 || LOG_LEVEL_4)
        string hash_ = "";
#endif
    }
}
