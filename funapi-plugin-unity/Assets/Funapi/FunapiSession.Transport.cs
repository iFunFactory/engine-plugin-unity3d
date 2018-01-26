// Copyright 2013 iFunFactory Inc. All Rights Reserved.
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

    public enum TransportEventType
    {
        kStarted,
        kStopped,
        kReconnecting
    };

    public class TransportError
    {
        public enum Type
        {
            kNone,
            kStartingFailed,
            kConnectionTimeout,
            kEncryptionFailed,
            kSendingFailed,
            kReceivingFailed,
            kRequestFailed,
            kDisconnected
        }

        public Type type = Type.kNone;
        public string message = null;
    }

    // Transport options
    public class TransportOption
    {
        public EncryptionType Encryption = EncryptionType.kDefaultEncryption;
        public FunCompressionType CompressionType = FunCompressionType.kNone;
        public bool ReliableTransport = false;
        public bool SequenceValidation = false;
        public float ConnectionTimeout = 0f;

        public override bool Equals (object obj)
        {
            if (obj == null || !(obj is TransportOption))
                return false;

            TransportOption option = obj as TransportOption;

            return Encryption == option.Encryption &&
                   CompressionType == option.CompressionType &&
                   ConnectionTimeout == option.ConnectionTimeout &&
                   SequenceValidation == option.SequenceValidation;
        }

        public override int GetHashCode ()
        {
            return base.GetHashCode ();
        }
    }

    public class TcpTransportOption : TransportOption
    {
        public bool AutoReconnect = false;
        public bool DisableNagle = false;
        public bool EnablePing = false;
        public bool EnablePingLog = false;
        public int PingIntervalSeconds = 0;
        public float PingTimeoutSeconds = 0f;

        public void SetPing (int interval, float timeout, bool enable_log = false)
        {
            EnablePing = true;
            EnablePingLog = enable_log;
            PingIntervalSeconds = interval;
            PingTimeoutSeconds = timeout;
        }

        public override bool Equals (object obj)
        {
            if (obj == null || !base.Equals(obj) || !(obj is TcpTransportOption))
                return false;

            TcpTransportOption option = obj as TcpTransportOption;

            return AutoReconnect == option.AutoReconnect &&
                   DisableNagle == option.DisableNagle &&
                   EnablePing == option.EnablePing &&
                   EnablePingLog == option.EnablePingLog &&
                   PingIntervalSeconds == option.PingIntervalSeconds &&
                   PingTimeoutSeconds == option.PingTimeoutSeconds;
        }

        public override int GetHashCode ()
        {
            return base.GetHashCode ();
        }
    }

    public class HttpTransportOption : TransportOption
    {
        public bool HTTPS = false;
        public bool UseWWW = false;

        public override bool Equals (object obj)
        {
            if (obj == null || !base.Equals(obj) || !(obj is HttpTransportOption))
                return false;

            HttpTransportOption option = obj as HttpTransportOption;

            return HTTPS == option.HTTPS && UseWWW == option.UseWWW;
        }

        public override int GetHashCode ()
        {
            return base.GetHashCode ();
        }
    }


    public partial class FunapiSession
    {
        // Abstract class to represent Transport used by Funapi
        // TCP, UDP, and HTTP.
        public abstract class Transport : FunapiEncryptor
        {
            public Transport ()
            {
                state_ = State.kUnknown;
            }

            public void Init ()
            {
                if (option_ == null)
                    return;

                if (option_.CompressionType != FunCompressionType.kNone)
                {
                    compression_type_ = option_.CompressionType;

                    if (this.CreateCompressorCallback != null)
                        compressor_ = this.CreateCompressorCallback(protocol_);

                    if (compressor_ == null)
                    {
                        if (compression_type_ == FunCompressionType.kZstd)
                            compressor_ = new FunapiZstdCompressor();
                        else if (compression_type_ == FunCompressionType.kDeflate)
                            compressor_ = new FunapiDeflateCompressor();
                    }
                }
            }

            // Starts transport
            public void Start ()
            {
                try
                {
                    if (state_ != State.kUnknown)
                    {
                        LogWarning("{0} Transport.Start() called, but the state is {1}.",
                                   str_protocol_, state_);
                        return;
                    }

                    DebugLog1("{0} starting transport.", str_protocol_);
                    setConnectionTimeout();

                    onStart();
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kStartingFailed;
                    last_error_message_ = string.Format("{0} failure in Start: {1}", str_protocol_, e.ToString());
                    event_.Add(onFailure);
                }
            }

            // Stops transport
            public void Stop ()
            {
                if (state_ == State.kUnknown)
                    return;

                DebugLog1("{0} stopping transport. (state:{1})", str_protocol_, state_);

                state_ = State.kUnknown;
                cstate_ = ConnectState.kUnknown;

                timer_.Clear();
                event_.Clear();
                connect_timer_id_ = 0;
                exponential_time_ = 0f;

                stopPingTimer();

                onClose();

                if (!IsReliable)
                {
                    lock (sending_lock_)
                        pending_.Clear();
                }

                lock (session_id_sent_lock_)
                    session_id_has_been_sent = false;

                resetEncryptors();

                onTransportEventCallback(TransportEventType.kStopped);
            }

            public void SetEstablish (SessionId sid)
            {
                if (state_ == State.kEstablished)
                    return;

                state_ = State.kEstablished;

                if (!session_id_.Equals(sid))
                {
                    session_id_.SetId(sid);

                    if (IsReliable || IsSendingSequence)
                    {
                        System.Random rnd = new System.Random();
                        seq_ = (UInt32)rnd.Next() + (UInt32)rnd.Next();
                    }
                }

                if (seq_ == 0)
                    DebugLog1("{0} has set to Establish.", str_protocol_);
                else
                    DebugLog1("{0} has set to Establish. (seq:{1})", str_protocol_, seq_);

                if (enable_ping_)
                    startPingTimer();

                if (IsReliable && delayed_ack_interval_ > 0f)
                {
                    timer_.Add(onDelayedAckEvent, true, delayed_ack_interval_);
                    Log("{0} sets delayed ack timer - interval: {1}s", str_protocol_, delayed_ack_interval_);
                }

                sendUnsentMessages();

                if (Connected && isSendable)
                    sendPendingMessages();
            }

            public void SetAbolish ()
            {
                session_id_.Clear();

                last_seq_ = 0;
                sent_ack_ = 0;
                first_seq_ = true;

                lock (sending_lock_)
                {
                    pending_.Clear();
                    sent_queue_.Clear();
                    unsent_queue_.Clear();
                }
            }

            // Sends a message
            public void SendMessage (FunapiMessage msg, bool sendingFirst = false)
            {
                if (!sendingFirst && state_ != State.kEstablished)
                {
                    lock (sending_lock_)
                    {
                        unsent_queue_.Enqueue(msg);
                        Log("{0} - '{1}' message queued. state:{2}", str_protocol_, msg.msg_type, state_);
                    }
                }
                else
                {
                    sendMessage(msg, sendingFirst);
                }
            }

            // Update
            public void Update (float deltaTime)
            {
                // Events
                event_.Update(deltaTime);
                timer_.Update(deltaTime);
            }


            //
            // Properties
            //
            public abstract HostAddr address { get; }

            public TransportProtocol protocol
            {
                get { return protocol_; }
            }

            public string str_protocol
            {
                get { return str_protocol_; }
            }

            public FunEncoding encoding
            {
                get { return encoding_; }
            }

            public TransportOption option
            {
                get { return option_; }
                set { option_ = value; applyOption(option_); }
            }

            public State state
            {
                get { return state_; }
                set { state_ = value; }
            }

            public bool IsReliable
            {
                get { return option_.ReliableTransport; }
            }

            public bool IsSendingSequence
            {
                get { return option_.SequenceValidation; }
            }

            public bool IsStandby
            {
                get { return state_ == State.kStandby; }
            }

            public bool IsEstablished
            {
                get { return state_ == State.kEstablished; }
            }

            public bool sendSessionIdOnlyOnce
            {
                set { send_session_id_only_once_ = value; }
            }

            public float delayedAckInterval
            {
                set { delayed_ack_interval_ = value; }
            }

            public abstract bool Connected { get; }

            public bool Connecting
            {
                get { return state_ != State.kUnknown && state_ != State.kEstablished; }
            }

            public bool Reconnecting
            {
                get { return cstate_ == ConnectState.kReconnecting; }
            }

            public bool InProcess
            {
                get
                {
                    // Waiting for connecting.
                    if (state_ == State.kConnecting)
                        return true;

                    // Waiting for unsent messages.
                    if (protocol_ == TransportProtocol.kTcp)
                    {
                        if (Connected && HasUnsentMessages)
                            return true;
                    }

                    return false;
                }
            }

            // If the transport has unsent messages..
            public bool HasUnsentMessages
            {
                get
                {
                    lock (sending_lock_)
                    {
                        return first_.Count > 0 || sending_.Count > 0 || pending_.Count > 0;
                    }
                }
            }

            public bool SendSessionId
            {
                set
                {
                    lock (session_id_sent_lock_)
                        session_id_has_been_sent = !value;
                }
            }

            // ping time in milliseconds
            public int PingTime
            {
                get { return ping_time_; }
            }

            public TransportError.Type LastErrorCode
            {
                get { return last_error_code_; }
            }

            public string LastErrorMessage
            {
                get { return last_error_message_; }
            }


            public void OnPaused (bool paused)
            {
                is_paused_ = paused;

                if (paused)
                {
                    if (enable_ping_)
                        stopPingTimer();
                }
                else
                {
                    if (enable_ping_)
                        startPingTimer();

                    if (Connected && isSendable)
                        sendPendingMessages();
                }
            }

            public void ForcedDisconnect()
            {
                onClose();

                last_error_code_ = TransportError.Type.kDisconnected;
                last_error_message_ = string.Format("{0} forcibly closed the connection for testing.", str_protocol_);
                event_.Add(onDisconnected);
            }

            // Creates a socket.
            protected virtual void onStart ()
            {
                // Resets buffer.
                lock (sending_lock_)
                {
                    first_.Clear();
                    sending_.Clear();
                }

                lock (receive_lock_)
                {
                    first_message_ = true;
                    header_decoded_ = false;
                    received_size_ = 0;
                    next_decoding_offset_ = 0;
                    header_fields_.Clear();
                }

                last_error_code_ = TransportError.Type.kNone;
                last_error_message_ = "";
            }

            // Closes a socket
            protected abstract void onClose ();

            // Sends a packet.
            protected abstract void wireSend ();

            // Is able to sending?
            protected virtual bool isSendable
            {
                get
                {
                    lock (sending_lock_)
                    {
                        if (is_paused_)
                            return false;

                        if (sending_.Count > 0)
                            return false;

                        return true;
                    }
                }
            }

            void applyOption (TransportOption opt)
            {
                if (protocol_ == TransportProtocol.kTcp)
                {
                    TcpTransportOption tcp_option = opt as TcpTransportOption;
                    auto_reconnect_ = tcp_option.AutoReconnect;
                    enable_ping_ = tcp_option.EnablePing;
                    enable_ping_log_ = tcp_option.EnablePingLog;
                    ping_interval_ = tcp_option.PingIntervalSeconds;
                    ping_timeout_ = tcp_option.PingTimeoutSeconds;
                }

                if (opt.Encryption != EncryptionType.kDefaultEncryption)
                {
                    setEncryption(opt.Encryption);
                    Log("{0} encrypt type: {1}", str_protocol_, convertString(opt.Encryption));
                }
            }

            void setConnectionTimeout ()
            {
                if (option_.ConnectionTimeout <= 0f)
                    return;

                timer_.Remove(connect_timer_id_);
                connect_timer_id_ = timer_.Add(onConnectionTimedout, option_.ConnectionTimeout);

                DebugLog1("{0} sets connection timeout - id:{1} timeout:{2}.",
                          str_protocol_, connect_timer_id_, option_.ConnectionTimeout);
            }

            void resetConnectionTimeout ()
            {
                if (option_.ConnectionTimeout <= 0f)
                    return;

                timer_.Remove(connect_timer_id_);
                connect_timer_id_ = 0;
            }


            //---------------------------------------------------------------------
            // Transport event
            //---------------------------------------------------------------------
            protected void onStarted ()
            {
                if (session_id_.IsValid && IsReliable)
                {
                    state_ = State.kWaitForAck;

                    if (last_seq_ != 0)
                        sendAck(last_seq_ + 1, true);
                    else
                        sendEmptyMessage();
                }
                else
                {
                    onStandby();
                }
            }

            void onStandby ()
            {
                state_ = State.kStandby;

                sendPendingMessages();
                onTransportEventCallback(TransportEventType.kStarted);
            }

            void onConnectionTimedout ()
            {
                if (state_ == State.kUnknown || state_ == State.kEstablished)
                    return;

                last_error_code_ = TransportError.Type.kConnectionTimeout;
                last_error_message_ = string.Format("{0} Connection waiting time has been exceeded.", str_protocol_);
                LogWarning(last_error_message_);

                Stop();
            }

            protected void onDisconnected ()
            {
                LogWarning("{0} disconnected - state: {1}, error: {2}\n{3}\n",
                           str_protocol_, state_, last_error_code_, last_error_message_);

                if (checkAutoReconnect())
                    return;

                Stop();
            }

            protected virtual void onFailure ()
            {
                if (state_ != State.kEstablished)
                {
                    if (checkAutoReconnect())
                    {
                        Log("{0} connection failed. will try to connect again. (state: {1}, error: {2})\n{3}\n",
                            str_protocol_, state_, last_error_code_, last_error_message_);
                        return;
                    }
                }

                LogWarning("{0} error occurred - state: {1}, error: {2}\n{3}\n",
                           str_protocol_, state_, last_error_code_, last_error_message_);

                if (state_ != State.kEstablished)
                {
                    Stop();
                }
                else
                {
                    onTransportErrorCallback(last_error_code_, last_error_message_);
                }
            }

            void onTransportEventCallback (TransportEventType type)
            {
                if (EventCallback != null)
                    EventCallback(protocol_, type);
            }

            void onTransportErrorCallback (TransportError.Type type, string message)
            {
                if (ErrorCallback != null)
                {
                    TransportError error = new TransportError();
                    error.type = type;
                    error.message = message;

                    ErrorCallback(protocol_, error);
                }
            }


            //---------------------------------------------------------------------
            // auto-reconnect-related functions
            //---------------------------------------------------------------------
            bool checkAutoReconnect ()
            {
                if (!auto_reconnect_)
                    return false;

                if (cstate_ != ConnectState.kReconnecting)
                {
                    cstate_ = ConnectState.kReconnecting;
                    exponential_time_ = 1f;

                    if (state_ == State.kEstablished)
                        setConnectionTimeout();

                    onTransportEventCallback(TransportEventType.kReconnecting);
                }

                event_.Add(onReconnecting);
                return true;
            }

            void onReconnecting ()
            {
                // 1, 2, 4, 8, 8, 8,...
                float delay_time = exponential_time_;
                if (exponential_time_ < 8f)
                    exponential_time_ *= 2f;

                Log("Wait {0} seconds for reconnect to {1} transport.", delay_time, str_protocol_);

                event_.Add (delegate
                    {
                        Log("'{0}' Try to reconnect to server.", str_protocol_);
                        onStart();
                    },
                    delay_time
                );
            }


            //---------------------------------------------------------------------
            // Sending-related functions
            //---------------------------------------------------------------------
            void sendMessage (FunapiMessage msg, bool sendingFirst = false)
            {
                try
                {
                    // Adds to pending buffer...
                    lock (sending_lock_)
                    {
                        if (sendingFirst)
                        {
                            first_.Add(msg);
                            DebugLog3("{0} adds '{1}' message to first list.", str_protocol_, msg.msg_type);
                        }
                        else
                        {
                            pending_.Add(msg);
                            DebugLog3("{0} adds '{1}' message to pending list.", str_protocol_, msg.msg_type);
                        }

                        if (Connected && isSendable)
                            sendPendingMessages();
                    }
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = string.Format("{0} failure in sendMessage: {1}",
                                                        str_protocol_, e.ToString());
                    event_.Add(onFailure);
                }
            }

            void sendEmptyMessage ()
            {
                DebugLog1("{0} sending a empty message.", str_protocol_);

                if (encoding_ == FunEncoding.kJson)
                {
                    sendMessage(new FunapiMessage(protocol_, kEmptyMessageType, FunapiMessage.Deserialize("{}")), true);
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    sendMessage(new FunapiMessage(protocol_, kEmptyMessageType, new FunMessage()), true);
                }
            }

            void sendAck (UInt32 ack, bool sendingFirst = false)
            {
                DebugLog1("{0} sending a ack - {1}", str_protocol_, ack);

                if (encoding_ == FunEncoding.kJson)
                {
                    object message = FunapiMessage.Deserialize("{}");
                    json_helper_.SetIntegerField(message, kAckNumberField, ack);
                    sendMessage(new FunapiMessage(protocol_, kAckNumberField, message), sendingFirst);
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    FunMessage message = new FunMessage();
                    message.ack = ack;
                    sendMessage(new FunapiMessage(protocol_, kAckNumberField, message), sendingFirst);
                }

                sent_ack_ = ack;
            }

            void sendUnsentMessages ()
            {
                lock (sending_lock_)
                {
                    if (unsent_queue_.Count <= 0)
                        return;

                    DebugLog1("{0} has {1} unsent messages.", str_protocol_, unsent_queue_.Count);

                    foreach (FunapiMessage msg in unsent_queue_)
                    {
                        DebugLog1("{0} sending a unsent message - '{1}'", str_protocol_, msg.msg_type);
                        sendMessage(msg);
                    }

                    unsent_queue_.Clear();
                }
            }

            void onDelayedAckEvent ()
            {
                if (sent_ack_ < (last_seq_ + 1))
                {
                    sendAck(last_seq_ + 1);
                }
            }


            //---------------------------------------------------------------------
            // Session-reliability-related functions
            //---------------------------------------------------------------------
            bool onSeqReceived (UInt32 seq)
            {
                DebugLog1("{0} received sequence number - {1}", str_protocol_, seq);

                if (first_seq_)
                {
                    first_seq_ = false;
                }
                else
                {
                    if (!seqLess(last_seq_, seq))
                    {
                        LogWarning("Last sequence number is {0} but {1} received. Skipping message.", last_seq_, seq);
                        return false;
                    }
                    else if (seq != last_seq_ + 1)
                    {
                        string message = string.Format("Received wrong sequence number {0}. {1} expected.", seq, last_seq_ + 1);
                        LogWarning(message);

                        Stop();
                        return false;
                    }
                }

                last_seq_ = seq;

                if (delayed_ack_interval_ <= 0f)
                    sendAck(last_seq_ + 1);

                return true;
            }

            void onAckReceived (UInt32 ack)
            {
                if (!Connected)
                    return;

                DebugLog1("{0} Received ack number - {1}", str_protocol_, ack);

                lock (sending_lock_)
                {
                    if (sent_queue_.Count > 0)
                        DebugLog1("The send queue has {0} messages.", sent_queue_.Count);

                    while (sent_queue_.Count > 0)
                    {
                        FunapiMessage msg = sent_queue_.Peek();

                        if (seqLess(msg.seq, ack))
                        {
                            sent_queue_.Dequeue();
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (state_ == State.kWaitForAck)
                    {
                        if (sent_queue_.Count > 0)
                        {
                            foreach (FunapiMessage msg in sent_queue_)
                            {
                                if (msg.seq == ack || seqLess(ack, msg.seq))
                                {
                                    sendMessage(msg);
                                    Log("{0} resending '{1}' message. (seq:{2})", str_protocol_, msg.msg_type, msg.seq);
                                }
                                else
                                {
                                    LogWarning("onAckReceived({0}) - wrong sequence number {1}. ", ack, msg.seq);
                                }
                            }

                            Log("Resending {0} messages.", sent_queue_.Count);
                        }

                        onStandby();
                    }
                }
            }

            // Makes sequence-number
            UInt32 getNextSeq ()
            {
                return ++seq_;
            }

            // Serial-number arithmetic
            static bool seqLess (UInt32 x, UInt32 y)
            {
                // 아래 참고
                //  - http://en.wikipedia.org/wiki/Serial_number_arithmetic
                //  - RFC 1982
                return (Int32)(y - x) > 0;
            }


            //---------------------------------------------------------------------
            // Message-related functions
            //---------------------------------------------------------------------
            void makeHeader (FunapiMessage msg, int uncompressed_size)
            {
                EncryptionType enc_type = getEncryption(msg);

                // Adds header
                StringBuilder header = new StringBuilder();
                header.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter,
                                    FunapiVersion.kProtocolVersion, kHeaderDelimeter);
                if (first_sending_)
                {
                    header.AppendFormat("{0}{1}{2}{3}", kPluginVersionHeaderField, kHeaderFieldDelimeter,
                                        FunapiVersion.kPluginVersion, kHeaderDelimeter);
                    first_sending_ = false;
                }
                header.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField, kHeaderFieldDelimeter,
                                    msg.body.Count, kHeaderDelimeter);
                if (enc_type != EncryptionType.kNoneEncryption)
                {
                    header.AppendFormat("{0}{1}{2}-{3}{4}", kEncryptionHeaderField, kHeaderFieldDelimeter,
                                        Convert.ToInt32(enc_type), msg.enc_header, kHeaderDelimeter);
                }
                if (uncompressed_size > 0)
                {
                    header.AppendFormat("{0}{1}{2}{3}", kUncompressedLengthHeaderField, kHeaderFieldDelimeter,
                                        uncompressed_size, kHeaderDelimeter);
                }
                header.Append(kHeaderDelimeter);

                msg.header = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(header.ToString()));
            }

            bool buildMessage (FunapiMessage msg)
            {
                if (msg.ready)
                    return true;

                UInt32 ack = 0;

                if (msg.message != null)
                {
                    // Adds session id
                    if (session_id_.IsValid)
                    {
                        bool send_session_id = false;
                        lock (session_id_sent_lock_)
                        {
                            send_session_id = protocol_ == TransportProtocol.kHttp ||
                                              !send_session_id_only_once_ || !session_id_has_been_sent;
                        }

                        if (send_session_id)
                        {
                            if (encoding_ == FunEncoding.kJson)
                            {
                                json_helper_.SetStringField(msg.message, kSessionIdField, session_id_);
                            }
                            else if (encoding_ == FunEncoding.kProtobuf)
                            {
                                FunMessage proto = msg.message as FunMessage;
                                proto.sid = session_id_;
                            }
                        }
                    }

                    if (msg.msg_type != null &&
                        msg.msg_type != kAckNumberField && msg.msg_type != kEmptyMessageType)
                    {
                        // Adds message type
                        if (encoding_ == FunEncoding.kJson)
                        {
                            json_helper_.SetStringField(msg.message, kMessageTypeField, msg.msg_type);
                        }
                        else if (encoding_ == FunEncoding.kProtobuf)
                        {
                            FunMessage proto = msg.message as FunMessage;

                            if (msg.msg_type.Contains(kIntMessageType))
                                proto.msgtype2 = Convert.ToInt32(msg.msg_type.Substring(kIntMessageType.Length));
                            else
                                proto.msgtype = msg.msg_type;
                        }

                        // Adds sequence number & ack number
                        if (IsReliable || IsSendingSequence)
                        {
                            if (msg.msg_type != kServerPingMessageType && msg.msg_type != kClientPingMessageType)
                            {
                                msg.seq = getNextSeq();

                                if (encoding_ == FunEncoding.kJson)
                                {
                                    json_helper_.SetIntegerField(msg.message, kSeqNumberField, msg.seq);
                                }
                                else if (encoding_ == FunEncoding.kProtobuf)
                                {
                                    FunMessage proto = msg.message as FunMessage;
                                    proto.seq = msg.seq;
                                }

                                if (IsReliable && sent_ack_ < (last_seq_ + 1))
                                {
                                    ack = last_seq_ + 1;
                                    if (encoding_ == FunEncoding.kJson)
                                    {
                                        json_helper_.SetIntegerField(msg.message, kAckNumberField, ack);
                                    }
                                    else if (encoding_ == FunEncoding.kProtobuf)
                                    {
                                        FunMessage proto = msg.message as FunMessage;
                                        proto.ack = ack;
                                    }

                                    sent_ack_ = ack;
                                }

                                sent_queue_.Enqueue(msg);
                            }
                        }
                    }
                }

                // Serializes message
                msg.body = new ArraySegment<byte>(msg.GetBytes(encoding_));

                // Compress the message
                int uncompressed_size = 0;
                if (compressor_ != null && msg.body.Count >= compressor_.compression_threshold)
                {
                    ArraySegment<byte> compressed = compressor_.Compress(msg.body);
                    if (compressed.Count > 0)
                    {
                        DebugLog3("{0} compression successed: {1}bytes -> {2}bytes",
                                  str_protocol_, msg.body.Count, compressed.Count);

                        uncompressed_size = msg.body.Count;
                        msg.body = compressed;
                    }
                }

                // Encrypt message
                EncryptionType enc_type = getEncryption(msg);
                if (msg.msg_type == kEncryptionPublicKey)
                {
                    string enc_key = generatePublicKey(enc_type);
                    if (enc_key == null)
                        return false;

                    msg.enc_header = enc_key;
                }
                else if (enc_type != EncryptionType.kNoneEncryption)
                {
                    if (!encryptMessage(msg, enc_type))
                    {
                        last_error_code_ = TransportError.Type.kEncryptionFailed;
                        last_error_message_ = string.Format("Message encryption failed. type:{0}", (int)enc_type);
                        event_.Add(onFailure);
                        return false;
                    }
                }

                makeHeader(msg, uncompressed_size);

                msg.ready = true;

                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("{0} built a message - '{1}' ({2} + {3} bytes)",
                                    str_protocol_, msg.msg_type, msg.header.Count, msg.body.Count);
                if (msg.seq > 0) strlog.AppendFormat(" (seq : {0})", msg.seq);
                if (ack > 0) strlog.AppendFormat(" (ack : {0})", ack);
                DebugLog3(strlog.ToString());

                return true;
            }

            bool rebuildMessage (FunapiMessage msg)
            {
                // Encrypt message
                EncryptionType enc_type = getEncryption(msg);
                if (enc_type != EncryptionType.kNoneEncryption)
                {
                    // Serializes message
                    msg.body = new ArraySegment<byte>(msg.GetBytes(encoding_));
                    int uncompressed_size = 0;
                    if (compressor_ != null && msg.body.Count >= compressor_.compression_threshold)
                    {
                        ArraySegment<byte> compressed = compressor_.Compress(msg.body);
                        if (compressed.Count > 0)
                        {
                            uncompressed_size = msg.body.Count;
                            msg.body = compressed;
                        }
                    }

                    if (!encryptMessage(msg, enc_type))
                    {
                        last_error_code_ = TransportError.Type.kEncryptionFailed;
                        last_error_message_ = string.Format("Message encryption failed. type:{0}", (int)enc_type);
                        event_.Add(onFailure);
                        return false;
                    }

                    makeHeader(msg, uncompressed_size);

                    StringBuilder strlog = new StringBuilder();
                    strlog.AppendFormat("{0} rebuilt a message - '{1}' ({2} + {3} bytes)",
                                        str_protocol_, msg.msg_type, msg.header.Count, msg.body.Count);
                    if (msg.seq > 0) strlog.AppendFormat(" (seq : {0})", msg.seq);
                    DebugLog3(strlog.ToString());
                }

                return true;
            }

            void sendPendingMessages ()
            {
                try
                {
                    lock (sending_lock_)
                    {
                        if (isSendable && (first_.Count > 0 || pending_.Count > 0))
                        {
                            // Otherwise, try to process pending messages.
                            List<FunapiMessage> tmp = sending_;
                            if (first_.Count > 0)
                            {
                                sending_ = first_;
                                first_ = tmp;
                            }
                            else
                            {
                                if (!session_id_.IsValid)
                                    return;

                                sending_ = pending_;
                                pending_ = tmp;
                            }

                            foreach (FunapiMessage msg in sending_)
                            {
                                if (!msg.ready)
                                    buildMessage(msg);
                                else
                                    rebuildMessage(msg);
                            }

                            wireSend();
                        }
                    }
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = string.Format("{0} failure in sendPendingMessages: {1}",
                                                        str_protocol_, e.ToString());
                    event_.Add(onFailure);
                }
            }

            protected void checkPendingMessages ()
            {
                lock (sending_lock_)
                {
                    if (sending_.Count > 0)
                    {
                        DebugLog1("{0} continues to send unsent messages. ({1} remaining messages)",
                                  str_protocol_, sending_.Count);

                        wireSend();
                    }
                    else if (isSendable)
                    {
                        sendPendingMessages();
                    }
                }
            }

            // Checking the buffer space before starting another async receive.
            protected void checkReceiveBuffer (int additional_size = 0)
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
                    // fit in the receive buffer boundary.
                    DebugLog3("{0} compacting the receive buffer to save {1} bytes.",
                              str_protocol_, next_decoding_offset_);
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0,
                                     received_size_ - next_decoding_offset_);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    DebugLog3("{0} increasing the receive buffer to {1} bytes.", str_protocol_, new_length);
                    Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                    receive_buffer_ = new_buffer;
                }
            }

            // Decoding a messages
            protected void tryToDecodeMessage ()
            {
                if (protocol_ == TransportProtocol.kTcp)
                {
                    // Try to decode as many messages as possible.
                    while (true)
                    {
                        if (!header_decoded_)
                        {
                            if (!tryToDecodeHeader())
                                break;
                        }

                        if (header_decoded_)
                        {
                            if (!tryToDecodeBody())
                                break;
                        }
                    }
                }
                else
                {
                    // Try to decode a message.
                    if (tryToDecodeHeader())
                    {
                        if (!tryToDecodeBody())
                        {
                            LogError("{0} failed to decode body.", str_protocol_);
                        }
                    }
                    else
                    {
                        LogError("{0} failed to decode header.", str_protocol_);
                    }
                }
            }

            bool tryToDecodeHeader ()
            {
                DebugLog3("{0} trying to decode header fields.", str_protocol_);
                int length = 0;

                for (; next_decoding_offset_ < received_size_; )
                {
                    ArraySegment<byte> haystack = new ArraySegment<byte>(
                        receive_buffer_, next_decoding_offset_, received_size_ - next_decoding_offset_);
                    int offset = bytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                    if (offset < 0)
                    {
                        // Not enough bytes. Wait for more bytes to come.
                        DebugLog3("{0} need more bytes for a header field. Waiting.", str_protocol_);
                        return false;
                    }

                    string line = System.Text.Encoding.ASCII.GetString(
                        receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                    length += (offset - next_decoding_offset_ + 1);
                    next_decoding_offset_ = offset + 1;

                    if (line == "")
                    {
                        // End of header.
                        header_decoded_ = true;
                        DebugLog3("{0} read {1} bytes for header.", str_protocol_, length);
                        return true;
                    }

                    string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                    if (tuple.Length == 2)
                    {
                        tuple[0] = tuple[0].ToUpper();
                        header_fields_[tuple[0]] = tuple[1];
                        DebugLog3("  > {0} header '{1} : {2}'", str_protocol_, tuple[0], tuple[1]);
                    }
                    else
                    {
                        LogWarning("  > {0} header: invalid tuple - '{1}'", str_protocol_, line);
                    }
                }

                return false;
            }

            static int bytePatternMatch (ArraySegment<byte> haystack, ArraySegment<byte> needle)
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

            bool tryToDecodeBody ()
            {
                if (!header_fields_.ContainsKey(kVersionHeaderField) ||
                    !header_fields_.ContainsKey(kLengthHeaderField))
                {
                    LogWarning("{0} header is invalid. It doesn't have '{1}' field or '{2}' field",
                               str_protocol_, kVersionHeaderField, kLengthHeaderField);
                    return false;
                }

                // Header version
                int version = Convert.ToUInt16(header_fields_[kVersionHeaderField]);
                if (version != FunapiVersion.kProtocolVersion)
                {
                    LogWarning("The protocol version does not match with server. client:{0} server:{1}",
                               FunapiVersion.kProtocolVersion, version);
                }

                // Header length
                int body_length = Convert.ToInt32(header_fields_[kLengthHeaderField]);
                if (body_length > 0)
                {
                    DebugLog3("{0} message body is {1} bytes. Buffer has {2} bytes.",
                              str_protocol_, body_length, received_size_ - next_decoding_offset_);
                }
                else
                {
                    DebugLog3("{0} {1} bytes left in buffer.",
                              str_protocol_, received_size_ - next_decoding_offset_);
                }

                if (received_size_ - next_decoding_offset_ < body_length)
                {
                    // Need more bytes.
                    DebugLog3("{0} need more bytes for a message body. Waiting.", str_protocol_);
                    return false;
                }

                // Encryption
                string encryption_type = "";
                string encryption_header = "";
                string compression_header = "";
                int uncompressed_size = 0;

                if (header_fields_.TryGetValue(kEncryptionHeaderField, out encryption_header))
                    parseEncryptionHeader(ref encryption_type, ref encryption_header);

                if (header_fields_.TryGetValue(kUncompressedLengthHeaderField, out compression_header))
                    uncompressed_size = Convert.ToInt32(compression_header);

                if (state_ == State.kHandshaking)
                {
                    FunDebug.Assert(body_length == 0);

                    if (doHandshaking(encryption_type, encryption_header))
                    {
                        state_ = State.kConnected;
                        DebugLog1("{0} handshaking is complete.", str_protocol_);

                        // Send public key (Do not change this order)
                        if (hasEncryption(EncryptionType.kChaCha20Encryption))
                            sendPublicKey(EncryptionType.kChaCha20Encryption);

                        if (hasEncryption(EncryptionType.kAes128Encryption))
                            sendPublicKey(EncryptionType.kAes128Encryption);

                        event_.Add(onHandshakeComplete);
                    }
                }

                if (body_length > 0)
                {
                    if (!Connected)
                    {
                        LogWarning("{0} received a message but the transport has been stopped.", str_protocol_);
                    }

                    ArraySegment<byte> body = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, body_length);
                    FunDebug.Assert(body.Count == body_length);
                    next_decoding_offset_ += body_length;

                    if (encryption_type.Length > 0)
                    {
                        if (!decryptMessage(body, encryption_type, encryption_header))
                            return false;
                    }

                    if (uncompressed_size > 0)
                    {
                        if (compressor_ == null)
                        {
                            LogError("Received a compressed message. But the transport is not configured with compression.");
                            return false;
                        }

                        ArraySegment<byte> decompressed = compressor_.Decompress(body, uncompressed_size);
                        if (decompressed.Count == 0)
                        {
                            LogError("Failed to decompress the mssage.");
                            return false;
                        }

                        DebugLog3("{0} decompression successed: {1}bytes -> {2}bytes",
                                  str_protocol_, body.Count, decompressed.Count);

                        body = decompressed;
                    }

                    if (first_message_)
                    {
                        first_message_ = false;
                        cstate_ = ConnectState.kConnected;

                        resetConnectionTimeout();
                    }

                    onReceived(header_fields_, body);
                }
                else
                {
                    onReceived(header_fields_, new ArraySegment<byte>());
                }

                // Prepares a next message.
                header_decoded_ = false;
                header_fields_.Clear();
                return true;
            }

            void sendPublicKey (EncryptionType type)
            {
                DebugLog1("{0} sending a {1}-pubkey message.", str_protocol_, (int)type);

                lock (sending_lock_)
                {
                    FunapiMessage msg = new FunapiMessage(protocol_, kEncryptionPublicKey, null, type);
                    first_.Add(msg);
                }
            }

            // Sends messages & Calls start callback
            void onHandshakeComplete ()
            {
                onStarted();

                if (state_ == State.kEstablished)
                {
                    sendPendingMessages();
                }
            }

            void onReceived (Dictionary<string, string> header, ArraySegment<byte> body)
            {
                if (body.Count <= 0)
                    return;

                // Deserializing a message
                object message = FunapiMessage.Deserialize(body, encoding_);
                if (message == null)
                {
                    LogWarning("{0} failed to deserialize a message.", str_protocol_);
                    return;
                }

                // Checks ack and seq, gets message type
                string msg_type = "";
                if (encoding_ == FunEncoding.kJson)
                {
                    if (IsReliable)
                    {
                        if (json_helper_.HasField(message, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)json_helper_.GetIntegerField(message, kAckNumberField);
                            onAckReceived(ack);
                        }

                        if (json_helper_.HasField(message, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)json_helper_.GetIntegerField(message, kSeqNumberField);
                            if (!onSeqReceived(seq))
                                return;
                        }
                    }

                    if (json_helper_.HasField(message, kMessageTypeField))
                    {
                        msg_type = json_helper_.GetStringField(message, kMessageTypeField);
                    }
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    FunMessage funmsg = (FunMessage)message;

                    if (IsReliable)
                    {
                        if (funmsg.ackSpecified)
                        {
                            onAckReceived(funmsg.ack);
                        }

                        if (funmsg.seqSpecified)
                        {
                            if (!onSeqReceived(funmsg.seq))
                                return;
                        }
                    }

                    if (funmsg.msgtypeSpecified)
                    {
                        msg_type = funmsg.msgtype;
                    }
                    else if (funmsg.msgtype2Specified)
                    {
                        msg_type = MessageTable.Lookup((MessageType)funmsg.msgtype2);
                    }
                }

                // Checks sent session id
                lock (session_id_sent_lock_)
                {
                    if (send_session_id_only_once_ && !session_id_has_been_sent)
                    {
                        if (protocol_ != TransportProtocol.kHttp &&
                            state_ == State.kEstablished && msg_type.Length > 0)
                        {
                            session_id_has_been_sent = true;
                        }
                    }
                }

                if (msg_type.Length > 0)
                {
                    // Checks ping messages
                    if (msg_type == kServerPingMessageType)
                    {
                        onServerPingMessage(message);
                        return;
                    }
                    else if (msg_type == kClientPingMessageType)
                    {
                        onClientPingMessage(message);
                        return;
                    }
                }
                else
                {
                    if (state_ == State.kWaitForAck)
                    {
                        onStandby();
                    }
                }

                if (ReceivedCallback != null)
                    ReceivedCallback(protocol_, encoding_, msg_type, message);
            }

            protected void onFailedSending ()
            {
                lock (sending_lock_)
                    sending_.Clear();

                LogWarning("{0} sending failed. Clears the sending buffer.", str_protocol_);
            }


            //---------------------------------------------------------------------
            // Ping-related functions
            //---------------------------------------------------------------------
            void startPingTimer ()
            {
                if (!enable_ping_ || protocol_ != TransportProtocol.kTcp)
                    return;

                if (ping_interval_ <= 0)
                    ping_interval_ = kPingIntervalDefault;

                if (ping_timer_id_ != 0)
                    timer_.Remove(ping_timer_id_);

                ping_timer_id_ = timer_.Add (() => onPingTimerEvent(), true, ping_interval_);
                ping_wait_time_ = 0f;

                Log("Start ping - interval seconds: {0}, timeout seconds: {1}",
                    ping_interval_, ping_timeout_);
            }

            void stopPingTimer ()
            {
                if (!enable_ping_ || protocol_ != TransportProtocol.kTcp)
                    return;

                lock (sending_lock_)
                {
                    if (pending_.Count > 0)
                        pending_.RemoveAll(msg => { return msg.msg_type == kClientPingMessageType; });
                }

                if (ping_timer_id_ != 0)
                {
                    timer_.Remove(ping_timer_id_);
                    ping_timer_id_ = 0;
                    ping_time_ = 0;

                    Log("{0} ping timer stopped.", str_protocol_);
                }
            }

            void onPingTimerEvent ()
            {
                if (!Connected)
                {
                    stopPingTimer();
                    return;
                }

                if (ping_wait_time_ > ping_timeout_)
                {
                    last_error_code_ = TransportError.Type.kDisconnected;
                    last_error_message_ = string.Format("{0} has not received a ping message for a long time.", str_protocol_);
                    onDisconnected();
                    return;
                }

                sendPingMessage();
            }

            void sendPingMessage ()
            {
                long timestamp = DateTime.Now.Ticks;

                if (encoding_ == FunEncoding.kJson)
                {
                    object msg = FunapiMessage.Deserialize("{}");
                    json_helper_.SetIntegerField(msg, kPingTimestampField, timestamp);
                    sendMessage(new FunapiMessage(protocol_, kClientPingMessageType, msg));
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    FunPingMessage ping = new FunPingMessage();
                    ping.timestamp = timestamp;
                    FunMessage msg = FunapiMessage.CreateFunMessage(ping, MessageType.cs_ping);
                    sendMessage(new FunapiMessage(protocol_, kClientPingMessageType, msg));
                }

                ping_wait_time_ += ping_interval_;
                DebugLog1("Send ping - timestamp: {0}", timestamp);
            }

            void onServerPingMessage (object body)
            {
                // Send response
                if (encoding_ == FunEncoding.kJson)
                {
                    if (!session_id_.IsValid && json_helper_.HasField(body, kSessionIdField))
                        session_id_.SetId(json_helper_.GetStringField(body, kSessionIdField));

                    sendMessage(new FunapiMessage(protocol_, kServerPingMessageType, json_helper_.Clone(body)));
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
                    sendMessage(new FunapiMessage(protocol_, kServerPingMessageType, send_msg));
                }
            }

            void onClientPingMessage (object body)
            {
                long timestamp = 0;

                if (encoding_ == FunEncoding.kJson)
                {
                    if (json_helper_.HasField(body, kPingTimestampField))
                    {
                        timestamp = (long)json_helper_.GetIntegerField(body, kPingTimestampField);
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
                    ping_wait_time_ -= ping_interval_;

                ping_time_ = (int)((DateTime.Now.Ticks - timestamp) / 10000);

                if (enable_ping_log_)
                    DebugLog1("Received ping - timestamp:{0} time={1} ms", timestamp, ping_time_);
            }


            public enum State
            {
                kUnknown = 0,
                kConnecting,
                kHandshaking,
                kConnected,
                kWaitForAck,
                kStandby,
                kEstablished
            };

            enum ConnectState
            {
                kUnknown = 0,
                kReconnecting,
                kConnected
            };


            // constants.
            const int kReconnectCountMax = 3;
            const int kPingIntervalDefault = 3;

            // Buffer-related constants.
            protected const int kUnitBufferSize = 65536;

            // Funapi header-related constants.
            protected const string kHeaderDelimeter = "\n";
            protected const string kHeaderFieldDelimeter = ":";
            protected const string kVersionHeaderField = "VER";
            protected const string kPluginVersionHeaderField = "PVER";
            protected const string kLengthHeaderField = "LEN";
            protected const string kEncryptionHeaderField = "ENC";
            protected const string kUncompressedLengthHeaderField = "C";

            // message-related constants.
            const string kEncryptionPublicKey = "_pub_key";
            const string kServerPingMessageType = "_ping_s";
            const string kClientPingMessageType = "_ping_c";
            const string kPingTimestampField = "timestamp";

            // for speed-up.
            static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(kHeaderDelimeter));
            static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

            // Event handlers
            public event CreateCompressorHandler CreateCompressorCallback;
            public event TransportEventHandler EventCallback;
            public event TransportErrorHandler ErrorCallback;
            public event MessageNotifyHandler ReceivedCallback;

            // member variables.
            protected State state_;
            protected SessionId session_id_ = new SessionId();
            protected TransportProtocol protocol_;
            protected string str_protocol_;
            protected FunEncoding encoding_ = FunEncoding.kNone;
            protected TransportOption option_ = null;
            protected ThreadSafeEventList event_ = new ThreadSafeEventList();
            protected ThreadSafeEventList timer_ = new ThreadSafeEventList();
            protected bool is_paused_ = false;

            // Connect-related member variables.
            ConnectState cstate_ = ConnectState.kUnknown;
            bool auto_reconnect_ = false;
            uint connect_timer_id_ = 0;
            float exponential_time_ = 0f;

            // Ping-related variables.
            bool enable_ping_ = false;
            bool enable_ping_log_ = false;
            int ping_time_ = 0;
            uint ping_timer_id_ = 0;
            int ping_interval_ = 0;
            float ping_timeout_ = 0f;
            float ping_wait_time_ = 0f;

            // Message-related variables.
            bool first_sending_ = true;
            bool first_message_ = true;
            bool header_decoded_ = false;
            bool send_session_id_only_once_ = false;
            bool session_id_has_been_sent = false;
            object session_id_sent_lock_ = new object();
            Dictionary<string, string> header_fields_ = new Dictionary<string, string>();

            protected int received_size_ = 0;
            protected int next_decoding_offset_ = 0;
            protected object sending_lock_ = new object();
            protected object receive_lock_ = new object();
            protected byte[] receive_buffer_ = new byte[kUnitBufferSize];
            protected List<FunapiMessage> first_ = new List<FunapiMessage>();
            protected List<FunapiMessage> pending_ = new List<FunapiMessage>();
            protected List<FunapiMessage> sending_ = new List<FunapiMessage>();

            // Compression releated variables.
            FunCompressionType compression_type_ = FunCompressionType.kNone;
            FunapiCompressor compressor_ = null;

            // Reliability-related variables.
            UInt32 seq_ = 0;
            UInt32 last_seq_ = 0;
            UInt32 sent_ack_ = 0;
            bool first_seq_ = true;
            float delayed_ack_interval_ = 0f;
            Queue<FunapiMessage> sent_queue_ = new Queue<FunapiMessage>();
            Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();

            // Error-related member variables.
            protected TransportError.Type last_error_code_ = TransportError.Type.kNone;
            protected string last_error_message_ = "";
        }


        // TCP transport layer
        class TcpTransport : Transport
        {
            public TcpTransport (string hostname_or_ip, UInt16 port,
                                 FunEncoding type, TransportOption tcp_option)
            {
                protocol_ = TransportProtocol.kTcp;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = tcp_option;

                setAddress(hostname_or_ip, port);
            }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get
                {
                    lock (sock_lock_)
                        return sock_ != null && sock_.Connected && state_ >= State.kConnected;
                }
            }

            void setAddress (string host, UInt16 port)
            {
                addr_ = new HostIP(host, port);

                TcpTransportOption tcp_option = (TcpTransportOption)option_;
                Log("TCP connect - {0}:{1}, {2}, {3}, Compression:{4}, Sequence:{5}, Timeout:{6}, Reconnect:{7}, Nagle:{8}, Ping:{9}",
                    addr_.ip, addr_.port, convertString(encoding_), convertString(tcp_option.Encryption),
                    tcp_option.CompressionType, tcp_option.SequenceValidation, tcp_option.ConnectionTimeout,
                    tcp_option.AutoReconnect, !tcp_option.DisableNagle, tcp_option.EnablePing);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnecting;

                addr_.refresh();

                lock (sock_lock_)
                {
                    sock_ = new Socket(addr_.inet, SocketType.Stream, ProtocolType.Tcp);

                    bool disable_nagle = (option_ as TcpTransportOption).DisableNagle;
                    if (disable_nagle)
                        sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                    sock_.BeginConnect(addr_.list, addr_.port, new AsyncCallback(this.startCb), this);
                }
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (sock_ != null)
                    {
                        sock_.Close();
                        sock_ = null;
                    }
                }
            }

            protected override void wireSend ()
            {
                List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();

                lock (sending_lock_)
                {
                    int length = 0;

                    foreach (FunapiMessage msg in sending_)
                    {
                        if (msg.header.Count > 0)
                        {
                            list.Add(msg.header);
                            length += msg.header.Count;
                        }

                        if (msg.body.Count > 0)
                        {
                            list.Add(msg.body);
                            length += msg.body.Count;
                        }
                    }

                    DebugLog2("TCP sending {0} messages. ({1}bytes)", sending_.Count, length);
                }

                try
                {
                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        sock_.BeginSend(list, SocketFlags.None, new AsyncCallback(this.sendBytesCb), this);
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        DebugLog1("TCP BeginSend operation has been cancelled.");
                        return;
                    }

                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = "TCP failure in wireSend: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void startCb (IAsyncResult ar)
            {
                try
                {
                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        sock_.EndConnect(ar);
                        if (sock_.Connected == false)
                        {
                            last_error_code_ = TransportError.Type.kStartingFailed;
                            last_error_message_ = string.Format("TCP connection failed.");
                            event_.Add(onFailure);
                            return;
                        }
                        DebugLog1("TCP transport connected. Starts handshaking..");

                        state_ = State.kHandshaking;

                        lock (receive_lock_)
                        {
                            // Wait for handshaking message.
                            ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                            List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                            buffer.Add(wrapped);
                            sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.receiveBytesCb), this);
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    DebugLog1("TCP BeginConnect operation has been cancelled.");
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kStartingFailed;
                    last_error_message_ = "TCP failure in startCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void sendBytesCb (IAsyncResult ar)
            {
                try
                {
                    int nSent = 0;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        nSent = sock_.EndSend(ar);
                    }
                    FunDebug.Assert(nSent > 0, "TCP failed to transfer messages.");

                    DebugLog2("TCP sent {0} bytes.", nSent);

                    lock (sending_lock_)
                    {
                        while (nSent > 0)
                        {
                            FunDebug.Assert(sending_.Count > 0,
                                string.Format("TCP couldn't find the sending buffers that sent messages.\n" +
                                              "Sent {0} more bytes but there are no sending buffers.", nSent));

                            // removes a sent message.
                            FunapiMessage msg = sending_[0];
                            int length = msg.header.Count + msg.body.Count;
                            nSent -= length;
                            sending_.RemoveAt(0);

                            DebugLog3("TCP removes '{0}' message ({1}bytes)", msg.msg_type, length);
                        }

                        FunDebug.Assert(sending_.Count == 0,
                            string.Format("sendBytesCb - sending buffer has {0} messages.", sending_.Count));

                        // Sends pending messages
                        checkPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    DebugLog1("TCP BeginSend operation has been cancelled.");
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = "TCP failure in sendBytesCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void receiveBytesCb (IAsyncResult ar)
            {
                try
                {
                    int nRead = 0;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        nRead = sock_.EndReceive(ar);
                    }

                    lock (receive_lock_)
                    {
                        if (nRead > 0)
                        {
                            received_size_ += nRead;
                            DebugLog3("TCP received {0} bytes. Buffer has {1} bytes.",
                                      nRead, received_size_ - next_decoding_offset_);

                            // Decoding a messages
                            tryToDecodeMessage();

                            // Checks buffer space
                            checkReceiveBuffer();

                            // Starts another async receive
                            ArraySegment<byte> residual = new ArraySegment<byte>(
                                receive_buffer_, received_size_, receive_buffer_.Length - received_size_);

                            List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                            buffer.Add(residual);

                            lock (sock_lock_)
                            {
                                sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.receiveBytesCb), this);
                                DebugLog3("TCP ready to receive more. We can receive upto {0} more bytes.",
                                          receive_buffer_.Length - received_size_);
                            }
                        }
                        else
                        {
                            LogWarning("TCP socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                LogWarning("TCP buffer has {0} bytes but they failed to decode. Discarding.",
                                           received_size_ - next_decoding_offset_);
                            }

                            last_error_code_ = TransportError.Type.kDisconnected;
                            last_error_message_ = "TCP can't receive messages. Maybe the socket is closed.";
                            event_.Add(onDisconnected, 1f);
                        }
                    }
                }
                catch (Exception e)
                {
                    // When Stop is called Socket.EndReceive may return a NullReferenceException
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        DebugLog1("TCP BeginReceive operation has been cancelled.");
                        return;
                    }

                    last_error_code_ = TransportError.Type.kReceivingFailed;
                    last_error_message_ = "TCP failure in receiveBytesCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }


            Socket sock_;
            HostIP addr_;
            object sock_lock_ = new object();
        }


        // UDP transport layer
        class UdpTransport : Transport
        {
            public UdpTransport (string hostname_or_ip, UInt16 port,
                                 FunEncoding type, TransportOption udp_option)
            {
                protocol_ = TransportProtocol.kUdp;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = udp_option;

                setAddress(hostname_or_ip, port);
            }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get
                {
                    lock (sock_lock_)
                        return sock_ != null && state_ >= State.kConnected;
                }
            }

            protected void setAddress (string host, UInt16 port)
            {
                addr_ = new HostIP(host, port);

                Log("UDP connect - {0}:{1}, {2}, {3}, Compression:{4}, Sequence:{5}, Timeout:{6}",
                    addr_.ip, addr_.port, convertString(encoding_), convertString(option_.Encryption),
                    option_.CompressionType, option_.SequenceValidation, option_.ConnectionTimeout);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnected;

                lock (sock_lock_)
                {
                    sock_ = new Socket(addr_.inet, SocketType.Dgram, ProtocolType.Udp);
                    sock_.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    int port = 0;
#if FIXED_UDP_LOCAL_PORT
                    port = LocalPort.Next();
#endif
                    if (addr_.inet == AddressFamily.InterNetworkV6)
                        sock_.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                    else
                        sock_.Bind(new IPEndPoint(IPAddress.Any, port));

                    IPEndPoint lep = (IPEndPoint)sock_.LocalEndPoint;
                    DebugLog1("UDP bind - local:{0}:{1}", lep.Address, lep.Port);

                    send_ep_ = new IPEndPoint(addr_.ip, addr_.port);
                    if (addr_.inet == AddressFamily.InterNetworkV6)
                        receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.IPv6Any, addr_.port);
                    else
                        receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, addr_.port);

                    lock (receive_lock_)
                    {
                        sock_.BeginReceiveFrom(receive_buffer_, 0, receive_buffer_.Length, SocketFlags.None,
                                               ref receive_ep_, new AsyncCallback(this.receiveBytesCb), this);
                    }
                }

                onStarted();
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (sock_ != null)
                    {
                        sock_.Close();
                        sock_ = null;
                    }
                }
            }

            // Send a packet.
            protected override void wireSend ()
            {
                int offset = 0;

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count > 0);

                    // Sends one message.
                    FunapiMessage msg = sending_[0];
                    int length = msg.header.Count + msg.body.Count;
                    if (length > kUdpBufferSize)
                    {
                        string error = string.Format("'{0}' message's length is {1} bytes " +
                                                     "but UDP single message can't bigger than {2} bytes.",
                                                     msg.msg_type, length, kUdpBufferSize);
                        FunDebug.Assert(false, error);
                    }

                    if (msg.header.Count > 0)
                    {
                        Buffer.BlockCopy(msg.header.Array, 0, send_buffer_, offset, msg.header.Count);
                        offset += msg.header.Count;
                    }

                    if (msg.body.Count > 0)
                    {
                        Buffer.BlockCopy(msg.body.Array, 0, send_buffer_, offset, msg.body.Count);
                        offset += msg.body.Count;
                    }

                    DebugLog2("UDP sending {0} bytes.", length);
                }

                if (offset > 0)
                {
                    try
                    {
                        lock (sock_lock_)
                        {
                            sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                              send_ep_, new AsyncCallback(this.sendBytesCb), this);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        DebugLog1("UDP BeginSendTo operation has been cancelled.");
                    }
                }
            }

            void sendBytesCb (IAsyncResult ar)
            {
                try
                {
                    int nSent = 0;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        nSent = sock_.EndSend(ar);
                        FunDebug.Assert(nSent > 0, "UDP failed to transfer messages.");
                    }

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);

                        FunapiMessage msg = sending_[0];
                        DebugLog2("UDP sent a message - '{0}' ({1}bytes)", msg.msg_type, nSent);

                        // Removes header and body segment
                        int nLength = msg.header.Count + msg.body.Count;
                        sending_.RemoveAt(0);

                        if (nSent != nLength)
                        {
                            string error = string.Format("UDP failed to sending a whole message. " +
                                                         "buffer:{0} sent:{1}", nLength, nSent);
                            FunDebug.Assert(false, error);
                        }

                        // Checks unsent messages
                        checkPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    DebugLog1("UDP BeginSendTo operation has been cancelled.");
                }
                catch (Exception e)
                {
                    onFailedSending();

                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = "UDP failure in sendBytesCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void receiveBytesCb (IAsyncResult ar)
            {
                try
                {
                    int nRead = 0;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        nRead = sock_.EndReceive(ar);
                    }

                    lock (receive_lock_)
                    {
                        if (nRead > 0)
                        {
                            received_size_ += nRead;
                            DebugLog3("UDP received {0} bytes. Buffer has {1} bytes.",
                                      nRead, received_size_ - next_decoding_offset_);
                        }

                        // Decoding a message
                        tryToDecodeMessage();

                        if (nRead > 0)
                        {
                            // Resets buffer
                            received_size_ = 0;
                            next_decoding_offset_ = 0;

                            lock (sock_lock_)
                            {
                                // Starts another async receive
                                sock_.BeginReceiveFrom(receive_buffer_, received_size_,
                                                       receive_buffer_.Length - received_size_,
                                                       SocketFlags.None, ref receive_ep_,
                                                       new AsyncCallback(this.receiveBytesCb), this);

                                DebugLog3("UDP ready to receive more. We can receive upto {0} more bytes",
                                          receive_buffer_.Length);
                            }
                        }
                        else
                        {
                            LogWarning("UDP socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                LogWarning("UDP buffer has {0} bytes but they failed to decode. Discarding.",
                                           received_size_ - next_decoding_offset_);
                            }

                            last_error_code_ = TransportError.Type.kDisconnected;
                            last_error_message_ = "UDP can't receive messages. Maybe the socket is closed.";
                            event_.Add(onDisconnected);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        DebugLog1("UDP BeginReceiveFrom operation has been cancelled.");
                        return;
                    }

                    last_error_code_ = TransportError.Type.kReceivingFailed;
                    last_error_message_ = "UDP failure in receiveBytesCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

#if FIXED_UDP_LOCAL_PORT
            // This class is to prevent UDP local ports from overlapping.
            static class LocalPort
            {
                static LocalPort ()
                {
                    string path = FunapiUtils.GetLocalDataPath;
                    if (path.Length > 0 && path[path.Length - 1] != '/')
                        path += "/";
                    save_path_ = path + kFileName;

                    if (File.Exists(save_path_))
                    {
                        string text = File.ReadAllText(save_path_);
                        if (!int.TryParse(text, out local_port_))
                            local_port_ = kLocalPortMin;
                    }
                    else
                    {
                        local_port_ = kLocalPortMin;
                        File.WriteAllText(save_path_, local_port_.ToString());
                    }

                    FunDebug.Log("The udp local port start value ({0}) has been loaded.", local_port_);
                }

                public static int Next ()
                {
                    lock (local_port_lock_)
                    {
                        ++local_port_;
                        if (local_port_ > kLocalPortMax)
                            local_port_ = kLocalPortMin;

                        File.WriteAllText(save_path_, local_port_.ToString());

                        return local_port_;
                    }
                }


                const string kFileName = "udp.localport";
                const int kLocalPortMin = 49152;
                const int kLocalPortMax = 65534;

                static object local_port_lock_ = new object();
                static int local_port_ = 0;
                static string save_path_ = "";
            }
#endif


            // member variables
            Socket sock_;
            HostIP addr_;
            IPEndPoint send_ep_;
            EndPoint receive_ep_;
            object sock_lock_ = new object();

            // This length is 64KB minus 8 bytes UDP header and 48 bytes IP header.
            // (IP header size is based on IPV6. IPV4 uses 20 bytes.)
            // https://en.wikipedia.org/wiki/User_Datagram_Protocol
            const int kUdpBufferSize = 65479;

            // Sending buffer
            byte[] send_buffer_ = new byte[kUdpBufferSize];
        }


        // HTTP transport layer
        class HttpTransport : Transport
        {
            public HttpTransport (string hostname_or_ip, UInt16 port, bool https,
                                 FunEncoding type, TransportOption http_option)
            {
                protocol_ = TransportProtocol.kHttp;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = http_option;

                setAddress(hostname_or_ip, port, https);

                if (https)
                    MozRoots.LoadRootCertificates();
            }

            public MonoBehaviour mono { private get; set; }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get { return state_ >= State.kConnected; }
            }

            protected void setAddress (string host, UInt16 port, bool https)
            {
                addr_ = new HostHttp(host, port, https);

                // Sets host url
                host_url_ = string.Format("{0}://{1}:{2}/v{3}/",
                                          (https ? "https" : "http"), host, port,
                                          FunapiVersion.kProtocolVersion);

                HttpTransportOption http_option = (HttpTransportOption)option_;
                Log("HTTP connect - {0}, {1}, {2}, Compression:{3}, Sequence:{4}, Timeout:{5}, WWW:{6}",
                    host_url_, convertString(encoding_), convertString(http_option.Encryption),
                    http_option.CompressionType, http_option.SequenceValidation,
                    http_option.ConnectionTimeout, http_option.UseWWW);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnected;
                str_cookie_ = "";
#if !NO_UNITY
                using_www_ = (option_ as HttpTransportOption).UseWWW;
#endif
                onStarted();
            }

            protected override void onClose ()
            {
                cancelRequest();
            }

            protected override bool isSendable
            {
                get
                {
                    if (is_paused_)
                        return false;

                    if (cur_request_ != null)
                        return false;

                    return true;
                }
            }

            protected override void wireSend ()
            {
                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count > 0);
                    DebugLog2("HTTP Host Url: {0}", host_url_);

                    FunapiMessage msg = sending_[0];

                    // Header
                    Dictionary<string, string> headers = new Dictionary<string, string>();
                    string str_header = System.Text.Encoding.ASCII.GetString(msg.header.Array);
                    string[] list = str_header.Split(kHeaderSeparator, StringSplitOptions.None);

                    for (int i = 0; i < list.Length; i += 2)
                    {
                        if (list[i].Length <= 0)
                            break;

                        if (list[i] == kEncryptionHeaderField)
                            headers.Add(kEncryptionHttpHeaderField, list[i+1]);
                        else if (list[i] == kUncompressedLengthHeaderField)
                            headers.Add(kUncompressedLengthHttpHeaderField, list[i+1]);
                        else
                            headers.Add(list[i], list[i+1]);
                    }

                    if (str_cookie_.Length > 0)
                        headers.Add(kCookieHeaderField, str_cookie_);

                    DebugLog2("HTTP sending {0} bytes.", msg.body.Count);

#if !NO_UNITY
                    // Sending a message
                    if (using_www_)
                    {
                        sendWWWRequest(headers, msg);
                    }
                    else
#endif
                    {
                        sendHttpWebRequest(headers, msg);
                    }
                }
            }

