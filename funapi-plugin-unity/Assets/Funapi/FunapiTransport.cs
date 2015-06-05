// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

#if !NO_UNITY
using UnityEngine;
#endif


namespace Fun
{
    // Funapi transport protocol
    public enum TransportProtocol
    {
        kDefault = 0,
        kTcp,
        kUdp,
        kHttp
    };


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

        public FunEncoding encoding
        {
            get { return encoding_; }
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
        internal virtual void Update() {}

        // Check unsent messages
        internal abstract bool HasUnsentMessages { get; }

        // Send a message
        internal abstract void SendMessage(FunapiMessage fun_msg);

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

        internal void OnFailureCallback ()
        {
            if (FailureCallback != null)
            {
                FailureCallback(protocol);
            }
        }

        internal void OnMessageFailureCallback (FunapiMessage fun_msg)
        {
            if (MessageFailureCallback != null)
            {
                MessageFailureCallback(protocol, fun_msg);
            }

            OnFailureCallback();
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
        public event TransportEventHandler ConnectTimeoutCallback;
        public event TransportEventHandler StartedCallback;
        public event TransportEventHandler StoppedCallback;
        public event TransportEventHandler FailureCallback;
        internal event TransportEventHandler StartedInternalCallback;
        internal event TransportReceivedHandler ReceivedCallback;
        internal event TransportMessageHandler MessageFailureCallback;

        // member variables.
        internal string host_addr_ = "";
        internal UInt16 host_port_ = 0;
        internal FunEncoding encoding_ = FunEncoding.kNone;
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
                AddToEventQueue(OnFailureAndStop);
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

        internal override void Update()
        {
            Queue<DelegateEventHandler> queue = null;

            lock (event_lock_)
            {
                if (event_queue_.Count > 0)
                {
                    queue = event_queue_;
                    event_queue_ = new Queue<DelegateEventHandler>();
                }
            }

            if (queue != null)
            {
                foreach (DelegateEventHandler func in queue)
                {
                    func();
                }
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
                event_queue_.Enqueue(handler);
            }
        }

        internal void AddFailureCallback (FunapiMessage fun_msg)
        {
            AddToEventQueue(
                delegate
                {
                    OnMessageFailureCallback(fun_msg);
                }
            );
        }

        internal void SetEncryption (EncryptionType encryption)
        {
            Encryptor encryptor = Encryptor.Create(encryption);
            if (encryptor == null)
            {
                last_error_code_ = ErrorCode.kInvalidEncryption;
                last_error_message_ = "Failed to create encryptor: " + encryption;
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
                return;
            }

            default_encryptor_ = (int)encryption;
            encryptors_[encryption] = encryptor;
        }

        internal override void SendMessage (FunapiMessage fun_msg)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                string str = this.JsonHelper.Serialize(fun_msg.message);
                byte[] body = Encoding.UTF8.GetBytes(str);

                DebugUtils.Log(String.Format("JSON to send : {0}", str));

                SendMessage(fun_msg, body);
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                MemoryStream stream = new MemoryStream();
                this.ProtobufHelper.Serialize (stream, fun_msg.message);

                byte[] body = new byte[stream.Length];
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(body, 0, body.Length);

                SendMessage(fun_msg, body);
            }
            else
            {
                Debug.Log("SendMessage - Invalid FunEncoding type: " + encoding_);
            }
        }

