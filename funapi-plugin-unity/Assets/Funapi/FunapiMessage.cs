// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// protobuf
using ProtoBuf;
using funapi.network.fun_message;
using funapi.service.multicast_message;

using Newtonsoft.Json.Linq;

namespace Fun
{
    public class FunapiMessage
    {
        public FunapiMessage (TransportProtocol protocol, string msg_type, object message = null,
                              EncryptionType enc = EncryptionType.kDefaultEncryption)
        {
            this.protocol = protocol;
            this.enc_type = enc;
            this.msg_type = msg_type;
            this.message = message;
        }

        public byte[] GetBytes (FunEncoding encoding)
        {
            if (message == null)
            {
                return new byte[0];
            }

            if (encoding == FunEncoding.kJson)
            {
                string str = json_helper_.Serialize(message);
                return System.Text.Encoding.UTF8.GetBytes(str);
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                MemoryStream stream = new MemoryStream();
                serializer_.Serialize(stream, message);

                byte[] buffer = new byte[stream.Length];
                if (stream.Length > 0)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    stream.Read(buffer, 0, buffer.Length);
                }

                return buffer;
            }

            return null;
        }

        public static JsonAccessor JsonHelper
        {
            get { return json_helper_; }
            set { json_helper_ = value; }
        }

        // For FunMessage
        public static FunMessage CreateFunMessage (object msg, MessageType msg_type)
        {
            if (msg is Enum)
                msg = (Int32)msg;

            try
            {
                FunMessage _msg = new FunMessage();
                Extensible.AppendValue(serializer_, _msg, (int)msg_type, DataFormat.Default, msg);
                return _msg;
            }
            catch (Exception e)
            {
                Type type = MessageTable.GetType(msg_type);
                FunDebug.LogError("FunapiMessage.CreateFunMessage - Failed to create '{0}' ({1})\n{2}",
                                  type, msg_type, e.ToString());

                if (ParsingErrorCallback != null)
                    ParsingErrorCallback(type);
            }

            return null;
        }

        public static object GetMessage (FunMessage msg, MessageType msg_type)
        {
            return GetMessage<object>(msg, msg_type);
        }

        public static T GetMessage<T> (FunMessage msg, MessageType msg_type)
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
                FunDebug.LogError("FunapiMessage.GetMessage - Failed to decode '{0}' ({1})\n{2}",
                                  type, msg_type, e.ToString());

                if (ParsingErrorCallback != null)
                    ParsingErrorCallback(type);
            }

            return default(T);
        }

        // For Multicast messages
        public static FunMulticastMessage CreateMulticastMessage (object msg, MulticastMessageType msg_type)
        {
            if (msg is Enum)
                msg = (Int32)msg;

            try
            {
                FunMulticastMessage _msg = new FunMulticastMessage();
                Extensible.AppendValue(serializer_, _msg, (int)msg_type, DataFormat.Default, msg);
                return _msg;
            }
            catch (Exception e)
            {
                Type type = MessageTable.GetType(msg_type);
                FunDebug.LogError("FunapiMessage.CreateMulticastMessage - Failed to create '{0}' ({1})\n{2}",
                                  type, msg_type, e.ToString());

                if (ParsingErrorCallback != null)
                    ParsingErrorCallback(type);
            }

            return null;
        }

        public static object GetMulticastMessage (FunMulticastMessage msg, MulticastMessageType msg_type)
        {
            return GetMulticastMessage<object>(msg, msg_type);
        }

        public static T GetMulticastMessage<T> (FunMulticastMessage msg, MulticastMessageType msg_type)
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
                FunDebug.LogError("FunapiMessage.GetMulticastMessage - Failed to decode '{0}' ({1})\n{2}",
                                  type, msg_type, e.ToString());

                if (ParsingErrorCallback != null)
                    ParsingErrorCallback(type);
            }

            return default(T);
        }

        public static object Deserialize (string buffer)
        {
            return json_helper_.Deserialize(buffer);
        }

        public static object Deserialize (ArraySegment<byte> buffer, FunEncoding encoding)
        {
            if (encoding == FunEncoding.kJson)
            {
                string str = System.Text.Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                //FunDebug.LogDebug("Parsed json: {0}", str);
                return json_helper_.Deserialize(str);
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                return serializer_.Deserialize(stream, null, funmsg_type_);
            }

            return null;
        }

        public static void DebugString (FunMessage msg, StringBuilder log)
        {
            foreach(MessageType msg_type in Enum.GetValues(typeof(MessageType)))
            {
                object _msg = null;
                Type type = MessageTable.GetType(msg_type);
                bool succeed = Extensible.TryGetValue(serializer_, type, msg,
                                                      (int)msg_type, DataFormat.Default, true, out _msg);
                if (succeed)
                {
                    var json = JObject.FromObject(_msg);
                    log.AppendFormat(" {0}", prettyJsonString(json.ToString()));

                    debugString((IExtensible)_msg, type, log);
                    log.AppendFormat("}}");
                }
            }
        }

        static void debugString (IExtensible msg, Type type, StringBuilder log)
        {
            MethodInfo method = typeof(MessageTable).GetMethod("GetExtensions");
            if (method != null)
            {
                Dictionary<int, Type> extensions = method.Invoke(null, new object[] {type}) as Dictionary<int, Type>;
                foreach(KeyValuePair<int, Type> pair in extensions)
                {
                    object _msg = null;
                    bool succeed = Extensible.TryGetValue(serializer_, pair.Value, msg,
                                                          pair.Key, DataFormat.Default, true, out _msg);
                    if (succeed)
                    {
                        var json = JObject.FromObject(_msg);
                        log.AppendFormat(" {0}", prettyJsonString(json.ToString()));

                        debugString((IExtensible)_msg, pair.Value, log);
                        log.AppendFormat("}}");
                    }
                }
            }
        }

        static String prettyJsonString (string ugly_string)
        {
            ugly_string = Regex.Replace(ugly_string, @"\{|\}|\t|\n|\r|", "");
            List<String> list = new List<String>(ugly_string.Split(','));
            list.RemoveAll(item => item.Contains("Specified"));
            for(int i = 0; i < list.Count; ++i)
            {
                list[i] = list[i].Trim();
            }

            return string.Format("{{{0}", String.Join(",", list.ToArray()));
        }


        // member variables.
        public TransportProtocol protocol;
        public EncryptionType enc_type;
        public string msg_type;
        public object message;

        public bool ready = false;
        public ArraySegment<byte> header;
        public ArraySegment<byte> body;
        public string enc_header = "";
        public UInt32 seq = 0;

        public object reply = null;

        // json-related members.
        static JsonAccessor json_helper_ = new DictionaryJsonAccessor();

        // protobuf-related members.
        static Type funmsg_type_ = typeof(FunMessage);
        static FunMessageSerializer serializer_ = new FunMessageSerializer();

        // message error callback
        public static event Action<Type> ParsingErrorCallback;    // message type
    }
}