#if !NO_UNITY
            void sendWWWRequest (Dictionary<string, string> headers, FunapiMessage msg)
            {
                if (msg.body.Count > 0)
                {
                    mono.StartCoroutine(wwwPost(new WWW(host_url_, msg.body.Array, headers)));
                }
                else
                {
                    mono.StartCoroutine(wwwPost(new WWW(host_url_, null, headers)));
                }
            }
#endif

            void sendHttpWebRequest (Dictionary<string, string> headers, FunapiMessage msg)
            {
                // Request
                HttpWebRequest web_request = (HttpWebRequest)WebRequest.Create(host_url_);
                web_request.ConnectionGroupName = session_id_;
                web_request.Method = "POST";
                web_request.ContentType = "application/octet-stream";
                web_request.ContentLength = msg.body.Count;

                foreach (KeyValuePair<string, string> item in headers) {
                    web_request.Headers[item.Key] = item.Value;
                }

                Request request = new Request();
                request.message = msg;
                request.web_request = web_request;

                FunDebug.Assert(cur_request_ == null);
                cur_request_ = request;

                web_request.BeginGetRequestStream(new AsyncCallback(requestStreamCb), request);
            }

            void onReceiveHeader (string headers)
            {
                StringBuilder buffer = new StringBuilder();
                string[] lines = headers.Replace("\r", "").Split('\n');
                int body_length = 0;

                buffer.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter,
                                    FunapiVersion.kProtocolVersion, kHeaderDelimeter);

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
                            DebugLog3("HTTP set-cookie : {0}", str_cookie_);
                            break;
                        case "content-length":
                            body_length = Convert.ToInt32(value);
                            buffer.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField,
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        case "x-ifun-enc":
                            buffer.AppendFormat("{0}{1}{2}{3}", kEncryptionHeaderField,
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        case "x-ifun-c":
                            buffer.AppendFormat("{0}{1}{2}{3}", kUncompressedLengthHeaderField,
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        default:
                            buffer.AppendFormat("{0}{1}{2}{3}", tuple[0],
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                buffer.Append(kHeaderDelimeter);

                byte[] header_bytes = System.Text.Encoding.ASCII.GetBytes(buffer.ToString());

                // Checks buffer's space
                received_size_ = 0;
                next_decoding_offset_ = 0;
                checkReceiveBuffer(header_bytes.Length + body_length);

                // Copy to buffer
                Buffer.BlockCopy(header_bytes, 0, receive_buffer_, 0, header_bytes.Length);
                received_size_ += header_bytes.Length;
            }

            void requestStreamCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    Stream stream = request.web_request.EndGetRequestStream(ar);

                    FunapiMessage msg = request.message;
                    if (msg.body.Count > 0)
                        stream.Write(msg.body.Array, 0, msg.body.Count);
                    stream.Close();

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        DebugLog2("HTTP sent a message - '{0}' ({1}bytes)", msg.msg_type, msg.body.Count);

                        sending_.RemoveAt(0);
                    }

                    request.web_request.BeginGetResponse(new AsyncCallback(responseCb), request);
                }
                catch (Exception e)
                {
                    WebException we = e as WebException;
                    if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                        (e is ObjectDisposedException || e is NullReferenceException))
                    {
                        // When Stop is called HttpWebRequest.EndGetRequestStream may return a Exception
                        DebugLog1("HTTP request operation has been cancelled.");
                        return;
                    }

                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = "HTTP failure in requestStreamCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void responseCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    if (request.was_aborted)
                    {
                        Log("HTTP responseCb - request aborted. ({0})", request.message.msg_type);
                        return;
                    }

                    request.web_response = (HttpWebResponse)request.web_request.EndGetResponse(ar);
                    request.web_request = null;

                    if (request.web_response.StatusCode == HttpStatusCode.OK)
                    {
                        lock (receive_lock_)
                        {
                            byte[] header = request.web_response.Headers.ToByteArray();
                            string str_header = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
                            onReceiveHeader(str_header);

                            request.read_stream = request.web_response.GetResponseStream();
                            request.read_stream.BeginRead(receive_buffer_, received_size_,
                                                          receive_buffer_.Length - received_size_,
                                                          new AsyncCallback(readCb), request);
                        }
                    }
                    else
                    {
                        last_error_code_ = TransportError.Type.kReceivingFailed;
                        last_error_message_ = string.Format("Failed response. status:{0}",
                                                            request.web_response.StatusDescription);
                        event_.Add(onFailure);
                    }
                }
                catch (Exception e)
                {
                    WebException we = e as WebException;
                    if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                        (e is ObjectDisposedException || e is NullReferenceException))
                    {
                        // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                        DebugLog1("Http request operation has been cancelled.");
                        return;
                    }

                    last_error_code_ = TransportError.Type.kReceivingFailed;
                    last_error_message_ = "HTTP failure in responseCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void readCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    int nRead = request.read_stream.EndRead(ar);
                    if (nRead > 0)
                    {
                        lock (receive_lock_)
                        {
                            received_size_ += nRead;
                            request.read_stream.BeginRead(receive_buffer_, received_size_,
                                                          receive_buffer_.Length - received_size_,
                                                          new AsyncCallback(readCb), request);
                        }
                    }
                    else
                    {
                        if (request.web_response == null)
                        {
                            last_error_code_ = TransportError.Type.kReceivingFailed;
                            last_error_message_ = "Response instance is null.";
                            event_.Add(onFailure);
                            return;
                        }

                        DebugLog3("HTTP received {0} bytes.", received_size_);

                        lock (receive_lock_)
                        {
                            // Decoding a message
                            tryToDecodeMessage();
                        }

                        request.read_stream.Close();
                        request.web_response.Close();

                        cur_request_ = null;

                        // Checks unsent messages
                        checkPendingMessages();
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        DebugLog1("HTTP request operation has been cancelled.");
                        return;
                    }

                    last_error_code_ = TransportError.Type.kReceivingFailed;
                    last_error_message_ = "HTTP failure in readCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