        private void SendMessage (FunapiMessage fun_msg, byte[] body)
        {
            try
            {
                lock (sending_lock_)
                {
                    fun_msg.buffer = new ArraySegment<byte>(body);
                    pending_.Add(fun_msg);

                    if (Started && sending_.Count == 0)
                    {
                        List<FunapiMessage> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                        EncryptThenSendMessage();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendMessage: " + e.ToString();
                Debug.Log(last_error_message_);
                AddFailureCallback(fun_msg);
            }
        }

        internal bool EncryptThenSendMessage()
        {
            DebugUtils.Assert((int)state >= (int)State.kConnected);
            DebugUtils.Assert(sending_.Count > 0);

            for (int i = 0; i < sending_.Count; i+=2)
            {
                FunapiMessage message = sending_[i];

                EncryptionType encryption = message.enc_type;
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
                        AddFailureCallback(message);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kEstablished)
                    {
                        last_error_code_ = ErrorCode.kInvalidEncryption;
                        last_error_message_ = String.Format("'{0}' is invalid encryption type. Check out the encryption type of server.", encryptor.name);
                        Debug.Log(last_error_message_);
                        AddFailureCallback(message);
                        return false;
                    }

                    Int64 nSize = encryptor.Encrypt(message.buffer, message.buffer, ref encryption_header);
                    if (nSize <= 0)
                    {
                        last_error_code_ = ErrorCode.kEncryptionFailed;
                        last_error_message_ = "Encrypt failure: " + encryptor.name;
                        Debug.Log(last_error_message_);
                        AddFailureCallback(message);
                        return false;
                    }

                    DebugUtils.Assert(nSize == message.buffer.Count);
                }

                StringBuilder header = new StringBuilder();
                header.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kProtocolVersion, kHeaderDelimeter);
                if (first_sending_)
                {
                    header.AppendFormat("{0}{1}{2}{3}", kPluginVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kPluginVersion, kHeaderDelimeter);
                    first_sending_ = false;
                }
                header.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter, message.buffer.Count, kHeaderDelimeter);
                if ((int)encryption != kNoneEncryption)
                {
                    DebugUtils.Assert(encryptor != null);
                    DebugUtils.Assert(encryptor.encryption == encryption);
                    header.AppendFormat("{0}{1}{2}", kEncryptionHeaderField, kHeaderFieldDelimeter, Convert.ToInt32(encryption));
                    header.AppendFormat("-{0}{1}", encryption_header, kHeaderDelimeter);
                }
                header.Append(kHeaderDelimeter);

                FunapiMessage header_buffer = new FunapiMessage(protocol, message.msg_type, header);
                header_buffer.buffer = new ArraySegment<byte>(Encoding.ASCII.GetBytes(header.ToString()));
                sending_.Insert(i, header_buffer);

