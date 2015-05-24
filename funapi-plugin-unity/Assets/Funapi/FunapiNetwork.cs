// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Protobuf
using funapi.network.fun_message;


namespace Fun
{
    // Funapi version
    public class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 73;
    }

    // Funapi transport protocol
    public enum TransportProtocol {
        kDefault = 0,
        kTcp,
        kUdp,
        kHttp
    };

    // Funapi message type
    public enum FunMsgType
    {
        kJson,
        kProtobuf
    }

    // Error code
    public enum ErrorCode
    {
        kNone,
        kConnectFailed,
        kSendFailed,
        kReceiveFailed,
        kEncryptionFailed,
        kInvalidEncryption,
        kUnknownEncryption,
        kExceptionError
    }

    // Event handler delegate
    public delegate void ReceivedEventHandler(TransportProtocol protocol,
                                              Dictionary<string, string> header, ArraySegment<byte> body);
    public delegate void ConnectTimeoutHandler(TransportProtocol protocol);
    public delegate void StartedEventHandler(TransportProtocol protocol);
    public delegate void StoppedEventHandler(TransportProtocol protocol);

    // Container to hold json-related functions.
    public abstract class JsonAccessor
    {
        public abstract string Serialize(object json_obj);
        public abstract object Deserialize(string json_str);
        public abstract string GetStringField(object json_obj, string field_name);
        public abstract void SetStringField(object json_obj, string field_name, string value);
        public abstract Int64 GetIntegerField(object json_obj, string field_name);
        public abstract void SetIntegerField(object json_obj, string field_name, Int64 value);
        public abstract bool HasField(object json_obj, string field_name);
        public abstract void RemoveStringField(object json_obj, string field_name);
        public abstract object Clone(object json_obj);
    }

    public class DictionaryJsonAccessor : JsonAccessor
    {
        public override string Serialize(object json_obj)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return Json.Serialize(d);
        }

        public override object Deserialize(string json_string)
        {
            return Json.Deserialize(json_string) as Dictionary<string, object>;
        }

        public override string GetStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return d[field_name] as string;
        }

        public override void SetStringField(object json_obj, string field_name, string value)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d[field_name] = value;
        }

        public override Int64 GetIntegerField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return Convert.ToInt64(d [field_name]);
        }

        public override void SetIntegerField(object json_obj, string field_name, Int64 value)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert (d != null);
            d [field_name] = value;
        }

        public override bool HasField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert (d != null);
            return d.ContainsKey (field_name);
        }

        public override void RemoveStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d.Remove(field_name);
        }

        public override object Clone(object json_obj)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return new Dictionary<string, object>(d);

        }
    }


    // Abstract class to represent Transport used by Funapi
    // There are 3 transport types at the moment (though this plugin implements only TCP one.)
    // TCP, UDP, and HTTP.
    public abstract class FunapiTransport
    {
        #region public interface
        public FunapiTransport()
        {
            state = State.kUnknown;
            protocol = TransportProtocol.kDefault;
        }

        public TransportProtocol protocol
        {
            get; set;
        }

        public virtual bool IsStream
        {
            get { return false; }
        }

        public virtual bool IsDatagram
        {
            get { return false; }
        }

        public virtual bool IsRequestResponse
        {
            get { return false; }
        }

        public float ConnectTimeout
        {
            get; set;
        }

        public ErrorCode LastErrorCode
        {
            get { return last_error_code_; }
        }

        public string LastErrorMessage
        {
            get { return last_error_message_; }
        }

        // Encoding/Decoding related
        public JsonAccessor JsonHelper
        {
            get { return json_accessor_; }
            set { json_accessor_ = value; }
        }

        // FunMessage serializer/deserializer
        public FunMessageSerializer ProtobufHelper {
            get { return serializer_; }
            set { serializer_ = value; }
        }
        #endregion

        #region internal implementation
        // Start connecting
        internal abstract void Start();

        // Disconnection
        internal abstract void Stop();

        // Check connection
        internal abstract bool Started { get; }

        // Update
        internal virtual void Update () {}

        // Check unsent messages
        internal abstract bool HasUnsentMessages { get; }

        // Send a message
        internal abstract void SendMessage(string msgtype, object json_message, EncryptionType encryption);
        internal abstract void SendMessage(FunMessage message, EncryptionType encryption);

        internal State state
        {
            get; set;
        }

        internal void OnConnectionTimeout ()
        {
            if (ConnectTimeoutCallback != null)
            {
                ConnectTimeoutCallback(protocol);
            }
        }

        internal void OnReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            if (ReceivedCallback != null)
            {
                ReceivedCallback(protocol, header, body);
            }
        }

        internal void OnStarted ()
        {
            state = State.kEstablished;

            if (StartedCallback != null)
            {
                StartedCallback(protocol);
            }
        }

        internal void OnStartedInternal ()
        {
            if (StartedInternalCallback != null)
            {
                StartedInternalCallback(protocol);
            }
        }

        internal void OnStopped ()
        {
            if (StoppedCallback != null)
            {
                StoppedCallback(protocol);
            }
        }
        #endregion


        internal enum State
        {
            kUnknown = 0,
            kConnecting,
            kEncryptionHandshaking,
            kConnected,
            kWaitForSessionResponse,
            kWaitForSession,
            kWaitForAck,
            kEstablished
        };

        internal enum EncryptionMethod
        {
            kNone = 0,
            kIFunEngine1
        }

        // Event handlers
        public event ConnectTimeoutHandler ConnectTimeoutCallback;
        public event StartedEventHandler StartedCallback;
        public event StoppedEventHandler StoppedCallback;
        internal event StartedEventHandler StartedInternalCallback;
        internal event ReceivedEventHandler ReceivedCallback;

        // member variables.
        internal JsonAccessor json_accessor_ = new DictionaryJsonAccessor();
        internal FunMessageSerializer serializer_ = null;
        internal ErrorCode last_error_code_ = ErrorCode.kNone;
        internal string last_error_message_ = "";
    }


    // Transport class for socket
    public abstract class FunapiEncryptedTransport : FunapiTransport
    {
        // Create a socket.
        internal abstract void Init();

        // Sends a packet.
        internal abstract void WireSend();

        // Starts a socket.
        internal override void Start()
        {
            bool failed = false;

            try
            {
                // Resets states.
                header_decoded_ = false;
                received_size_ = 0;
                next_decoding_offset_ = 0;
                header_fields_.Clear();
                sending_.Clear();
                last_error_code_ = ErrorCode.kNone;
                last_error_message_ = "";

                Init();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in Start: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        // Stops a socket.
        internal override void Stop()
        {
            if (state == State.kUnknown)
                return;

            state = State.kUnknown;
            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";

            AddToEventQueue(OnStopped);
        }

        internal override void Update ()
        {
            lock (event_lock_)
            {
                if (event_queue_.Count > 0)
                {
                    foreach (DelegateEventHandler func in event_queue_)
                    {
                        func();
                    }
                }

                event_queue_.Clear();
            }
        }

        internal override bool HasUnsentMessages
        {
            get
            {
                lock (sending_lock_)
                {
                    return sending_.Count > 0 || pending_.Count > 0;
                }
            }
        }

        internal void AddToEventQueue (DelegateEventHandler handler)
        {
            if (handler == null)
            {
                Debug.Log("AddToEventQueue - handler is null.");
                return;
            }

            lock (event_lock_)
            {
                event_queue_.Add(handler);
            }
        }

        internal void SetEncryption (EncryptionType encryption)
        {
            Encryptor encryptor = Encryptor.Create(encryption);
            if (encryptor == null)
            {
                last_error_code_ = ErrorCode.kInvalidEncryption;
                last_error_message_ = "Failed to create encryptor: " + encryption;
                Debug.Log(last_error_message_);
                return;
            }

            default_encryptor_ = (int)encryption;
            encryptors_[encryption] = encryptor;
        }

        // Sends a JSON message through a socket.
        internal override void SendMessage (string msgtype, object json_message, EncryptionType encryption)
        {
            string str = this.JsonHelper.Serialize(json_message);
            byte[] body = Encoding.UTF8.GetBytes(str);

            DebugUtils.Log("JSON to send : " + str);

            SendMessage(msgtype, body, encryption);
        }

        internal override void SendMessage (FunMessage message, EncryptionType encryption)
        {
            MemoryStream stream = new MemoryStream();
            this.ProtobufHelper.Serialize (stream, message);

            byte[] body = new byte[stream.Length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(body, 0, body.Length);

            SendMessage(message.msgtype, body, encryption);
        }

        private void SendMessage (string msgtype, byte[] body, EncryptionType encryption)
        {
            bool failed = false;
            try
            {
                lock (sending_lock_)
                {
                    pending_.Add(new SendingBuffer(msgtype, new ArraySegment<byte>(body), encryption));

                    if (Started && sending_.Count == 0)
                    {
                        List<SendingBuffer> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                        if (!EncryptThenSendMessage())
                            failed = true;
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendMessage: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        internal bool EncryptThenSendMessage()
        {
            DebugUtils.Assert((int)state >= (int)State.kConnected);
            DebugUtils.Assert(sending_.Count > 0);

            for (int i = 0; i < sending_.Count; i+=2)
            {
                SendingBuffer buffer = sending_[i];

                EncryptionType encryption = buffer.encryption;
                if (encryption == EncryptionType.kDefaultEncryption)
                    encryption = (EncryptionType)default_encryptor_;

                Encryptor encryptor = null;
                string encryption_header = "";
                if ((int)encryption != kNoneEncryption)
                {
                    encryptor = encryptors_[encryption];
                    if (encryptor == null)
                    {
                        last_error_code_ = ErrorCode.kUnknownEncryption;
                        last_error_message_ = "Unknown encryption: " + encryption;
                        Debug.Log(last_error_message_);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kEstablished)
                    {
                        last_error_code_ = ErrorCode.kInvalidEncryption;
                        last_error_message_ = "'" + encryptor.name + "' is invalid encryption type. Check out the encryption type of server.";
                        Debug.Log(last_error_message_);
                        return false;
                    }

                    Int64 nSize = encryptor.Encrypt(buffer.data, buffer.data, ref encryption_header);
                    if (nSize <= 0)
                    {
                        last_error_code_ = ErrorCode.kEncryptionFailed;
                        last_error_message_ = "Encrypt failure: " + encryptor.name;
                        Debug.Log(last_error_message_);
                        return false;
                    }

                    DebugUtils.Assert(nSize == buffer.data.Count);
                }

                string header = "";
                header += kVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kProtocolVersion + kHeaderDelimeter;
                if (first_sending_)
                {
                    header += kPluginVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kPluginVersion + kHeaderDelimeter;
                    first_sending_ = false;
                }
                header += kLengthHeaderField + kHeaderFieldDelimeter + buffer.data.Count + kHeaderDelimeter;
                if ((int)encryption != kNoneEncryption)
                {
                    DebugUtils.Assert(encryptor != null);
                    DebugUtils.Assert(encryptor.encryption == encryption);
                    header += kEncryptionHeaderField + kHeaderFieldDelimeter + Convert.ToInt32(encryption);
                    header += "-" + encryption_header + kHeaderDelimeter;
                }
                header += kHeaderDelimeter;

                SendingBuffer header_buffer = new SendingBuffer(buffer.msgtype,
                                                                new ArraySegment<byte>(Encoding.ASCII.GetBytes(header)),
                                                                EncryptionType.kDefaultEncryption);
                sending_.Insert(i, header_buffer);

                DebugUtils.Log("Header to send: " + header + " body length: " + buffer.data.Count);
            }

            WireSend();

            return true;
        }

        internal bool SendUnsentMessages ()
        {
            lock (sending_lock_)
            {
                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    Debug.Log("Retrying unsent messages.");
                    WireSend();
                }
                else if (pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<SendingBuffer> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;

                    if (!EncryptThenSendMessage())
                        return false;
                }
            }

            return true;
        }

        // Checks buffer space before starting another async receive.
        internal void CheckReceiveBuffer()
        {
            int remaining_size = receive_buffer_.Length - received_size_;

            if (remaining_size <= 0)
            {
                byte[] new_buffer = null;

                if (remaining_size == 0 && next_decoding_offset_ > 0)
                    new_buffer = new byte[receive_buffer_.Length];
                else
                    new_buffer = new byte[receive_buffer_.Length + kUnitBufferSize];

                // If there are space can be collected, compact it first.
                // Otherwise, increase the receiving buffer size.
                if (next_decoding_offset_ > 0)
                {
                    DebugUtils.Log("Compacting a receive buffer to save " + next_decoding_offset_ + " bytes.");
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0, received_size_ - next_decoding_offset_);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    DebugUtils.Log("Increasing a receive buffer to " + (receive_buffer_.Length + kUnitBufferSize) + " bytes.");
                    Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                    receive_buffer_ = new_buffer;
                }
            }
        }

        internal bool TryToDecodeHeader()
        {
            DebugUtils.Log("Trying to decode header fields.");

            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    DebugUtils.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }
                string line = Encoding.ASCII.GetString(receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    DebugUtils.Log("End of header reached. Will decode body from now.");
                    return true;
                }

                DebugUtils.Log("Header line: " + line);
                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                tuple[0] = tuple[0].ToUpper();
                DebugUtils.Log("Decoded header field '" + tuple[0] + "' => '" + tuple[1] + "'");
                DebugUtils.Assert(tuple.Length == 2);
                header_fields_[tuple[0]] = tuple[1];
            }

            return false;
        }

        internal bool TryToDecodeBody()
        {
            // Header version
            DebugUtils.Assert(header_fields_.ContainsKey(kVersionHeaderField));
            int version = Convert.ToUInt16(header_fields_[kVersionHeaderField]);
            DebugUtils.Assert(version == FunapiVersion.kProtocolVersion);

            // Header length
            DebugUtils.Assert(header_fields_.ContainsKey(kLengthHeaderField));
            int body_length = Convert.ToInt32(header_fields_[kLengthHeaderField]);
            DebugUtils.Log("We need " + body_length + " bytes for a message body. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");

            if (received_size_ - next_decoding_offset_ < body_length)
            {
                // Need more bytes.
                DebugUtils.Log("We need more bytes for a message body. Waiting.");
                return false;
            }

            // Encryption
            string encryption_str = "";
            string encryption_header;

            if (header_fields_.TryGetValue(kEncryptionHeaderField, out encryption_header))
            {
                int index = encryption_header.IndexOf(kDelim1);
                if (index != -1)
                {
                    encryption_str = encryption_header.Substring(0, index);
                    encryption_header = encryption_header.Substring(index + 1);
                }
                else if (encryption_header != " ") // for HTTP header's blank
                {
                    encryption_str = encryption_header;
                }
            }

            if (state == State.kEncryptionHandshaking)
            {
                DebugUtils.Assert(body_length == 0);

                if (encryption_str == kEncryptionHandshakeBegin)
                {
                    // Start handshake message.

                    // encryption list
                    List<EncryptionType> encryption_list = new List<EncryptionType>();

                    if (encryption_header.Length > 0)
                    {
                        int begin = 0;
                        int end = encryption_header.IndexOf(kDelim2);
                        EncryptionType encryption;

                        while (end != -1)
                        {
                            encryption = (EncryptionType)Convert.ToInt32(encryption_header.Substring(begin, end - begin));
                            encryption_list.Add(encryption);
                            begin = end + 1;
                            end = encryption_header.IndexOf(kDelim2, begin);
                        }

                        encryption = (EncryptionType)Convert.ToInt32(encryption_header.Substring(begin));
                        encryption_list.Add(encryption);
                    }

                    if (encryption_list.Count > 0)
                    {
                        default_encryptor_ = (int)encryption_list[0];
                        Debug.Log("Set default encryption: " + default_encryptor_);
                    }

                    // Create encryptors
                    foreach (EncryptionType type in encryption_list)
                    {
                        Encryptor encryptor = Encryptor.Create(type);
                        if (encryptor == null)
                        {
                            Debug.Log("Failed to create encryptor: " + type);
                            return false;
                        }

                        encryptors_[type] = encryptor;
                    }
                }
                else
                {
                    // Encryption handshake message
                    EncryptionType encryption = (EncryptionType)Convert.ToInt32(encryption_str);
                    Encryptor encryptor = encryptors_[encryption];
                    if (encryptor == null)
                    {
                        Debug.Log("Unknown encryption: " + encryption_str);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kHandshaking)
                    {
                        Debug.Log("Unexpected handshake message: " + encryptor.name);
                        return false;
                    }

                    string out_header = "";
                    if (!encryptor.Handshake(encryption_header, ref out_header))
                    {
                        Debug.Log("Encryption handshake failure: " + encryptor.name);
                        return false;
                    }

                    if (out_header.Length > 0)
                    {
                        // TODO: Implementation
                        DebugUtils.Assert(false);
                    }
                    else
                    {
                        DebugUtils.Assert(encryptor.state == Encryptor.State.kEstablished);
                    }
                }

                bool handshake_complete = true;
                foreach (KeyValuePair<EncryptionType, Encryptor> pair in encryptors_)
                {
                    if (pair.Value.state != Encryptor.State.kEstablished)
                    {
                        handshake_complete = false;
                        break;
                    }
                }

                if (handshake_complete)
                {
                    // Makes a state transition.
                    state = State.kConnected;
                    Debug.Log("Ready to receive.");

                    AddToEventQueue(OnHandshakeComplete);
                }
            }

            if (body_length > 0)
            {
                if ((int)state < (int)State.kConnected)
                {
                    Debug.Log("Unexpected message. state:" + state);
                    return false;
                }

                if ((encryptors_.Count == 0) != (encryption_str.Length == 0))
                {
                    Debug.Log("Unknown encryption: " + encryption_str);
                    return false;
                }

                if (encryptors_.Count > 0)
                {
                    EncryptionType encryption = (EncryptionType)Convert.ToInt32(encryption_str);
                    Encryptor encryptor = encryptors_[encryption];

                    if (encryptor == null)
                    {
                        Debug.Log("Unknown encryption: " + encryption_str);
                        return false;
                    }

                    ArraySegment<byte> body_bytes = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, body_length);
                    DebugUtils.Assert(body_bytes.Count == body_length);

                    Int64 nSize = encryptor.Decrypt(body_bytes, body_bytes, encryption_header);
                    if (nSize <= 0)
                    {
                        Debug.Log("Failed to decrypt.");
                        return false;
                    }

                    // TODO: Implementation
                    DebugUtils.Assert(body_length == nSize);
                }

                ArraySegment<byte> body = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, body_length);
                next_decoding_offset_ += body_length;

                // The network module eats the fields and invoke registered handler.
                OnReceived(header_fields_, body);
            }

            // Prepares a next message.
            header_decoded_ = false;
            header_fields_.Clear();
            return true;
        }

        // Sends messages & Calls start callback
        private void OnHandshakeComplete ()
        {
            lock (sending_lock_)
            {
                if (sending_.Count == 0)
                {
                    if (pending_.Count > 0)
                    {
                        DebugUtils.Log("Flushing pending messages.");
                        List<SendingBuffer> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                        EncryptThenSendMessage();
                    }

                    OnStartedInternal();
                }
            }
        }

        private static int BytePatternMatch (ArraySegment<byte> haystack, ArraySegment<byte> needle)
        {
            if (haystack.Count < needle.Count)
            {
                return -1;
            }

            for (int i = 0; i <= haystack.Count - needle.Count; ++i)
            {
                bool found = true;
                for (int j = 0; j < needle.Count; ++j)
                {
                    if (haystack.Array[haystack.Offset + i + j] != needle.Array[needle.Offset + j])
                    {
                        found = false;
                    }
                }
                if (found)
                {
                    return haystack.Offset + i;
                }
            }

            return -1;
        }


        internal class SendingBuffer
        {
            public SendingBuffer (string msgtype, ArraySegment<byte> data, EncryptionType encryption)
            {
                this.encryption = encryption;
                this.msgtype = msgtype;
                this.data = data;
            }

            public string msgtype;
            public ArraySegment<byte> data;
            public EncryptionType encryption;
        }


        internal delegate void DelegateEventHandler();

        // Buffer-related constants.
        internal static readonly int kUnitBufferSize = 65536;

        // Funapi header-related constants.
        internal static readonly string kHeaderDelimeter = "\n";
        internal static readonly string kHeaderFieldDelimeter = ":";
        internal static readonly string kVersionHeaderField = "VER";
        internal static readonly string kPluginVersionHeaderField = "PVER";
        internal static readonly string kLengthHeaderField = "LEN";
        internal static readonly string kEncryptionHeaderField = "ENC";

        // Encryption-releated constants.
        private static readonly string kEncryptionHandshakeBegin = "HELLO!";
        private static readonly int kNoneEncryption = 0;
        private static readonly char kDelim1 = '-';
        private static readonly char kDelim2 = ',';

        // for speed-up.
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // State-related.
        private bool first_sending_ = true;
        internal bool header_decoded_ = false;
        internal int received_size_ = 0;
        internal int next_decoding_offset_ = 0;
        internal object sending_lock_ = new object();
        internal object receive_lock_ = new object();
        internal object event_lock_ = new object();
        internal byte[] receive_buffer_ = new byte[kUnitBufferSize];
        internal byte[] send_buffer_ = new byte[kUnitBufferSize];
        internal List<SendingBuffer> pending_ = new List<SendingBuffer>();
        internal List<SendingBuffer> sending_ = new List<SendingBuffer>();
        internal Dictionary<string, string> header_fields_ = new Dictionary<string, string>();
        internal int default_encryptor_ = kNoneEncryption;
        internal Dictionary<EncryptionType, Encryptor> encryptors_ = new Dictionary<EncryptionType, Encryptor>();
        internal List<DelegateEventHandler> event_queue_ = new List<DelegateEventHandler>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiEncryptedTransport
    {
        #region public interface
        public FunapiTcpTransport (string hostname_or_ip, UInt16 port)
        {
            protocol = TransportProtocol.kTcp;
            DisableNagle = false;

            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            connect_ep_ = new IPEndPoint(address, port);
        }

        // Stops a socket.
        internal override void Stop()
        {
            if (state == State.kUnknown)
                return;

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }

            base.Stop();
        }

        internal override bool Started
        {
            get
            {
                return sock_ != null && sock_.Connected && (int)state >= (int)State.kConnected;
            }
        }

        public override bool IsStream
        {
            get { return true; }
        }

        public bool DisableNagle
        {
            get; set;
        }

        internal override void Update ()
        {
            base.Update();

            if (state == State.kConnecting && connect_timeout_ > 0f)
            {
                connect_timeout_ -= Time.deltaTime;
                if (connect_timeout_ <= 0f)
                {
                    DebugUtils.Log("Connection waiting time has been exceeded.");
                    OnConnectionTimeout();
                }
            }
        }
        #endregion

        #region internal implementation
        // Create a socket.
        internal override void Init()
        {
            state = State.kConnecting;
            connect_timeout_ = ConnectTimeout;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            if (DisableNagle)
                sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

            sock_.BeginConnect(connect_ep_, new AsyncCallback(this.StartCb), this);
        }

        internal override void WireSend()
        {
            List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();
            lock (sending_lock_)
            {
                foreach (SendingBuffer buffer in sending_)
                {
                    list.Add(buffer.data);
                }
            }

            sock_.BeginSend(list, 0, new AsyncCallback(this.SendBytesCb), this);
        }

        private void StartCb(IAsyncResult ar)
        {
            DebugUtils.Log("StartCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    Debug.Log(last_error_message_);
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    Debug.Log(last_error_message_);
                    return;
                }
                Debug.Log("Connected.");

                state = State.kEncryptionHandshaking;

                lock (receive_lock_)
                {
                    // Wait for encryption handshaking message.
                    ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                    buffer.Add(wrapped);
                    sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginConnect operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in StartCb: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("SendBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    Debug.Log(last_error_message_);
                    return;
                }

                int nSent = sock_.EndSend(ar);
                DebugUtils.Log("Sent " + nSent + "bytes");

                lock (sending_lock_)
                {
                    // Removes any segment fully sent.
                    while (nSent > 0)
                    {
                        DebugUtils.Assert(sending_.Count > 0);

                        if (sending_[0].data.Count > nSent)
                        {
                            // partial data
                            DebugUtils.Log("Partially sent. Will resume.");
                            break;
                        }
                        else
                        {
                            // fully sent.
                            DebugUtils.Log("Discarding a fully sent message.");
                            nSent -= sending_[0].data.Count;
                            sending_.RemoveAt(0);
                        }
                    }

                    while (sending_.Count > 0 && sending_[0].data.Count <= 0)
                    {
                        DebugUtils.Log("Remove zero byte buffer.");
                        sending_.RemoveAt(0);
                    }

                    // If the first segment has been sent partially, we need to reconstruct the first segment.
                    if (nSent > 0)
                    {
                        DebugUtils.Assert(sending_.Count > 0);
                        ArraySegment<byte> original = sending_[0].data;

                        DebugUtils.Assert(nSent <= sending_[0].data.Count);
                        ArraySegment<byte> adjusted = new ArraySegment<byte>(original.Array, original.Offset + nSent, original.Count - nSent);
                        sending_[0].data = adjusted;

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }

                    if (!SendUnsentMessages())
                        failed = true;
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginSend operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    Debug.Log(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        DebugUtils.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
                    }

                    // Try to decode as many messages as possible.
                    while (true)
                    {
                        if (header_decoded_ == false)
                        {
                            if (TryToDecodeHeader() == false)
                            {
                                break;
                            }
                        }
                        if (header_decoded_)
                        {
                            if (TryToDecodeBody() == false)
                            {
                                break;
                            }
                        }
                    }

                    if (nRead > 0)
                    {
                        // Checks buffer space
                        CheckReceiveBuffer();

                        // Starts another async receive
                        ArraySegment<byte> residual = new ArraySegment<byte>(receive_buffer_, received_size_, receive_buffer_.Length - received_size_);
                        List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                        buffer.Add(residual);
                        sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                        DebugUtils.Log("Ready to receive more. We can receive upto " + (receive_buffer_.Length - received_size_) + " more bytes");
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        DebugUtils.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            DebugUtils.Log("Buffer has " + (receive_buffer_.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                        }
                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can't not receive messages. Maybe the socket is closed.";
                        Debug.Log(last_error_message_);
                        failed = true;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginReceive operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        internal Socket sock_;
        private IPEndPoint connect_ep_;
        private float connect_timeout_ = 0f;
        #endregion
    }


    // UDP transport layer
    public class FunapiUdpTransport : FunapiEncryptedTransport
    {
        #region public interface
        public FunapiUdpTransport(string hostname_or_ip, UInt16 port)
        {
            protocol = TransportProtocol.kUdp;
            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            send_ep_ = new IPEndPoint(address, port);
            receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, port);
        }

        // Stops a socket.
        internal override void Stop()
        {
            if (state == State.kUnknown)
                return;

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }

            base.Stop();
        }

        internal override bool Started
        {
            get { return sock_ != null && (int)state >= (int)State.kConnected; }
        }

        public override bool IsDatagram
        {
            get { return true; }
        }
        #endregion

        #region internal implementation
        // Create a socket.
        internal override void Init()
        {
            state = State.kConnected;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock_.BeginReceiveFrom(receive_buffer_, 0, receive_buffer_.Length, SocketFlags.None,
                                   ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

            OnStartedInternal();
        }

        // Send a packet.
        internal override void WireSend()
        {
            int offset = 0;

            lock (sending_lock_)
            {
                DebugUtils.Assert(sending_.Count >= 2);

                int length = sending_[0].data.Count + sending_[1].data.Count;
                if (length > send_buffer_.Length)
                {
                    send_buffer_ = new byte[length];
                }

                // one header + one body
                for (int i = 0; i < 2; ++i)
                {
                    ArraySegment<byte> item = sending_[i].data;
                    Buffer.BlockCopy(item.Array, 0, send_buffer_, offset, item.Count);
                    offset += item.Count;
                }
            }

            if (offset > 0)
            {
                if (offset > kUnitBufferSize)
                {
                    Debug.Log("Message is greater than 64KB. It will be truncated.");
                    DebugUtils.Assert(false);
                }

                sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                  send_ep_, new AsyncCallback(this.SendBytesCb), this);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("SendBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    Debug.Log(last_error_message_);
                    return;
                }

                lock (sending_lock_)
                {
                    int nSent = sock_.EndSend(ar);
                    DebugUtils.Log("Sent " + nSent + "bytes");

                    DebugUtils.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    int nToSend = 0;
                    for (int i = 0; i < 2; ++i)
                    {
                        nToSend += sending_[0].data.Count;
                        sending_.RemoveAt(0);
                    }

                    if (nSent > 0 && nSent < nToSend)
                    {
                        Debug.Log("Failed to transfer udp messages.");
                        DebugUtils.Assert(false);
                    }

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    if (!SendUnsentMessages())
                        failed = true;
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginSendTo operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    Debug.Log(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        DebugUtils.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
                    }

                    // Decoding a message
                    if (TryToDecodeHeader())
                    {
                        if (TryToDecodeBody() == false)
                        {
                            DebugUtils.LogWarning("Failed to decode body.");
                            DebugUtils.Assert(false);
                        }
                    }
                    else
                    {
                        DebugUtils.LogWarning("Failed to decode header.");
                        DebugUtils.Assert(false);
                    }

                    if (nRead > 0)
                    {
                        // Resets buffer
                        receive_buffer_ = new byte[kUnitBufferSize];
                        received_size_ = 0;
                        next_decoding_offset_ = 0;

                        // Starts another async receive
                        sock_.BeginReceiveFrom(receive_buffer_, received_size_, receive_buffer_.Length - received_size_, SocketFlags.None,
                                               ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

                        DebugUtils.Log("Ready to receive more. We can receive upto " + receive_buffer_.Length + " more bytes");
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        DebugUtils.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            DebugUtils.Log("Buffer has " + (receive_buffer_.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                        }
                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can't not receive messages. Maybe the socket is closed.";
                        Debug.Log(last_error_message_);
                        failed = true;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginReceiveFrom operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                Debug.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }


        internal Socket sock_;
        private IPEndPoint send_ep_;
        private EndPoint receive_ep_;
        #endregion
    }


    // HTTP transport layer
    public class FunapiHttpTransport : FunapiEncryptedTransport
    {
        #region public interface
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port, bool https)
        {
            protocol = TransportProtocol.kHttp;

            // Url
            host_url_ = https ? "https://" : "http://";
            host_url_ += hostname_or_ip + ":" + port;

            // Version
            host_url_ += "/v" + FunapiVersion.kProtocolVersion + "/";
        }

        internal override void Stop()
        {
            if (state == State.kUnknown)
                return;

            foreach (WebState ws in list_)
            {
                if (ws.request != null)
                {
                    ws.aborted = true;
                    ws.request.Abort();
                }

                if (ws.stream != null)
                    ws.stream.Close();
            }

            list_.Clear();

            base.Stop();
        }

        internal override bool Started
        {
            get { return (int)state >= (int)State.kConnected; }
        }

        public override bool IsRequestResponse
        {
            get { return true; }
        }

        internal override void Update ()
        {
            base.Update();

            if (response_time_ > 0f)
            {
                response_time_ -= Time.deltaTime;
                if (response_time_ <= 0f)
                {
                    RequestFailure();
                }
            }
        }
        #endregion

        #region internal implementation
        internal override void Init()
        {
            state = State.kConnected;

            OnStartedInternal();
        }

        internal override void WireSend()
        {
            DebugUtils.Log("Send a Message.");

            try
            {
                lock (sending_lock_)
                {
                    DebugUtils.Assert(sending_.Count >= 2);
                    DebugUtils.Log("Host Url: " + host_url_);

                    SendingBuffer header = sending_[0];
                    SendingBuffer body = sending_[1];

                    // Request
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.ContentLength = body.data.Count;

                    // encryption type
                    string str_header = Encoding.ASCII.GetString(header.data.Array, header.data.Offset, header.data.Count);
                    int start_offset = str_header.IndexOf(kEncryptionHeaderField);
                    if (start_offset != -1)
                    {
                        start_offset += kEncryptionHeaderField.Length + 1;
                        int end_offset = str_header.IndexOf(kHeaderDelimeter, start_offset);
                        request.Headers[kEncryptionHttpHeaderField] = str_header.Substring(start_offset, end_offset - start_offset);
                    }

                    // Response
                    WebState ws = new WebState();
                    ws.request = request;
                    ws.msgtype = body.msgtype;
                    ws.sending = body.data;
                    list_.Add(ws);

                    cur_request_ = ws;
                    response_time_ = kResponseTimeout;

                    request.BeginGetRequestStream(new AsyncCallback(RequestStreamCb), ws);
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in WireSend: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(RequestFailure);
            }
        }

        private void RequestStreamCb (IAsyncResult ar)
        {
            DebugUtils.Log("RequestStreamCb called.");

            try
            {
                WebState ws = (WebState)ar.AsyncState;
                HttpWebRequest request = ws.request;

                Stream stream = request.EndGetRequestStream(ar);
                stream.Write(ws.sending.Array, 0, ws.sending.Count);
                stream.Close();
                DebugUtils.Log("Sent " + ws.sending.Count + "bytes");

                request.BeginGetResponse(new AsyncCallback(ResponseCb), ws);
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in RequestStreamCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(RequestFailure);
            }
        }

        private void ResponseCb (IAsyncResult ar)
        {
            DebugUtils.Log("ResponseCb called.");

            try
            {
                WebState ws = (WebState)ar.AsyncState;
                if (ws.aborted)
                    return;

                HttpWebResponse response = (HttpWebResponse)ws.request.EndGetResponse(ar);
                ws.request = null;
                ws.response = response;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Stream stream = response.GetResponseStream();
                    ws.stream = stream;
                    ws.buffer = new byte[kUnitBufferSize];
                    ws.read_data = new byte[kUnitBufferSize];
                    ws.read_offset = 0;

                    stream.BeginRead(ws.buffer, 0, ws.buffer.Length, new AsyncCallback(ReadCb), ws);
                }
                else
                {
                    DebugUtils.Log("Failed response. status:" + response.StatusDescription);
                    DebugUtils.Assert(false);
                    AddToEventQueue(RequestFailure);
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ResponseCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(RequestFailure);
            }
        }

        private void ReadCb (IAsyncResult ar)
        {
            DebugUtils.Log("ReadCb called.");

            try
            {
                WebState ws = (WebState)ar.AsyncState;
                int nRead = ws.stream.EndRead(ar);

                if (nRead > 0)
                {
                    DebugUtils.Log("We need more bytes for response. Waiting.");
                    if (ws.read_offset + nRead > ws.read_data.Length)
                    {
                        byte[] temp = new byte[ws.read_data.Length + kUnitBufferSize];
                        Buffer.BlockCopy(ws.read_data, 0, temp, 0, ws.read_offset);
                        ws.read_data = temp;
                    }

                    Buffer.BlockCopy(ws.buffer, 0, ws.read_data, ws.read_offset, nRead);
                    ws.read_offset += nRead;

                    ws.stream.BeginRead(ws.buffer, 0, ws.buffer.Length, new AsyncCallback(ReadCb), ws);
                }
                else
                {
                    if (ws.response == null)
                    {
                        DebugUtils.LogWarning("Response instance is null.");
                        DebugUtils.Assert(false);
                        AddToEventQueue(RequestFailure);
                        return;
                    }

                    lock (receive_lock_)
                    {
                        // Header
                        byte[] header = ws.response.Headers.ToByteArray();
                        string str_header = Encoding.ASCII.GetString(header, 0, header.Length);
                        str_header = str_header.Insert(0, kVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kProtocolVersion + kHeaderDelimeter);
                        str_header = str_header.Replace(kLengthHttpHeaderField, kLengthHeaderField);
                        str_header = str_header.Replace(kEncryptionHttpHeaderField, kEncryptionHeaderField);
                        str_header = str_header.Replace("\r", "");
                        header = Encoding.ASCII.GetBytes(str_header);

                        // Checks buffer space
                        int offset = received_size_;
                        received_size_ += header.Length + ws.read_offset;
                        CheckReceiveBuffer();

                        // Copy to buffer
                        Buffer.BlockCopy(header, 0, receive_buffer_, offset, header.Length);
                        Buffer.BlockCopy(ws.read_data, 0, receive_buffer_, offset + header.Length, ws.read_offset);

                        // Decoding a message
                        if (TryToDecodeHeader())
                        {
                            if (TryToDecodeBody() == false)
                            {
                                DebugUtils.LogWarning("Failed to decode body.");
                                DebugUtils.Assert(false);
                            }
                        }
                        else
                        {
                            DebugUtils.LogWarning("Failed to decode header.");
                            DebugUtils.Assert(false);
                        }

                        ws.stream.Close();
                        ws.stream = null;
                        list_.Remove(ws);

                        cur_request_ = null;
                        response_time_ = -1f;
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }

                    lock (sending_lock_)
                    {
                        DebugUtils.Assert(sending_.Count >= 2);

                        // Removes header and body segment
                        sending_.RemoveAt(0);
                        sending_.RemoveAt(0);

                        SendUnsentMessages();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReadCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(RequestFailure);
            }
        }

        private void RequestFailure ()
        {
            DebugUtils.Log("RequestFailure - state: " + state);
            if (state == State.kUnknown || cur_request_ == null)
            {
                OnRequestFailureCallback("");
                return;
            }

            WebState ws = cur_request_;

            cur_request_ = null;
            response_time_ = -1f;

            if (ws.request != null)
            {
                ws.aborted = true;
                ws.request.Abort();
            }

            if (ws.stream != null)
                ws.stream.Close();

            list_.Remove(ws);

            lock (sending_lock_)
            {
                DebugUtils.Assert(sending_.Count >= 2);

                // Removes header and body segment
                sending_.RemoveAt(0);
                sending_.RemoveAt(0);

                OnRequestFailureCallback(ws.msgtype);

                SendUnsentMessages();
            }
        }

        private void OnRequestFailureCallback (string msg_type)
        {
            if (RequestFailureCallback != null)
            {
                RequestFailureCallback(msg_type);
            }
        }
        #endregion


        // Funapi header-related constants.
        private static readonly string kLengthHttpHeaderField = "content-length";
        private static readonly string kEncryptionHttpHeaderField = "X-iFun-Enc";

        // waiting time for response
        private static readonly float kResponseTimeout = 30f;    // seconds

        // Delegates
        public delegate void RequestFailureHandler(string msg_type);
        public event RequestFailureHandler RequestFailureCallback;

        // Response-related.
        class WebState
        {
            public HttpWebRequest request = null;
            public HttpWebResponse response = null;
            public Stream stream = null;
            public byte[] buffer = null;
            public byte[] read_data = null;
            public int read_offset = 0;
            public bool aborted = false;
            public string msgtype;
            public ArraySegment<byte> sending;
        }

        // member variables.
        private string host_url_;
        private float response_time_ = -1f;
        private WebState cur_request_ = null;
        private List<WebState> list_ = new List<WebState>();
    }


    // Driver to use Funapi network plugin.
    public class FunapiNetwork
    {
        #region public interface
        public FunapiNetwork(FunMsgType type, bool session_reliability)
        {
            state_ = State.kUnknown;
            msg_type_ = type;
            recv_type_ = typeof(FunMessage);

            seq_recvd_ = 0;
            first_receiving_ = true;
            session_reliability_ = session_reliability;
            seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();

            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            message_handlers_[kMaintenanceMessageType] = this.OnMaintenanceMessage;
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiNetwork(FunMsgType, bool)' instead.")]
        public FunapiNetwork(FunapiTransport transport, FunMsgType type, bool session_reliability,
                             SessionInitHandler on_session_initiated, SessionCloseHandler on_session_closed)
            : this(type, session_reliability)
        {
            OnSessionInitiated += new SessionInitHandler(on_session_initiated);
            OnSessionClosed += new SessionCloseHandler(on_session_closed);

            AttachTransport(transport);
            SetDefaultProtocol(transport.protocol);
        }

        // Set default protocol
        public void SetDefaultProtocol (TransportProtocol protocol)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);

            default_protocol_ = protocol;
            Debug.Log("SetProtocol - default protocol is '" + protocol + "'.");
        }

        public TransportProtocol GetDefaultProtocol()
        {
            return default_protocol_;
        }

        // Set message protocol
        public void SetMessageProtocol (TransportProtocol protocol, string msg_type)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);
            message_protocols_[msg_type] = protocol;
        }

        public TransportProtocol GetMessageProtocol (string msg_type)
        {
            if (message_protocols_.ContainsKey(msg_type))
                return message_protocols_[msg_type];

            return default_protocol_;
        }

        public void AttachTransport (FunapiTransport transport)
        {
            DebugUtils.Assert(transport != null);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(transport.protocol))
                {
                    Debug.LogWarning("AttachTransport - transport of '" + transport.protocol +
                                     "' type already exists. You should call DetachTransport first.");
                    return;
                }

                transport.ConnectTimeoutCallback += new ConnectTimeoutHandler(OnConnectTimeout);
                transport.StartedInternalCallback += new StartedEventHandler(OnTransportStarted);
                transport.StoppedCallback += new StoppedEventHandler(OnTransportStopped);
                transport.ReceivedCallback += new ReceivedEventHandler(OnTransportReceived);

                serializer_ = new FunMessageSerializer ();
                transport.ProtobufHelper = serializer_;

                transports_[transport.protocol] = transport;

                if (default_protocol_ == TransportProtocol.kDefault)
                {
                    SetDefaultProtocol(transport.protocol);
                }

                if (Started)
                {
                    StartTransport(transport);
                }

                Debug.Log("'" + transport.protocol + "' transport attached.");
            }
        }

        public void DetachTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                {
                    FunapiTransport transport = transports_[protocol];
                    if (transport != null && transport.Started)
                        StopTransport(transport);

                    transports_.Remove(protocol);
                    Debug.Log("'" + protocol + "' transport detached.");

                    if (protocol == default_protocol_)
                    {
                        FunapiTransport other = FindOtherTransport(transport.protocol);
                        if (other != null)
                        {
                            SetDefaultProtocol(other.protocol);
                        }
                        else
                        {
                            default_protocol_ = TransportProtocol.kDefault;
                            Debug.LogWarning("DetachTransport - Deletes default protocol.\n" +
                                             "You need to set default protocol up.");
                        }
                    }
                }
                else
                {
                    Debug.Log("DetachTransport - Can't find a transport of '" + protocol + "' type.");
                }
            }
        }

        public FunapiTransport GetTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

            return null;
        }

        public bool HasTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return true;
            }

            return false;
        }

        public void StartTransport (TransportProtocol protocol)
        {
            StartTransport(GetTransport(protocol));
        }

        internal void StartTransport (FunapiTransport transport)
        {
            if (transport == null)
                return;

            Debug.Log("Starting " + transport.protocol + " transport.");

            lock (state_lock_)
            {
                if (state_ == State.kUnknown)
                {
                    Start();
                    return;
                }
            }

            transport.Start();
        }

        public void StopTransport (TransportProtocol protocol)
        {
            StopTransport(GetTransport(protocol));
        }

        internal void StopTransport (FunapiTransport transport)
        {
            if (transport == null)
                return;

            Debug.Log("Stopping " + transport.protocol + " transport.");

            lock (state_lock_)
            {
                if (state_ == State.kWaitForSession &&
                    transport.state == FunapiTransport.State.kWaitForSessionResponse)
                {
                    FunapiTransport other = FindOtherTransport(transport.protocol);
                    if (other != null)
                    {
                        other.state = FunapiTransport.State.kWaitForSessionResponse;
                        SendEmptyMessage(other.protocol);
                    }
                }
            }

            if (transport.protocol == default_protocol_)
            {
                FunapiTransport other = FindOtherTransport(transport.protocol);
                if (other != null)
                {
                    SetDefaultProtocol(other.protocol);
                }
            }

            transport.Stop();
        }

        public void StopTransportAll()
        {
            lock (state_lock_)
            {
                state_ = State.kStopped;
            }

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    StopTransport(transport);
                }
            }

            OnStoppedAllTransportCallback();
        }

        // Starts FunapiNetwork
        public void Start()
        {
            Debug.Log("Starting a network module.");

            lock (state_lock_)
            {
                state_ = State.kStarted;
            }

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    StartTransport(transport);
                }
            }
        }

        // Stops FunapiNetwork
        public void Stop()
        {
            // Waits for unsent messages.
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.Started && transport.HasUnsentMessages)
                    {
                        lock (state_lock_)
                        {
                            state_ = State.kWaitForStop;
                            return;
                        }
                    }
                }
            }

            StopTransportAll();
            transports_.Clear();

            CloseSession();

            lock (state_lock_)
            {
                state_ = State.kUnknown;
            }

            Debug.Log("Stopping a network module.");
        }

        // Your update method inheriting MonoBehaviour should explicitly invoke this method.
        public void Update()
        {
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport != null)
                        transport.Update();
                }
            }

            lock (state_lock_)
            {
                if (state_ == State.kWaitForStop)
                {
                    Stop();
                    return;
                }
            }

            lock (message_lock_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugUtils.Log("Update messages. count: " + message_buffer_.Count);

                    string msg_type;
                    foreach (KeyValuePair<TransportProtocol, ArraySegment<byte>> buffer in message_buffer_)
                    {
                        msg_type = ProcessMessage(buffer.Key, buffer.Value);

                        lock (expected_reply_lock)
                        {
                            if (expected_replies_.ContainsKey(msg_type))
                            {
                                List<ExpectedReplyMessage> list = expected_replies_[msg_type];
                                if (list.Count > 0)
                                {
                                    list.RemoveAt(0);
                                    Debug.Log("Deletes expected reply message - " + msg_type);
                                }

                                if (list.Count <= 0)
                                    expected_replies_.Remove(msg_type);
                            }
                        }
                    }

                    message_buffer_.Clear();
                }
            }

            lock (expected_reply_lock)
            {
                if (expected_replies_.Count > 0)
                {
                    foreach (var item in expected_replies_)
                    {
                        int remove_count = 0;
                        foreach (ExpectedReplyMessage exp in item.Value)
                        {
                            exp.wait_time -= Time.deltaTime;
                            if (exp.wait_time <= 0f)
                            {
                                Debug.Log("'" + exp.msgtype + "' message waiting time has been exceeded.");
                                exp.callback(exp.msgtype);
                                ++remove_count;
                            }
                        }

                        if (remove_count > 0)
                        {
                            if (item.Value.Count <= remove_count)
                                expected_replies_.Remove(item.Key);
                            else
                                item.Value.RemoveRange(0, remove_count);
                        }
                    }
                }
            }
        }

        public bool Started
        {
            get
            {
                lock (state_lock_)
                {
                    return state_ != State.kUnknown && state_ != State.kStopped;
                }
            }
        }

        public bool Connected
        {
            get
            {
                lock (state_lock_)
                {
                    return state_ == State.kConnected;
                }
            }
        }

        public bool SessionReliability
        {
            get { return session_reliability_; }
        }

        public FunMsgType MsgType
        {
            get { return msg_type_; }
        }

        public FunMessage CreateFunMessage(object msg, int msg_index)
        {
            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, msg_index, ProtoBuf.DataFormat.Default, msg);
            return _msg;
        }

        public object GetMessage(FunMessage msg, Type msg_type, int msg_index)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(serializer_, msg_type, msg,
                                                  msg_index, ProtoBuf.DataFormat.Default, true, out _msg);
            if (!success)
            {
                Debug.Log(String.Format("Failed to decode {0} {1}", msg_type, msg_index));
                return null;
            }

            return _msg;
        }

        public void SendMessage(string msg_type, FunMessage message)
        {
            SendMessage(msg_type, message, EncryptionType.kDefaultEncryption, GetMessageProtocol(msg_type));
        }

        public void SendMessage(string msg_type, FunMessage message, EncryptionType encryption)
        {
            SendMessage(msg_type, message, encryption, GetMessageProtocol(msg_type));
        }

        public void SendMessage(string msg_type, FunMessage message, EncryptionType encryption, TransportProtocol protocol)
        {
            DebugUtils.Assert(msg_type_ == FunMsgType.kProtobuf);
            bool transport_reliability = (protocol == TransportProtocol.kTcp && session_reliability_);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            message.msgtype = msg_type;

            // Encodes a session id, if any.
            if (session_id_.Length > 0)
            {
                message.sid = session_id_;
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && transport.state == FunapiTransport.State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                if (transport_reliability)
                {
                    message.seq = seq_;
                    ++seq_;

                    send_queue_.Enqueue(message);
                    Debug.Log(protocol + " send message - msgtype:" + msg_type + " seq:" + message.seq);
                }
                else
                {
                    Debug.Log(protocol + " send message - msgtype:" + msg_type);
                }

                transport.SendMessage(message, encryption);
            }
            else if (transport_reliability ||
                     (transport != null && transport.state == FunapiTransport.State.kEstablished))
            {
                unsent_queue_.Enqueue(new UnsentMessage(msg_type, message, protocol));
                Debug.Log("SendMessage - '" + msg_type + "' message queued.");
            }
            else
            {
                string str_log = "SendMessage - '" + msg_type + "' message skipped.";
                if (transport == null)
                    str_log += "\nThere's no '" + protocol + "' transport.";
                else if (transport.state != FunapiTransport.State.kEstablished)
                    str_log += "\nTransport's state is '" + transport.state + "'.";

                Debug.Log(str_log);
            }
        }

        public void SendMessage(string msg_type, FunMessage message,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, message, EncryptionType.kDefaultEncryption, GetMessageProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, FunMessage message, EncryptionType encryption,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, message, encryption, GetMessageProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, FunMessage message, EncryptionType encryption, TransportProtocol protocol,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            lock (expected_reply_lock)
            {
                if (!expected_replies_.ContainsKey(msg_type))
                {
                    expected_replies_[msg_type] = new List<ExpectedReplyMessage>();
                }

                expected_replies_[msg_type].Add(new ExpectedReplyMessage(expected_reply_type, expected_reply_time, onReplyMissed));
                Debug.Log("Adds expected reply message - " + expected_reply_type);
            }

            SendMessage(msg_type, message, encryption, protocol);
        }

        public void SendMessage(string msg_type, object body)
        {
            SendMessage(msg_type, body, EncryptionType.kDefaultEncryption, GetMessageProtocol(msg_type));
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption)
        {
            SendMessage(msg_type, body, encryption, GetMessageProtocol(msg_type));
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption, TransportProtocol protocol)
        {
            DebugUtils.Assert(msg_type_ == FunMsgType.kJson);
            bool transport_reliability = (protocol == TransportProtocol.kTcp && session_reliability_);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && transport.state == FunapiTransport.State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                // Encodes a messsage type
                transport.JsonHelper.SetStringField(body, kMsgTypeBodyField, msg_type);

                // Encodes a session id, if any.
                if (session_id_.Length > 0)
                {
                    transport.JsonHelper.SetStringField(body, kSessionIdBodyField, session_id_);
                }

                if (transport_reliability)
                {
                    transport.JsonHelper.SetIntegerField(body, kSeqNumberField, seq_);
                    ++seq_;

                    send_queue_.Enqueue(transport.JsonHelper.Clone(body));
                    Debug.Log(protocol + " send message - msgtype:" + msg_type + " seq:" + (seq_ - 1));
                }
                else
                {
                    Debug.Log(protocol + " send message - msgtype:" + msg_type);
                }

                transport.SendMessage(msg_type, body, encryption);
            }
            else if (transport_reliability ||
                     (transport != null && transport.state == FunapiTransport.State.kEstablished))
            {
                if (transport == null)
                    unsent_queue_.Enqueue(new UnsentMessage(msg_type, body, protocol));
                else
                    unsent_queue_.Enqueue(new UnsentMessage(msg_type, transport.JsonHelper.Clone(body), protocol));

                Debug.Log("SendMessage - '" + msg_type + "' message queued.");
            }
            else
            {
                string str_log = "SendMessage - '" + msg_type + "' message skipped.";
                if (transport == null)
                    str_log += "\nThere's no '" + protocol + "' transport.";
                else if (transport.state != FunapiTransport.State.kEstablished)
                    str_log += "\nTransport's state is '" + transport.state + "'.";

                Debug.Log(str_log);
            }
        }

        public void SendMessage(string msg_type, object body,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, body, EncryptionType.kDefaultEncryption, GetMessageProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, body, encryption, GetMessageProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption, TransportProtocol protocol,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            lock (expected_reply_lock)
            {
                if (!expected_replies_.ContainsKey(msg_type))
                {
                    expected_replies_[msg_type] = new List<ExpectedReplyMessage>();
                }

                expected_replies_[msg_type].Add(new ExpectedReplyMessage(expected_reply_type, expected_reply_time, onReplyMissed));
                Debug.Log("Adds expected reply message - " + expected_reply_type);
            }

            SendMessage(msg_type, body, encryption, protocol);
        }

        public void RegisterHandler(string type, MessageEventHandler handler)
        {
            DebugUtils.Log("New handler for message type '" + type + "'");
            message_handlers_[type] = handler;
        }

        public void RegisterHandlerWithProtocol(string type, TransportProtocol protocol, MessageEventHandler handler)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                RegisterHandler(type, handler);
                return;
            }

            DebugUtils.Log("New handler for and message type '" + type + "' of '" + protocol + "' protocol.");
            message_protocols_[type] = protocol;
            message_handlers_[type] = handler;
        }

        public ErrorCode LastErrorCode (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return ErrorCode.kNone;

            return transport.LastErrorCode;
        }

        public string LastErrorMessage (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return "";

            return transport.LastErrorMessage;
        }
        #endregion

        #region internal implementation
        private FunapiTransport FindOtherTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (protocol == TransportProtocol.kDefault || transports_.Count <= 0)
                    return null;

                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.protocol != protocol && transport.Started)
                    {
                        return transport;
                    }
                }
            }

            return null;
        }

        private void PrepareSession(string session_id)
        {
            if (session_id_.Length == 0)
            {
                DebugUtils.Log("New session id: " + session_id);
                OpenSession(session_id);
            }

            if (session_id_ != session_id)
            {
                DebugUtils.Log("Session id changed: " + session_id_ + " => " + session_id);

                CloseSession();
                OpenSession(session_id);
            }
        }

        private void OpenSession(string session_id)
        {
            DebugUtils.Assert(session_id_.Length == 0);

            lock (state_lock_)
            {
                state_ = State.kConnected;
            }

            session_id_ = session_id;

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.state == FunapiTransport.State.kWaitForSession ||
                        transport.state == FunapiTransport.State.kWaitForSessionResponse)
                    {
                        transport.OnStarted();
                    }
                }
            }

            if (OnSessionInitiated != null)
            {
                OnSessionInitiated(session_id_);
            }

            if (unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }
        }

        private void CloseSession()
        {
            if (session_id_.Length == 0)
                return;

            lock (state_lock_)
            {
                state_ = State.kUnknown;
            }

            session_id_ = "";

            if (session_reliability_)
            {
                seq_recvd_ = 0;
                first_receiving_ = true;
                send_queue_.Clear();
                seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            }

            if (OnSessionClosed != null)
            {
                OnSessionClosed();
            }
        }

        private void OnTransportReceived (TransportProtocol protocol, Dictionary<string, string> header, ArraySegment<byte> body)
        {
            DebugUtils.Log("OnTransportReceived invoked.");
            last_received_ = DateTime.Now;

            lock (message_lock_)
            {
                message_buffer_.Add(new KeyValuePair<TransportProtocol, ArraySegment<byte>>(protocol, body));
            }
        }

        private string ProcessMessage (TransportProtocol protocol, ArraySegment<byte> buffer)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return "";

            string msg_type = "";
            string session_id = "";

            if (msg_type_ == FunMsgType.kJson)
            {
                object json;

                try
                {
                    string str = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                    json = transport.JsonHelper.Deserialize(str);
                    DebugUtils.Log("Parsed json: " + str);

                    DebugUtils.Assert(transport.JsonHelper.GetStringField(json, kSessionIdBodyField) is string);
                    string session_id_node = transport.JsonHelper.GetStringField(json, kSessionIdBodyField) as string;
                    session_id = session_id_node;
                    transport.JsonHelper.RemoveStringField(json, kSessionIdBodyField);

                    PrepareSession(session_id);

                    if (protocol == TransportProtocol.kTcp && session_reliability_)
                    {
                        if (transport.JsonHelper.HasField(json, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)transport.JsonHelper.GetIntegerField(json, kAckNumberField);
                            OnAckReceived(ack);
                            // Does not support piggybacking.
                            DebugUtils.Assert(!transport.JsonHelper.HasField(json, kMsgTypeBodyField));
                            return msg_type;
                        }

                        if (transport.JsonHelper.HasField(json, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(json, kSeqNumberField);
                            if (!OnSeqReceived(seq))
                            {
                                return msg_type;
                            }
                            transport.JsonHelper.RemoveStringField(json, kSeqNumberField);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Failure in ProcessMessage: " + e.ToString());
                    StopTransport(transport);
                    return msg_type;
                }

                if (transport.JsonHelper.HasField(json, kMsgTypeBodyField))
                {
                    string msg_type_node = transport.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                    msg_type = msg_type_node;
                    transport.JsonHelper.RemoveStringField(json, kMsgTypeBodyField);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, json);
                }
            }
            else if (msg_type_ == FunMsgType.kProtobuf)
            {
                FunMessage message;

                try
                {
                    MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                    message = (FunMessage)serializer_.Deserialize(stream, null, recv_type_);

                    session_id = message.sid;

                    PrepareSession(session_id);

                    if (protocol == TransportProtocol.kTcp && session_reliability_)
                    {
                        if (message.ackSpecified)
                        {
                            OnAckReceived(message.ack);
                            // Does not support piggybacking.
                            return msg_type;
                        }

                        if (message.seqSpecified)
                        {
                            if (!OnSeqReceived(message.seq))
                            {
                                return msg_type;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Failure in ProcessMessage: " + e.ToString());
                    StopTransport(transport);
                    return msg_type;
                }

                if (message.msgtype != null && message.msgtype.Length > 0)
                {
                    msg_type = message.msgtype;

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, message);
                }
            }
            else
            {
                Debug.Log("Invalid message type. type: " + msg_type_);
                DebugUtils.Assert(false);
                return msg_type;
            }

            if (!message_handlers_.ContainsKey(msg_type))
            {
                if (session_id_.Length > 0 && transport.state == FunapiTransport.State.kWaitForAck)
                {
                    transport.OnStarted();

                    if (unsent_queue_.Count > 0)
                    {
                        SendUnsentMessages();
                    }
                }

                Debug.Log("No handler for message '" + msg_type + "'. Ignoring.");
            }

            return msg_type;
        }

        private void SendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            Debug.Log("SendUnsentMessages - " + unsent_queue_.Count + " unsent messages.");

            foreach (UnsentMessage msg in unsent_queue_)
            {
                FunapiTransport transport = GetTransport(msg.protocol);
                if (transport == null)
                {
                    Debug.Log("SendUnsentMessages - Can't find a '" + msg.protocol + "' transport.\n" +
                              "message skipped.");
                    continue;
                }

                if (msg_type_ == FunMsgType.kJson)
                {
                    object json = msg.message;

                    // Encodes a messsage type
                    transport.JsonHelper.SetStringField(json, kMsgTypeBodyField, msg.msgtype);

                    if (session_id_.Length > 0)
                        transport.JsonHelper.SetStringField(json, kSessionIdBodyField, session_id_);

                    if (session_reliability_ && transport.protocol == TransportProtocol.kTcp)
                    {
                        transport.JsonHelper.SetIntegerField(json, kSeqNumberField, seq_);
                        ++seq_;

                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msgtype + " seq:" + (seq_ - 1));
                    }
                    else
                    {
                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msgtype);
                    }

                    transport.SendMessage(msg.msgtype, json, EncryptionType.kDefaultEncryption);
                }
                else if (msg_type_ == FunMsgType.kProtobuf)
                {
                    FunMessage message = msg.message as FunMessage;
                    if (session_id_.Length > 0)
                        message.sid = session_id_;

                    if (session_reliability_ && transport.protocol == TransportProtocol.kTcp)
                    {
                        message.seq = seq_;
                        ++seq_;

                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msgtype + " seq:" + message.seq);
                    }
                    else
                    {
                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msgtype);
                    }

                    transport.SendMessage(message, EncryptionType.kDefaultEncryption);
                }
                else
                {
                    DebugUtils.Assert(false);
                }
            }

            unsent_queue_.Clear();
        }

        private bool SeqLess(UInt32 x, UInt32 y)
        {
            Int32 dist = (Int32)(x - y);
            return dist > 0;
        }

        private void SendAck(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
            if (transport == null)
                return;

            Debug.Log(transport.protocol + " send ack message - ack:" + ack);

            if (msg_type_ == FunMsgType.kJson)
            {
                object ack_msg = transport.JsonHelper.Deserialize("{}");
                transport.JsonHelper.SetStringField(ack_msg, kSessionIdBodyField, session_id_);
                transport.JsonHelper.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport.SendMessage("", ack_msg, EncryptionType.kDefaultEncryption);
            }
            else
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.sid = session_id_;
                ack_msg.ack = ack;
                transport.SendMessage(ack_msg, EncryptionType.kDefaultEncryption);
            }
        }

        private void SendEmptyMessage (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                Debug.Log("SendEmptyMessage - transport is null.");
                return;
            }

            Debug.Log(transport.protocol + " send empty message");

            if (msg_type_ == FunMsgType.kJson)
            {
                object ack_msg = transport.JsonHelper.Deserialize("{}");
                transport.SendMessage("", ack_msg, EncryptionType.kDefaultEncryption);
            }
            else
            {
                FunMessage ack_msg = new FunMessage();
                transport.SendMessage(ack_msg, EncryptionType.kDefaultEncryption);
            }
        }

        private bool OnSeqReceived(UInt32 seq)
        {
            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                if (seq_recvd_ + 1 != seq)
                {
                    Debug.LogWarning("Received wrong sequence number " + seq.ToString() +
                                     ".(" + (seq_recvd_ + 1).ToString() + " expected");
                    DebugUtils.Assert(false);
                    Stop();
                    return false;
                }
            }

            seq_recvd_ = seq;
            SendAck(seq_recvd_ + 1);
            return true;
        }

        private void OnAckReceived(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
            if (transport == null)
            {
                Debug.LogError("OnAckReceived - transport is null.");
                return;
            }

            while (send_queue_.Count > 0)
            {
                UInt32 seq;
                object last_msg = send_queue_.Peek();
                if (msg_type_ == FunMsgType.kJson)
                {
                    seq = (UInt32)transport.JsonHelper.GetIntegerField(last_msg, kSeqNumberField);
                }
                else if (msg_type_ == FunMsgType.kProtobuf)
                {
                    seq = (last_msg as FunMessage).seq;
                }
                else
                {
                    DebugUtils.Assert(false);
                    seq = 0;
                }

                if (SeqLess(ack, seq))
                {
                    send_queue_.Dequeue();
                }
                else
                {
                    break;
                }
            }

            if (transport.state == FunapiTransport.State.kWaitForAck)
            {
                foreach (object msg in send_queue_)
                {
                    if (msg_type_ == FunMsgType.kJson)
                    {
                        UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(msg, kSeqNumberField);
                        DebugUtils.Assert(seq == ack || SeqLess(seq, ack));

                        string msgtype = transport.JsonHelper.GetStringField(msg, kMsgTypeBodyField) as string;
                        transport.SendMessage(msgtype, msg, EncryptionType.kDefaultEncryption);
                    }
                    else if (msg_type_ == FunMsgType.kProtobuf)
                    {
                        UInt32 seq = (msg as FunMessage).seq;
                        DebugUtils.Assert(seq == ack || SeqLess (seq, ack));
                        transport.SendMessage(msg as FunMessage, EncryptionType.kDefaultEncryption);
                    }
                    else
                    {
                        DebugUtils.Assert(false);
                    }
                }

                transport.OnStarted();

                if (unsent_queue_.Count > 0)
                {
                    SendUnsentMessages();
                }
            }
        }

        private void OnConnectTimeout (TransportProtocol protocol)
        {
            if (protocol != TransportProtocol.kTcp || session_reliability_ == false)
            {
                StopTransport(protocol);
            }
        }

        private void OnTransportStarted (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            DebugUtils.Assert(transport != null);
            Debug.Log("'" + protocol + "' Transport started.");

            lock (state_lock_)
            {
                if (session_id_.Length > 0)
                {
                    state_ = State.kConnected;

                    if (session_reliability_ && protocol == TransportProtocol.kTcp && seq_recvd_ != 0)
                    {
                        transport.state = FunapiTransport.State.kWaitForAck;
                        SendAck(seq_recvd_ + 1);
                    }
                    else
                    {
                        transport.OnStarted();

                        if (unsent_queue_.Count > 0)
                        {
                            SendUnsentMessages();
                        }
                    }
                }
                else if (state_ == State.kStarted)
                {
                    state_ = State.kWaitForSession;
                    transport.state = FunapiTransport.State.kWaitForSessionResponse;

                    // To get a session id
                    SendEmptyMessage(protocol);
                }
                else if (state_ == State.kWaitForSession)
                {
                    transport.state = FunapiTransport.State.kWaitForSession;
                }
            }
        }

        private void OnTransportStopped (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            DebugUtils.Assert(transport != null);
            Debug.Log("'" + protocol + "' Transport Stopped.");

            lock (state_lock_)
            {
                if (state_ != State.kStopped)
                {
                    lock (transports_lock_)
                    {
                        bool all_stopped = true;
                        foreach (FunapiTransport t in transports_.Values)
                        {
                            if (t.Started)
                            {
                                all_stopped = false;
                                break;
                            }
                        }

                        if (all_stopped)
                        {
                            state_ = State.kStopped;
                            OnStoppedAllTransportCallback();
                        }
                    }
                }
            }
        }

        private void OnStoppedAllTransportCallback()
        {
            Debug.Log("All transports has stopped.");

            if (StoppedAllTransportCallback != null)
                StoppedAllTransportCallback();
        }
        #endregion

        #region Funapi system message handlers
        private void OnNewSession(string msg_type, object body)
        {
            // ignore.
        }

        private void OnSessionTimedout(string msg_type, object body)
        {
            Debug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");

            CloseSession();
        }

        private void OnMaintenanceMessage(string msg_type, object body)
        {
            if (MaintenanceCallback != null)
            {
                MaintenanceCallback(msg_type, body);
            }
        }
        #endregion


        // Status
        public enum State
        {
            kUnknown = 0,
            kStarted,
            kConnected,
            kWaitForSession,
            kWaitForStop,
            kStopped
        };

        // Delegates
        public delegate void MessageEventHandler(string msg_type, object body);
        public delegate void TimeoutEventHandler(string msg_type);
        public delegate void SessionInitHandler(string session_id);
        public delegate void SessionCloseHandler();
        public delegate void NotifyHandler();

        // Unsent queue-releated class
        class UnsentMessage
        {
            public UnsentMessage(string msgtype, object message, TransportProtocol protocol)
            {
                this.msgtype = msgtype;
                this.message = message;
                this.protocol = protocol;
            }

            public string msgtype;
            public object message;
            public TransportProtocol protocol;
        }

        // Callback timer for expected reply messages
        class ExpectedReplyMessage
        {
            public ExpectedReplyMessage (string type, float time, TimeoutEventHandler cb)
            {
                msgtype = type;
                wait_time = time;
                callback = cb;
            }

            public string msgtype;
            public float wait_time;
            public TimeoutEventHandler callback;
        }

        // Funapi message-related events.
        public event SessionInitHandler OnSessionInitiated;
        public event SessionCloseHandler OnSessionClosed;
        public event MessageEventHandler MaintenanceCallback;
        public event NotifyHandler StoppedAllTransportCallback;

        // Funapi message-related constants.
        private static readonly float kFunapiSessionTimeout = 3600.0f;
        private static readonly string kMsgTypeBodyField = "_msgtype";
        private static readonly string kSessionIdBodyField = "_sid";
        private static readonly string kSeqNumberField = "_seq";
        private static readonly string kAckNumberField = "_ack";
        private static readonly string kNewSessionMessageType = "_session_opened";
        private static readonly string kSessionClosedMessageType = "_session_closed";
        private static readonly string kMaintenanceMessageType = "_maintenance";

        // Member variables.
        private State state_;
        private Type recv_type_;
        private FunMsgType msg_type_;
        private TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        private FunMessageSerializer serializer_;
        private Dictionary<TransportProtocol, FunapiTransport> transports_ = new Dictionary<TransportProtocol, FunapiTransport>();
        private string session_id_ = "";
        private Dictionary<string, TransportProtocol> message_protocols_ = new Dictionary<string, TransportProtocol>();
        private Dictionary<string, MessageEventHandler> message_handlers_ = new Dictionary<string, MessageEventHandler>();
        private Dictionary<string, List<ExpectedReplyMessage>> expected_replies_ = new Dictionary<string, List<ExpectedReplyMessage>>();
        private List<KeyValuePair<TransportProtocol, ArraySegment<byte>>> message_buffer_ = new List<KeyValuePair<TransportProtocol, ArraySegment<byte>>>();
        private object state_lock_ = new object();
        private object message_lock_ = new object();
        private object transports_lock_ = new object();
        private object expected_reply_lock = new object();
        private DateTime last_received_ = DateTime.Now;

        // Reliability-releated member variables.
        private bool session_reliability_;
        private UInt32 seq_;
        private UInt32 seq_recvd_;
        private bool first_receiving_;
        private System.Collections.Queue send_queue_ = new System.Collections.Queue();
        private System.Collections.Queue unsent_queue_ = new System.Collections.Queue();
        private System.Random rnd_ = new System.Random();
    }
}  // namespace Fun
