// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.IO;
using System.Collections;
using UnityEngine;

using ProtoBuf;
using funapi.network.fun_message;


namespace Fun
{
    public class FunapiMessage
    {
        public FunapiMessage (TransportProtocol protocol, string msg_type, object message,
                              EncryptionType enc = EncryptionType.kDefaultEncryption)
        {
            this.protocol = protocol;
            this.enc_type = enc;
            this.msg_type = msg_type;
            this.message = message;
        }

        // Sets expected reply
        public void SetReply (string reply_type, float reply_timeout, TimeoutEventHandler callback)
        {
            this.reply_type = reply_type;
            this.reply_timeout = reply_timeout;
            this.timeout_callback = callback;
        }

        public byte[] GetBytes (FunEncoding encoding)
        {
            byte[] buffer = null;

            if (encoding == FunEncoding.kJson)
            {
                string str = json_helper_.Serialize(message);
                buffer = System.Text.Encoding.UTF8.GetBytes(str);
            }
            else
            {
                MemoryStream stream = new MemoryStream();
                serializer_.Serialize(stream, message);

                buffer = new byte[stream.Length];
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(buffer, 0, buffer.Length);
            }

            return buffer;
        }

        public static JsonAccessor JsonHelper
        {
            get { return json_helper_; }
            set { json_helper_ = value; }
        }

        public static FunMessage CreateFunMessage (object msg, MessageType msg_type)
        {
            if (msg is Enum)
                msg = (Int32)msg;

            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, (int)msg_type, DataFormat.Default, msg);
            return _msg;
        }

        public static object GetMessage (FunMessage msg, MessageType msg_type)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(serializer_, MessageTable.GetType(msg_type), msg,
                                                  (int)msg_type, DataFormat.Default, true, out _msg);
            if (!success)
            {
                FunDebug.Log("Failed to decode {0} {1}", MessageTable.GetType(msg_type), (int)msg_type);
                return null;
            }

            return _msg;
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
                //FunDebug.DebugLog("Parsed json: {0}", str);
                return json_helper_.Deserialize(str);
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                return serializer_.Deserialize(stream, null, funmsg_type_);
            }

            return null;
        }


        // member variables.
        public TransportProtocol protocol;
        public EncryptionType enc_type;
        public string msg_type;
        public object message;
        public ArraySegment<byte> buffer;

        // expected reply-related members.
        public string reply_type = "";
        public float reply_timeout = 0f;
        public TimeoutEventHandler timeout_callback = null;

        // json-related members.
        private static JsonAccessor json_helper_ = new DictionaryJsonAccessor();

        // protobuf-related members.
        private static Type funmsg_type_ = typeof(FunMessage);
        private static FunMessageSerializer serializer_ = new FunMessageSerializer();
    }
}