                DebugUtils.Log(String.Format("Header to send: {0} body length: {1}", header, message.buffer.Count));
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
                    List<FunapiMessage> tmp = sending_;
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
                    Debug.Log(String.Format("Compacting a receive buffer to save {0} bytes.", next_decoding_offset_));
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0, received_size_ - next_decoding_offset_);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    Debug.Log(String.Format("Increasing a receive buffer to {0} bytes.", receive_buffer_.Length + kUnitBufferSize));
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
                    Debug.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }
                string line = Encoding.ASCII.GetString(receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    Debug.Log("End of header reached. Will decode body from now.");
                    return true;
                }

                DebugUtils.Log(String.Format("Header line: {0}", line));
                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                tuple[0] = tuple[0].ToUpper();
                DebugUtils.Log(String.Format("Decoded header field '{0}' => '{1}'", tuple[0], tuple[1]));
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
            DebugUtils.Log(String.Format("We need {0} bytes for a message body. Buffer has {1} bytes.",
                                         body_length, received_size_ - next_decoding_offset_));

            if (received_size_ - next_decoding_offset_ < body_length)
            {
                // Need more bytes.
                Debug.Log("We need more bytes for a message body. Waiting.");
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
                        List<FunapiMessage> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                        EncryptThenSendMessage();
                    }

                    OnStartedInternal();
                }
            }
        }

        internal virtual void OnFailure()
        {
            Debug.Log(String.Format("OnFailure({0}) - state: {1}", protocol, state));
            OnFailureCallback();
        }

        internal void OnFailureAndStop()
        {
            OnFailure();
            Stop();
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
        internal List<FunapiMessage> pending_ = new List<FunapiMessage>();
        internal List<FunapiMessage> sending_ = new List<FunapiMessage>();
        internal Dictionary<string, string> header_fields_ = new Dictionary<string, string>();
        internal int default_encryptor_ = kNoneEncryption;
        internal Dictionary<EncryptionType, Encryptor> encryptors_ = new Dictionary<EncryptionType, Encryptor>();
        internal Queue<DelegateEventHandler> event_queue_ = new Queue<DelegateEventHandler>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiEncryptedTransport
    {
        #region public interface
        public FunapiTcpTransport (string hostname_or_ip, UInt16 port, FunEncoding type)
        {
            protocol = TransportProtocol.kTcp;
            DisableNagle = false;
            encoding_ = type;

            SetAddress(hostname_or_ip, port);
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiTcpTransport(..., FunEncoding type)' instead.")]
        public FunapiTcpTransport (string hostname_or_ip, UInt16 port)
            : this(hostname_or_ip, port, Fun.FunEncoding.kNone)
        {
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
                    Debug.Log("Connection waiting time has been exceeded.");
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

        internal void SetAddress (string hostname_or_ip, UInt16 port)
        {
            if (host_addr_ == hostname_or_ip && host_port_ == port)
                return;

            host_addr_ = hostname_or_ip;
            host_port_ = port;

            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            connect_ep_ = new IPEndPoint(address, port);
        }

        internal void Redirect(string hostname_or_ip, UInt16 port)
        {
            if (host_addr_ == hostname_or_ip && host_port_ == port)
            {
                Debug.Log(String.Format("Redirect Tcp [{0}:{1}] - The same address is already connected.",
                                        hostname_or_ip, port));
                return;
            }

            if (Started)
            {
                Stop();
            }

            AddToEventQueue(
                delegate
                {
                    SetAddress(hostname_or_ip, port);
                    Start();
                }
            );
        }

        internal override void WireSend()
        {
            List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();
            lock (sending_lock_)
            {
                foreach (FunapiMessage message in sending_)
                {
                    list.Add(message.buffer);
                }
            }

            sock_.BeginSend(list, 0, new AsyncCallback(this.SendBytesCb), this);
        }

        private void StartCb(IAsyncResult ar)
        {
            DebugUtils.Log("StartCb called.");

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
                    AddToEventQueue(OnFailureAndStop);
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
                AddToEventQueue(OnFailureAndStop);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("SendBytesCb called.");

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
                DebugUtils.Log(String.Format("Sent {0}bytes", nSent));

                lock (sending_lock_)
                {
                    // Removes any segment fully sent.
                    while (nSent > 0)
                    {
                        DebugUtils.Assert(sending_.Count > 0);

                        if (sending_[0].buffer.Count > nSent)
                        {
                            // partial data
                            Debug.Log("Partially sent. Will resume.");
                            break;
                        }
                        else
                        {
                            // fully sent.
                            DebugUtils.Log("Discarding a fully sent message.");
                            nSent -= sending_[0].buffer.Count;
                            sending_.RemoveAt(0);
                        }
                    }

                    while (sending_.Count > 0 && sending_[0].buffer.Count <= 0)
                    {
                        Debug.Log("Remove zero byte buffer.");
                        sending_.RemoveAt(0);
                    }

                    // If the first segment has been sent partially, we need to reconstruct the first segment.
                    if (nSent > 0)
                    {
                        DebugUtils.Assert(sending_.Count > 0);
                        ArraySegment<byte> original = sending_[0].buffer;

                        DebugUtils.Assert(nSent <= sending_[0].buffer.Count);
                        ArraySegment<byte> adjusted = new ArraySegment<byte>(original.Array, original.Offset + nSent, original.Count - nSent);
                        sending_[0].buffer = adjusted;

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }

                    SendUnsentMessages();
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
                AddToEventQueue(OnFailure);
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("ReceiveBytesCb called.");

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
                        DebugUtils.Log(String.Format("Received {0} bytes. Buffer has {1} bytes.",
                                                     nRead, received_size_ - next_decoding_offset_));
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
                        DebugUtils.Log(String.Format("Ready to receive more. We can receive upto {0} more bytes",
                                                     receive_buffer_.Length - received_size_));

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        Debug.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            Debug.Log(String.Format("Buffer has {0} bytes. But they failed to decode. Discarding.",
                                                    receive_buffer_.Length - received_size_));
                        }

                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        Debug.Log(last_error_message_);
                        AddToEventQueue(OnFailure);
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
                AddToEventQueue(OnFailure);
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
        public FunapiUdpTransport(string hostname_or_ip, UInt16 port, FunEncoding type)
        {
            protocol = TransportProtocol.kUdp;
            encoding_ = type;

            SetAddress(hostname_or_ip, port);
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiUdpTransport(..., FunEncoding type)' instead.")]
        public FunapiUdpTransport (string hostname_or_ip, UInt16 port)
            : this(hostname_or_ip, port, Fun.FunEncoding.kNone)
        {
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

        internal void SetAddress (string hostname_or_ip, UInt16 port)
        {
            if (host_addr_ == hostname_or_ip && host_port_ == port)
                return;

            host_addr_ = hostname_or_ip;
            host_port_ = port;

            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            send_ep_ = new IPEndPoint(address, port);
            receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, port);
        }

        internal void Redirect(string hostname_or_ip, UInt16 port)
        {
            if (host_addr_ == hostname_or_ip && host_port_ == port)
            {
                Debug.Log(String.Format("Redirect Udp [{0}:{1}] - The same address is already connected.",
                                        hostname_or_ip, port));
                return;
            }

            if (Started)
            {
                Stop();
            }

            AddToEventQueue(
                delegate
                {
                    SetAddress(hostname_or_ip, port);
                    Start();
                }
            );
        }

        // Send a packet.
        internal override void WireSend()
        {
            int offset = 0;

            lock (sending_lock_)
            {
                DebugUtils.Assert(sending_.Count >= 2);

                int length = sending_[0].buffer.Count + sending_[1].buffer.Count;
                if (length > send_buffer_.Length)
                {
                    send_buffer_ = new byte[length];
                }

                // one header + one body
                for (int i = 0; i < 2; ++i)
                {
                    ArraySegment<byte> item = sending_[i].buffer;
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
                    DebugUtils.Log(String.Format("Sent {0}bytes", nSent));

                    DebugUtils.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    int nToSend = 0;
                    for (int i = 0; i < 2; ++i)
                    {
                        nToSend += sending_[0].buffer.Count;
                        sending_.RemoveAt(0);
                    }

                    if (nSent > 0 && nSent < nToSend)
                    {
                        Debug.Log("Failed to transfer udp messages.");
                        DebugUtils.Assert(false);
                    }

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    SendUnsentMessages();
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
                AddToEventQueue(OnFailure);
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("ReceiveBytesCb called.");

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
                        DebugUtils.Log(String.Format("Received {0} bytes. Buffer has {1} bytes.",
                                                     nRead, received_size_ - next_decoding_offset_));
                    }

                    // Decoding a message
                    if (TryToDecodeHeader())
                    {
                        if (TryToDecodeBody() == false)
                        {
                            Debug.LogWarning("Failed to decode body.");
                            DebugUtils.Assert(false);
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Failed to decode header.");
                        DebugUtils.Assert(false);
                    }

                    if (nRead > 0)
                    {
                        // Resets buffer
                        receive_buffer_ = new byte[kUnitBufferSize];
                        received_size_ = 0;
                        next_decoding_offset_ = 0;

                        // Starts another async receive
                        sock_.BeginReceiveFrom(receive_buffer_, received_size_, receive_buffer_.Length - received_size_,
                                               SocketFlags.None, ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);
                        DebugUtils.Log(String.Format("Ready to receive more. We can receive upto {0} more bytes", receive_buffer_.Length));

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        Debug.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            Debug.Log(String.Format("Buffer has {0} bytes. But they failed to decode. Discarding.",
                                                    receive_buffer_.Length - received_size_));
                        }

                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        Debug.Log(last_error_message_);

                        AddToEventQueue(OnFailure);
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
                AddToEventQueue(OnFailure);
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
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port, bool https, FunEncoding type)
        {
            protocol = TransportProtocol.kHttp;
            encoding_ = type;

            SetAddress(hostname_or_ip, port, https);
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiHttpTransport(..., FunEncoding type)' instead.")]
        public FunapiHttpTransport (string hostname_or_ip, UInt16 port, bool https = false)
            : this(hostname_or_ip, port, https, Fun.FunEncoding.kNone)
        {
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
                    OnFailure();
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

        internal void SetAddress (string hostname_or_ip, UInt16 port, bool https)
        {
            if (host_addr_ == hostname_or_ip && host_port_ == port)
                return;

            host_addr_ = hostname_or_ip;
            host_port_ = port;

            // Url
            host_url_ = String.Format("{0}://{1}:{2}/v{3}/",
                                      (https ? "https" : "http"), hostname_or_ip, port,
                                      FunapiVersion.kProtocolVersion);
        }

        internal void Redirect(string hostname_or_ip, UInt16 port, bool https = false)
        {
            if (host_addr_ == hostname_or_ip && host_port_ == port)
            {
                Debug.Log(String.Format("Redirect Http [{0}:{1}] - The same address is already connected.",
                                        hostname_or_ip, port));
                return;
            }

            if (Started)
            {
                Stop();
            }

            AddToEventQueue(
                delegate
                {
                    SetAddress(hostname_or_ip, port, https);
                    Start();
                }
            );
        }

        internal override void WireSend()
        {
            DebugUtils.Log("Send a Message.");

            try
            {
                lock (sending_lock_)
                {
                    DebugUtils.Assert(sending_.Count >= 2);
                    DebugUtils.Log(String.Format("Host Url: {0}", host_url_));

                    FunapiMessage header = sending_[0];
                    FunapiMessage body = sending_[1];

                    // Request
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.ContentLength = body.buffer.Count;

                    // encryption type
                    string str_header = Encoding.ASCII.GetString(header.buffer.Array, header.buffer.Offset, header.buffer.Count);
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
                    ws.msg_type = body.msg_type;
                    ws.sending = body.buffer;
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
                AddToEventQueue(OnFailure);
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
                DebugUtils.Log(String.Format("Sent {0}bytes.", ws.sending.Count));

                request.BeginGetResponse(new AsyncCallback(ResponseCb), ws);
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in RequestStreamCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
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
                    Debug.Log("Failed response. status:" + response.StatusDescription);
                    DebugUtils.Assert(false);
                    AddToEventQueue(OnFailure);
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ResponseCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
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
                        Debug.LogWarning("Response instance is null.");
                        DebugUtils.Assert(false);
                        AddToEventQueue(OnFailure);
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
                                Debug.LogWarning("Failed to decode body.");
                                DebugUtils.Assert(false);
                            }
                        }
                        else
                        {
                            Debug.LogWarning("Failed to decode header.");
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
                AddToEventQueue(OnFailure);
            }
        }

        internal override void OnFailure ()
        {
            Debug.Log(String.Format("OnFailure({0}) - state: {1}", protocol, state));
            if (state == State.kUnknown || cur_request_ == null)
            {
                OnFailureCallback();
                Stop();
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

                OnMessageFailureCallback(sending_[1]);

                // Removes header and body segment
                sending_.RemoveAt(0);
                sending_.RemoveAt(0);

                SendUnsentMessages();
            }
        }
        #endregion


        // Funapi header-related constants.
        private static readonly string kLengthHttpHeaderField = "content-length";
        private static readonly string kEncryptionHttpHeaderField = "X-iFun-Enc";

        // waiting time for response
        private static readonly float kResponseTimeout = 30f;    // seconds

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
            public string msg_type;
            public ArraySegment<byte> sending;
        }

        // member variables.
        private string host_url_;
        private float response_time_ = -1f;
        private WebState cur_request_ = null;
        private List<WebState> list_ = new List<WebState>();
    }


    // Event handler delegate
    public delegate void TransportEventHandler(TransportProtocol protocol);
    public delegate void TimeoutEventHandler(string msg_type);
    internal delegate void TransportMessageHandler(TransportProtocol protocol, FunapiMessage fun_msg);
    internal delegate void TransportReceivedHandler(TransportProtocol protocol,
                                                    Dictionary<string, string> header, ArraySegment<byte> body);

}  // namespace Fun
