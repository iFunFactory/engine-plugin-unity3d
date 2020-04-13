// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// protobuf
using ProtoBuf;
using funapi.network.fun_message;
using funapi.network.ping_message;


namespace Fun
{
    // Transport protocol
    public enum TransportProtocol
    {
        kDefault = 0,
        kTcp,
        kUdp,
        kHttp,
        kWebsocket
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
            kDeserializeFailed,
            kSendingFailed,
            kReceivingFailed,
            kRequestFailed,
            kWebsocketError,
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
        public float ConnectionTimeout = 10f;

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
        public bool UseTLS = false;

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
                   PingTimeoutSeconds == option.PingTimeoutSeconds &&
                   UseTLS == option.UseTLS;
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

    public class WebsocketTransportOption : TransportOption
    {
        public bool WSS = false;
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
            if (obj == null || !base.Equals(obj) || !(obj is WebsocketTransportOption))
                return false;

            WebsocketTransportOption option = obj as WebsocketTransportOption;

            return WSS == option.WSS &&
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


    public partial class FunapiSession
    {
        // Abstract class to represent Transport used by Funapi
        // TCP, UDP, and HTTP.
        public abstract class Transport : FunapiEncryptor
        {
            public Transport ()
            {
                state_ = State.kUnknown;
                timer_.debug = debug;
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
                        debug.LogWarning("[{0}] Start called but the transport already started. ({1})",
                                         str_protocol_, state_);
                        return;
                    }

                    debug.LogDebug("[{0}] Starting the transport.", str_protocol_);
                    setConnectionTimeout();

                    onStart();
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kStartingFailed;
                    error.message = string.Format("[{0}] Failure in Start: {1}", str_protocol_, e.ToString());
                    onFailure(error);
                }
            }

            // Stops transport
            public void Stop ()
            {
                if (state_ == State.kUnknown)
                    return;

                debug.LogDebug("[{0}] Stopping transport. (state:{1})", str_protocol_, state_);

                state_ = State.kUnknown;
                cstate_ = ConnectState.kUnknown;

                decodeMessages();

                event_.Clear();
                timer_.Clear();
                exponential_time_ = 0f;

                onClose();

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
                    debug.LogDebug("[{0}] Established.", str_protocol_);
                else
                    debug.LogDebug("[{0}] Established. (seq:{1})", str_protocol_, seq_);

                if (enable_ping_)
                    startPingTimer();

                if (IsReliable && delayed_ack_interval_ > 0f)
                {
                    timer_.Add(new FunapiLoopTimer("delayed_ack",
                                                   delayed_ack_interval_,
                                                   onDelayedAckEvent));
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

                        debug.Log("[{0}] - '{1}' message queued. state:{2}",
                                  str_protocol_, msg.msg_type, state_);
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
                // Decodes messages
                decodeMessages();

                // Events
                event_.Update(deltaTime);
                timer_.Update(deltaTime);
            }


            //
            // Properties
            //
            public FunapiMono.Listener mono { protected get; set; }

            public FunapiSession session { protected get; set; }

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

            public bool IsStopped
            {
                get { return state_ == State.kUnknown; }
            }

            public bool IsRedirecting
            {
                get { return redirecting_; }
                set { redirecting_ = value; }
            }

            public bool sendSessionIdOnlyOnce
            {
                set { send_session_id_only_once_ = value; }
            }

            public float delayedAckInterval
            {
                set { delayed_ack_interval_ = value; }
            }

            public string encryptionPublicKey
            {
                set
                {
                    if (value != null)
                    {
                        encryption_pub_key_ = UnHexifyKey(value);
                    }
                }
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

                if (state_ != State.kEstablished)
                    return;

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
                TransportError error = new TransportError();
                error.type = TransportError.Type.kDisconnected;
                error.message = string.Format("[{0}] The connection will be forcibly closed.",
                                              str_protocol_);
                onDisconnected(error);
            }

            public void ForcedFail ()
            {
                TransportError error = new TransportError();
                error.type = TransportError.Type.kSendingFailed;
                error.message = string.Format("{0} The connection will be forcibly closed.",
                                              str_protocol_);
                onFailure(error);
            }

            public void ForcedException ()
            {
                TransportError error = new TransportError();
                error.type = TransportError.Type.kReceivingFailed;
                error.message = string.Format("{0} The connection will be forcibly closed." +
                                              " (System.Net.Sockets.SocketException)",
                                              str_protocol_);
                onFailure(error);
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
                    received_size_ = 0;
                    next_decoding_offset_ = 0;
                }

                udp_handshake_id_ = Guid.NewGuid();
                last_error_code_ = TransportError.Type.kNone;
                last_error_message_ = "";
            }

