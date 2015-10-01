// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
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

    // Message encoding type
    public enum FunEncoding
    {
        kNone,
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
        kRequestTimeout,
        kDisconnected,
        kExceptionError
    }


    // Abstract class to represent Transport used by Funapi
    // TCP, UDP, and HTTP.
    public abstract class FunapiTransport
    {
        public FunapiTransport()
        {
            state_ = State.kUnknown;
            protocol_ = TransportProtocol.kDefault;
            ConnectTimeout = 10f;
        }

        // Start connecting
        internal abstract void Start();

        // Disconnection
        internal abstract void Stop();

        // Check connection
        internal abstract bool Started { get; }

        internal float PingWaitTime { get; set; }

        // Check unsent messages
        internal abstract bool HasUnsentMessages { get; }

        // Send a message
        internal abstract void SendMessage(FunapiMessage fun_msg);

        internal State state
        {
            get { return state_; }
            set { state_ = value; }
        }

        public TransportProtocol Protocol
        {
            get { return protocol_; }
        }

        public string str_protocol
        {
            get; set;
        }

        public FunEncoding Encoding
        {
            get { return encoding_; }
            set { encoding_ = value; }
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

        public bool AutoReconnect
        {
            get; set;
        }

        public bool EnablePing
        {
            get; set;
        }

        public int PingTime
        {
            get; internal set;
        }

        public bool IsConnecting
        {
            get { return cstate_ == ConnectState.kConnecting ||
                         cstate_ == ConnectState.kReconnecting ||
                         cstate_ == ConnectState.kRedirecting; }
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

        public void AddServerList (List<HostAddr> list)
        {
            if (list == null || list.Count <= 0)
                return;

            extra_list_.Add(list);
        }

        // Sets address
        public void ResetAddress (HostAddr addr)
        {
            ip_list_.Clear();

            if (addr is HostHttp)
                ip_list_.Add(addr.host, addr.port, ((HostHttp)addr).https);
            else
                ip_list_.Add(addr.host, addr.port);

            SetNextAddress();
        }

        internal bool SetNextAddress ()
        {
            HostAddr addr = ip_list_.GetNextAddress();
            if (addr != null)
            {
                SetAddress(addr);
            }
            else
            {
                HostAddr extra = extra_list_.GetNextAddress();
                if (extra == null) {
                    Debug.Log("SetAvailableAddress - There's no available address.");
                    return false;
                }

                SetAddress(extra);
            }

            return true;
        }

        internal abstract void SetAddress (HostAddr addr);

        // Connect functions
        internal void Connect ()
        {
            Debug.Log(string.Format("'{0}' Try to connect to server.", str_protocol));
            exponential_time_ = 1f;
            reconnect_count_ = 0;

            Start();
        }

        internal void Connect (HostAddr addr)
        {
            SetAddress(addr);
            Connect();
        }

        internal void Reconnect ()
        {
            Debug.Log(string.Format("'{0}' Try to reconnect to server.", str_protocol));
            cstate_ = ConnectState.kReconnecting;
            Connect();
        }

        internal void Redirect (HostAddr addr)
        {
            Debug.Log(String.Format("Redirect {0} [{1}:{2}]", str_protocol, addr.host, addr.port));

            if (Started) {
                Stop();
            }

            AddToEventQueue(
                delegate {
                    cstate_ = ConnectState.kRedirecting;
                    Connect(addr);
                }
            );
        }

        // Checks connection list
        internal void CheckConnectList ()
        {
            if (IsConnecting)
                return;

            cstate_ = ConnectState.kConnecting;
            exponential_time_ = 1f;
        }

        internal void CheckReconnect ()
        {
            if (!AutoReconnect || cstate_ == ConnectState.kReconnecting)
                return;

            cstate_ = ConnectState.kReconnecting;
            exponential_time_ = 1f;
            reconnect_count_ = 0;
        }

        private bool TryToConnect ()
        {
            if (reconnect_count_ >= 0)
            {
                if (TryToReconnect())
                    return true;
            }

            float delay_time = 0f;
            if (ip_list_.IsNextAvailable)
            {
                if (exponential_time_ < kMaxConnectingTime)
                {
                    delay_time = exponential_time_;
                    exponential_time_ *= 2f;
                }
                else
                {
                    ip_list_.SetLast();
                }
            }

            if (delay_time <= 0f)
            {
                if (extra_list_.IsNextAvailable)
                    delay_time = kFixedConnectWaitTime;

                if (delay_time <= 0f)
                    return false;
            }

            Debug.Log(string.Format("Wait {0} seconds for connect to {1} transport.",
                                    delay_time, str_protocol));

            timer_.AddTimer(kTryConnectTimerId, delay_time,
                delegate (object param) {
                    Debug.Log(string.Format("'{0}' Try to connect to server.", str_protocol));
                    SetNextAddress();
                    Connect();
                }
            );

            return true;
        }

        private bool TryToReconnect ()
        {
            ++reconnect_count_;
            if (reconnect_count_ > kMaxReconnectCount)
                return false;

            float delay_time = 0f;
            if (exponential_time_ < kMaxConnectingTime)
            {
                delay_time = exponential_time_;
                exponential_time_ *= 2f;
            }

            if (delay_time <= 0f)
                return false;

            Debug.Log(string.Format("Wait {0} seconds for reconnect to {1} transport.",
                                    delay_time, str_protocol));

            timer_.AddTimer(kTryReconnectTimerId, delay_time,
                delegate (object param) {
                    Debug.Log(string.Format("'{0}' Try to reconnect to server.", str_protocol));
                    Start();
                }
            );

            return true;
        }

        // Update
        internal virtual void Update()
        {
            // Timer
            timer_.Update();

            // Events
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
                delegate {
                    OnMessageFailureCallback(fun_msg);
                    OnFailureCallback();
                }
            );
        }

        public ErrorCode LastErrorCode
        {
            get { return last_error_code_; }
        }

        public string LastErrorMessage
        {
            get { return last_error_message_; }
        }

        //---------------------------------------------------------------------
        // Callback-related functions
        //---------------------------------------------------------------------
        internal void OnConnectionTimeout ()
        {
            CheckConnectList();

            if (ConnectTimeoutCallback != null)
            {
                ConnectTimeoutCallback(protocol_);
            }
        }

        internal void OnReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            if (ReceivedCallback != null)
            {
                ReceivedCallback(protocol_, header, body);
            }
        }

        internal void OnStarted ()
        {
            state_ = State.kEstablished;

            if (StartedCallback != null)
            {
                StartedCallback(protocol_);
            }
        }

        internal void OnStartedInternal ()
        {
            if (StartedInternalCallback != null)
            {
                StartedInternalCallback(protocol_);
            }
        }

        internal void OnStopped ()
        {
            if (StoppedCallback != null)
            {
                StoppedCallback(protocol_);
            }

            if (cstate_ == ConnectState.kConnecting)
            {
                if (!TryToConnect())
                {
                    cstate_ = ConnectState.kUnknown;
                    OnConnectFailureCallback();
                }
            }
            else if (cstate_ == ConnectState.kReconnecting)
            {
                if (!TryToReconnect())
                {
                    cstate_ = ConnectState.kUnknown;
                    OnDisconnectedCallback();
                }
            }
            else if (cstate_ == ConnectState.kRedirecting)
            {
                if (!TryToReconnect())
                {
                    cstate_ = ConnectState.kUnknown;
                    OnConnectFailureCallback();
                }
            }

            if (cstate_ == ConnectState.kUnknown)
            {
                exponential_time_ = 1f;
                reconnect_count_ = 0;
            }
        }

        internal void OnDisconnected ()
        {
            CheckReconnect();

            Stop();

            if (cstate_ != ConnectState.kReconnecting)
            {
                OnDisconnectedCallback();
            }
        }

        internal void OnConnectFailureCallback ()
        {
            ip_list_.SetFirst();
            extra_list_.SetFirst();

            if (ConnectFailureCallback != null)
            {
                ConnectFailureCallback(protocol_);
            }
        }

        internal void OnDisconnectedCallback ()
        {
            if (DisconnectedCallback != null)
            {
                DisconnectedCallback(protocol_);
            }
        }

        internal void OnFailureCallback ()
        {
            if (FailureCallback != null)
            {
                FailureCallback(protocol_);
            }
        }

        internal void OnMessageFailureCallback (FunapiMessage fun_msg)
        {
            if (MessageFailureCallback != null)
            {
                MessageFailureCallback(protocol_, fun_msg);
            }
        }


        internal enum State
        {
            kUnknown = 0,
            kConnecting,
            kConnected,
            kWaitForSession,
            kWaitForAck,
            kEstablished
        };

        internal enum ConnectState
        {
            kUnknown = 0,
            kConnecting,
            kReconnecting,
            kRedirecting,
            kConnected
        };


        internal delegate void DelegateEventHandler();

        // constants.
        private static readonly string kTryConnectTimerId = "try_connect";
        private static readonly string kTryReconnectTimerId = "try_reconnect";

        private static readonly int kMaxReconnectCount = 3;
        private static readonly float kMaxConnectingTime = 120f;
        private static readonly float kFixedConnectWaitTime = 10f;

        // Event handlers
        public event TransportEventHandler ConnectTimeoutCallback;
        public event TransportEventHandler StartedCallback;
        public event TransportEventHandler StoppedCallback;
        public event TransportEventHandler FailureCallback;

        internal event TransportEventHandler StartedInternalCallback;
        internal event TransportEventHandler DisconnectedCallback;
        internal event TransportReceivedHandler ReceivedCallback;
        internal event TransportMessageHandler MessageFailureCallback;
        internal event TransportEventHandler ConnectFailureCallback;

        // Connect-releated member variables.
        internal ConnectState cstate_ = ConnectState.kUnknown;
        internal ConnectList ip_list_ = new ConnectList();
        internal ConnectList extra_list_ = new ConnectList();
        internal float exponential_time_ = 0f;
        internal int reconnect_count_ = 0;

        // Encoding-serializer-releated member variables.
        internal FunEncoding encoding_ = FunEncoding.kNone;
        internal JsonAccessor json_accessor_ = new DictionaryJsonAccessor();
        internal FunMessageSerializer serializer_ = null;

        // Error-releated member variables.
        internal ErrorCode last_error_code_ = ErrorCode.kNone;
        internal string last_error_message_ = "";

        // member variables.
        internal State state_;
        internal TransportProtocol protocol_;
        internal FunapiTimer timer_ = new FunapiTimer();
        internal object event_lock_ = new object();
        internal Queue<DelegateEventHandler> event_queue_ = new Queue<DelegateEventHandler>();
    }


    // Transport class for socket
    public abstract class FunapiDecodedTransport : FunapiTransport
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
                first_receiving_ = true;
                header_decoded_ = false;
                received_size_ = 0;
                next_decoding_offset_ = 0;
                header_fields_.Clear();
                sending_.Clear();
                last_error_code_ = ErrorCode.kNone;
                last_error_message_ = "";

                if (ConnectTimeout > 0f)
                {
                    timer_.KillTimer(kConnectTimeoutTimerId);
                    timer_.AddTimer(kConnectTimeoutTimerId, ConnectTimeout,
                        delegate (object param) {
                            if (state_ == State.kEstablished)
                                return;

                            Debug.Log(string.Format("{0} Connection waiting time has been exceeded.", str_protocol));
                            OnConnectionTimeout();
                        }
                    );
                }

                Init();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kConnectFailed;
                last_error_message_ = "Failure in Start: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
            }
        }

        // Stops a socket.
        internal override void Stop()
        {
            if (state_ == State.kUnknown)
                return;

            state_ = State.kUnknown;
            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";
            timer_.Clear();

            AddToEventQueue(OnStopped);
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

        internal virtual bool IsSendable
        {
            get { return sending_.Count == 0; }
        }

        internal override void SendMessage (FunapiMessage fun_msg)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                string str = this.JsonHelper.Serialize(fun_msg.message);
                byte[] body = System.Text.Encoding.UTF8.GetBytes(str);

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

        private void SendMessage (FunapiMessage msg_body, byte[] buffer)
        {
            try
            {
                lock (sending_lock_)
                {
                    StringBuilder header = new StringBuilder();
                    header.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kProtocolVersion, kHeaderDelimeter);
                	if (first_sending_)
                    {
                        header.AppendFormat("{0}{1}{2}{3}", kPluginVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kPluginVersion, kHeaderDelimeter);
                    	first_sending_ = false;
                    }
                    header.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter, buffer.Length, kHeaderDelimeter);
                    header.Append(kHeaderDelimeter);

                    FunapiMessage msg_header = new FunapiMessage(msg_body.protocol, msg_body.msg_type, header);
                    msg_header.buffer = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(header.ToString()));
                    msg_body.buffer = new ArraySegment<byte>(buffer);

                    pending_.Add(msg_header);
                    pending_.Add(msg_body);

                    if (Started && IsSendable)
                    {
                        List<FunapiMessage> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                    	WireSend();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in SendMessage: " + e.ToString();
                Debug.Log(last_error_message_);
                AddFailureCallback(msg_body);
            }
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
                else if (IsSendable && pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<FunapiMessage> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;

                    WireSend();
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
                    // calc copy_length first to make sure
                    // src range[next_decoding_offset_ .. next_decoding_offset_ + copy_length)
                    // fit in src buffer boundary
                    int copy_length = Math.Min (receive_buffer_.Length, received_size_) - next_decoding_offset_;
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0, copy_length);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    Debug.Log(String.Format("Increasing a receive buffer to {0} bytes.", receive_buffer_.Length + kUnitBufferSize));
                    Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, receive_buffer_.Length);
                    receive_buffer_ = new_buffer;
                }
            }
        }

        // Decoding a messages
        internal void TryToDecodeMessage ()
        {
            if (IsStream)
            {
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
            }
            else
            {
                // Try to decode a message.
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

                string line = System.Text.Encoding.ASCII.GetString(receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    DebugUtils.Log("End of header reached. Will decode body from now.");
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

            if (body_length > 0)
            {
                if ((int)state_ < (int)State.kConnected)
                {
                    Debug.Log("Unexpected message. state:" + state_);
                    return false;
                }

                if (first_receiving_)
                {
                    first_receiving_ = false;
                    cstate_ = ConnectState.kConnected;
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

        internal virtual void OnFailure()
        {
            Debug.Log(String.Format("OnFailure({0}) - state: {1}\n{2}:{3}",
                                    str_protocol, state_, last_error_code_, last_error_message_));

            OnFailureCallback();

            if (state_ != State.kEstablished)
            {
                CheckConnectList();

                Stop();
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


        // Timer-related constants
        private static readonly string kConnectTimeoutTimerId = "connect_timeout";

        // Buffer-related constants.
        internal static readonly int kUnitBufferSize = 65536;

        // Funapi header-related constants.
        internal static readonly string kHeaderDelimeter = "\n";
        internal static readonly string kHeaderFieldDelimeter = ":";
        internal static readonly string kVersionHeaderField = "VER";
        internal static readonly string kPluginVersionHeaderField = "PVER";
        internal static readonly string kLengthHeaderField = "LEN";

        // for speed-up.
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // Message-related.
        private bool first_sending_ = true;
        private bool first_receiving_ = true;
        internal bool header_decoded_ = false;
        internal int received_size_ = 0;
        internal int next_decoding_offset_ = 0;
        internal object sending_lock_ = new object();
        internal object receive_lock_ = new object();
        internal byte[] receive_buffer_ = new byte[kUnitBufferSize];
        internal byte[] send_buffer_ = new byte[kUnitBufferSize];
        internal List<FunapiMessage> pending_ = new List<FunapiMessage>();
        internal List<FunapiMessage> sending_ = new List<FunapiMessage>();
        internal Dictionary<string, string> header_fields_ = new Dictionary<string, string>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiDecodedTransport
    {
        public FunapiTcpTransport (string hostname_or_ip, UInt16 port, FunEncoding type)
        {
            protocol_ = TransportProtocol.kTcp;
            str_protocol = "Tcp";
            DisableNagle = false;
            encoding_ = type;

            ip_list_.Add(hostname_or_ip, port);
            SetNextAddress();
        }

        // Stops a socket.
        internal override void Stop()
        {
            if (state_ == State.kUnknown)
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
                return sock_ != null && sock_.Connected && (int)state_ >= (int)State.kConnected;
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

        // Create a socket.
        internal override void Init()
        {
            state_ = State.kConnecting;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            if (DisableNagle)
                sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

            sock_.BeginConnect(connect_ep_, new AsyncCallback(this.StartCb), this);
        }

        internal override void SetAddress (HostAddr addr)
        {
            IPAddress ip = null;
            if (addr is HostIP)
            {
                ip = ((HostIP)addr).ip;
            }
            else
            {
                IPHostEntry host_info = Dns.GetHostEntry(addr.host);
                DebugUtils.Assert(host_info.AddressList.Length > 0);
                ip = host_info.AddressList[0];
            }

            connect_ep_ = new IPEndPoint(ip, addr.port);
            Debug.Log(String.Format("SetAddress - {0}:{1}", ip, addr.port));
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    Debug.Log(last_error_message_);
                    AddToEventQueue(OnFailure);
                    return;
                }
                Debug.Log("Connected.");

                state_ = State.kConnected;

                OnStartedInternal();

                lock (receive_lock_)
                {
                    ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                    buffer.Add(wrapped);
                    sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginConnect operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kConnectFailed;
                last_error_message_ = "Failure in StartCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                int nSent = sock_.EndSend(ar);
                DebugUtils.Log(String.Format("Sent {0}bytes", nSent));
                DebugUtils.Assert(nSent > 0, "Failed to transfer tcp messages.");

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
                            DebugUtils.Log(string.Format("Discarding a fully sent message. ({0}bytes)",
                                                         sending_[0].buffer.Count));

                            // fully sent.
                            nSent -= sending_[0].buffer.Count;
                            sending_.RemoveAt(0);
                        }
                    }

                    while (sending_.Count > 0 && sending_[0].buffer.Count <= 0)
                    {
                        DebugUtils.Log("Remove empty buffer.");
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
                    }

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    SendUnsentMessages();
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginSend operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
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
                    DebugUtils.Log(last_error_message_);
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

                    // Decoding a messages
                    TryToDecodeMessage();

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

                        last_error_code_ = ErrorCode.kDisconnected;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        Debug.Log(last_error_message_);
                        AddToEventQueue(OnDisconnected);
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginReceive operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (NullReferenceException e)
            {
                // When Stop is called Socket.EndReceive may return a NullReferenceException
                Debug.Log("BeginReceive operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
            }
        }

        internal Socket sock_;
        private IPEndPoint connect_ep_;
    }


    // UDP transport layer
    public class FunapiUdpTransport : FunapiDecodedTransport
    {
        public FunapiUdpTransport (string hostname_or_ip, UInt16 port, FunEncoding type)
        {
            protocol_ = TransportProtocol.kUdp;
            str_protocol = "Udp";
            encoding_ = type;

            ip_list_.Add(hostname_or_ip, port);
            SetNextAddress();
        }

        // Stops a socket.
        internal override void Stop()
        {
            if (state_ == State.kUnknown)
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
            get { return sock_ != null && (int)state_ >= (int)State.kConnected; }
        }

        public override bool IsDatagram
        {
            get { return true; }
        }

        // Create a socket.
        internal override void Init()
        {
            state_ = State.kConnected;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock_.BeginReceiveFrom(receive_buffer_, 0, receive_buffer_.Length, SocketFlags.None,
                                   ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

            OnStartedInternal();
        }

        internal override void SetAddress (HostAddr addr)
        {
            IPAddress ip = null;
            if (addr is HostIP)
            {
                ip = ((HostIP)addr).ip;
            }
            else
            {
                IPHostEntry host_info = Dns.GetHostEntry(addr.host);
                DebugUtils.Assert(host_info.AddressList.Length > 0);
                ip = host_info.AddressList[0];
            }

            send_ep_ = new IPEndPoint(ip, addr.port);
            receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, addr.port);
            Debug.Log(String.Format("SetAddress - {0}:{1}", ip, addr.port));
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
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (sending_lock_)
                {
                    int nSent = sock_.EndSend(ar);
                    DebugUtils.Log(String.Format("Sent {0}bytes", nSent));
                    DebugUtils.Assert(nSent > 0, "Failed to transfer udp messages.");

                    DebugUtils.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    int nToSend = 0;
                    for (int i = 0; i < 2; ++i)
                    {
                        nToSend += sending_[0].buffer.Count;
                        sending_.RemoveAt(0);
                    }

                    DebugUtils.Assert(nSent == nToSend,
                        string.Format("Failed to sending whole messages. {0}:{1}", nToSend, nSent));

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    SendUnsentMessages();
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginSendTo operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
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
                    DebugUtils.Log(last_error_message_);
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
                    TryToDecodeMessage();

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

                        last_error_code_ = ErrorCode.kDisconnected;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        Debug.Log(last_error_message_);
                        AddToEventQueue(OnFailure);
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginReceiveFrom operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
            }
        }


        internal Socket sock_;
        private IPEndPoint send_ep_;
        private EndPoint receive_ep_;
    }


    // HTTP transport layer
	public class FunapiHttpTransport : FunapiDecodedTransport
    {
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port, bool https, FunEncoding type)
        {
            protocol_ = TransportProtocol.kHttp;
            str_protocol = "Http";
            encoding_ = type;

            ip_list_.Add(hostname_or_ip, port, https);
            SetNextAddress();
        }

        internal override void Stop()
        {
            if (state_ == State.kUnknown)
                return;

#if !NO_UNITY
            if (cur_www_ != null)
                cancel_www_ = true;
#endif

            ClearRequest();

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
            get { return (int)state_ >= (int)State.kConnected; }
        }

        public override bool IsRequestResponse
        {
            get { return true; }
        }

#if !NO_UNITY
        public bool UseWWW
        {
            set { using_www_ = value; }
        }
#endif

        internal override void Init()
        {
            state_ = State.kConnected;
            str_cookie_ = "";

            OnStartedInternal();
        }

        internal override void SetAddress (HostAddr addr)
        {
            DebugUtils.Assert(addr is HostHttp);
            HostHttp http = (HostHttp)addr;

            // Url
            host_url_ = String.Format("{0}://{1}:{2}/v{3}/",
                                      (http.https ? "https" : "http"), http.host, http.port,
                                      FunapiVersion.kProtocolVersion);

            Debug.Log(String.Format("SetAddress - {0}:{1}", http.host, http.port));
        }

        internal override bool IsSendable
        {
            get
            {
#if !NO_UNITY
                if (cur_www_ != null)
                    return false;
#endif

                if (cur_request_ != null)
                    return false;

                return true;
            }
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

                    // Header
                    Dictionary<string, string> headers = new Dictionary<string, string>();
                    string str_header = ((StringBuilder)header.message).ToString();
                    string[] list = str_header.Split(kHeaderSeparator, StringSplitOptions.None);

                    for (int i = 0; i < list.Length; i += 2)
                    {
                        if (list[i].Length <= 0)
                            break;

                        headers.Add(list[i], list[i+1]);
                    }

                    if (str_cookie_.Length > 0)
                        headers.Add(kCookieHeaderField, str_cookie_);

                    // Sets timeout timer
                    timer_.KillTimer(kTimeoutTimerId);
                    timer_.AddTimer(kTimeoutTimerId, kTimeoutSeconds, OnRequestTimeout, body.msg_type);

                    // Sending a message
#if !NO_UNITY
                    if (using_www_)
                    {
                        SendWWWRequest(headers, body);
                    }
                    else
#endif
                    {
                        SendHttpWebRequest(headers, body);
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in WireSend: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
            }
        }

#if !NO_UNITY
        private void SendWWWRequest (Dictionary<string, string> headers, FunapiMessage body)
        {
            cancel_www_ = false;

            if (body.buffer.Count > 0)
            {
                FunapiManager.instance.StartCoroutine(
                    WWWPost(new WWW(host_url_, body.buffer.Array, headers)));
            }
            else
            {
                FunapiManager.instance.StartCoroutine(
                    WWWPost(new WWW(host_url_, null, headers)));
            }
        }
#endif

        private void SendHttpWebRequest (Dictionary<string, string> headers, FunapiMessage body)
        {
            // Request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = body.buffer.Count;

            foreach (KeyValuePair<string, string> item in headers) {
                request.Headers[item.Key] = item.Value;
            }

            // Response
            WebState ws = new WebState();
            ws.request = request;
            ws.msg_type = body.msg_type;
            ws.sending = body.buffer;
            list_.Add(ws);

            cur_request_ = ws;
            request.BeginGetRequestStream(new AsyncCallback(RequestStreamCb), ws);
        }

        private void DecodeMessage (string header, ArraySegment<byte> body)
        {
            StringBuilder headers = new StringBuilder();
            headers.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kProtocolVersion, kHeaderDelimeter);

            string[] lines = header.Replace("\r", "").Split('\n');
            foreach (string n in lines)
            {
                if (n.Length > 0)
                {
                    string[] tuple = n.Split(kHeaderSeparator, StringSplitOptions.RemoveEmptyEntries);
                    string item = tuple[0].ToLower();

                    switch (item)
                    {
                    case "content-type":
                        break;
                    case "set-cookie":
                        str_cookie_ = tuple[1];
                        DebugUtils.Log("Set Cookie : " + str_cookie_);
                        break;
                    case "content-length":
                        headers.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter, tuple[1], kHeaderDelimeter);
                        break;
                    default:
                        headers.AppendFormat("{0}{1}{2}{3}", tuple[0], kHeaderFieldDelimeter, tuple[1], kHeaderDelimeter);
                        break;
                    }
                }
                else {
                    break;
                }
            }
            headers.Append(kHeaderDelimeter);

            byte[] header_bytes = System.Text.Encoding.ASCII.GetBytes(headers.ToString());

            // Checks buffer space
            int total_size = header_bytes.Length + body.Count;
            received_size_ += total_size;
            CheckReceiveBuffer();

            // Copy to buffer
            // NOTE: offset should be calculated after CheckReceiveBuffer()
            //       (CheckReceiveBuffer() may change received_size_)
            int offset = received_size_ - total_size;
            Buffer.BlockCopy(header_bytes, 0, receive_buffer_, offset, header_bytes.Length);
            Buffer.BlockCopy(body.Array, 0, receive_buffer_, offset + header_bytes.Length, body.Count);

            // Decoding a message
            TryToDecodeMessage();
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

                lock (sending_lock_)
                {
                    DebugUtils.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    sending_.RemoveAt(0);
                    sending_.RemoveAt(0);
                }

                request.BeginGetResponse(new AsyncCallback(ResponseCb), ws);
            }
            catch (WebException e)
            {
                // When Stop is called HttpWebRequest.EndGetRequestStream may return a Exception
                Debug.Log("Http request operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
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
                    AddToEventQueue(OnFailure);
                }
            }
            catch (WebException e)
            {
                // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                Debug.Log("Http request operation has been Cancelled.");
                DebugUtils.Log(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
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
                        AddToEventQueue(OnFailure);
                        return;
                    }

                    lock (receive_lock_)
                    {
                        // Decodes message
                        byte[] header = ws.response.Headers.ToByteArray();
                        string str_header = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
                        DecodeMessage(str_header, new ArraySegment<byte>(ws.read_data, 0, ws.read_offset));

                        ws.stream.Close();
                        ws.stream = null;
                        list_.Remove(ws);

                        ClearRequest();

                        // Sends unsent messages
                        SendUnsentMessages();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in ReadCb: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
            }
        }

#if !NO_UNITY
        private IEnumerator WWWPost (WWW www)
        {
            cur_www_ = www;

            while (!www.isDone && !cancel_www_)
            {
                yield return null;
            }

            if (cancel_www_)
            {
                cur_www_ = null;
                yield break;
            }

            try
            {
                lock (sending_lock_)
                {
                    DebugUtils.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    sending_.RemoveAt(0);
                    sending_.RemoveAt(0);
                }

                if (www.error != null && www.error.Length > 0)
                {
                    throw new Exception(www.error);
                }

                // Decodes message
                StringBuilder headers = new StringBuilder();
                foreach (KeyValuePair<string, string> item in www.responseHeaders)
                {
                    headers.AppendFormat("{0}{1}{2}{3}",
                        item.Key, kHeaderFieldDelimeter, item.Value, kHeaderDelimeter);
                }
                headers.Append(kHeaderDelimeter);

                DecodeMessage(headers.ToString(), new ArraySegment<byte>(www.bytes));

                ClearRequest();

                // Sends unsent messages
                SendUnsentMessages();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in WWWPost: " + e.ToString();
                Debug.Log(last_error_message_);
                AddToEventQueue(OnFailure);
            }
        }
#endif

        private void CancelRequest ()
        {
#if !NO_UNITY
            if (cur_www_ != null)
                cancel_www_ = true;
#endif
            if (cur_request_ != null)
            {
                WebState ws = cur_request_;

                if (ws.request != null)
                {
                    ws.aborted = true;
                    ws.request.Abort();
                }

                if (ws.stream != null)
                    ws.stream.Close();

                if (list_.Contains(ws))
                    list_.Remove(ws);
            }

            ClearRequest();
        }

        private void ClearRequest ()
        {
#if !NO_UNITY
            cur_www_ = null;
#endif
            cur_request_ = null;
            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";

            timer_.KillTimer(kTimeoutTimerId);
        }

        private void OnRequestTimeout (object param)
        {
            last_error_code_ = ErrorCode.kRequestTimeout;
            last_error_message_ = string.Format("Http Request timeout - msg_type:{0}", (string)param);
            Debug.Log(last_error_message_);
            OnFailure();
        }

        internal override void OnFailure ()
        {
            CancelRequest();
            base.OnFailure();
        }


        // Funapi header-related constants.
        private static readonly string[] kHeaderSeparator = { kHeaderFieldDelimeter, kHeaderDelimeter };
        private static readonly string kCookieHeaderField = "Cookie";

        // waiting time for response
        private static readonly string kTimeoutTimerId = "http_request_timeout";
        private static readonly float kTimeoutSeconds = 30f;

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
        private string str_cookie_;

        // WWW-related member variables.
#if !NO_UNITY
        private bool using_www_ = false;
        private bool cancel_www_ = false;
        private WWW cur_www_ = null;
#endif

        // WebRequest-related member variables.
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
