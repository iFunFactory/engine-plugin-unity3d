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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
#if !NO_UNITY
using UnityEngine;
#endif

// Protobuf
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
    public abstract class FunapiTransport
    {
        public FunapiTransport()
        {
            state_ = State.kUnknown;
            protocol_ = TransportProtocol.kDefault;
            PingIntervalSeconds = kPingIntervalSecond;
            PingTimeoutSeconds = kPingTimeoutSeconds;
            ConnectTimeout = 10f;
        }

        // Start connecting
        internal abstract void Start();

        // Disconnection
        internal abstract void Stop();

        // Set Encryption type
        internal abstract void SetEncryption (EncryptionType encryption);

        // Check connection
        internal abstract bool Started { get; }

        internal float PingWaitTime
        {
            get { return ping_wait_time_; }
            set { ping_wait_time_ = value; }
        }

        // Check unsent messages
        internal abstract bool HasUnsentMessages { get; }

        // Send a message
        internal abstract void SendMessage (FunapiMessage fun_msg);

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
            get; internal set;
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

        // This function is no longer used.
        [System.Obsolete("This will be deprecated Oct 2016. " +
                         "You can use FunapiMessage.JsonHelper instead of this function.")]
        public JsonAccessor JsonHelper
        {
            get { return FunapiMessage.JsonHelper; }
            set { FunapiMessage.JsonHelper = value; }
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
                    FunDebug.Log("SetAvailableAddress - There's no available address.");
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
            FunDebug.Log("'{0}' Try to connect to server.", str_protocol);
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
            FunDebug.Log("'{0}' Try to reconnect to server.", str_protocol);
            cstate_ = ConnectState.kReconnecting;
            Connect();
        }

        internal void Redirect (HostAddr addr)
        {
            FunDebug.Log("Redirect {0} [{1}:{2}]", str_protocol, addr.host, addr.port);

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
        internal void CheckConnectList ()
        {
            if (!AutoReconnect || IsReconnecting)
                return;

            cstate_ = ConnectState.kConnecting;
            exponential_time_ = 1f;
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

            FunDebug.Log("Wait {0} seconds for connect to {1} transport.",
                           delay_time, str_protocol);

            event_.Add (delegate {
                    FunDebug.Log("'{0}' Try to connect to server.", str_protocol);
                    SetNextAddress();
                    Connect();
                },
                delay_time
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

            FunDebug.Log("Wait {0} seconds for reconnect to {1} transport.",
                           delay_time, str_protocol);

            event_.Add (delegate {
                    FunDebug.Log("'{0}' Try to reconnect to server.", str_protocol);
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

        internal void AddFailureCallback (FunapiMessage fun_msg)
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
            object message = FunapiMessage.Deserialize(body, encoding_);
            if (message == null)
            {
                FunDebug.Log("OnReceived - message is null.");
                return;
            }

            string msg_type = "";

            if (encoding_ == FunEncoding.kJson)
            {
                if (FunapiMessage.JsonHelper.HasField(message, kMsgTypeBodyField))
                {
                    msg_type = FunapiMessage.JsonHelper.GetStringField(message, kMsgTypeBodyField) as string;
                    FunapiMessage.JsonHelper.RemoveStringField(message, kMsgTypeBodyField);
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

                if (funmsg.msgtype != null)
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

        internal void OnStarted ()
        {
            state_ = State.kEstablished;

            if (EnablePing && PingIntervalSeconds > 0)
            {
                StartPingTimer();
            }

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
            StopPingTimer();

            if (StoppedCallback != null)
            {
                StoppedCallback(protocol_);
            }

            event_.Add(CheckConnectState);
        }

        internal void OnDisconnected ()
        {
            Stop();

            OnDisconnectedCallback();
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

        internal void CheckConnectState ()
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
        private void StartPingTimer ()
        {
            if (protocol_ != TransportProtocol.kTcp)
                return;

            if (ping_timer_id_ != 0)
                timer_.Remove(ping_timer_id_);

            ping_timer_id_ = timer_.Add (() => OnPingTimerEvent(), true, PingIntervalSeconds);
            ping_wait_time_ = 0f;

            FunDebug.Log("Start ping - interval seconds: {0}, timeout seconds: {1}",
                         PingIntervalSeconds, PingTimeoutSeconds);
        }

        private void StopPingTimer ()
        {
            if (protocol_ != TransportProtocol.kTcp)
                return;

            timer_.Remove(ping_timer_id_);
            ping_timer_id_ = 0;
            PingTime = 0;
        }

        private void OnPingTimerEvent ()
        {
            if (ping_wait_time_ > PingTimeoutSeconds)
            {
                FunDebug.LogWarning("Network seems disabled. Stopping the transport.");
                OnDisconnected();
                return;
            }

            SendPingMessage();
        }

        private void SendPingMessage ()
        {
            long timestamp = DateTime.Now.Ticks;

            // Send response
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
#if NO_UNITY
            FunDebug.DebugLog("Send {0} ping - timestamp: {1}", str_protocol, timestamp);
#else
            FunDebug.DebugLog("Send {0} ping - timestamp: {1}", str_protocol, timestamp);
#endif
        }

        private void OnServerPingMessage (object body)
        {
            // Send response
            if (encoding_ == FunEncoding.kJson)
            {
                FunapiMessage.JsonHelper.SetStringField(body, kMsgTypeBodyField, kServerPingMessageType);

                if (session_id_.Length > 0)
                    FunapiMessage.JsonHelper.SetStringField(body, kSessionIdBodyField, session_id_);

                SendMessage(new FunapiMessage(protocol_, kServerPingMessageType, FunapiMessage.JsonHelper.Clone(body)));
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                FunMessage msg = body as FunMessage;
                FunPingMessage obj = (FunPingMessage)FunapiMessage.GetMessage(msg, MessageType.cs_ping);
                if (obj == null)
                    return;

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

        private void OnClientPingMessage (object body)
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
                object obj = FunapiMessage.GetMessage(msg, MessageType.cs_ping);
                if (obj == null)
                    return;

                FunPingMessage ping = obj as FunPingMessage;
                timestamp = ping.timestamp;
            }

            if (ping_wait_time_ > 0)
                ping_wait_time_ -= PingIntervalSeconds;

            PingTime = (int)((DateTime.Now.Ticks - timestamp) / 10000);

#if NO_UNITY
            FunDebug.DebugLog("Receive {0} ping - timestamp:{1} time={2} ms",
                              str_protocol, timestamp, PingTime);
#else
            FunDebug.DebugLog("Receive {0} ping - timestamp:{1} time={2} ms",
                              str_protocol, timestamp, PingTime);
#endif
        }


        internal enum State
        {
            kUnknown = 0,
            kConnecting,
            kEncryptionHandshaking,
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

        internal enum EncryptionMethod
        {
            kNone = 0,
            kIFunEngine1
        }


        // constants.
        private static readonly int kMaxReconnectCount = 3;
        private static readonly float kMaxConnectingTime = 120f;
        private static readonly float kFixedConnectWaitTime = 10f;
        private static readonly string kMsgTypeBodyField = "_msgtype";
        private static readonly string kSessionIdBodyField = "_sid";

        // Ping message-related constants.
        private static readonly int kPingIntervalSecond = 3;
        private static readonly float kPingTimeoutSeconds = 20f;
        private static readonly string kServerPingMessageType = "_ping_s";
        private static readonly string kClientPingMessageType = "_ping_c";
        private static readonly string kPingTimestampField = "timestamp";

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

        // Ping-related variables.
        private int ping_timer_id_ = 0;
        private float ping_wait_time_ = 0f;

        // Encoding-serializer-releated member variables.
        internal FunEncoding encoding_ = FunEncoding.kNone;
        internal string session_id_ = "";

        // Error-releated member variables.
        internal ErrorCode last_error_code_ = ErrorCode.kNone;
        internal string last_error_message_ = "";

        // member variables.
        internal State state_;
        internal TransportProtocol protocol_;
        internal ThreadSafeEventList event_ = new ThreadSafeEventList();
        internal ThreadSafeEventList timer_ = new ThreadSafeEventList();
    }


    // Transport class for socket
    public abstract class FunapiEncryptedTransport : FunapiTransport
    {
        // Starts a socket.
        internal override void Start()
        {
            try
            {
                if (state_ != State.kUnknown)
                {
                    FunDebug.LogWarning("{0} Transport.Start() called, but the state is {1}. This request has ignored.\n{2}",
                                          str_protocol, state_, "If you want to reconnect, call Transport.Stop() first and wait for it to stop.");
                    return;
                }

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
                    timer_.Remove(connect_timeout_id_);
                    connect_timeout_id_ = timer_.Add (delegate {
                            if (state_ == State.kUnknown || state_ == State.kEstablished)
                                return;

                            FunDebug.Log("{0} Connection waiting time has been exceeded.", str_protocol);
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
                last_error_message_ = "Failure in Start: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        // Create a socket.
        protected abstract void StartConnect();

        // Sends a packet.
        protected abstract void WireSend();

        // Stops a socket.
        internal override void Stop()
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

        internal override void SetEncryption (EncryptionType encryption)
        {
            Encryptor encryptor = Encryptor.Create(encryption);
            if (encryptor == null)
            {
                last_error_code_ = ErrorCode.kInvalidEncryption;
                last_error_message_ = "Failed to create encryptor: " + encryption;
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
                return;
            }

            default_encryptor_ = (int)encryption;
            encryptors_[encryption] = encryptor;
            FunDebug.Log("Set encryption type - {0}", default_encryptor_);
        }

        internal virtual bool IsSendable
        {
            get { return sending_.Count == 0; }
        }

        internal override void SendMessage (FunapiMessage fun_msg)
        {
            try
            {
                lock (sending_lock_)
                {
                    fun_msg.buffer = new ArraySegment<byte>(fun_msg.GetBytes(encoding_));

                    pending_.Add(fun_msg);

                    if (Started && IsSendable)
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
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in SendMessage: " + e.ToString();
                FunDebug.Log(last_error_message_);
                AddFailureCallback(fun_msg);
            }
        }

        internal bool EncryptThenSendMessage()
        {
            FunDebug.Assert((int)state_ >= (int)State.kConnected);
            FunDebug.Assert(sending_.Count > 0);

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
                        FunDebug.Log(last_error_message_);
                        AddFailureCallback(message);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kEstablished)
                    {
                        last_error_code_ = ErrorCode.kInvalidEncryption;
                        last_error_message_ = string.Format("'{0}' is invalid encryption type. Check out the encryption type of server.", encryptor.name);
                        FunDebug.Log(last_error_message_);
                        AddFailureCallback(message);
                        return false;
                    }

                    if (message.buffer.Count > 0)
                    {
                        Int64 nSize = encryptor.Encrypt(message.buffer, message.buffer, ref encryption_header);
                        if (nSize <= 0)
                        {
                            last_error_code_ = ErrorCode.kEncryptionFailed;
                            last_error_message_ = "Encrypt failure: " + encryptor.name;
                            FunDebug.Log(last_error_message_);
                            AddFailureCallback(message);
                            return false;
                        }

                        FunDebug.Assert(nSize == message.buffer.Count);
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
                if ((int)encryption != kNoneEncryption)
                {
                    FunDebug.Assert(encryptor != null);
                    FunDebug.Assert(encryptor.encryption == encryption);
                    header.AppendFormat("{0}{1}{2}", kEncryptionHeaderField, kHeaderFieldDelimeter, Convert.ToInt32(encryption));
                    header.AppendFormat("-{0}{1}", encryption_header, kHeaderDelimeter);
                }
                header.Append(kHeaderDelimeter);

                FunapiMessage header_buffer = new FunapiMessage(protocol_, message.msg_type, header);
                header_buffer.buffer = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(header.ToString()));
                sending_.Insert(i, header_buffer);

                //FunDebug.DebugLog("Header to send: {0}", header.ToString());
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
                    FunDebug.Log("Retrying unsent messages.");
                    WireSend();
                }
                else if (IsSendable && pending_.Count > 0)
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
                    FunDebug.Log("Compacting a receive buffer to save {0} bytes.", next_decoding_offset_);
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
                    FunDebug.Log("Increasing a receive buffer to {0} bytes.", receive_buffer_.Length + kUnitBufferSize);
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
                        FunDebug.LogWarning("Failed to decode body.");
                        FunDebug.Assert(false);
                    }
                }
                else
                {
                    FunDebug.LogWarning("Failed to decode header.");
                    FunDebug.Assert(false);
                }
            }
        }

        internal bool TryToDecodeHeader()
        {
            FunDebug.DebugLog("Trying to decode header fields.");

            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    FunDebug.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }

                string line = System.Text.Encoding.ASCII.GetString(receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    //FunDebug.DebugLog("End of header reached. Will decode body from now.");
                    return true;
                }

                FunDebug.DebugLog("> {0}", line);
                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                tuple[0] = tuple[0].ToUpper();
                //FunDebug.DebugLog("Decoded header field '{0}' => '{1}'", tuple[0], tuple[1]);
                FunDebug.Assert(tuple.Length == 2);
                header_fields_[tuple[0]] = tuple[1];
            }

            return false;
        }

        internal bool TryToDecodeBody()
        {
            // Header version
            FunDebug.Assert(header_fields_.ContainsKey(kVersionHeaderField));
            int version = Convert.ToUInt16(header_fields_[kVersionHeaderField]);
            FunDebug.Assert(version == FunapiVersion.kProtocolVersion);

            // Header length
            FunDebug.Assert(header_fields_.ContainsKey(kLengthHeaderField));
            int body_length = Convert.ToInt32(header_fields_[kLengthHeaderField]);
            FunDebug.DebugLog("We need {0} bytes for a message body. Buffer has {1} bytes.",
                                body_length, received_size_ - next_decoding_offset_);

            if (received_size_ - next_decoding_offset_ < body_length)
            {
                // Need more bytes.
                FunDebug.Log("We need more bytes for a message body. Waiting.");
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
                FunDebug.Assert(body_length == 0);

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

                    if (default_encryptor_ == kNoneEncryption && encryption_list.Count > 0)
                    {
                        default_encryptor_ = (int)encryption_list[0];
                        FunDebug.Log("Set default encryption: {0}", default_encryptor_);
                    }

                    // Create encryptors
                    foreach (EncryptionType type in encryption_list)
                    {
                        Encryptor encryptor = Encryptor.Create(type);
                        if (encryptor == null)
                        {
                            FunDebug.Log("Failed to create encryptor: {0}", type);
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
                        FunDebug.Log("Unknown encryption: {0}", encryption_str);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kHandshaking)
                    {
                        FunDebug.Log("Unexpected handshake message: {0}", encryptor.name);
                        return false;
                    }

                    string out_header = "";
                    if (!encryptor.Handshake(encryption_header, ref out_header))
                    {
                        FunDebug.Log("Encryption handshake failure: {0}", encryptor.name);
                        return false;
                    }

                    if (out_header.Length > 0)
                    {
                        // TODO: Implementation
                        FunDebug.Assert(false);
                    }
                    else
                    {
                        FunDebug.Assert(encryptor.state == Encryptor.State.kEstablished);
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
                    state_ = State.kConnected;
                    FunDebug.Log("Ready to receive.");

                    event_.Add(OnHandshakeComplete);
                }
            }

            if (body_length > 0)
            {
                if ((int)state_ < (int)State.kConnected)
                {
                    FunDebug.Log("Unexpected message. state:{0}", state_);
                    return false;
                }

                if ((encryptors_.Count == 0) != (encryption_str.Length == 0))
                {
                    FunDebug.Log("Unknown encryption: {0}", encryption_str);
                    return false;
                }

                if (encryptors_.Count > 0)
                {
                    EncryptionType encryption = (EncryptionType)Convert.ToInt32(encryption_str);
                    Encryptor encryptor = encryptors_[encryption];

                    if (encryptor == null)
                    {
                        FunDebug.Log("Unknown encryption: {0}", encryption_str);
                        return false;
                    }

                    ArraySegment<byte> body_bytes = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, body_length);
                    FunDebug.Assert(body_bytes.Count == body_length);

                    Int64 nSize = encryptor.Decrypt(body_bytes, body_bytes, encryption_header);
                    if (nSize <= 0)
                    {
                        FunDebug.Log("Failed to decrypt.");
                        return false;
                    }

                    // TODO: Implementation
                    FunDebug.Assert(body_length == nSize);
                }

                if (first_receiving_)
                {
                    first_receiving_ = false;
                    cstate_ = ConnectState.kConnected;

                    timer_.Remove(connect_timeout_id_);
                    connect_timeout_id_ = 0;
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
                if (Started && IsSendable)
                {
                    if (pending_.Count > 0)
                    {
                        FunDebug.DebugLog("Flushing pending messages.");
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
            FunDebug.Log("OnFailure({0}) - state: {1}\n{2}:{3}",
                           str_protocol, state_, last_error_code_, last_error_message_);

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
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // Encryption-related.
        internal int default_encryptor_ = kNoneEncryption;
        internal Dictionary<EncryptionType, Encryptor> encryptors_ = new Dictionary<EncryptionType, Encryptor>();

        // Message-related.
        private int connect_timeout_id_ = 0;
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
        protected override void StartConnect()
        {
            state_ = State.kConnecting;
            sock_ = new Socket(ip_af_, SocketType.Stream, ProtocolType.Tcp);
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
                FunDebug.Assert(host_info.AddressList.Length > 0);
                ip = host_info.AddressList[0];
            }

            ip_af_ = ip.AddressFamily;
            connect_ep_ = new IPEndPoint(ip, addr.port);
            FunDebug.Log("TCP transport - {0}:{1}", ip, addr.port);
        }

        protected override void WireSend()
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
            FunDebug.DebugLog("StartCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect. socket is null.";
                    FunDebug.Log(last_error_message_);
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    FunDebug.Log(last_error_message_);
                    event_.Add(OnFailure);
                    return;
                }
                FunDebug.Log("Connected.");

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
                FunDebug.DebugLog("BeginConnect operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kConnectFailed;
                last_error_message_ = "Failure in StartCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            FunDebug.DebugLog("SendBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    FunDebug.DebugLog(last_error_message_);
                    return;
                }

                int nSent = sock_.EndSend(ar);
                FunDebug.DebugLog("Sent {0}bytes", nSent);
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
                            FunDebug.Log("Partially sent. Will resume.");
                            break;
                        }
                        else
                        {
                            FunDebug.DebugLog("Discarding a fully sent message. ({0}bytes)",
                                                sending_[0].buffer.Count);

                            // fully sent.
                            nSent -= sending_[0].buffer.Count;
                            sending_.RemoveAt(0);
                        }
                    }

                    while (sending_.Count > 0 && sending_[0].buffer.Count <= 0)
                    {
                        FunDebug.DebugLog("Remove empty buffer.");
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

                    SendUnsentMessages();
                }
            }
            catch (ObjectDisposedException)
            {
                FunDebug.DebugLog("BeginSend operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            FunDebug.DebugLog("ReceiveBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    FunDebug.Log(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        FunDebug.DebugLog("Received {0} bytes. Buffer has {1} bytes.",
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
                        FunDebug.DebugLog("Ready to receive more. We can receive upto {0} more bytes",
                                            receive_buffer_.Length - received_size_);

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        FunDebug.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            FunDebug.Log("Buffer has {0} bytes. But they failed to decode. Discarding.",
                                           receive_buffer_.Length - received_size_);
                        }

                        last_error_code_ = ErrorCode.kDisconnected;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        FunDebug.Log(last_error_message_);
                        event_.Add(OnDisconnected);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                FunDebug.DebugLog("BeginReceive operation has been Cancelled.");
            }
            catch (NullReferenceException)
            {
                // When Stop is called Socket.EndReceive may return a NullReferenceException
                FunDebug.DebugLog("BeginReceive operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        internal Socket sock_;
        private AddressFamily ip_af_;
        private IPEndPoint connect_ep_;
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
        protected override void StartConnect()
        {
            state_ = State.kConnected;
            sock_ = new Socket(ip_af_, SocketType.Dgram, ProtocolType.Udp);
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
                FunDebug.Assert(host_info.AddressList.Length > 0);
                ip = host_info.AddressList[0];
            }

            ip_af_ = ip.AddressFamily;
            send_ep_ = new IPEndPoint(ip, addr.port);
            if (ip_af_ == AddressFamily.InterNetwork)
                receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, addr.port);
            else
                receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.IPv6Any, addr.port);
            FunDebug.Log("UDP transport - {0}:{1}", ip, addr.port);
        }

        // Send a packet.
        protected override void WireSend()
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
                    FunDebug.Log("Message is greater than 64KB. It will be truncated.");
                    FunDebug.Assert(false);
                }

                sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                  send_ep_, new AsyncCallback(this.SendBytesCb), this);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            FunDebug.DebugLog("SendBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    FunDebug.Log(last_error_message_);
                    return;
                }

                lock (sending_lock_)
                {
                    int nSent = sock_.EndSend(ar);
                    FunDebug.DebugLog("Sent {0}bytes", nSent);
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

                    SendUnsentMessages();
                }
            }
            catch (ObjectDisposedException)
            {
                FunDebug.DebugLog("BeginSendTo operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            FunDebug.DebugLog("ReceiveBytesCb called.");

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    FunDebug.Log(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        FunDebug.DebugLog("Received {0} bytes. Buffer has {1} bytes.",
                                            nRead, received_size_ - next_decoding_offset_);
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
                        FunDebug.DebugLog("Ready to receive more. We can receive upto {0} more bytes", receive_buffer_.Length);

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        FunDebug.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            FunDebug.Log("Buffer has {0} bytes. But they failed to decode. Discarding.",
                                           receive_buffer_.Length - received_size_);
                        }

                        last_error_code_ = ErrorCode.kDisconnected;
                        last_error_message_ = "Can not receive messages. Maybe the socket is closed.";
                        FunDebug.Log(last_error_message_);
                        event_.Add(OnFailure);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                FunDebug.DebugLog("BeginReceiveFrom operation has been Cancelled.");
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }


        internal Socket sock_;
        private AddressFamily ip_af_;
        private IPEndPoint send_ep_;
        private EndPoint receive_ep_;
    }


    // HTTP transport layer
    public class FunapiHttpTransport : FunapiEncryptedTransport
    {
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port, bool https, FunEncoding type)
        {
            protocol_ = TransportProtocol.kHttp;
            str_protocol = "Http";
            encoding_ = type;
            RequestTimeout = kTimeoutSeconds;

            ip_list_.Add(hostname_or_ip, port, https);
            SetNextAddress();

            if (https)
            {
#if !NO_UNITY
                MozRoots.LoadRootCertificates();
#endif
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallback;
            }
        }

        internal MonoBehaviour mono { set; private get; }

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

        public float RequestTimeout
        {
            set; get;
        }

        protected override void StartConnect()
        {
            state_ = State.kConnected;
            str_cookie_ = "";

            OnStartedInternal();
        }

        internal override void SetAddress (HostAddr addr)
        {
            FunDebug.Assert(addr is HostHttp);
            HostHttp http = (HostHttp)addr;

            // Url
            host_url_ = string.Format("{0}://{1}:{2}/v{3}/",
                                      (http.https ? "https" : "http"), http.host, http.port,
                                      FunapiVersion.kProtocolVersion);

            FunDebug.Log("HTTP transport - {0}:{1}", http.host, http.port);
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

        protected override void WireSend()
        {
            FunDebug.DebugLog("Send a Message.");

            try
            {
                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count >= 2);
                    FunDebug.DebugLog("Host Url: {0}", host_url_);

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
                    FunDebug.DebugLog("Set http request timeout - msg_type:{0} time:{1}",
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
                last_error_message_ = "Failure in WireSend: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

#if !NO_UNITY
        private void SendWWWRequest (Dictionary<string, string> headers, FunapiMessage body)
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

        private void SendHttpWebRequest (Dictionary<string, string> headers, FunapiMessage body)
        {
            // Request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
            request.ConnectionGroupName = session_id_;
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
                        FunDebug.DebugLog("Set Cookie : {0}", str_cookie_);
                        break;
                    case "content-length":
                        headers.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter, value, kHeaderDelimeter);
                        break;
                    case "x-ifun-enc":
                        headers.AppendFormat("{0}{1}{2}{3}", kEncryptionHeaderField, kHeaderFieldDelimeter, value, kHeaderDelimeter);
                        break;
                    default:
                        headers.AppendFormat("{0}{1}{2}{3}", tuple[0], kHeaderFieldDelimeter, value, kHeaderDelimeter);
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
            FunDebug.DebugLog("RequestStreamCb called.");

            try
            {
                WebState ws = (WebState)ar.AsyncState;
                HttpWebRequest request = ws.request;

                Stream stream = request.EndGetRequestStream(ar);
                stream.Write(ws.sending.Array, 0, ws.sending.Count);
                stream.Close();
                FunDebug.DebugLog("Sent {0}bytes.", ws.sending.Count);

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    sending_.RemoveAt(0);
                    sending_.RemoveAt(0);
                }

                request.BeginGetResponse(new AsyncCallback(ResponseCb), ws);
            }
            catch (WebException e)
            {
                // When Stop is called HttpWebRequest.EndGetRequestStream may return a Exception
                FunDebug.DebugLog("Http request operation has been Cancelled.");
                FunDebug.DebugLog(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kSendFailed;
                last_error_message_ = "Failure in RequestStreamCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        private void ResponseCb (IAsyncResult ar)
        {
            FunDebug.DebugLog("ResponseCb called.");

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
                    FunDebug.Log("Failed response. status:{0}", response.StatusDescription);
                    event_.Add(OnFailure);
                }
            }
            catch (WebException e)
            {
                // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                FunDebug.DebugLog("Http request operation has been Cancelled.");
                FunDebug.DebugLog(e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kReceiveFailed;
                last_error_message_ = "Failure in ResponseCb: " + e.ToString();
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
            }
        }

        private void ReadCb (IAsyncResult ar)
        {
            FunDebug.DebugLog("ReadCb called.");

            try
            {
                WebState ws = (WebState)ar.AsyncState;
                int nRead = ws.stream.EndRead(ar);

                if (nRead > 0)
                {
                    FunDebug.DebugLog("We need more bytes for response. Waiting.");
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
                        FunDebug.LogWarning("Response instance is null.");
                        event_.Add(OnFailure);
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
                FunDebug.Log(last_error_message_);
                event_.Add(OnFailure);
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

                DecodeMessage(headers.ToString(), new ArraySegment<byte>(www.bytes));

                ClearRequest();

                // Sends unsent messages
                SendUnsentMessages();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in WWWPost: " + e.ToString();
                FunDebug.Log(last_error_message_);
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

            timer_.Remove(request_timeout_id_);
            request_timeout_id_ = 0;
        }

        private void OnRequestTimeout (string msg_type)
        {
            last_error_code_ = ErrorCode.kRequestTimeout;
            last_error_message_ = string.Format("Http Request timeout - msg_type:{0}", msg_type);
            FunDebug.Log(last_error_message_);
            OnFailure();
        }

        internal override void OnFailure ()
        {
            CancelRequest();
            base.OnFailure();
        }

        private static bool CertificateValidationCallback (System.Object sender, X509Certificate certificate,
                                                           X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
#if !NO_UNITY
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            for (int i = 0; i < chain.ChainStatus.Length; ++i)
            {
                if (chain.ChainStatus[i].Status == X509ChainStatusFlags.RevocationStatusUnknown)
                {
                    continue;
                }
                else if (chain.ChainStatus[i].Status == X509ChainStatusFlags.UntrustedRoot)
                {
                    if (!MozRoots.CheckRootCertificate(chain))
                        return false;
                    else
                        continue;
                }
                else
                {
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                    if (!chain.Build((X509Certificate2)certificate))
                        return false;
                }
            }
#endif

            return true;
        }


        // Funapi header-related constants.
        private static readonly string kEncryptionHttpHeaderField = "X-iFun-Enc";
        private static readonly string kCookieHeaderField = "Cookie";

        private static readonly string[] kHeaderSeparator = { kHeaderFieldDelimeter, kHeaderDelimeter };

        // waiting time for response
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
        private int request_timeout_id_ = 0;

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
    internal delegate void TransportReceivedHandler(FunapiMessage message);

}  // namespace Fun