            // Closes a socket
            protected virtual void onClose ()
            {
                stopPingTimer();
                resetEncryptors();

                lock (messages_lock_)
                    messages_.Clear();

                if (!IsReliable)
                {
                    lock (sending_lock_)
                        pending_.Clear();
                }

                lock (session_id_sent_lock_)
                    session_id_has_been_sent = false;
            }

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
                }
                else if (protocol_ == TransportProtocol.kWebsocket)
                {
                    WebsocketTransportOption websocket_option = opt as WebsocketTransportOption;
                    enable_ping_ = websocket_option.EnablePing;
                    enable_ping_log_ = websocket_option.EnablePingLog;
                }

                if (opt.Encryption != EncryptionType.kDefaultEncryption)
                {
                    setEncryption(opt.Encryption);
                    debug.Log("[{0}] Encrypt type: {1}", str_protocol_, convertString(opt.Encryption));
                }
            }

            void setConnectionTimeout ()
            {
                if (option_.ConnectionTimeout <= 0f)
                {
                    FunDebug.LogWarning("Connection timeout is disabled(0), Disabling timeout would cause the infinite waiting state." +
                                        " It is recommended that you use it only for debugging purposes.");
                    return;
                }

                timer_.Add(new FunapiTimeoutTimer("connection",
                                                  option_.ConnectionTimeout,
                                                  onConnectionTimedout), true);
            }

            void resetConnectionTimeout ()
            {
                if (option_.ConnectionTimeout <= 0f)
                    return;

                timer_.Remove("connection");
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
                last_error_message_ = string.Format("[{0}] Connection waiting time has been exceeded.",
                                                    str_protocol_);
                debug.LogWarning(last_error_message_);

                if (ErrorCallback != null)
                {
                    TransportError error = new TransportError();
                    error.type = last_error_code_;
                    error.message = last_error_message_;

                    ErrorCallback(protocol_, error);
                }

                event_.Add(Stop);
            }

            protected void onDisconnected (TransportError error)
            {
                event_.Add(delegate ()
                {
                    last_error_code_ = error.type;
                    last_error_message_ = error.message;

                    debug.LogWarning("[{0}] Disconnected. state: {1}, error: {2}\n{3}\n",
                                     str_protocol_, state_, error.type, error.message);

                    if (ErrorCallback != null)
                        ErrorCallback(protocol_, error);

                    if (auto_reconnect_ && !redirecting_)
                    {
                        onAutoReconnect();
                        return;
                    }

                    event_.Add(Stop);
                });
            }

            protected virtual void onFailure (TransportError error)
            {
                event_.Add(delegate ()
                {
                    last_error_code_ = error.type;
                    last_error_message_ = error.message;

                    debug.LogWarning(error.message);

                    if (ErrorCallback != null)
                        ErrorCallback(protocol_, error);

                    if (auto_reconnect_ && !redirecting_)
                    {
                        if (state_ != State.kEstablished || last_error_message_.Contains("SocketException"))
                        {
                            debug.Log("[{0}] Error occurred. It will try to reconnect. " +
                                      "(state: {1}, error: {2})\n{3}\n",
                                      str_protocol_, state_, error.type, error.message);

                            onAutoReconnect();
                            return;
                        }
                    }

                    // If an error occurs, stops the connection.
                    event_.Add(Stop);
                });
            }

            void onTransportEventCallback (TransportEventType type)
            {
                if (EventCallback != null)
                    EventCallback(protocol_, type);
            }


            //---------------------------------------------------------------------
            // auto-reconnect-related functions
            //---------------------------------------------------------------------
            bool onAutoReconnect ()
            {
                if (!auto_reconnect_ || redirecting_)
                    return false;

                if (cstate_ != ConnectState.kReconnecting)
                {
                    cstate_ = ConnectState.kReconnecting;
                    exponential_time_ = 1f;

                    onClose();

                    if (state_ == State.kEstablished)
                        setConnectionTimeout();

                    onTransportEventCallback(TransportEventType.kReconnecting);
                }

                // 1, 2, 4, 8, 8, 8,...
                float delay_time = exponential_time_;
                if (exponential_time_ < 8f)
                    exponential_time_ *= 2f;

                debug.Log("[{0}] Wait {1} second(s) for reconnecting.",
                          str_protocol_, delay_time);

                timer_.Add(new FunapiTimer("reconnect", delay_time, onStart));

                return true;
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
                        }
                        else
                        {
                            pending_.Add(msg);
                        }

