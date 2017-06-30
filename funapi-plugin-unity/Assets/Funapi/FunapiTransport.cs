// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
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

// protobuf
using ProtoBuf;
using funapi.network.fun_message;
using funapi.network.ping_message;


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
    public abstract class FunapiTransport : FunapiEncryptor
    {
        public FunapiTransport ()
        {
            state_ = State.kUnknown;
            protocol_ = TransportProtocol.kDefault;
            PingIntervalSeconds = kPingIntervalSecond;
            PingTimeoutSeconds = kPingTimeoutSeconds;
            ConnectTimeout = 10f;

            setDebugObject(this);
        }

        // Start connecting
        public abstract void Start ();

        // Disconnection
        public abstract void Stop ();

        // Check connection
        public abstract bool Started { get; }

        // Set Encryption type
        public void SetEncryption (EncryptionType encryption)
        {
            setEncryption(encryption);
            Log("{0} encrypt type: {1}", str_protocol, encryption);
        }

        public float PingWaitTime
        {
            get { return ping_wait_time_; }
            set { ping_wait_time_ = value; }
        }

        // Check unsent messages
        public abstract bool HasUnsentMessages { get; }

        // Send a message
        public abstract void SendMessage (FunapiMessage fun_msg);

        public State state
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

        public int PingIntervalSeconds
        {
            get; set;
        }

        public float PingTimeoutSeconds
        {
            get; set;
        }

        public int PingTime
        {
            get; private set;
        }

        public bool IsConnecting
        {
            get { return state_ == State.kConnecting; }
        }

        public bool IsReconnecting
        {
            get { return cstate_ == ConnectState.kConnecting ||
                         cstate_ == ConnectState.kReconnecting ||
                         cstate_ == ConnectState.kRedirecting; }
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

        protected bool SetNextAddress ()
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
                    LogWarning("SetAvailableAddress - There's no available address.");
                    return false;
                }

                SetAddress(extra);
            }

            return true;
        }

        protected abstract void SetAddress (HostAddr addr);

        // Connect functions
        public void Connect ()
        {
            Log("'{0}' Try to connect to server.", str_protocol);
            exponential_time_ = 1f;
            reconnect_count_ = 0;

            Start();
        }

        void Connect (HostAddr addr)
        {
            SetAddress(addr);
            Connect();
        }

        public void Reconnect ()
        {
            Log("'{0}' Try to reconnect to server.", str_protocol);
            cstate_ = ConnectState.kReconnecting;
            Connect();
        }

        public void Redirect (HostAddr addr)
        {
            Log("'{0}' Try to redirect to server. {1}:{2}", str_protocol, addr.host, addr.port);

            if (Started) {
                Stop();
            }

            event_.Add(
                delegate {
                    cstate_ = ConnectState.kRedirecting;
                    Connect(addr);
                }
            );
        }

        // Checks connection list
        protected void CheckConnectList ()
        {
            if (!AutoReconnect || IsReconnecting)
                return;

            cstate_ = ConnectState.kConnecting;
            exponential_time_ = 1f;
        }

        bool TryToConnect ()
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

            Log("Wait {0} seconds for connect to {1} transport.", delay_time, str_protocol);

            event_.Add (delegate {
                    Log("'{0}' Try to connect to server.", str_protocol);
                    SetNextAddress();
                    Connect();
                },
                delay_time
            );

            return true;
        }

        bool TryToReconnect ()
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

            Log("Wait {0} seconds for reconnect to {1} transport.", delay_time, str_protocol);

            event_.Add (delegate {
                    Log("'{0}' Try to reconnect to server.", str_protocol);
                    Start();
                },
                delay_time
            );

            return true;
        }

        // Update
        public void Update (float deltaTime)
        {
            // Events
            event_.Update(deltaTime);
            timer_.Update(deltaTime);
        }

        protected void AddFailureCallback (FunapiMessage fun_msg)
        {
            event_.Add(delegate {
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
        protected void OnConnectionTimeout ()
        {
            CheckConnectList();

            if (ConnectTimeoutCallback != null)
            {
                ConnectTimeoutCallback(protocol_);
            }
        }

        protected void OnReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            object message = FunapiMessage.Deserialize(body, encoding_);
            if (message == null)
            {
                LogWarning("OnReceived - message is null.");
                return;
            }

            string msg_type = "";

            if (encoding_ == FunEncoding.kJson)
            {
                if (FunapiMessage.JsonHelper.HasField(message, kMsgTypeBodyField))
                {
                    msg_type = FunapiMessage.JsonHelper.GetStringField(message, kMsgTypeBodyField);
                    FunapiMessage.JsonHelper.RemoveField(message, kMsgTypeBodyField);
                }

                if (msg_type.Length > 0)
                {
                    if (msg_type == kServerPingMessageType)
                    {
                        OnServerPingMessage(message);
                        return;
                    }
                    else if (msg_type == kClientPingMessageType)
                    {
                        OnClientPingMessage(message);
                        return;
                    }
                }
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FunMessage funmsg = (FunMessage)message;

                if (funmsg.msgtypeSpecified)
                {
                    msg_type = funmsg.msgtype;
                }

                if (msg_type.Length > 0)
                {
                    if (msg_type == kServerPingMessageType)
                    {
                        OnServerPingMessage(funmsg);
                        return;
                    }
                    else if (msg_type == kClientPingMessageType)
                    {
                        OnClientPingMessage(funmsg);
                        return;
                    }
                }
            }

            if (ReceivedCallback != null)
            {
                ReceivedCallback(new FunapiMessage(protocol_, msg_type, message));
            }
        }

        public void SetEstablish (SessionId sid)
        {
            state_ = State.kEstablished;
            session_id_.SetId(sid);

            if (EnablePing && PingIntervalSeconds > 0)
            {
                StartPingTimer();
            }

            if (StartedCallback != null)
            {
                StartedCallback(protocol_);
            }
        }

        public abstract void SetAbolish ();

        protected void OnStartedInternal ()
        {
            if (StartedInternalCallback != null)
            {
                StartedInternalCallback(protocol_);
            }
        }

        protected void OnStopped ()
        {
            StopPingTimer();

            if (StoppedCallback != null)
            {
                StoppedCallback(protocol_);
            }

            event_.Add(CheckConnectState);
        }

        protected void OnDisconnected ()
        {
            Stop();

            OnDisconnectedCallback();
        }

        protected void OnConnectFailureCallback ()
        {
            ip_list_.SetFirst();
            extra_list_.SetFirst();

            if (ConnectFailureCallback != null)
            {
                ConnectFailureCallback(protocol_);
            }
        }

        protected void OnDisconnectedCallback ()
        {
            if (DisconnectedCallback != null)
            {
                DisconnectedCallback(protocol_);
            }
        }

        protected void OnFailureCallback ()
        {
            if (FailureCallback != null)
            {
                FailureCallback(protocol_);
            }
        }

        protected void OnMessageFailureCallback (FunapiMessage fun_msg)
        {
            if (MessageFailureCallback != null)
            {
                MessageFailureCallback(protocol_, fun_msg);
            }
        }

        protected void CheckConnectState ()
        {
            if (cstate_ == ConnectState.kConnecting)
            {
                if (!TryToConnect())
                {
                    cstate_ = ConnectState.kUnknown;
                    OnConnectFailureCallback();
                }
            }
            else if (cstate_ == ConnectState.kReconnecting ||
                     cstate_ == ConnectState.kRedirecting)
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

        //---------------------------------------------------------------------
        // Ping-related functions
        //---------------------------------------------------------------------
        void StartPingTimer ()
        {
            if (protocol_ != TransportProtocol.kTcp)
                return;

            if (ping_timer_id_ != 0)
                timer_.Remove(ping_timer_id_);

            ping_timer_id_ = timer_.Add (() => OnPingTimerEvent(), true, PingIntervalSeconds);
            ping_wait_time_ = 0f;

            Log("Start ping - interval seconds: {0}, timeout seconds: {1}",
                PingIntervalSeconds, PingTimeoutSeconds);
        }

        void StopPingTimer ()
        {
            if (protocol_ != TransportProtocol.kTcp)
                return;

            timer_.Remove(ping_timer_id_);
            ping_timer_id_ = 0;
            PingTime = 0;
        }

        void OnPingTimerEvent ()
        {
            if (ping_wait_time_ > PingTimeoutSeconds)
            {
                LogWarning("Network seems disabled. Stopping the transport.");
                OnDisconnected();
                return;
            }

            SendPingMessage();
        }

        void SendPingMessage ()
        {
            if (!session_id_.IsValid)
                return;

            long timestamp = DateTime.Now.Ticks;

            if (encoding_ == FunEncoding.kJson)
            {
                object msg = FunapiMessage.Deserialize("{}");
                FunapiMessage.JsonHelper.SetStringField(msg, kMsgTypeBodyField, kClientPingMessageType);
                FunapiMessage.JsonHelper.SetStringField(msg, kSessionIdBodyField, session_id_);
                FunapiMessage.JsonHelper.SetIntegerField(msg, kPingTimestampField, timestamp);
                SendMessage(new FunapiMessage(protocol_, kClientPingMessageType, msg));
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FunPingMessage ping = new FunPingMessage();
                ping.timestamp = timestamp;
                FunMessage msg = FunapiMessage.CreateFunMessage(ping, MessageType.cs_ping);
                msg.msgtype = kClientPingMessageType;
                msg.sid = session_id_;
                SendMessage(new FunapiMessage(protocol_, kClientPingMessageType, msg));
            }

            ping_wait_time_ += PingIntervalSeconds;

            DebugLog2("Send {0} ping - timestamp: {1}", str_protocol, timestamp);
        }

        void OnServerPingMessage (object body)
        {
            // Send response
            if (encoding_ == FunEncoding.kJson)
            {
                FunapiMessage.JsonHelper.SetStringField(body, kMsgTypeBodyField, kServerPingMessageType);

                if (!session_id_.IsValid && FunapiMessage.JsonHelper.HasField(body, kSessionIdBodyField))
                    session_id_.SetId(FunapiMessage.JsonHelper.GetStringField(body, kSessionIdBodyField));

                FunapiMessage.JsonHelper.SetStringField(body, kSessionIdBodyField, session_id_);

                SendMessage(new FunapiMessage(protocol_, kServerPingMessageType, FunapiMessage.JsonHelper.Clone(body)));
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FunMessage msg = body as FunMessage;
                FunPingMessage obj = FunapiMessage.GetMessage<FunPingMessage>(msg, MessageType.cs_ping);
                if (obj == null)
                    return;

                if (!session_id_.IsValid && msg.sidSpecified)
                    session_id_.SetId(msg.sid);

                FunPingMessage ping = new FunPingMessage();
                ping.timestamp = obj.timestamp;
                if (obj.data.Length > 0) {
                    ping.data = new byte[obj.data.Length];
                    Buffer.BlockCopy(ping.data, 0, obj.data, 0, obj.data.Length);
                }

                FunMessage send_msg = FunapiMessage.CreateFunMessage(ping, MessageType.cs_ping);
                send_msg.msgtype = msg.msgtype;
                send_msg.sid = session_id_;

                SendMessage(new FunapiMessage(protocol_, kServerPingMessageType, send_msg));
            }
        }

        void OnClientPingMessage (object body)
        {
            long timestamp = 0;

            if (encoding_ == FunEncoding.kJson)
            {
                if (FunapiMessage.JsonHelper.HasField(body, kPingTimestampField))
                {
                    timestamp = (long)FunapiMessage.JsonHelper.GetIntegerField(body, kPingTimestampField);
                }
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FunMessage msg = body as FunMessage;
                FunPingMessage ping = FunapiMessage.GetMessage<FunPingMessage>(msg, MessageType.cs_ping);
                if (ping == null)
                    return;

                timestamp = ping.timestamp;
            }

            if (ping_wait_time_ > 0)
                ping_wait_time_ -= PingIntervalSeconds;

            PingTime = (int)((DateTime.Now.Ticks - timestamp) / 10000);

            DebugLog2("Receive {0} ping - timestamp:{1} time={2} ms", str_protocol, timestamp, PingTime);
        }


        public enum State
        {
            kUnknown = 0,
            kConnecting,
            kEncryptionHandshaking,
            kConnected,
            kWaitForSessionId,
            kWaitForAck,
            kEstablished
        };

        protected enum ConnectState
        {
            kUnknown = 0,
            kConnecting,
            kReconnecting,
            kRedirecting,
            kConnected
        };


        // constants.
        static readonly int kMaxReconnectCount = 3;
        static readonly float kMaxConnectingTime = 120f;
        static readonly float kFixedConnectWaitTime = 10f;
        static readonly string kMsgTypeBodyField = "_msgtype";
        static readonly string kSessionIdBodyField = "_sid";

        // Ping message-related constants.
        static readonly int kPingIntervalSecond = 3;
        static readonly float kPingTimeoutSeconds = 20f;
        static readonly string kServerPingMessageType = "_ping_s";
        static readonly string kClientPingMessageType = "_ping_c";
        static readonly string kPingTimestampField = "timestamp";

        // Event handlers
        public event TransportEventHandler ConnectTimeoutCallback;
        public event TransportEventHandler StartedCallback;
        public event TransportEventHandler StoppedCallback;
        public event TransportEventHandler FailureCallback;

        public event TransportEventHandler StartedInternalCallback;
        public event TransportEventHandler DisconnectedCallback;
        public event TransportReceivedHandler ReceivedCallback;
        public event TransportMessageHandler MessageFailureCallback;
        public event TransportEventHandler ConnectFailureCallback;

        // Connect-related member variables.
        protected ConnectState cstate_ = ConnectState.kUnknown;
        protected ConnectList ip_list_ = new ConnectList();
        protected ConnectList extra_list_ = new ConnectList();
        protected float exponential_time_ = 0f;
        protected int reconnect_count_ = 0;

        // Ping-related variables.
        int ping_timer_id_ = 0;
        float ping_wait_time_ = 0f;

        // Encoding-serializer-related member variables.
        protected FunEncoding encoding_ = FunEncoding.kNone;
        protected SessionId session_id_ = new SessionId();

        // Error-related member variables.
        protected ErrorCode last_error_code_ = ErrorCode.kNone;
        protected string last_error_message_ = "";

        // member variables.
        protected State state_;
        protected TransportProtocol protocol_;
        protected ThreadSafeEventList event_ = new ThreadSafeEventList();
        protected ThreadSafeEventList timer_ = new ThreadSafeEventList();
    }


    // Transport class for socket
    public abstract class FunapiEncryptedTransport : FunapiTransport
    {
        // Starts a socket.
        public override void Start ()
        {
            try
            {
                if (state_ != State.kUnknown)
                {
                    LogWarning("{0} Transport.Start() called, but the state is {1}. This request has ignored.\n" +
                               "If you want to reconnect, call Transport.Stop() first and wait for it to stop.",
                               str_protocol, state_);
                    return;
                }

                // Resets states.
                first_receiving_ = true;
                header_decoded_ = false;
                received_size_ = 0;
                next_decoding_offset_ = 0;
                header_fields_.Clear();
                sending_.Clear();
                resetEncryptors();
                last_error_code_ = ErrorCode.kNone;
                last_error_message_ = "";

                if (ConnectTimeout > 0f)
                {
                    timer_.Remove(connect_timeout_id_);
                    connect_timeout_id_ = timer_.Add (delegate {
                            if (state_ == State.kUnknown || state_ == State.kEstablished)
                                return;

                            LogWarning("{0} Connection waiting time has been exceeded.", str_protocol);
                            OnConnectionTimeout();
                        },
                        ConnectTimeout
                    );
                }

                StartConnect();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kConnectFailed;
                last_error_message_ = "Failure in Transport.Start: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        // Create a socket.
        protected abstract void StartConnect ();

        // Sends a packet.
        protected abstract void WireSend ();

        // Stops a socket.
        public override void Stop ()
        {
            if (state_ == State.kUnknown)
                return;

            state_ = State.kUnknown;
            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";

            timer_.Clear();
            connect_timeout_id_ = 0;

            OnStopped();
        }

        public override void SetAbolish ()
        {
            session_id_.Clear();
            pending_.Clear();
        }

        public override bool HasUnsentMessages
        {
            get
            {
                lock (sending_lock_)
                {
                    return sending_.Count > 0 || pending_.Count > 0;
                }
            }
        }

        protected virtual bool IsSendable
        {
            get { return sending_.Count == 0; }
        }

        public override void SendMessage (FunapiMessage fun_msg)
        {
            try
            {
                lock (sending_lock_)
                {
                    fun_msg.buffer = new ArraySegment<byte>(fun_msg.GetBytes(encoding_));
                    pending_.Add(fun_msg);

                    if (Started && IsSendable)
                    {
                        SendPendingMessages();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in Transport.SendMessage: " + e.ToString();
                LogError(last_error_message_);
                AddFailureCallback(fun_msg);
            }
        }

        bool EncryptThenSendMessage ()
        {
            FunDebug.Assert(state_ >= State.kConnected);
            FunDebug.Assert(sending_.Count > 0);

            for (int i = 0; i < sending_.Count; i+=2)
            {
                FunapiMessage message = sending_[i];

                string enc_header = "";
                EncryptionType type = getEncryption(message);
                if (message.msg_type == kEncryptionPublicKey)
                {
                    enc_header = generatePublicKey(type);
                }
                else if (type != EncryptionType.kNoneEncryption)
                {
                    if (!encryptMessage(message, type, ref enc_header))
                    {
                        last_error_code_ = ErrorCode.kEncryptionFailed;
                        last_error_message_ = string.Format("Encrypt message failed. type:{0}", (int)type);
                        LogWarning(last_error_message_);
                        AddFailureCallback(message);
                        return false;
                    }
                }

                StringBuilder header = new StringBuilder();
                header.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kProtocolVersion, kHeaderDelimeter);
                if (first_sending_)
                {
                    header.AppendFormat("{0}{1}{2}{3}", kPluginVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kPluginVersion, kHeaderDelimeter);
                    first_sending_ = false;
                }
                header.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter, message.buffer.Count, kHeaderDelimeter);
                if (type != EncryptionType.kNoneEncryption)
                {
                    header.AppendFormat("{0}{1}{2}", kEncryptionHeaderField, kHeaderFieldDelimeter, Convert.ToInt32(type));
                    header.AppendFormat("-{0}{1}", enc_header, kHeaderDelimeter);
                }
                header.Append(kHeaderDelimeter);

                FunapiMessage header_buffer = new FunapiMessage(protocol_, message.msg_type, header);
                header_buffer.buffer = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(header.ToString()));
                sending_.Insert(i, header_buffer);

                DebugLog2("Header to send: {0}", header.ToString());
            }

            WireSend();

            return true;
        }

        protected void SendPendingMessages ()
        {
            lock (sending_lock_)
            {
                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    DebugLog1("Retrying unsent messages.");
                    WireSend();
                }
                else if (IsSendable && pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<FunapiMessage> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;

                    EncryptThenSendMessage();
                }
            }
        }

        // Checking the buffer space before starting another async receive.
        protected void CheckReceiveBuffer (int additional_size = 0)
        {
            int remaining_size = receive_buffer_.Length - (received_size_ + additional_size);
            if (remaining_size > 0)
                return;

            int retain_size = received_size_ - next_decoding_offset_ + additional_size;
            int new_length = receive_buffer_.Length;
            while (new_length <= retain_size)
                new_length += kUnitBufferSize;

            byte[] new_buffer = new byte[new_length];

            // If there are spaces that can be collected, compact it first.
            // Otherwise, increase the receiving buffer size.
            if (next_decoding_offset_ > 0)
            {
                DebugLog2("Compacting the receive buffer to save {0} bytes.", next_decoding_offset_);
                // fit in the receive buffer boundary.
                Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0, received_size_ - next_decoding_offset_);
                receive_buffer_ = new_buffer;
                received_size_ -= next_decoding_offset_;
                next_decoding_offset_ = 0;
            }
            else
            {
                DebugLog2("Increasing the receive buffer to {0} bytes.", new_length);
                Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                receive_buffer_ = new_buffer;
            }
        }

        // Decoding a messages
        protected void TryToDecodeMessage ()
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
                        LogWarning("Failed to decode body.");
                        FunDebug.Assert(false);
                    }
                }
                else
                {
                    LogWarning("Failed to decode header.");
                    FunDebug.Assert(false);
                }
            }
        }

        bool TryToDecodeHeader ()
        {
            DebugLog2("Trying to decode header fields.");

            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    DebugLog2("We need more bytes for a header field. Waiting.");
                    return false;
                }

                string line = System.Text.Encoding.ASCII.GetString(receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    DebugLog2("End of header reached. Will decode body from now.");
                    return true;
                }

                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                FunDebug.Assert(tuple.Length == 2);
                DebugLog2("Decoded header field '{0} : {1}'", tuple[0], tuple[1]);
                tuple[0] = tuple[0].ToUpper();
                header_fields_[tuple[0]] = tuple[1];
            }

            return false;
        }

        bool TryToDecodeBody ()
        {
            // Header version
            FunDebug.Assert(header_fields_.ContainsKey(kVersionHeaderField));
            int version = Convert.ToUInt16(header_fields_[kVersionHeaderField]);
            FunDebug.Assert(version == FunapiVersion.kProtocolVersion);

            // Header length
            FunDebug.Assert(header_fields_.ContainsKey(kLengthHeaderField));
            int body_length = Convert.ToInt32(header_fields_[kLengthHeaderField]);
            DebugLog2("We need {0} bytes for a message body. Buffer has {1} bytes.",
                      body_length, received_size_ - next_decoding_offset_);

            if (received_size_ - next_decoding_offset_ < body_length)
            {
                // Need more bytes.
                DebugLog2("We need more bytes for a message body. Waiting.");
                return false;
            }

            // Encryption
            string encryption_type = "";
            string encryption_header = "";
            if (header_fields_.TryGetValue(kEncryptionHeaderField, out encryption_header))
                parseEncryptionHeader(ref encryption_type, ref encryption_header);

            if (state_ == State.kEncryptionHandshaking)
            {
                FunDebug.Assert(body_length == 0);

                if (doHandshaking(encryption_type, encryption_header))
                {
                    state_ = State.kConnected;
                    Log("Ready to receive.");

                    // Send public key (Do not change this order)
                    if (hasEncryption(EncryptionType.kAes128Encryption))
                        SendPublicKey(EncryptionType.kAes128Encryption);

                    if (hasEncryption(EncryptionType.kChaCha20Encryption))
                        SendPublicKey(EncryptionType.kChaCha20Encryption);

                    event_.Add(OnHandshakeComplete);
                }
            }

            if (body_length > 0)
            {
                if (state_ < State.kConnected)
                {
                    LogWarning("Unexpected message. state:{0}", state_);
                    return false;
                }

                ArraySegment<byte> body = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, body_length);
                FunDebug.Assert(body.Count == body_length);
                next_decoding_offset_ += body_length;

                if (encryption_type.Length > 0)
                {
                    if (!decryptMessage(body, encryption_type, encryption_header))
                        return false;
                }

                if (first_receiving_)
                {
                    first_receiving_ = false;
                    cstate_ = ConnectState.kConnected;

                    timer_.Remove(connect_timeout_id_);
                    connect_timeout_id_ = 0;
                }

                // The network module eats the fields and invoke registered handler.
                OnReceived(header_fields_, body);
            }

            // Prepares a next message.
            header_decoded_ = false;
            header_fields_.Clear();
            return true;
        }

        void SendPublicKey (EncryptionType type)
        {
            FunapiMessage fun_msg = null;
            if (encoding_ == FunEncoding.kJson)
            {
                fun_msg = new FunapiMessage(protocol_, kEncryptionPublicKey, null, type);
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FunMessage msg = new FunMessage();
                fun_msg = new FunapiMessage(protocol_, kEncryptionPublicKey, msg, type);
            }

            // Add a message to front of pending list...
            lock (sending_lock_)
            {
                fun_msg.buffer = new ArraySegment<byte>(fun_msg.GetBytes(encoding_));
                pending_.Insert(0, fun_msg);
            }

            DebugLog1("{0} sending a {1}-pubkey message.", str_protocol, (int)type);
        }

        // Sends messages & Calls start callback
        void OnHandshakeComplete ()
        {
            lock (sending_lock_)
            {
                if (Started && IsSendable)
                {
                    SendPendingMessages();
                    OnStartedInternal();
                }
            }
        }

        protected virtual void OnFailure ()
        {
            LogWarning("OnFailure({0}) - state: {1}\n{2}:{3}",
                       str_protocol, state_, last_error_code_, last_error_message_);

            if (state_ != State.kEstablished)
            {
                CheckConnectList();

                Stop();
            }

            OnFailureCallback();
        }

        static int BytePatternMatch (ArraySegment<byte> haystack, ArraySegment<byte> needle)
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


        // Buffer-related constants.
        protected static readonly int kUnitBufferSize = 65536;

        // Funapi header-related constants.
        protected static readonly string kHeaderDelimeter = "\n";
        protected static readonly string kHeaderFieldDelimeter = ":";
        protected static readonly string kVersionHeaderField = "VER";
        protected static readonly string kPluginVersionHeaderField = "PVER";
        protected static readonly string kLengthHeaderField = "LEN";
        protected static readonly string kEncryptionHeaderField = "ENC";

        // Encryption-related constants.
        static readonly string kEncryptionPublicKey = "_pub_key";

        // for speed-up.
        static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(kHeaderDelimeter));
        static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // Message-related.
        int connect_timeout_id_ = 0;
        bool first_sending_ = true;
        bool first_receiving_ = true;
        bool header_decoded_ = false;
        protected int received_size_ = 0;
        protected int next_decoding_offset_ = 0;
        protected object sending_lock_ = new object();
        protected object receive_lock_ = new object();
        protected byte[] receive_buffer_ = new byte[kUnitBufferSize];
        protected byte[] send_buffer_ = new byte[kUnitBufferSize];
        protected List<FunapiMessage> pending_ = new List<FunapiMessage>();
        protected List<FunapiMessage> sending_ = new List<FunapiMessage>();
        Dictionary<string, string> header_fields_ = new Dictionary<string, string>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiEncryptedTransport
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
        public override void Stop ()
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

        public override bool Started
        {
            get
            {
                return sock_ != null && sock_.Connected && state_ >= State.kConnected;
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
        protected override void StartConnect ()
        {
            state_ = State.kConnecting;
            sock_ = new Socket(ip_af_, SocketType.Stream, ProtocolType.Tcp);
            if (DisableNagle)
                sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

            sock_.BeginConnect(connect_ep_, new AsyncCallback(this.StartCb), this);
        }

        protected override void SetAddress (HostAddr addr)
        {
            IPAddress ip = null;
            if (addr is HostIP)
            {
                ip = ((HostIP)addr).ip;
            }
            else
            {
                IPHostEntry host_info = Dns.GetHostEntry(addr.host);
                FunDebug.Assert(host_info.AddressList.Length > 0);
                ip = host_info.AddressList[0];
            }

            ip_af_ = ip.AddressFamily;
            connect_ep_ = new IPEndPoint(ip, addr.port);

            Log("TCP transport - {0}:{1}", ip, addr.port);
        }

        protected override void WireSend ()
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

        void StartCb (IAsyncResult ar)
        {
            DebugLog1("StartCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect. socket is null.";
                    LogWarning(last_error_message_);
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    LogWarning(last_error_message_);
                    event_.Add(OnFailure);
                    return;
                }
                Log("Connected.");

                state_ = State.kEncryptionHandshaking;

                lock (receive_lock_)
                {
                    // Wait for encryption handshaking message.
                    ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                    buffer.Add(wrapped);
                    sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLog1("BeginConnect operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kConnectFailed;
                last_error_message_ = "Failure in Tcp.StartCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        void SendBytesCb (IAsyncResult ar)
        {
            DebugLog1("SendBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    LogWarning(last_error_message_);
                    return;
                }

                int nSent = sock_.EndSend(ar);
                DebugLog2("Sent {0}bytes", nSent);
                FunDebug.Assert(nSent > 0, "Failed to transfer tcp messages.");

                lock (sending_lock_)
                {
                    // Removes any segment fully sent.
                    while (nSent > 0)
                    {
                        FunDebug.Assert(sending_.Count > 0);

                        if (sending_[0].buffer.Count > nSent)
                        {
                            // partial data
                            DebugLog2("Partially sent. Will resume. (buffer:{0}, nSent:{1})",
                                      sending_[0].buffer.Count, nSent);
                            break;
                        }
                        else
                        {
                            DebugLog2("Discarding a fully sent message. ({0}bytes)",
                                      sending_[0].buffer.Count);

                            // fully sent.
                            nSent -= sending_[0].buffer.Count;
                            sending_.RemoveAt(0);
                        }
                    }

                    while (sending_.Count > 0 && sending_[0].buffer.Count <= 0)
                    {
                        DebugLog2("Remove empty buffer.");
                        sending_.RemoveAt(0);
                    }

                    // If the first segment has been sent partially, we need to reconstruct the first segment.
                    if (nSent > 0)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        ArraySegment<byte> original = sending_[0].buffer;

                        FunDebug.Assert(nSent <= sending_[0].buffer.Count);
                        ArraySegment<byte> adjusted = new ArraySegment<byte>(original.Array, original.Offset + nSent, original.Count - nSent);
                        sending_[0].buffer = adjusted;
                    }

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    SendPendingMessages();
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLog1("BeginSend operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in Tcp.SendBytesCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        void ReceiveBytesCb (IAsyncResult ar)
        {
            DebugLog1("ReceiveBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    LogWarning(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        DebugLog2("Received {0} bytes. Buffer has {1} bytes.",
                                  nRead, received_size_ - next_decoding_offset_);
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

                        DebugLog2("Ready to receive more. We can receive upto {0} more bytes",
                                  receive_buffer_.Length - received_size_);

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        LogWarning("Socket closed");

                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            LogWarning("Buffer has {0} bytes but they failed to decode. Discarding.",
                                       receive_buffer_.Length - received_size_);
                        }

                        last_error_code_ = ErrorCode.kDisconnected;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        LogWarning(last_error_message_);
                        event_.Add(OnDisconnected);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLog1("BeginReceive operation has been Cancelled.");
            }
            catch (NullReferenceException)
            {
                // When Stop is called Socket.EndReceive may return a NullReferenceException
                DebugLog1("BeginReceive operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in Tcp.ReceiveBytesCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }


        Socket sock_;
        AddressFamily ip_af_;
        IPEndPoint connect_ep_;
    }


    // UDP transport layer
    public class FunapiUdpTransport : FunapiEncryptedTransport
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
        public override void Stop ()
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

        public override bool Started
        {
            get { return sock_ != null && state_ >= State.kConnected; }
        }

        public override bool IsDatagram
        {
            get { return true; }
        }

        // Create a socket.
        protected override void StartConnect ()
        {
            state_ = State.kConnected;
            sock_ = new Socket(ip_af_, SocketType.Dgram, ProtocolType.Udp);
            if (ip_af_ == AddressFamily.InterNetwork)
                sock_.Bind(new IPEndPoint(IPAddress.Any, 0));
            else
                sock_.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));

            lock (receive_lock_)
            {
                sock_.BeginReceiveFrom(receive_buffer_, 0, receive_buffer_.Length, SocketFlags.None,
                                       ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);
            }

            OnStartedInternal();
        }

        protected override void SetAddress (HostAddr addr)
        {
            IPAddress ip = null;
            if (addr is HostIP)
            {
                ip = ((HostIP)addr).ip;
            }
            else
            {
                IPHostEntry host_info = Dns.GetHostEntry(addr.host);
                FunDebug.Assert(host_info.AddressList.Length > 0);
                ip = host_info.AddressList[0];
            }

            ip_af_ = ip.AddressFamily;
            send_ep_ = new IPEndPoint(ip, addr.port);
            if (ip_af_ == AddressFamily.InterNetwork)
                receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, addr.port);
            else
                receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.IPv6Any, addr.port);

            Log("UDP transport - {0}:{1}", ip, addr.port);
        }

        // Send a packet.
        protected override void WireSend ()
        {
            int offset = 0;

            lock (sending_lock_)
            {
                FunDebug.Assert(sending_.Count >= 2);

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
                    LogWarning("Message is greater than 64KB. It will be truncated.");
                    FunDebug.Assert(false);
                }

                sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                  send_ep_, new AsyncCallback(this.SendBytesCb), this);
            }
        }

        void SendBytesCb (IAsyncResult ar)
        {
            DebugLog1("SendBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    LogWarning(last_error_message_);
                    return;
                }

                lock (sending_lock_)
                {
                    int nSent = sock_.EndSend(ar);
                    DebugLog2("Sent {0}bytes", nSent);
                    FunDebug.Assert(nSent > 0, "Failed to transfer udp messages.");

                    FunDebug.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    int nToSend = 0;
                    for (int i = 0; i < 2; ++i)
                    {
                        nToSend += sending_[0].buffer.Count;
                        sending_.RemoveAt(0);
                    }

                    FunDebug.Assert(nSent == nToSend,
                                    string.Format("Failed to sending whole messages. {0}:{1}", nToSend, nSent));

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    SendPendingMessages();
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLog1("BeginSendTo operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in Udp.SendBytesCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        void ReceiveBytesCb (IAsyncResult ar)
        {
            DebugLog1("ReceiveBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    LogWarning(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        DebugLog2("Received {0} bytes. Buffer has {1} bytes.",
                                  nRead, received_size_ - next_decoding_offset_);
                    }

                    // Decoding a message
                    TryToDecodeMessage();

                    if (nRead > 0)
                    {
                        // Resets buffer
                        received_size_ = 0;
                        next_decoding_offset_ = 0;

                        // Starts another async receive
                        sock_.BeginReceiveFrom(receive_buffer_, received_size_,
                                               receive_buffer_.Length - received_size_,
                                               SocketFlags.None, ref receive_ep_,
                                               new AsyncCallback(this.ReceiveBytesCb), this);

                        DebugLog2("Ready to receive more. We can receive upto {0} more bytes",
                                  receive_buffer_.Length);

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        LogWarning("Socket closed");

                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            LogWarning("Buffer has {0} bytes but they failed to decode. Discarding.",
                                       receive_buffer_.Length - received_size_);
                        }

                        last_error_code_ = ErrorCode.kDisconnected;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        LogWarning(last_error_message_);
                        event_.Add(OnDisconnected);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLog1("BeginReceiveFrom operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in Udp.ReceiveBytesCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }


        Socket sock_;
        AddressFamily ip_af_;
        IPEndPoint send_ep_;
        EndPoint receive_ep_;
    }


    // HTTP transport layer
    public class FunapiHttpTransport : FunapiEncryptedTransport
    {
        public FunapiHttpTransport (string hostname_or_ip, UInt16 port, bool https, FunEncoding type)
        {
            protocol_ = TransportProtocol.kHttp;
            str_protocol = "Http";
            encoding_ = type;
            RequestTimeout = kTimeoutSeconds;

            ip_list_.Add(hostname_or_ip, port, https);
            SetNextAddress();

            if (https)
                MozRoots.LoadRootCertificates();
        }

        public MonoBehaviour mono { set; get; }

        public override void Stop ()
        {
            if (state_ == State.kUnknown)
                return;

            CancelRequest();

            base.Stop();
        }

        public override bool Started
        {
            get { return state_ >= State.kConnected; }
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

        public float RequestTimeout
        {
            set; get;
        }

        protected override void StartConnect ()
        {
            state_ = State.kConnected;
            str_cookie_ = "";

            OnStartedInternal();
        }

        protected override void SetAddress (HostAddr addr)
        {
            FunDebug.Assert(addr is HostHttp);
            HostHttp http = (HostHttp)addr;

            // Url
            host_url_ = string.Format("{0}://{1}:{2}/v{3}/",
                                      (http.https ? "https" : "http"), http.host, http.port,
                                      FunapiVersion.kProtocolVersion);

            Log("HTTP transport - {0}:{1}", http.host, http.port);
        }

        protected override bool IsSendable
        {
            get
            {
#if !NO_UNITY
                if (cur_www_ != null)
                    return false;
#endif
                if (web_request_ != null || web_response_ != null)
                    return false;

                return true;
            }
        }

        protected override void WireSend ()
        {
            DebugLog1("Send a Message.");

            try
            {
                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count >= 2);
                    DebugLog2("Host Url: {0}", host_url_);

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

                        if (list[i] == kEncryptionHeaderField)
                            headers.Add(kEncryptionHttpHeaderField, list[i+1]);
                        else
                            headers.Add(list[i], list[i+1]);
                    }

                    if (str_cookie_.Length > 0)
                        headers.Add(kCookieHeaderField, str_cookie_);

                    // Sets timeout timer
                    timer_.Remove(request_timeout_id_);
                    request_timeout_id_ = timer_.Add (delegate {
                            OnRequestTimeout(body.msg_type);
                        },
                        RequestTimeout
                    );
                    DebugLog2("Set http request timeout - msg_type:{0} time:{1}",
                              body.msg_type, RequestTimeout);


#if !NO_UNITY
                    // Sending a message
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
                last_error_message_ = "Failure in Http.WireSend: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

#if !NO_UNITY
        void SendWWWRequest (Dictionary<string, string> headers, FunapiMessage body)
        {
            cancel_www_ = false;

            if (body.buffer.Count > 0)
            {
                mono.StartCoroutine(WWWPost(new WWW(host_url_, body.buffer.Array, headers)));
            }
            else
            {
                mono.StartCoroutine(WWWPost(new WWW(host_url_, null, headers)));
            }
        }
#endif

        void SendHttpWebRequest (Dictionary<string, string> headers, FunapiMessage body)
        {
            // Request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
            request.ConnectionGroupName = session_id_;
            request.Method = "POST";
            request.ContentType = "application/octet-stream";
            request.ContentLength = body.buffer.Count;

            foreach (KeyValuePair<string, string> item in headers) {
                request.Headers[item.Key] = item.Value;
            }

            web_request_ = request;
            was_aborted_ = false;

            web_request_.BeginGetRequestStream(new AsyncCallback(RequestStreamCb), body);
        }

        void OnReceiveHeader (string headers)
        {
            StringBuilder buffer = new StringBuilder();
            string[] lines = headers.Replace("\r", "").Split('\n');
            int body_length = 0;

            buffer.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter, FunapiVersion.kProtocolVersion, kHeaderDelimeter);

            foreach (string n in lines)
            {
                if (n.Length > 0)
                {
                    string[] tuple = n.Split(kHeaderSeparator, StringSplitOptions.RemoveEmptyEntries);
                    string key = tuple[0].ToLower();
                    string value = "";

                    if (tuple.Length >= 2)
                        value = tuple[1];

                    switch (key)
                    {
                    case "content-type":
                        break;
                    case "set-cookie":
                        str_cookie_ = value;
                        DebugLog1("Set Cookie : {0}", str_cookie_);
                        break;
                    case "content-length":
                        body_length = Convert.ToInt32(value);
                        buffer.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter, value, kHeaderDelimeter);
                        break;
                    case "x-ifun-enc":
                        buffer.AppendFormat("{0}{1}{2}{3}", kEncryptionHeaderField, kHeaderFieldDelimeter, value, kHeaderDelimeter);
                        break;
                    default:
                        buffer.AppendFormat("{0}{1}{2}{3}", tuple[0], kHeaderFieldDelimeter, value, kHeaderDelimeter);
                        break;
                    }
                }
                else {
                    break;
                }
            }
            buffer.Append(kHeaderDelimeter);

            byte[] header_bytes = System.Text.Encoding.ASCII.GetBytes(buffer.ToString());

            // Checks buffer's space
            received_size_ = 0;
            next_decoding_offset_ = 0;
            CheckReceiveBuffer(header_bytes.Length + body_length);

            // Copy to buffer
            Buffer.BlockCopy(header_bytes, 0, receive_buffer_, 0, header_bytes.Length);
            received_size_ += header_bytes.Length;
        }

        void RequestStreamCb (IAsyncResult ar)
        {
            DebugLog1("RequestStreamCb called.");

            try
            {
                FunapiMessage body = (FunapiMessage)ar.AsyncState;

                Stream stream = web_request_.EndGetRequestStream(ar);
                stream.Write(body.buffer.Array, 0, body.buffer.Count);
                stream.Close();
                DebugLog2("Sent {0}bytes.", body.buffer.Count);

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    sending_.RemoveAt(0);
                    sending_.RemoveAt(0);
                }

                web_request_.BeginGetResponse(new AsyncCallback(ResponseCb), null);
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if (we != null && we.Status == WebExceptionStatus.RequestCanceled)
                {
                    // When Stop is called HttpWebRequest.EndGetRequestStream may return a Exception
                    DebugLog1("Http request operation has been Cancelled.");
                    DebugLog1(e.ToString());
                    return;
                }

                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in Http.RequestStreamCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        void ResponseCb (IAsyncResult ar)
        {
            DebugLog1("ResponseCb called.");

            try
            {
                if (was_aborted_)
                    return;

                web_response_ = (HttpWebResponse)web_request_.EndGetResponse(ar);
                web_request_ = null;

                if (web_response_.StatusCode == HttpStatusCode.OK)
                {
                    lock (receive_lock_)
                    {
                        byte[] header = web_response_.Headers.ToByteArray();
                        string str_header = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
                        OnReceiveHeader(str_header);

                        read_stream_ = web_response_.GetResponseStream();
                        read_stream_.BeginRead(receive_buffer_, received_size_, receive_buffer_.Length - received_size_,
                                               new AsyncCallback(ReadCb), null);
                    }
                }
                else
                {
                    LogWarning("Failed response. status:{0}", web_response_.StatusDescription);
                    event_.Add(OnFailure);
                }
            }
            catch (Exception e)
            {
                WebException we = e as WebException;
                if (we != null && we.Status == WebExceptionStatus.RequestCanceled)
                {
                    // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                    DebugLog1("Http request operation has been Cancelled.");
                    DebugLog1(e.ToString());
                    return;
                }

                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in Http.ResponseCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        void ReadCb (IAsyncResult ar)
        {
            DebugLog1("ReadCb called.");

            try
            {
                int nRead = read_stream_.EndRead(ar);
                if (nRead > 0)
                {
                    lock (receive_lock_)
                    {
                        received_size_ += nRead;
                        read_stream_.BeginRead(receive_buffer_, received_size_, receive_buffer_.Length - received_size_,
                                               new AsyncCallback(ReadCb), null);
                    }
                }
                else
                {
                    if (web_response_ == null)
                    {
                        LogWarning("Response instance is null.");
                        event_.Add(OnFailure);
                        return;
                    }

                    lock (receive_lock_)
                    {
                        // Decoding a message
                        TryToDecodeMessage();
                    }

                    read_stream_.Close();
                    web_response_.Close();
                    ClearRequest();

                    // Sends unsent messages
                    SendPendingMessages();
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in Http.ReadCb: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }

#if !NO_UNITY
        IEnumerator WWWPost (WWW www)
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
                    FunDebug.Assert(sending_.Count >= 2);

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

                lock (receive_lock_)
                {
                    OnReceiveHeader(headers.ToString());

                    Buffer.BlockCopy(www.bytes, 0, receive_buffer_, received_size_, www.bytes.Length);
                    received_size_ += www.bytes.Length;

                    // Decoding a message
                    TryToDecodeMessage();
                }

                ClearRequest();

                // Sends unsent messages
                SendPendingMessages();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in Http.WWWPost: " + e.ToString();
                LogError(last_error_message_);
                event_.Add(OnFailure);
            }
        }
#endif

        public void CancelRequest ()
        {
#if !NO_UNITY
            if (cur_www_ != null)
                cancel_www_ = true;
#endif
            if (web_request_ != null)
            {
                was_aborted_ = true;
                web_request_.Abort();
            }

            if (web_response_ != null)
            {
                read_stream_.Close();
                web_response_.Close();
            }

            ClearRequest();
        }

        void ClearRequest ()
        {
#if !NO_UNITY
            cur_www_ = null;
#endif
            web_request_ = null;
            web_response_ = null;

            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";

            timer_.Remove(request_timeout_id_);
            request_timeout_id_ = 0;
        }

        void OnRequestTimeout (string msg_type)
        {
            last_error_code_ = ErrorCode.kRequestTimeout;
            last_error_message_ = string.Format("Http Request timeout - msg_type:{0}", msg_type);
            LogWarning(last_error_message_);
            OnFailure();
        }

        protected override void OnFailure ()
        {
            CancelRequest();
            base.OnFailure();
        }


        // Funapi header-related constants.
        static readonly string kEncryptionHttpHeaderField = "X-iFun-Enc";
        static readonly string kCookieHeaderField = "Cookie";

        static readonly string[] kHeaderSeparator = { kHeaderFieldDelimeter, kHeaderDelimeter };

        // waiting time for response
        static readonly float kTimeoutSeconds = 30f;

        // member variables.
        string host_url_;
        string str_cookie_;
        int request_timeout_id_ = 0;

        // WebRequest-related member variables.
        HttpWebRequest web_request_ = null;
        HttpWebResponse web_response_ = null;
        Stream read_stream_ = null;
        bool was_aborted_ = false;

        // WWW-related member variables.
#if !NO_UNITY
        bool using_www_ = false;
        bool cancel_www_ = false;
        WWW cur_www_ = null;
#endif
    }


    // Event handler delegate
    public delegate void TransportEventHandler(TransportProtocol protocol);
    public delegate void TimeoutEventHandler(string msg_type);
    public delegate void TransportMessageHandler(TransportProtocol protocol, FunapiMessage fun_msg);
    public delegate void TransportReceivedHandler(FunapiMessage message);

}  // namespace Fun