#if !NO_UNITY
            IEnumerator wwwPost (WWW www)
            {
                Request request = new Request();
                request.www = www;

                FunDebug.Assert(cur_request_ == null);
                cur_request_ = request;

                while (!www.isDone && !request.cancel)
                {
                    yield return null;
                }

                if (request.cancel)
                {
                    cur_request_ = null;
                    yield break;
                }

                try
                {
                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        FunapiMessage msg = sending_[0];
                        DebugLog2("HTTP sent a message - '{0}' ({1}bytes)", msg.msg_type, msg.body.Count);

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
                        onReceiveHeader(headers.ToString());

                        Buffer.BlockCopy(www.bytes, 0, receive_buffer_, received_size_, www.bytes.Length);
                        received_size_ += www.bytes.Length;

                        DebugLog3("HTTP received {0} bytes.", received_size_);

                        // Decoding a message
                        tryToDecodeMessage();
                    }

                    cur_request_ = null;

                    // Checks unsent messages
                    checkPendingMessages();
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kRequestFailed;
                    last_error_message_ = "HTTP failure in wwwPost: " + e.ToString();
                    event_.Add(onFailure);
                }
            }
#endif

            void cancelRequest ()
            {
                if (cur_request_ != null)
                {
                    if (cur_request_.web_request != null)
                    {
                        cur_request_.was_aborted = true;
                        cur_request_.web_request.Abort();
                    }

                    if (cur_request_.web_response != null)
                        cur_request_.web_response.Close();

                    if (cur_request_.read_stream != null)
                        cur_request_.read_stream.Close();

#if !NO_UNITY
                    if (cur_request_.www != null)
                        cur_request_.cancel = true;
#endif
                    cur_request_ = null;
                }
            }

            protected override void onFailure ()
            {
                cancelRequest();
                base.onFailure();
            }


            // Request variables collection class
            class Request
            {
                public FunapiMessage message = null;

                // WebRequest-related
                public HttpWebRequest web_request = null;
                public HttpWebResponse web_response = null;
                public Stream read_stream = null;
                public bool was_aborted = false;

                // WWW-related
#if !NO_UNITY
                public WWW www = null;
                public bool cancel = false;
#endif
            }


            // Funapi header-related constants.
            const string kEncryptionHttpHeaderField = "X-iFun-Enc";
            const string kUncompressedLengthHttpHeaderField = "X-iFun-C";
            const string kCookieHeaderField = "Cookie";

            static readonly string[] kHeaderSeparator = { kHeaderFieldDelimeter, kHeaderDelimeter };

            // member variables.
            HostHttp addr_;
            string host_url_;
            string str_cookie_;
#if !NO_UNITY
            bool using_www_ = false;
#endif
            Request cur_request_ = null;
        }


        // Event handler delegate
        public delegate FunapiCompressor CreateCompressorHandler (TransportProtocol protocol);
        public delegate void TransportEventHandler (TransportProtocol protocol, TransportEventType type);
        public delegate void TransportErrorHandler (TransportProtocol protocol, TransportError type);
        public delegate void MessageNotifyHandler (TransportProtocol protocol, FunEncoding encoding,
                                                   string msg_type, object message);
    }

}  // namespace Fun
