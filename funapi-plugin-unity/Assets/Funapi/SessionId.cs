// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Text;


namespace Fun
{
    public class SessionId
    {
        public void SetId (object obj)
        {
            lock (lock_)
            {
                if (obj is SessionId)
                {
                    SessionId sid = obj as SessionId;
                    id_string_ = sid.id_string;
                    id_array_ = new byte[sid.id_array.Length];
                    Buffer.BlockCopy(sid.id_array, 0, id_array_, 0, sid.id_array.Length);
                }
                else if (obj is string)
                {
                    string str = obj as string;
                    id_string_ = str;
                    id_array_ = Encoding.UTF8.GetBytes(str);
                }
                else if (obj is byte[])
                {
                    id_string_ = ToString(obj);

                    byte[] array = obj as byte[];
                    id_array_ = new byte[array.Length];
                    Buffer.BlockCopy(array, 0, id_array_, 0, array.Length);
                }
                else
                {
                    FunDebug.LogWarning("SessionId.SetId - Wrong object type for session id. obj:{0}", obj);
                }
            }
        }

        public void Clear ()
        {
            lock (lock_)
            {
                id_string_ = "";
                id_array_ = new byte[0];
            }
        }

        public bool IsValid
        {
            get { lock (lock_) { return id_string_.Length > 0 && id_array_.Length > 0; } }
        }

        public bool IsStringArray
        {
            get { lock (lock_) { return id_array_.Length != kArrayLength; } }
        }

        public static string ToString (object obj)
        {
            if (obj is string)
            {
                return obj as string;
            }
            else if (obj is byte[])
            {
                byte[] array = obj as byte[];
                if (array.Length == kArrayLength)
                {
                    return makeHexString(array);
                }
                else if (array.Length == kStringLength)
                {
                    return Encoding.UTF8.GetString(array);
                }
            }

            return "";
        }

        public override int GetHashCode ()
        {
            return base.GetHashCode ();
        }

        public override bool Equals (object obj)
        {
            lock (lock_)
            {
                if (obj is SessionId)
                {
                    return id_string_ == (obj as SessionId).id_string;
                }
                else if (obj is string)
                {
                    return id_string_ == (string)obj;
                }
                else if (obj is byte[])
                {
                    return FunapiUtils.EqualsBytes(id_array_, obj as byte[]);
                }
            }

            return base.Equals (obj);
        }

        // Overload operator functions
        public static implicit operator string (SessionId sid)
        {
            return sid.id_string;
        }

        public static implicit operator byte[] (SessionId sid)
        {
            byte[] array = new byte[sid.id_array.Length];
            Buffer.BlockCopy(sid.id_array, 0, array, 0, sid.id_array.Length);
            return array;
        }

        public static bool operator == (SessionId sid, object obj)
        {
            return sid.Equals(obj);
        }

        public static bool operator != (SessionId sid, object obj)
        {
            return !sid.Equals(obj);
        }

        // Make session id of hexadecimal type
        static string makeHexString (byte[] array)
        {
            string hex = "";
            for (int i = 0; i < array.Length; ++i)
            {
                hex += array[i].ToString("x2");
                if (i == 3 || i == 5 || i == 7 || i == 9)
                    hex += "-";
            }

            return hex;
        }

        // Properties for protect members
        string id_string
        {
            get { lock (lock_) { return id_string_; } }
        }

        byte[] id_array
        {
            get { lock (lock_) { return id_array_; } }
        }


        public const int kArrayLength = 16;
        public const int kStringLength = 36;

        object lock_ = new object();
        string id_string_ = "";
        byte[] id_array_ = new byte[0];
    }
}
