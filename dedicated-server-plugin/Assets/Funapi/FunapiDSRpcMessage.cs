// Copyright 2019 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// protobuf
using ProtoBuf;
using funapi.distribution.fun_dedicated_server_rpc_message;
using Newtonsoft.Json.Linq;


namespace Fun
{
    public class FunapiDSRpcMessage
    {
        public static FunDedicatedServerRpcMessage CreateMessage (object msg, MessageType msg_type)
        {
            if (msg is Enum)
                msg = (Int32)msg;

            try
            {
                FunDedicatedServerRpcMessage _msg = new FunDedicatedServerRpcMessage();
                Extensible.AppendValue(serializer_, _msg, (int)msg_type, DataFormat.Default, msg);
                return _msg;
            }
            catch (Exception e)
            {
                Type type = MessageTable.GetType(msg_type);
                FunDebug.LogError("FunapiDSRpcMessage.CreateMessage - Failed to create '{0}' ({1})\n{2}",
                                  type, msg_type, e.ToString());
            }

            return null;
        }

        public static object GetMessage (FunDedicatedServerRpcMessage msg, MessageType msg_type)
        {
            return GetMessage<object>(msg, msg_type);
        }

        public static T GetMessage<T> (FunDedicatedServerRpcMessage msg, MessageType msg_type)
        {
            try
            {
                object _msg = null;
                Extensible.TryGetValue(serializer_, MessageTable.GetType(msg_type), msg,
                                       (int)msg_type, DataFormat.Default, true, out _msg);

                FunDebug.Assert(_msg != null, "TryGetValue() failed. Please check the message type.");

                return (T)_msg;
            }
            catch (Exception e)
            {
                Type type = MessageTable.GetType(msg_type);
                FunDebug.LogError("FunapiDSRpcMessage.GetMessage - Failed to decode '{0}' ({1})\n{2}",
                                  type, msg_type, e.ToString());
            }

            return default(T);
        }


        public static byte[] Serialize (object msg)
        {
            MemoryStream stream = new MemoryStream();
            serializer_.Serialize(stream, msg);

            uint length = (uint)stream.Length;
            if (length <= 0)
                return null;

            byte[] buffer = new byte[4 + length];

            // message length: must be network byte order
            byte[] bytes = BitConverter.GetBytes(length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes); // gets big endian
            Buffer.BlockCopy(bytes, 0, buffer, 0, 4);

            // message body
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(buffer, 4, (int)length);
            stream = null;

            return buffer;
        }

        public static object Deserialize (ArraySegment<byte> buffer)
        {
            MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
            return serializer_.Deserialize(stream, null, msgtype_);
        }

        public static object ParseJson (string buffer)
        {
            return json_helper_.Deserialize(buffer);
        }


        public static void DebugString (FunDedicatedServerRpcMessage msg, StringBuilder log)
        {
            foreach (MessageType msg_type in Enum.GetValues(typeof(MessageType)))
            {
                object _msg = null;
                Type type = MessageTable.GetType(msg_type);
                bool succeed = Extensible.TryGetValue(serializer_, type, msg,
                                                      (int)msg_type, DataFormat.Default, true, out _msg);
                if (succeed)
                {
                    var json = JObject.FromObject(_msg);
                    log.Append(prettyJsonString(json));

                    debugString((IExtensible)_msg, type, log);
                }
            }
        }

        static void debugString (IExtensible msg, Type type, StringBuilder log)
        {
            MethodInfo method = typeof(MessageTable).GetMethod("GetExtensions");
            if (method != null)
            {
                Dictionary<int, Type> extensions = method.Invoke(null, new object[] {type}) as Dictionary<int, Type>;
                foreach (KeyValuePair<int, Type> pair in extensions)
                {
                    object _msg = null;
                    bool succeed = Extensible.TryGetValue(serializer_, pair.Value, msg,
                                                          pair.Key, DataFormat.Default, true, out _msg);
                    if (succeed)
                    {
                        var json = JObject.FromObject(_msg);
                        log.Append(prettyJsonString(json));

                        debugString((IExtensible)_msg, pair.Value, log);
                    }
                }
            }
        }

        static String prettyJsonString (JObject json)
        {
            List<JProperty> properties = new List<JProperty>(json.Properties());
            foreach (JProperty item in properties)
            {
                if (item.Name.Contains("Specified"))
                    item.Remove();
            }

            return Regex.Replace(json.ToString(), @" |\t|\n|\r", "");
        }


        static JsonAccessor json_helper_ = new DictionaryJsonAccessor();

        static Type msgtype_ = typeof(FunDedicatedServerRpcMessage);
        static FunDedicatedServerRpcMessageSerializer serializer_ = new FunDedicatedServerRpcMessageSerializer();
    }
}