                        if (Connected && isSendable)
                            sendPendingMessages();
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = string.Format("[{0}] Failure in sendMessage: {1}",
                                                  str_protocol_, e.ToString());
                    onFailure(error);
                }
            }

            void sendEmptyMessage ()
            {
                sendMessage(new FunapiMessage(protocol_, kEmptyMessageType, new FunMessage()), true);
            }

            void sendUdpEmptyMessage()
            {
                lock (sending_lock_)
                {
                    if (encoding_ == FunEncoding.kJson)
                    {
                        first_.Add(new FunapiMessage(protocol_,
                                                     kUdpHandShakeType,
                                                     FunapiMessage.Deserialize("{}"),
                                                     EncryptionType.kDefaultEncryption));
                    }
                    else if (encoding_ == FunEncoding.kProtobuf)
                    {
                        first_.Add(new FunapiMessage(protocol_,
                                                     kUdpHandShakeType,
                                                     new FunMessage(),
                                                     EncryptionType.kDefaultEncryption));
                    }
                    if (isSendable)
                    {
                        sendPendingMessages();
                    }
                }
            }

            protected IEnumerator tryToSendUdpEmptyMessage()
            {
                if (session.Id.IsValid)
                {
                    session_id_.SetId(session.GetSessionId());
                }
                else
                {
                    session_id_.Clear();
                }

                exponential_time_ = 1f;

                while (true)
                {
                    if (IsEstablished || IsStopped)
                    {
                        exponential_time_ = 0f;
                        yield break;
                    }

                    sendUdpEmptyMessage();

                    // 0.1, 0.2, 0.4, 0.8, 0.8, ...
                    float delay_time = exponential_time_;
                    if (exponential_time_ < 8f)
                        exponential_time_ *= 2f;

                    yield return new SleepForSeconds(delay_time / 10f);
                }
            }

           void sendAck (UInt32 ack, bool sendingFirst = false)
            {
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

                    debug.LogDebug("[{0}] {1} unsent message(s).",
                                   str_protocol_, unsent_queue_.Count);

                    foreach (FunapiMessage msg in unsent_queue_)
                    {
                        sendMessage(msg);
                    }

                    unsent_queue_.Clear();
                }
            }

            void onDelayedAckEvent (float deltaTime)
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
                if (first_seq_)
                {
                    first_seq_ = false;
                }
                else
                {
                    if (!seqLess(last_seq_, seq))
                    {
                        debug.LogWarning("[{0}] Last sequence number is {1} but {2} received. Skipping message.",
                                         str_protocol_, last_seq_, seq);
                        return false;
                    }
                    else if (seq != last_seq_ + 1)
                    {
                        debug.LogWarning("[{0}] Received wrong sequence number {1}. {2} expected.",
                                         str_protocol_, seq, last_seq_ + 1);

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

                lock (sending_lock_)
                {
                    if (sent_queue_.Count > 0)
                    {
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
                    }

                    if (state_ == State.kWaitForAck)
                    {
                        if (sent_queue_.Count > 0)
                        {
                            debug.LogDebug("[{0}] ack:{1}, sent queue:{2}",
                                           str_protocol_, ack, sent_queue_.Count);

                            foreach (FunapiMessage msg in sent_queue_)
                            {
                                if (msg.seq == ack || seqLess(ack, msg.seq))
                                {
                                    sendMessage(msg);

                                    debug.Log("[{0}] Resending '{1}' message. (seq:{2})",
                                              str_protocol_, msg.msg_type, msg.seq);
                                }
                                else
                                {
                                    debug.LogWarning("[{0}] Wrong sequence number: {1} ",
                                                     str_protocol_, msg.seq);
                                }
                            }
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
                        msg.msg_type != kAckNumberField && msg.msg_type != kEmptyMessageType && msg.msg_type != kUdpHandShakeType)
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

                    if (msg.msg_type == kUdpHandShakeType)
                    {
                        msg.msg_type = kEmptyMessageType;

                        if (encoding_ == FunEncoding.kJson)
                        {
                            json_helper_.SetStringField(msg.message, kUdpHandshakeIdField, udp_handshake_id_.ToString());
                        }
                        else if (encoding_ == FunEncoding.kProtobuf)
                        {
                            FunMessage proto = msg.message as FunMessage;
                            proto.udp_handshake_id = udp_handshake_id_.ToByteArray();
                        }
                    }
                }

                // Serializes message
                msg.body = new ArraySegment<byte>(msg.GetBytes(encoding_));

                if (debug.IsDebug)
                {
                    logSendingMessageInfo(msg, ack);
                }

                // Compress the message
                int uncompressed_size = 0;
                if (compressor_ != null && msg.body.Count >= compressor_.compression_threshold)
                {
                    ArraySegment<byte> compressed = compressor_.Compress(msg.body);
                    if (compressed.Count > 0)
                    {
                        debug.LogDebug("[{0}] Compress message: {1}bytes -> {2}bytes",
                                       str_protocol_, msg.body.Count, compressed.Count);

                        uncompressed_size = msg.body.Count;
                        msg.body = compressed;
                    }
                }

                // Encrypt message
                EncryptionType enc_type = getEncryption(msg);
                if (msg.msg_type == kEncryptionPublicKey)
                {
                    string enc_key = generatePublicKey(enc_type, encryption_pub_key_);
                    if (enc_key == null)
                        return false;

                    msg.enc_header = enc_key;
                }
                else if (enc_type != EncryptionType.kNoneEncryption)
                {
                    if (!encryptMessage(msg, enc_type))
                    {
                        TransportError error = new TransportError();
                        error.type = TransportError.Type.kEncryptionFailed;
                        error.message = string.Format("Message encryption failed. type:{0}",
                                                      (int)enc_type);
                        onFailure(error);
                        return false;
                    }
                }

                if (msg.body.Count > kMaxPayloadSize)
                {
                    FunDebug.LogWarning("The message size is too large, which can cause unexpected problems. " +
                                        "Please make the message size smaller than {0} bytes.", kMaxPayloadSize);
                }

                makeHeader(msg, uncompressed_size);

                msg.ready = true;

                return true;
            }

            bool rebuildMessage (FunapiMessage msg)
            {
                if (msg.body.Count <= 0)
                    return true;

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
                        TransportError error = new TransportError();
                        error.type = TransportError.Type.kEncryptionFailed;
                        error.message = string.Format("Message encryption failed. type:{0}",
                                                      (int)enc_type);
                        onFailure(error);
                        return false;
                    }

                    makeHeader(msg, uncompressed_size);

                    if (debug.IsDebug)
                    {
                        logSendingMessageInfo(msg);
                    }
                }

                return true;
            }

            void logSendingMessageInfo (FunapiMessage msg, UInt32 ack = 0)
            {
                StringBuilder log = new StringBuilder();

                log.AppendFormat("[{0}] [C->S] {1}: type={2}, length={3}",
                                 str_protocol_, convertString(encoding_), msg.msg_type, msg.body.Count);

                if (msg.body.Count > 0)
                {
                    if (encoding_ == FunEncoding.kJson)
                    {
                        string str = System.Text.Encoding.UTF8.GetString(msg.body.Array, msg.body.Offset, msg.body.Count);
                        log.AppendFormat(" {0}", str);
                    }
                    else if (encoding_ == FunEncoding.kProtobuf)
                    {
                        FunMessage funmsg = msg.message as FunMessage;

                        if (funmsg.sidSpecified)
                        {
                            log.AppendFormat(", sid={0}", SessionId.ToString(funmsg.sid));
                        }

                        if (funmsg.seqSpecified)
                        {
                            log.AppendFormat(", seq={0}", funmsg.seq);
                        }

                        if (funmsg.ackSpecified)
                        {
                            log.AppendFormat(", ack={0}", funmsg.ack);
                        }

                        FunapiMessage.DebugString(funmsg, log);
                    }
                }

                debug.LogDebug(log.ToString());
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
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = string.Format("[{0}] Failure in sendPendingMessages: {1}",
                                                  str_protocol_, e.ToString());
                    onFailure(error);
                }
            }

            protected int getSendingBufferLength ()
            {
                int length = 0;

                lock (sending_lock_)
                {
                    foreach (FunapiMessage msg in sending_)
                    {
                        if (length > 0 && (length + msg.header.Count + msg.body.Count) > kSendBufferMax)
                            break;

                        if (msg.header.Count > 0)
                        {
                            length += msg.header.Count;
                        }
                        if (msg.body.Count > 0)
                        {
                            length += msg.body.Count;
                        }
                    }
                }

                return length;
            }

            protected void checkPendingMessages ()
            {
                lock (sending_lock_)
                {
                    if (sending_.Count > 0)
                    {
                        wireSend();
                    }
                    else if (isSendable)
                    {
                        sendPendingMessages();
                    }
                }
            }

            void sendPublicKey (EncryptionType type)
            {
                debug.LogDebug("[{0}] Sending a {1}-pubkey message.", str_protocol_, (int)type);

                lock (sending_lock_)
                {
                    FunapiMessage msg = new FunapiMessage(protocol_, kEncryptionPublicKey, null, type);
                    first_.Add(msg);
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
                    debug.LogDebug("[{0}] Compacting the receive buffer to save {1} bytes.",
                                   str_protocol_, next_decoding_offset_);
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0,
                                     received_size_ - next_decoding_offset_);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    debug.LogDebug("[{0}] Increasing the receive buffer to {1} bytes.",
                                   str_protocol_, new_length);
                    Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                    receive_buffer_ = new_buffer;
                }
            }

            protected void parseMessages ()
            {
                lock (messages_lock_)
                {
                    while (true)
                    {
                        if (next_decoding_offset_ >= received_size_)
                            break;

                        int offset = next_decoding_offset_;
                        bool valid = false;

                        for (; offset + 1 < received_size_; ++offset)
                        {
                            if (receive_buffer_[offset] == '\n' && receive_buffer_[offset + 1] == '\n')
                            {
                                valid = true;
                                break;
                            }
                        }

                        if (!valid)
                        {
                            // Not enough bytes. Wait for more bytes to come.
                            break;
                        }

                        // The last two bytes are two '\n'
                        int header_length = offset - next_decoding_offset_ + 2;
                        string header = System.Text.Encoding.ASCII.GetString(receive_buffer_,
                                                                             next_decoding_offset_, header_length);

                        Dictionary<string, string> fields = new Dictionary<string, string>();
                        string[] lines = header.Split(kHeaderDelimeterAsChars);

                        foreach (string line in lines)
                        {
                            if (line.Length == 0)
                                break;

                            string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                            if (tuple.Length != 2)
                            {
                                debug.LogWarning("[{0}] Header has a invalid tuple - '{1}'", str_protocol_, line);
                                continue;
                            }

                            fields[tuple[0].ToUpper()] = tuple[1];
                        }

                        if (!fields.ContainsKey(kLengthHeaderField))
                        {
                            throw new Exception(string.Format("{0} header is invalid. It doesn't have '{1}' field",
                                                              str_protocol_, kLengthHeaderField));
                        }

                        offset = next_decoding_offset_ + header_length;

                        // Body length
                        int body_length = Convert.ToInt32(fields[kLengthHeaderField]);
                        if (received_size_ - offset < body_length)
                        {
                            // Need more bytes for a message body. Waiting.
                            break;
                        }

                        // Makes a raw message
                        if (!readBodyAndSaveMessage(fields, offset))
                        {
                            break;
                        }
                    }
                }
            }

            protected bool readBodyAndSaveMessage (Dictionary<string, string> headers, int offset = 0)
            {
                lock (messages_lock_)
                {
                    if (headers.Count <= 0)
                    {
                        debug.LogWarning("[{0}] Header list is empty.", str_protocol_);
                        return false;
                    }

                    if (!headers.ContainsKey(kVersionHeaderField))
                    {
                        throw new Exception(string.Format("[{0}] Header is invalid. It doesn't have '{1}' field",
                                                          str_protocol_, kVersionHeaderField));
                    }

                    if (!headers.ContainsKey(kLengthHeaderField))
                    {
                        throw new Exception(string.Format("[{0}] Header is invalid. It doesn't have '{1}' field",
                                                          str_protocol_, kLengthHeaderField));
                    }

                    // Protocol version
                    int version = Convert.ToInt32(headers[kVersionHeaderField]);
                    if (version != FunapiVersion.kProtocolVersion)
                    {
                        throw new Exception(string.Format("The protocol version doesn't match. client:{0} server:{1}",
                                                          FunapiVersion.kProtocolVersion, version));
                    }

                    // Body length
                    int body_length = Convert.ToInt32(headers[kLengthHeaderField]);
                    if (received_size_ - offset < body_length)
                    {
                        // Need more bytes.
                        debug.LogError("[{0}] Received {1} bytes but body length is {2}.",
                                       str_protocol_, received_size_ - offset, body_length);
                        return false;
                    }

                    // Makes raw message buffer
                    RawMessage message = new RawMessage();

                    if (headers.ContainsKey(kEncryptionHeaderField))
                        message.encryption_header = Convert.ToString(headers[kEncryptionHeaderField]);

                    if (headers.ContainsKey(kUncompressedLengthHeaderField))
                        message.uncompressed_size = Convert.ToInt32(headers[kUncompressedLengthHeaderField]);

                    if (body_length > 0)
                    {
                        byte[] buffer = new byte[body_length];
                        Buffer.BlockCopy(receive_buffer_, offset, buffer, 0, body_length);
                        message.body = new ArraySegment<byte>(buffer, 0, body_length);
                    }
                    else
                    {
                        message.body = new ArraySegment<byte>();
                    }

                    next_decoding_offset_ = offset + body_length;

                    messages_.Enqueue(message);
                }

                return true;
            }

            bool decodeMessages ()
            {
                lock (messages_lock_)
                {
                    if (messages_.Count == 0)
                        return true;

                    while (messages_.Count > 0)
                    {
                        RawMessage msg = messages_.Dequeue();

                        // Handshaking
                        string encryption_type = "";
                        if (!string.IsNullOrEmpty(msg.encryption_header))
                            parseEncryptionHeader(ref encryption_type, ref msg.encryption_header);

                        if (state_ == State.kHandshaking)
                        {
                            if (msg.body.Count != 0)
                            {
                                debug.LogWarning("[{0}] This message will be ignored. " +
                                                 "It might come from previous connection.", str_protocol_);
                                return false;
                            }

                            if (doHandshaking(encryption_type, msg.encryption_header))
                            {
                                if (state_ == State.kUnknown)
                                    return false;

                                state_ = State.kConnected;
                                debug.LogDebug("[{0}] Handshaking is complete.", str_protocol_);

                                // Send public key (Do not change this order)
                                if (hasEncryption(EncryptionType.kChaCha20Encryption))
                                    sendPublicKey(EncryptionType.kChaCha20Encryption);

                                if (hasEncryption(EncryptionType.kAes128Encryption))
                                    sendPublicKey(EncryptionType.kAes128Encryption);

                                onStarted();

                                if (state_ == State.kEstablished)
                                {
                                    sendPendingMessages();
                                }
                            }
                        }

                        if (msg.body.Count > 0)
                        {
                            // Decrypts
                            if (encryption_type.Length > 0)
                            {
                                if (!decryptMessage(msg.body, encryption_type, msg.encryption_header))
                                    return false;
                            }

                            // Uncompresses
                            if (msg.uncompressed_size > 0)
                            {
                                if (compressor_ == null)
                                {
                                    debug.LogError("Received a compressed message. " +
                                                   "But the transport is not configured with compression.");
                                    return false;
                                }

                                ArraySegment<byte> decompressed = compressor_.Decompress(msg.body, msg.uncompressed_size);
                                if (decompressed.Count == 0)
                                {
                                    debug.LogError("Failed to decompress the message.");
                                    return false;
                                }

                                debug.LogDebug("[{0}] Decompress message: {1}bytes -> {2}bytes",
                                               str_protocol_, msg.body.Count, decompressed.Count);

                                msg.body = decompressed;
                            }
                        }

                        if (first_message_)
                        {
                            first_message_ = false;
                            cstate_ = ConnectState.kConnected;

                            resetConnectionTimeout();
                        }

                        if (!onReceivedMessage(msg.body))
                        {
                            // Udp message errors are not treated as errors.
                            if (protocol_ != TransportProtocol.kUdp)
                            {
                                messages_.Clear();
                                break;
                            }
                        }
                    }
                }

                return true;
            }

            bool onReceivedMessage (ArraySegment<byte> body)
            {
                if (body.Count <= 0)
                    return true;

                object message = null;
                string msg_type = "";

                try
                {
                    // Deserializing a message
                    message = FunapiMessage.Deserialize(body, encoding_);
                    if (message == null)
                    {
                        debug.LogWarning("[{0}] Failed to deserialize a message.", str_protocol_);
                        return false;
                    }

                    UInt32 ack = 0;

                    // Checks ack and seq, gets message type
                    if (encoding_ == FunEncoding.kJson)
                    {
                        if (IsReliable)
                        {
                            if (json_helper_.HasField(message, kAckNumberField))
                            {
                                ack = (UInt32)json_helper_.GetIntegerField(message, kAckNumberField);
                                onAckReceived(ack);
                            }

                            if (json_helper_.HasField(message, kSeqNumberField))
                            {
                                UInt32 seq = (UInt32)json_helper_.GetIntegerField(message, kSeqNumberField);
                                if (!onSeqReceived(seq))
                                    return true;
                            }
                        }

                        if (json_helper_.HasField(message, kMessageTypeField))
                        {
                            msg_type = json_helper_.GetStringField(message, kMessageTypeField);
                        }
                        else if (ack > 0)
                        {
                            msg_type = "_ack";
                        }
                    }
                    else if (encoding_ == FunEncoding.kProtobuf)
                    {
                        FunMessage funmsg = (FunMessage)message;

                        if (IsReliable)
                        {
                            if (funmsg.ackSpecified)
                            {
                                ack = funmsg.ack;
                                onAckReceived(ack);
                            }

                            if (funmsg.seqSpecified)
                            {
                                if (!onSeqReceived(funmsg.seq))
                                    return true;
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
                        else if (ack > 0)
                        {
                            msg_type = "_ack";
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
                                if (encoding_ == FunEncoding.kJson)
                                {
                                    if (!json_helper_.HasField(message, kUdpHandshakeIdField))
                                    {
                                        session_id_has_been_sent = true;
                                    }
                                }
                                else if (encoding_ == FunEncoding.kProtobuf)
                                {
                                    FunMessage funmsg = (FunMessage)message;

                                    if (!funmsg.udp_handshake_idSpecified)
                                    {
                                        session_id_has_been_sent = true;
                                    }
                                }
                            }
                        }
                    }

                    if (msg_type.Length > 0)
                    {
                        // Checks ping messages
                        if (msg_type == kServerPingMessageType)
                        {
                            onServerPingMessage(message);
                            return true;
                        }
                        else if (msg_type == kClientPingMessageType)
                        {
                            onClientPingMessage(message);
                            return true;
                        }
                    }
                    else
                    {
                        if (state_ == State.kWaitForAck)
                        {
                            lock (sending_lock_)
                            {
                                foreach (FunapiMessage msg in sent_queue_)
                                {
                                    sendMessage(msg);
                                }
                            }

                            onStandby();
                        }
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kDeserializeFailed;
                    error.message = string.Format("[{0}] Failure in onReceivedMessage: {1}",
                                                  str_protocol_, e.ToString());

                    if (protocol_ == TransportProtocol.kUdp)
                    {
                        last_error_code_ = error.type;
                        last_error_message_ = error.message;

                        debug.Log(error.message);
                    }
                    else
                    {
                        onFailure(error);
                    }

                    return false;
                }

                if (debug.IsDebug)
                {
                    StringBuilder log = new StringBuilder();

                    log.AppendFormat("[{0}] [S->C] {1}: type={2}, length={3}",
                                     str_protocol_, convertString(encoding_), msg_type, body.Count);

                    if (body.Count > 0)
                    {
                        if (encoding_ == FunEncoding.kJson)
                        {
                            string str = System.Text.Encoding.UTF8.GetString(body.Array, body.Offset, body.Count);
                            log.AppendFormat(" {0}", str);
                        }
                        else if (encoding_ == FunEncoding.kProtobuf)
                        {
                            FunMessage funmsg = (FunMessage)message;

                            if (funmsg.sidSpecified)
                            {
                                log.AppendFormat(", sid={0}", SessionId.ToString(funmsg.sid));
                            }

                            if (funmsg.seqSpecified)
                            {
                                log.AppendFormat(", seq={0}", funmsg.seq);
                            }

                            if (funmsg.ackSpecified)
                            {
                                log.AppendFormat(", ack={0}", funmsg.ack);
                            }

                            FunapiMessage.DebugString(funmsg, log);
                        }
                    }

                    debug.LogDebug(log.ToString());
                }

                if (msg_type != kUdpAttachedType && ReceivedCallback != null)
                    ReceivedCallback(this, msg_type, message);

                if (!string.IsNullOrEmpty(msg_type))
                {
                    if (protocol_ == TransportProtocol.kUdp && !Connected)
                    {
                        if (msg_type == kSessionOpenedType || msg_type == kUdpAttachedType)
                        {
                            processUdpFirstMessageReply(msg_type, message);
                        }
                    }
                }

                return true;
            }

            protected void processUdpFirstMessageReply(string msg_type, object message)
            {
                Guid received_udp_handshake_id = Guid.Empty;

                if (encoding_ == FunEncoding.kJson)
                {
                    if (json_helper_.HasField(message, kUdpHandshakeIdField))
                    {
                        string received_id_str = json_helper_.GetStringField(message, kUdpHandshakeIdField);
                        received_udp_handshake_id = new Guid(received_id_str);
                    }
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    FunMessage funmsg = (FunMessage)message;

                    if (funmsg.udp_handshake_idSpecified)
                    {
                        received_udp_handshake_id = new Guid(funmsg.udp_handshake_id);
                    }
                }

                if (received_udp_handshake_id == Guid.Empty)
                {
                    debug.LogWarning("[{0}] udp handshake id is null. This message is ignored. message_type:{1}", str_protocol_, msg_type);
                    return;
                }
                else if (!Guid.Equals(received_udp_handshake_id, udp_handshake_id_))
                {
                    debug.LogWarning("[{0}] This message is ignored. " +
                                        "It might come from previous connection. message_type:{1}", str_protocol_, msg_type);
                    return;
                }

                if(state_ == State.kConnecting)
                {
                    state_ = State.kConnected;
                    onStarted();
                }
            }

            protected void onFailedSending ()
            {
                lock (sending_lock_)
                {
                    if (sending_.Count <= 0)
                        return;

                    debug.LogWarning("[{0}] Sending failed. Clears the sending buffer. ({1})",
                                     str_protocol_, sending_.Count);

                    sending_.Clear();
                }
            }


            //---------------------------------------------------------------------
            // Ping-related functions
            //---------------------------------------------------------------------
            bool isPingEnabled ()
            {
                return enable_ping_ && (protocol == TransportProtocol.kTcp || protocol == TransportProtocol.kWebsocket);

            }

            void startPingTimer ()
            {
                if (!isPingEnabled())
                {
                    return;
                }

                float interval = 0f;
                float timeout = 0f;
                if (protocol_ == TransportProtocol.kTcp)
                {
                    TcpTransportOption tcp_option = option_ as TcpTransportOption;
                    interval = tcp_option.PingIntervalSeconds;
                    timeout = tcp_option.PingTimeoutSeconds;
                }
                else // if (protocol_ == TransportProtocol.kWebsocket)
                {
                    WebsocketTransportOption websocket_option = option_ as WebsocketTransportOption;
                    interval = websocket_option.PingIntervalSeconds;
                    timeout = websocket_option.PingTimeoutSeconds;
                }

                if (interval <= 0f)
                {
                    interval = kPingIntervalDefault;
                }

                ping_timer_ = new FunapiPingTimer(interval, timeout,
                                                  onPingUpdate, onPingTimeout);
                timer_.Add(ping_timer_, true);

                debug.Log("[{0}] Ping timer started. interval: {1}s timeout: {2}s",
                          str_protocol_, interval, timeout);
            }

            void stopPingTimer ()
            {
                if (!isPingEnabled())
                {
                    return;
                }

                lock (sending_lock_)
                {
                    if (pending_.Count > 0)
                        pending_.RemoveAll(msg => { return msg.msg_type == kClientPingMessageType; });
                }

                if (ping_timer_ != null)
                {
                    timer_.Remove(ping_timer_);
                    ping_timer_ = null;

                    debug.Log("[{0}] Ping timer stopped.", str_protocol_);
                }

                ping_time_ = 0;
            }

            void onPingUpdate (float deltaTime)
            {
                if (!Connected)
                {
                    stopPingTimer();
                    return;
                }

                sendPingMessage();

                if (ping_timer_ != null)
                {
                    ping_timer_.StartTimeout();
                }
            }

            void onPingTimeout ()
            {
                debug.LogWarning("[{0}] Ping timer timed out.", str_protocol_);

                TransportError error = new TransportError();
                error.type = TransportError.Type.kDisconnected;
                error.message = string.Format("[{0}] Didn't received a ping message for a long time.",
                                              str_protocol_);
                onDisconnected(error);
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

                if (enable_ping_log_)
                {
                    debug.LogDebug("[{0}] Send ping - timestamp: {1}", str_protocol_, timestamp);
                }
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

                if (ping_timer_ != null)
                {
                    ping_timer_.StopTimeout();
                }

                if (timestamp != 0)
                {
                    ping_time_ = (int)((DateTime.Now.Ticks - timestamp) / 10000);
                }
                else
                {
                    ping_time_ = 0;
                }

                if (enable_ping_log_)
                {
                    debug.LogDebug("[{0}] Received ping - timestamp:{1} time={2}ms",
                                   str_protocol_, timestamp, ping_time_);
                }
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

            class RawMessage
            {
                public string encryption_header = "";
                public int uncompressed_size = 0;
                public ArraySegment<byte> body;
            }


            // constants.
            const int kReconnectCountMax = 3;
            const int kPingIntervalDefault = 3;

            // Buffer-related constants.
            protected const int kUnitBufferSize = 65536; // 64kb
            protected const int kSendBufferMax = 32768;  // 32kb

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
            protected const int kMaxPayloadSize = 1048576; // 1MB

            // for speed-up.
            static readonly char[] kHeaderDelimeterAsChars = kHeaderDelimeter.ToCharArray();
            static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

            // Event handlers
            public event Func<TransportProtocol, FunapiCompressor> CreateCompressorCallback;
            public event Action<TransportProtocol, TransportEventType> EventCallback;
            public event Action<TransportProtocol, TransportError> ErrorCallback;
            public event Action<Transport, string, object> ReceivedCallback;        // msg_type, message

            // member variables.
            protected State state_;
            protected SessionId session_id_ = new SessionId();
            protected TransportProtocol protocol_;
            protected string str_protocol_;
            protected FunEncoding encoding_ = FunEncoding.kNone;
            protected TransportOption option_ = null;
            protected PostEventList event_ = new PostEventList();
            protected FunapiTimerList timer_ = new FunapiTimerList();
            protected bool is_paused_ = false;
            Guid udp_handshake_id_ = Guid.Empty;

            // Connect-related member variables.
            ConnectState cstate_ = ConnectState.kUnknown;
            bool auto_reconnect_ = false;
            float exponential_time_ = 0f;
            bool redirecting_ = false;

            // Ping-related variables.
            bool enable_ping_ = false;
            bool enable_ping_log_ = false;
            FunapiPingTimer ping_timer_ = null;
            int ping_time_ = 0;

            // Message-related variables.
            bool first_sending_ = true;
            bool first_message_ = true;
            protected int received_size_ = 0;
            protected int next_decoding_offset_ = 0;
            protected object receive_lock_ = new object();
            protected byte[] receive_buffer_ = new byte[kUnitBufferSize];
            object messages_lock_ = new object();
            Queue<RawMessage> messages_ = new Queue<RawMessage>();

            bool send_session_id_only_once_ = false;
            bool session_id_has_been_sent = false;
            object session_id_sent_lock_ = new object();
            protected object sending_lock_ = new object();
            protected List<FunapiMessage> first_ = new List<FunapiMessage>();
            protected List<FunapiMessage> pending_ = new List<FunapiMessage>();
            protected List<FunapiMessage> sending_ = new List<FunapiMessage>();

            // Compression related variables.
            FunCompressionType compression_type_ = FunCompressionType.kNone;
            FunapiCompressor compressor_ = null;

            // Reliability-related variables.
            UInt32 seq_ = 0;
            UInt32 last_seq_ = 0;
            UInt32 sent_ack_ = 0;
            bool first_seq_ = true;
            float delayed_ack_interval_ = 0f;
            byte[] encryption_pub_key_ = null;
            Queue<FunapiMessage> sent_queue_ = new Queue<FunapiMessage>();
            Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();

            // Error-related member variables.
            protected TransportError.Type last_error_code_ = TransportError.Type.kNone;
            protected string last_error_message_ = "";
        }
    }

}  // namespace Fun
