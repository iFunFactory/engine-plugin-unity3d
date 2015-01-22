// Copyright (C) 2013-2014 iFunFactory Inc. All Rights Reserved.
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
using System.Threading;
using UnityEngine;

// Protobuf
using funapi.network.fun_message;


namespace Fun
{
    // Funapi version
    public class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 48;
    }

    // Funapi message type
    public enum FunMsgType
    {
        kJson,
        kProtobuf
    }

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
    public delegate void ReceivedEventHandler(Dictionary<string, string> header, ArraySegment<byte> body);
    public delegate void ConnectTimeoutHandler();
    public delegate void StartedEventHandler();
    public delegate void StoppedEventHandler();

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
        // Start connecting
        public abstract void Start();

        // Disconnection
        public abstract void Stop();

        // Check connection
        public abstract bool Started { get; }

        // Send a message
        public abstract void SendMessage(object json_message, EncryptionType encryption);
        public abstract void SendMessage(FunMessage message, EncryptionType encryption);

        // Registered event handlers.
        public event ConnectTimeoutHandler ConnectTimeoutCallback;
        public event StartedEventHandler StartedCallback;
        public event StoppedEventHandler StoppedCallback;
        public event ReceivedEventHandler ReceivedCallback;

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
        public virtual void Update ()
        {
        }

        protected void OnConnectionTimeout ()
        {
            ConnectTimeoutCallback();
        }

        protected void OnReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            ReceivedCallback(header, body);
        }

        protected void OnStarted ()
        {
            if (StartedCallback != null)
            {
                StartedCallback();
            }
        }

        protected void OnStopped ()
        {
            StoppedCallback();
        }

        public virtual bool IsStream()
        {
            return false;
        }

        public virtual bool IsDatagram()
        {
            return false;
        }

        public virtual bool IsRequestResponse()
        {
            return false;
        }

        public float ConnectTimeout
        {
            get;
            set;
        }

        public ErrorCode LastErrorCode
        {
            get { return last_error_code_; }
        }

        public string LastErrorMessage
        {
            get { return last_error_message_; }
        }


        protected enum State
        {
            kDisconnected = 0,
            kConnecting,
            kEncryptionHandshaking,
            kConnected
        };

        protected enum EncryptionMethod
        {
            kNone = 0,
            kIFunEngine1
        }


        protected State state_ = State.kDisconnected;
        protected JsonAccessor json_accessor_ = new DictionaryJsonAccessor();
        protected FunMessageSerializer serializer_ = null;
        protected ErrorCode last_error_code_ = ErrorCode.kNone;
        protected string last_error_message_ = "";
        #endregion
    }


    // Transport class for socket
    public abstract class FunapiEncryptedTransport : FunapiTransport
    {
        // Create a socket.
        protected abstract void Init();

        // Sends a packet.
        protected abstract void WireSend (List<SendingBuffer> sending);

        #region public interface
        // Starts a socket.
        public override void Start()
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

                OnStarted();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in Start: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        // Stops a socket.
        public override void Stop()
        {
            if (state_ == State.kDisconnected)
                return;

            state_ = State.kDisconnected;
            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";

            OnStopped();
        }

        public void SetEncryption (EncryptionType encryption)
        {
            Encryptor encryptor = Encryptor.Create(encryption);
            if (encryptor == null)
            {
                last_error_code_ = ErrorCode.kInvalidEncryption;
                last_error_message_ = "Failed to create encryptor: " + encryption;
                DebugUtils.LogWarning(last_error_message_);
                return;
            }

            default_encryptor_ = (int)encryption;
            encryptors_[encryption] = encryptor;
        }

        // Sends a JSON message through a socket.
        public override void SendMessage (object json_message, EncryptionType encryption)
        {
            string str = this.JsonHelper.Serialize(json_message);
            byte[] body = Encoding.UTF8.GetBytes(str);

            DebugUtils.Log("JSON to send : " + str);

            SendMessage(body, encryption);
        }

        public override void SendMessage (FunMessage message, EncryptionType encryption)
        {
            MemoryStream stream = new MemoryStream();
            this.ProtobufHelper.Serialize (stream, message);

            byte[] body = new byte[stream.Length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(body, 0, body.Length);

            SendMessage(body, encryption);
        }
        #endregion

        #region internal implementation
        private void SendMessage (byte[] body, EncryptionType encryption)
        {
            bool failed = false;
            try
            {
                lock (sending_)
                {
                    pending_.Add(new SendingBuffer(new ArraySegment<byte>(body), encryption));

                    if (state_ == State.kConnected && sending_.Count == 0)
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
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        protected bool EncryptThenSendMessage()
        {
            DebugUtils.Assert(state_ == State.kConnected);
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
                        DebugUtils.LogWarning(last_error_message_);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kEstablished)
                    {
                        last_error_code_ = ErrorCode.kInvalidEncryption;
                        last_error_message_ = "'" + encryptor.name + "' is invalid encryption type. Check out the encryption type of server.";
                        DebugUtils.LogWarning(last_error_message_);
                        return false;
                    }

                    Int64 nSize = encryptor.Encrypt(buffer.data, buffer.data, ref encryption_header);
                    if (nSize <= 0)
                    {
                        last_error_code_ = ErrorCode.kEncryptionFailed;
                        last_error_message_ = "Encrypt failure: " + encryptor.name;
                        DebugUtils.LogWarning(last_error_message_);
                        return false;
                    }

                    DebugUtils.Assert(nSize == buffer.data.Count);
                }

                string header = "";
                header += kVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kProtocolVersion + kHeaderDelimeter;
                if (first_sending)
                {
                    header += kPluginVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kPluginVersion + kHeaderDelimeter;
                    first_sending = false;
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

                SendingBuffer header_buffer = new SendingBuffer(new ArraySegment<byte>(Encoding.ASCII.GetBytes(header)));
                sending_.Insert(i, header_buffer);

                DebugUtils.Log("Header to send: " + header + " body length: " + buffer.data.Count);
            }

            WireSend(sending_);

            return true;
        }

        // Checks buffer space before starting another async receive.
        protected void CheckReceiveBuffer()
        {
            int remaining_size = receive_buffer.Length - received_size_;

            if (remaining_size <= 0)
            {
                byte[] new_buffer = null;

                if (remaining_size == 0 && next_decoding_offset_ > 0)
                    new_buffer = new byte[receive_buffer.Length];
                else
                    new_buffer = new byte[receive_buffer.Length + kUnitBufferSize];

                // If there are space can be collected, compact it first.
                // Otherwise, increase the receiving buffer size.
                if (next_decoding_offset_ > 0)
                {
                    DebugUtils.Log("Compacting a receive buffer to save " + next_decoding_offset_ + " bytes.");
                    Buffer.BlockCopy(receive_buffer, next_decoding_offset_, new_buffer, 0, received_size_ - next_decoding_offset_);
                    receive_buffer = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    DebugUtils.Log("Increasing a receive buffer to " + (receive_buffer.Length + kUnitBufferSize) + " bytes.");
                    Buffer.BlockCopy(receive_buffer, 0, new_buffer, 0, received_size_);
                    receive_buffer = new_buffer;
                }
            }
        }

        protected bool TryToDecodeHeader()
        {
            DebugUtils.Log("Trying to decode header fields.");

            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    DebugUtils.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }
                string line = Encoding.ASCII.GetString(receive_buffer, next_decoding_offset_, offset - next_decoding_offset_);
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

        protected bool TryToDecodeBody()
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

            if (state_ == State.kEncryptionHandshaking)
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
                        DebugUtils.Log("Set default encryption: " + default_encryptor_);
                    }

                    // Create encryptors
                    foreach (EncryptionType type in encryption_list)
                    {
                        Encryptor encryptor = Encryptor.Create(type);
                        if (encryptor == null)
                        {
                            DebugUtils.LogWarning("Failed to create encryptor: " + type);
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
                        DebugUtils.LogWarning("Unknown encryption: " + encryption_str);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kHandshaking)
                    {
                        DebugUtils.LogWarning("Unexpected handshake message: " + encryptor.name);
                        return false;
                    }

                    string out_header = "";
                    if (!encryptor.Handshake(encryption_header, ref out_header))
                    {
                        DebugUtils.LogWarning("Encryption handshake failure: " + encryptor.name);
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

                bool handshake_complte = true;
                foreach (KeyValuePair<EncryptionType, Encryptor> pair in encryptors_)
                {
                    if (pair.Value.state != Encryptor.State.kEstablished)
                    {
                        handshake_complte = false;
                        break;
                    }
                }

                if (handshake_complte)
                {
                    // Makes a state transition.
                    state_ = State.kConnected;
                    DebugUtils.Log("Ready to receive.");

                    // Starts to process if there any data already queue.
                    lock (sending_)
                    {
                        if (pending_.Count > 0)
                        {
                            DebugUtils.Log("Flushing pending messages.");
                            List<SendingBuffer> tmp = sending_;
                            sending_ = pending_;
                            pending_ = tmp;

                            if (!EncryptThenSendMessage())
                                return false;
                        }
                    }
                }
            }

            if (body_length > 0)
            {
                DebugUtils.Assert(state_ == State.kConnected);

                if (state_ != State.kConnected)
                {
                    DebugUtils.Log("Unexpected message.");
                    return false;
                }

                if ((encryptors_.Count == 0) != (encryption_str.Length == 0))
                {
                    DebugUtils.Log("Unknown encryption: " + encryption_str);
                    return false;
                }

                if (encryptors_.Count > 0)
                {
                    EncryptionType encryption = (EncryptionType)Convert.ToInt32(encryption_str);
                    Encryptor encryptor = encryptors_[encryption];

                    if (encryptor == null)
                    {
                        DebugUtils.Log("Unknown encryption: " + encryption_str);
                        return false;
                    }

                    ArraySegment<byte> body_bytes = new ArraySegment<byte>(receive_buffer, next_decoding_offset_, body_length);
                    DebugUtils.Assert(body_bytes.Count == body_length);

                    Int64 nSize = encryptor.Decrypt(body_bytes, body_bytes, encryption_header);
                    if (nSize <= 0)
                    {
                        DebugUtils.Log("Failed to decrypt.");
                        return false;
                    }

                    // TODO: Implementation
                    DebugUtils.Assert(body_length == nSize);
                }

                ArraySegment<byte> body = new ArraySegment<byte>(receive_buffer, next_decoding_offset_, body_length);
                next_decoding_offset_ += body_length;

                // The network module eats the fields and invoke registered handler.
                OnReceived(header_fields_, body);
            }

            // Prepares a next message.
            header_decoded_ = false;
            header_fields_.Clear();
            return true;
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
        #endregion

        protected class SendingBuffer
        {
            public SendingBuffer (ArraySegment<byte> data, EncryptionType encryption = EncryptionType.kDefaultEncryption)
            {
                this.encryption = encryption;
                this.data = data;
            }

            public EncryptionType encryption;
            public ArraySegment<byte> data;
        }


        // Buffer-related constants.
        protected static readonly int kUnitBufferSize = 65536;

        // Funapi header-related constants.
        protected static readonly string kHeaderDelimeter = "\n";
        protected static readonly string kHeaderFieldDelimeter = ":";
        protected static readonly string kVersionHeaderField = "VER";
        protected static readonly string kPluginVersionHeaderField = "PVER";
        protected static readonly string kLengthHeaderField = "LEN";
        protected static readonly string kEncryptionHeaderField = "ENC";

        // Encryption-releated constants.
        private static readonly string kEncryptionHandshakeBegin = "HELLO!";
        private static readonly int kNoneEncryption = 0;
        private static readonly char kDelim1 = '-';
        private static readonly char kDelim2 = ',';

        // for speed-up.
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // State-related.
        private bool first_sending = true;
        protected bool header_decoded_ = false;
        protected int received_size_ = 0;
        protected int next_decoding_offset_ = 0;
        protected byte[] receive_buffer = new byte[kUnitBufferSize];
        protected byte[] send_buffer_ = new byte[kUnitBufferSize];
        protected List<SendingBuffer> pending_ = new List<SendingBuffer>();
        protected List<SendingBuffer> sending_ = new List<SendingBuffer>();
        protected Dictionary<string, string> header_fields_ = new Dictionary<string, string>();
        protected int default_encryptor_ = kNoneEncryption;
        protected Dictionary<EncryptionType, Encryptor> encryptors_ = new Dictionary<EncryptionType, Encryptor>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiEncryptedTransport
    {
        #region public interface
        public FunapiTcpTransport(string hostname_or_ip, UInt16 port)
        {
            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            connect_ep_ = new IPEndPoint(address, port);
        }

        // Stops a socket.
        public override void Stop()
        {
            if (state_ == State.kDisconnected)
                return;

            base.Stop();

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }
        }

        public override bool Started
        {
            get { return sock_ != null && sock_.Connected && state_ == State.kConnected; }
        }

        public override bool IsStream()
        {
            return true;
        }

        public override void Update ()
        {
            if (state_ == State.kConnecting && connect_timeout > 0f)
            {
                connect_timeout -= Time.deltaTime;
                if (connect_timeout <= 0f)
                {
                    DebugUtils.Log("Connection waiting time has been exceeded.");
                    OnConnectionTimeout();
                }
            }
        }
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnecting;
            connect_timeout = ConnectTimeout;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sock_.BeginConnect(connect_ep_, new AsyncCallback(this.StartCb), this);
        }

        protected override void WireSend(List<SendingBuffer> sending)
        {
            List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();
            foreach (SendingBuffer buffer in sending)
            {
                list.Add(buffer.data);
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }
                DebugUtils.Log("Connected.");

                state_ = State.kEncryptionHandshaking;

                lock (receive_buffer)
                {
                    // Wait for encryption handshaking message.
                    ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer, 0, receive_buffer.Length);
                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                    buffer.Add(wrapped);
                    sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in StartCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                int nSent = sock_.EndSend(ar);
                DebugUtils.Log("Sent " + nSent + "bytes");

                lock (sending_)
                {
                    // Removes any segment fully sent.
                    while (nSent > 0)
                    {
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

                    if (sending_.Count > 0)
                    {
                        // If we have more segments to send, we process more.
                        DebugUtils.Log("Retrying unsent messages.");
                        WireSend(sending_);
                    }
                    else if (pending_.Count > 0)
                    {
                        // Otherwise, try to process pending messages.
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
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (receive_buffer)
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
                        ArraySegment<byte> residual = new ArraySegment<byte>(receive_buffer, received_size_, receive_buffer.Length - received_size_);
                        List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                        buffer.Add(residual);
                        sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                        DebugUtils.Log("Ready to receive more. We can receive upto " + (receive_buffer.Length - received_size_) + " more bytes");
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        DebugUtils.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            DebugUtils.Log("Buffer has " + (receive_buffer.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                        }
                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can't not receive messages. Maybe the socket is closed.";
                        DebugUtils.Log(last_error_message_);
                        failed = true;
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        protected Socket sock_;
        private IPEndPoint connect_ep_;
        private float connect_timeout = 0f;
        #endregion
    }


    // UDP transport layer
    public class FunapiUdpTransport : FunapiEncryptedTransport
    {
        #region public interface
        public FunapiUdpTransport(string hostname_or_ip, UInt16 port)
        {
            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            send_ep_ = new IPEndPoint(address, port);
            receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, port);
        }

        // Stops a socket.
        public override void Stop()
        {
            if (state_ == State.kDisconnected)
                return;

            base.Stop();

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }
        }

        public override bool Started
        {
            get { return sock_ != null && state_ == State.kConnected; }
        }

        public override bool IsDatagram()
        {
            return true;
        }
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnected;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock_.BeginReceiveFrom(receive_buffer, 0, receive_buffer.Length, SocketFlags.None,
                                   ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);
        }

        // Send a packet.
        protected override void WireSend(List<SendingBuffer> sending)
        {
            DebugUtils.Assert(sending.Count >= 2);

            int length = sending[0].data.Count + sending[1].data.Count;
            if (length > send_buffer_.Length)
            {
                send_buffer_ = new byte[length];
            }

            int offset = 0;

            // one header + one body
            for (int i = 0; i < 2; ++i)
            {
                ArraySegment<byte> item = sending[i].data;
                Buffer.BlockCopy(item.Array, 0, send_buffer_, offset, item.Count);
                offset += item.Count;
            }

            if (offset > 0)
            {
                if (offset > kUnitBufferSize)
                {
                    DebugUtils.LogWarning("Message is greater than 64KB. It will be truncated.");
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (sending_)
                {
                    int nSent = sock_.EndSend(ar);
                    DebugUtils.Log("Sent " + nSent + "bytes");

                    // Removes header and body segment
                    int nToSend = 0;
                    for (int i = 0; i < 2; ++i)
                    {
                        nToSend += sending_[0].data.Count;
                        sending_.RemoveAt(0);
                    }

                    if (nSent > 0 && nSent < nToSend)
                    {
                        DebugUtils.LogWarning("Failed to transfer hole messages.");
                        DebugUtils.Assert(false);
                    }

                    if (sending_.Count > 0)
                    {
                        // If we have more segments to send, we process more.
                        DebugUtils.Log("Retrying unsent messages.");
                        WireSend(sending_);
                    }
                    else if (pending_.Count > 0)
                    {
                        // Otherwise, try to process pending messages.
                        List<SendingBuffer> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                        if (!EncryptThenSendMessage())
                            failed = true;
                    }

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (receive_buffer)
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
                        receive_buffer = new byte[kUnitBufferSize];
                        received_size_ = 0;
                        next_decoding_offset_ = 0;

                        // Starts another async receive
                        sock_.BeginReceiveFrom(receive_buffer, received_size_, receive_buffer.Length - received_size_, SocketFlags.None,
                                               ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

                        DebugUtils.Log("Ready to receive more. We can receive upto " + receive_buffer.Length + " more bytes");
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        DebugUtils.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            DebugUtils.Log("Buffer has " + (receive_buffer.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                        }
                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can't not receive messages. Maybe the socket is closed.";
                        DebugUtils.Log(last_error_message_);
                        failed = true;
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }


        protected Socket sock_;
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
            // Url
            if (https)
                host_url_ = "https://";
            else
                host_url_ = "http://";

            host_url_ += hostname_or_ip + ":" + port;

            // Version
            host_url_ += "/v" + FunapiVersion.kProtocolVersion + "/";
        }

        public override void Stop()
        {
            if (state_ == State.kDisconnected)
                return;

            base.Stop();

            foreach (WebState state in list_)
            {
                if (state.request != null)
                {
                    state.aborted = true;
                    state.request.Abort();
                }

                if (state.stream != null)
                    state.stream.Close();
            }

            list_.Clear();
        }

        public override bool Started
        {
            get { return state_ == State.kConnected; }
        }

        public override bool IsRequestResponse()
        {
            return true;
        }

        #endregion

        #region internal implementation
        protected override void Init()
        {
            state_ = State.kConnected;
        }

        protected override void WireSend(List<SendingBuffer> sending)
        {
            DebugUtils.Assert(sending.Count >= 2);
            DebugUtils.Log("Send a Message.");

            bool failed = false;
            try
            {
                DebugUtils.Log("Host Url: " + host_url_);

                SendingBuffer header = sending[0];
                SendingBuffer body = sending[1];

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
                WebState state = new WebState();
                state.request = request;
                state.sending = body.data;
                list_.Add(state);

                request.BeginGetRequestStream(new AsyncCallback(RequestStreamCb), state);
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in WireSend: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void RequestStreamCb (IAsyncResult ar)
        {
            DebugUtils.Log("RequestStreamCb called.");

            bool failed = false;
            try
            {
                WebState state = (WebState)ar.AsyncState;
                HttpWebRequest request = state.request;

                Stream stream = request.EndGetRequestStream(ar);
                stream.Write(state.sending.Array, 0, state.sending.Count);
                stream.Close();
                DebugUtils.Log("Sent " + state.sending.Count + "bytes");

                lock (sending_)
                {
                    // Removes header and body segment
                    sending_.RemoveAt(0);
                    sending_.RemoveAt(0);
                }

                request.BeginGetResponse(new AsyncCallback(ResponseCb), state);
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in RequestStreamCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ResponseCb (IAsyncResult ar)
        {
            DebugUtils.Log("ResponseCb called.");

            bool failed = false;
            try
            {
                WebState state = (WebState)ar.AsyncState;
                if (state.aborted)
                    return;

                HttpWebResponse response = (HttpWebResponse)state.request.EndGetResponse(ar);
                state.request = null;
                state.response = response;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Stream stream = response.GetResponseStream();
                    state.stream = stream;
                    state.buffer = new byte[kUnitBufferSize];
                    state.read_data = new byte[kUnitBufferSize];
                    state.read_offset = 0;

                    stream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(ReadCb), state);
                }
                else
                {
                    DebugUtils.Log("Failed response. status:" + response.StatusDescription);
                    DebugUtils.Assert(false);
                    list_.Remove(state);
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ResponseCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReadCb (IAsyncResult ar)
        {
            DebugUtils.Log("ReadCb called.");

            bool failed = false;
            try
            {
                WebState state = (WebState)ar.AsyncState;
                int nRead = state.stream.EndRead(ar);

                if (nRead > 0)
                {
                    DebugUtils.Log("We need more bytes for response. Waiting.");
                    if (state.read_offset + nRead > state.read_data.Length)
                    {
                        byte[] temp = new byte[state.read_data.Length + kUnitBufferSize];
                        Buffer.BlockCopy(state.read_data, 0, temp, 0, state.read_offset);
                        state.read_data = temp;
                    }

                    Buffer.BlockCopy(state.buffer, 0, state.read_data, state.read_offset, nRead);
                    state.read_offset += nRead;

                    state.stream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(ReadCb), state);
                }
                else
                {
                    if (state.response == null)
                    {
                        DebugUtils.LogWarning("Response instance is null.");
                        DebugUtils.Assert(false);
                    }

                    lock (receive_buffer)
                    {
                        // Header
                        byte[] header = state.response.Headers.ToByteArray();
                        string str_header = Encoding.ASCII.GetString(header, 0, header.Length);
                        str_header = str_header.Insert(0, kVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kProtocolVersion + kHeaderDelimeter);
                        str_header = str_header.Replace(kLengthHttpHeaderField, kLengthHeaderField);
                        str_header = str_header.Replace(kEncryptionHttpHeaderField, kEncryptionHeaderField);
                        str_header = str_header.Replace("\r", "");
                        header = Encoding.ASCII.GetBytes(str_header);

                        // Checks buffer space
                        int offset = received_size_;
                        received_size_ += header.Length + state.read_offset;
                        CheckReceiveBuffer();

                        // Copy to buffer
                        Buffer.BlockCopy(header, 0, receive_buffer, offset, header.Length);
                        Buffer.BlockCopy(state.read_data, 0, receive_buffer, offset + header.Length, state.read_offset);

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

                        state.stream.Close();
                        state.stream = null;
                        list_.Remove(state);

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }

                    lock (sending_)
                    {
                        if (sending_.Count > 0)
                        {
                            // If we have more segments to send, we process more.
                            DebugUtils.Log("Retrying unsent messages.");
                            WireSend(sending_);
                        }
                        else if (pending_.Count > 0)
                        {
                            // Otherwise, try to process pending messages.
                            List<SendingBuffer> tmp = sending_;
                            sending_ = pending_;
                            pending_ = tmp;

                            if (!EncryptThenSendMessage())
                                failed = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReadCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }
        #endregion


        // Funapi header-related constants.
        private static readonly string kLengthHttpHeaderField = "content-length";
        private static readonly string kEncryptionHttpHeaderField = "X-iFun-Enc";

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
            public ArraySegment<byte> sending;
        }

        // member variables.
        private string host_url_;
        private List<WebState> list_ = new List<WebState>();
    }


    // Driver to use Funapi network plugin.
    public class FunapiNetwork
    {
        #region Handler delegate definition
        public delegate void MessageHandler(string msg_type, object body);
        public delegate void TimeoutHandler(string msg_type);
        public delegate void OnSessionInitiated(string session_id);
        public delegate void OnSessionClosed();
        public delegate void OnTransportClosed();
        public delegate void OnMessageHandler(object body);
        #endregion

        #region public interface
        public FunapiNetwork(FunapiTransport transport, FunMsgType type, bool session_reliability,
                             OnSessionInitiated on_session_initiated, OnSessionClosed on_session_closed)
        {
            state_ = State.kUnknown;
            transport_ = transport;
            msg_type_ = type;
            on_session_initiated_ = on_session_initiated;
            on_session_closed_ = on_session_closed;
            transport_.ConnectTimeoutCallback += new ConnectTimeoutHandler(OnConnectTimeout);
            transport_.StartedCallback += new StartedEventHandler(OnTransportStarted);
            transport_.StoppedCallback += new StoppedEventHandler(OnTransportStopped);
            transport_.ReceivedCallback += new ReceivedEventHandler(OnTransportReceived);

            if (session_reliability && transport.IsStream() && !transport.IsRequestResponse())
            {
                session_reliability_ = true;
            }
            else
            {
                session_reliability_ = false;
            }
            seq_ = 0;
            seq_recvd_ = 0;
            first_receiving_ = true;
            send_queue_ = new System.Collections.Queue();
            rnd_ = new System.Random();

            serializer_ = new FunMessageSerializer ();
            transport_.ProtobufHelper = serializer_;

            recv_type_ = typeof(FunMessage);
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

        public void Start()
        {
            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            message_handlers_[kMaintenanceMessageType] = this.OnMaintenanceMessage;
            DebugUtils.Log("Starting a network module.");
            transport_.Start();
            started_ = true;
        }

        public void Stop()
        {
            DebugUtils.Log("Stopping a network module.");
            started_ = false;

            if (transport_.Started)
                transport_.Stop();

            CloseSession();
        }

        // Your update method inheriting MonoBehaviour should explicitly invoke this method.
        public void Update ()
        {
            if (transport_ != null)
                transport_.Update();

            lock (message_buffer_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugUtils.Log("Update messages. count: " + message_buffer_.Count);

                    try
                    {
                        string msg_type;
                        foreach (ArraySegment<byte> buffer in message_buffer_)
                        {
                            msg_type = ProcessMessage(buffer);

                            if (expected_replies_.ContainsKey(msg_type))
                            {
                                expected_replies_.Remove(msg_type);
                            }
                        }

                        message_buffer_.Clear();
                    }
                    catch (Exception e)
                    {
                        DebugUtils.Log("Failure in Update: " + e.ToString());
                    }
                }
            }

            if (expected_replies_.Count > 0)
            {
                List<string> remove_list = new List<string>();

                foreach (var item in expected_replies_)
                {
                    item.Value.wait_time -= Time.deltaTime;
                    if (item.Value.wait_time <= 0f)
                    {
                        DebugUtils.Log("'" + item.Key + "' message waiting time has been exceeded.");
                        remove_list.Add(item.Key);
                        item.Value.callback(item.Key);
                    }
                }

                if (remove_list.Count > 0)
                {
                    foreach (string key in remove_list)
                    {
                        expected_replies_.Remove(key);
                    }
                }
            }
        }

        public bool Started
        {
            get
            {
                return started_;
            }
        }

        public bool Connected
        {
            get
            {
                return transport_.Started;
            }
        }

        public bool SessionReliability
        {
            get
            {
                return session_reliability_;
            }
        }

        public FunMsgType MsgType
        {
            get { return msg_type_; }
        }

        public void SendMessage(string msg_type, FunMessage message)
        {
            SendMessage(msg_type, message, EncryptionType.kDefaultEncryption);
        }

        public void SendMessage(string msg_type, FunMessage message, EncryptionType encryption)
        {
            DebugUtils.Assert (msg_type_ == FunMsgType.kProtobuf);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                message.sid = session_id_;
            }

            if (session_reliability_)
            {
                if (session_id_ == null || session_id_.Length == 0)
                {
                    seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
                }
                message.seq = seq_;
                ++seq_;
                send_queue_.Enqueue(message);
            }

            if (state_ == State.kUnknown || state_ == State.kEstablished)
            {
                message.msgtype = msg_type;
                transport_.SendMessage(message, encryption);
            }
        }

        public void SendMessage(string msg_type, FunMessage message,
                                string expected_reply_type, float expected_reply_time, TimeoutHandler onReplyMissed)
        {
            SendMessage(msg_type, message, EncryptionType.kDefaultEncryption, expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, FunMessage message, EncryptionType encryption,
                                string expected_reply_type, float expected_reply_time, TimeoutHandler onReplyMissed)
        {
            if (expected_replies_.ContainsKey(message.msgtype))
            {
                DebugUtils.Log("ERROR: Dictionary has the same key already exists. key: " + message.msgtype);
                DebugUtils.Assert(false);
            }

            expected_replies_[expected_reply_type] = new ExpectedReplyMessage(expected_reply_time, onReplyMissed);

            SendMessage(msg_type, message, encryption);
        }

        public void SendMessage(string msg_type, object body)
        {
            SendMessage(msg_type, body, EncryptionType.kDefaultEncryption);
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption)
        {
            DebugUtils.Assert (msg_type_ == FunMsgType.kJson);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            // Encodes a messsage type.
            transport_.JsonHelper.SetStringField(body, kMsgTypeBodyField, msg_type);

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                transport_.JsonHelper.SetStringField(body, kSessionIdBodyField, session_id_);
            }

            if (session_reliability_)
            {
                if (session_id_ == null || session_id_.Length == 0)
                {
                    seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
                }
                transport_.JsonHelper.SetIntegerField(body, kSeqNumberField, seq_);
                ++seq_;
                send_queue_.Enqueue(transport_.JsonHelper.Clone(body));
            }

            if (state_ == State.kUnknown || state_ == State.kEstablished)
            {
                transport_.SendMessage(body, encryption);
            }
        }

        public void SendMessage(string msg_type, object body,
                                string expected_reply_type, float expected_reply_time, TimeoutHandler onReplyMissed)
        {
            SendMessage(msg_type, body, EncryptionType.kDefaultEncryption, expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption,
                                string expected_reply_type, float expected_reply_time, TimeoutHandler onReplyMissed)
        {
            if (expected_replies_.ContainsKey(msg_type))
            {
                DebugUtils.Log("ERROR: Dictionary has the same key already exists. key: " + msg_type);
                DebugUtils.Assert(false);
            }

            expected_replies_[expected_reply_type] =  new ExpectedReplyMessage(expected_reply_time, onReplyMissed);

            SendMessage(msg_type, body, encryption);
        }

        public void RegisterHandler(string type, MessageHandler handler)
        {
            DebugUtils.Log("New handler for message type '" + type + "'");
            message_handlers_[type] = handler;
        }

        public ErrorCode last_error_code_
        {
            get
            {
                if (transport_ != null)
                    return transport_.LastErrorCode;

                return ErrorCode.kNone;
            }
        }

        public string last_error_message_
        {
            get
            {
                if (transport_ != null)
                    return transport_.LastErrorMessage;

                return "";
            }
        }
        #endregion

        #region internal implementation
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
            session_id_ = session_id;
            state_ = State.kEstablished;
            if (on_session_initiated_ != null)
            {
                on_session_initiated_(session_id_);
            }
        }

        private void CloseSession()
        {
            if (session_id_.Length == 0)
                return;

            session_id_ = "";
            state_ = State.kUnknown;
            if (session_reliability_)
            {
                seq_ = 0;
                seq_recvd_ = 0;
                first_receiving_ = true;
                send_queue_.Clear();
            }

            if (on_session_closed_ != null)
            {
                on_session_closed_();
            }
        }

        private void OnTransportReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            DebugUtils.Log("OnTransportReceived invoked.");
            last_received_ = DateTime.Now;

            lock (message_buffer_)
            {
                message_buffer_.Add(body);
            }
        }

        private string ProcessMessage (ArraySegment<byte> buffer)
        {
            string msg_type = "";
            string session_id = "";

            if (msg_type_ == FunMsgType.kJson)
            {
                string str = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                object json = transport_.JsonHelper.Deserialize(str);
                DebugUtils.Log("Parsed json: " + str);

                DebugUtils.Assert(transport_.JsonHelper.GetStringField(json, kSessionIdBodyField) is string);
                string session_id_node = transport_.JsonHelper.GetStringField(json, kSessionIdBodyField) as string;
                session_id = session_id_node;
                transport_.JsonHelper.RemoveStringField(json, kSessionIdBodyField);

                PrepareSession(session_id);

                if (session_reliability_)
                {
                    if (transport_.JsonHelper.HasField(json, kAckNumberField))
                    {
                        UInt32 ack = (UInt32)transport_.JsonHelper.GetIntegerField(json, kAckNumberField);
                        OnAckReceived(ack);
                        // Does not support piggybacking.
                        DebugUtils.Assert(!transport_.JsonHelper.HasField(json, kMsgTypeBodyField));
                        return msg_type;
                    }

                    if (transport_.JsonHelper.HasField(json, kSeqNumberField))
                    {
                        UInt32 seq = (UInt32)transport_.JsonHelper.GetIntegerField(json, kSeqNumberField);
                        if (!OnSeqReceived(seq))
                        {
                            return msg_type;
                        }
                        transport_.JsonHelper.RemoveStringField(json, kSeqNumberField);
                    }
                }

                DebugUtils.Assert(transport_.JsonHelper.GetStringField(json, kMsgTypeBodyField) is string);
                string msg_type_node = transport_.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                msg_type = msg_type_node;
                transport_.JsonHelper.RemoveStringField(json, kMsgTypeBodyField);

                if (message_handlers_.ContainsKey(msg_type))
                    message_handlers_[msg_type](msg_type, json);
            }
            else if (msg_type_ == FunMsgType.kProtobuf)
            {
                MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                FunMessage message = (FunMessage)serializer_.Deserialize (stream, null, recv_type_);

                session_id = message.sid;
                PrepareSession(session_id);

                if (session_reliability_)
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

                msg_type = message.msgtype;

                if (message_handlers_.ContainsKey(msg_type))
                    message_handlers_[msg_type](msg_type, message);
            }
            else
            {
                DebugUtils.LogWarning("Invalid message type. type: " + msg_type_);
                DebugUtils.Assert(false);
                return msg_type;
            }

            if (!message_handlers_.ContainsKey(msg_type))
            {
                DebugUtils.Log("No handler for message '" + msg_type + "'. Ignoring.");
            }

            return msg_type;
        }

        private bool SeqLess(UInt32 x, UInt32 y)
        {
            Int32 dist = (Int32)(x - y);
            return dist > 0;
        }

        private void SendAck(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            if (msg_type_ == FunMsgType.kJson)
            {
                object ack_msg = transport_.JsonHelper.Deserialize("{}");
                transport_.JsonHelper.SetStringField(ack_msg, kSessionIdBodyField, session_id_);
                transport_.JsonHelper.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport_.SendMessage(ack_msg, EncryptionType.kDefaultEncryption);
            }
            else
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.sid = session_id_;
                ack_msg.ack = ack;
                transport_.SendMessage(ack_msg, EncryptionType.kDefaultEncryption);
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
                    DebugUtils.Log("Received wrong sequence number " + seq.ToString() +
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
            DebugUtils.Assert (session_reliability_);

            while (send_queue_.Count > 0)
            {
                UInt32 seq;
                object last_msg = send_queue_.Peek();
                if (msg_type_ == FunMsgType.kJson)
                {
                    seq = (UInt32)transport_.JsonHelper.GetIntegerField(last_msg, kSeqNumberField);
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

            if (state_ == State.kWaitForAck)
            {
                foreach (object msg in send_queue_)
                {
                    if (msg_type_ == FunMsgType.kJson)
                    {
                        UInt32 seq = (UInt32)transport_.JsonHelper.GetIntegerField(msg, kSeqNumberField);
                        DebugUtils.Assert(seq == ack || SeqLess(seq, ack));
                        transport_.SendMessage(msg, EncryptionType.kDefaultEncryption);
                    }
                    else if (msg_type_ == FunMsgType.kProtobuf)
                    {
                        UInt32 seq = (msg as FunMessage).seq;
                        DebugUtils.Assert(seq == ack || SeqLess (seq, ack));
                        transport_.SendMessage(msg as FunMessage, EncryptionType.kDefaultEncryption);
                    }
                    else
                    {
                        DebugUtils.Assert(false);
                    }
                }

                state_ = State.kEstablished;
            }
        }

        private void OnConnectTimeout()
        {
            Stop();
        }

        private void OnTransportStarted()
        {
            if (!session_reliability_)
            {
                return;
            }

            if (state_ == State.kTransportClosed)
            {
                state_ = State.kWaitForAck;
                SendAck(seq_recvd_ + 1);
            }
        }

        private void OnTransportStopped()
        {
            if (session_reliability_)
            {
                if (state_ == State.kEstablished || state_ == State.kWaitForAck)
                {
                    state_ = State.kTransportClosed;
                }
            }
            DebugUtils.Log("Transport terminated. Stopping. You may restart again.");
        }

        #region Funapi system message handlers
        private void OnNewSession(string msg_type, object body)
        {
            // ignore.
        }

        private void OnSessionTimedout(string msg_type, object body)
        {
            DebugUtils.Log("Session timed out. Resetting my session id. The server will send me another one next time.");

            CloseSession();
        }

        private void OnMaintenanceMessage(string msg_type, object body)
        {
            MaintenanceCallback(body);
        }
        #endregion

        enum State
        {
            kUnknown = 0,
            kEstablished,
            kTransportClosed,
            kWaitForAck
        }

        class ExpectedReplyMessage
        {
            public ExpectedReplyMessage (float t, TimeoutHandler cb)
            {
                wait_time = t;
                callback = cb;
            }

            public float wait_time;
            public TimeoutHandler callback;
        }

        // Funapi message-related events.
        public event OnMessageHandler MaintenanceCallback;

        // Funapi message-related constants.
        private static readonly float kFunapiSessionTimeout = 3600.0f;
        private static readonly string kMsgTypeBodyField = "_msgtype";
        private static readonly string kSessionIdBodyField = "_sid";
        private static readonly string kSeqNumberField = "_seq";
        private static readonly string kAckNumberField = "_ack";
        private static readonly string kNewSessionMessageType = "_session_opened";
        private static readonly string kSessionClosedMessageType = "_session_closed";
        private static readonly string kMaintenanceMessageType = "_maintenance";

        // member variables.
        private State state_;
        private FunMessageSerializer serializer_;
        private Type recv_type_;
        private FunMsgType msg_type_;
        private bool started_ = false;
        private FunapiTransport transport_;
        private OnSessionInitiated on_session_initiated_;
        private OnSessionClosed on_session_closed_;
        private string session_id_ = "";
        private Dictionary<string, MessageHandler> message_handlers_ = new Dictionary<string, MessageHandler>();
        private Dictionary<string, ExpectedReplyMessage> expected_replies_ = new Dictionary<string, ExpectedReplyMessage>();
        private List<ArraySegment<byte>> message_buffer_ = new List<ArraySegment<byte>>();
        private DateTime last_received_ = DateTime.Now;

        // reliability-releated member variables.
        private bool session_reliability_;
        private UInt32 seq_;
        private UInt32 seq_recvd_;
        private bool first_receiving_;
        private System.Collections.Queue send_queue_;
        private System.Random rnd_;

        #endregion
    }
}  // namespace Fun
