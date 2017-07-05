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
    public enum TransportEventType
    {
        kStarted,
        kStopped,
        kConnectionFailed,
        kConnectionTimedOut,
        kDisconnected
    };

    public class TransportError
    {
        public enum Type
        {
            kNone,
            kStartingFailed,
            kConnectingFailed,
            kInvalidSequence,
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
        public bool SequenceValidation = false;
        public float ConnectionTimeout = 0f;
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
    }

    public class HttpTransportOption : TransportOption
    {
        public bool HTTPS = false;
        public bool UseWWW = false;
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

                    // Resets states.
                    first_receiving_ = true;
                    header_decoded_ = false;
                    received_size_ = 0;
                    next_decoding_offset_ = 0;
                    header_fields_.Clear();
                    sending_.Clear();
                    resetEncryptors();

                    if (option_.ConnectionTimeout > 0f)
                    {
                        timer_.Remove(connect_timer_id_);
                        connect_timer_id_ = timer_.Add(onConnectionTimedout, option_.ConnectionTimeout);
                    }

                    onStart();
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kStartingFailed;
                    last_error_message_ = string.Format("{0} failure in Start: {1}", str_protocol_, e.ToString());
                    event_.Add(onFailure);
                }
            }

            public void Redirect (string host, ushort port)
            {
                ip_list_.Replace(host, port);
                setNextAddress();

                Log("'{0}' Try to redirect to server.", str_protocol_);
                exponential_time_ = 1f;
                reconnect_count_ = 0;

                Start();
            }

            // Stops transport
            public void Stop ()
            {
                if (state_ == State.kUnknown)
                    return;

                state_ = State.kUnknown;

                timer_.Clear();
                stopPingTimer();
                connect_timer_id_ = 0;
                session_id_has_been_sent = false;

                onClose();

                if (Reconnecting)
                    event_.Add(onReconnecting);

                if (StoppedCallback != null)
                    StoppedCallback(protocol_);
            }

            public void SetEstablish (SessionId sid, bool sendSessionIdOnlyOnce)
            {
                state_ = State.kEstablished;

                session_id_.SetId(sid);
                send_session_id_only_once_ = sendSessionIdOnlyOnce;

                if (enable_ping_ && ping_interval_ > 0)
                {
                    startPingTimer();
                }
            }

            public void SetAbolish ()
            {
                session_id_.Clear();
                pending_.Clear();
            }

            // Sends a message
            public void SendMessage (FunapiMessage fun_msg)
            {
                try
                {
                    // Add session id
                    if (session_id_.IsValid && fun_msg.message != null)
                    {
                        bool do_not_send = (protocol == TransportProtocol.kTcp || protocol == TransportProtocol.kUdp)
                                           && send_session_id_only_once_ && session_id_has_been_sent;

                        if (do_not_send == false)
                        {
                            if (send_session_id_only_once_)
                                session_id_has_been_sent = true;

                            if (encoding == FunEncoding.kJson)
                            {
                                json_helper_.SetStringField(fun_msg.message, kSessionIdField, session_id_);
                            }
                            else if (encoding == FunEncoding.kProtobuf)
                            {
                                FunMessage msg = fun_msg.message as FunMessage;
                                msg.sid = session_id_;
                            }
                        }
                    }

                    // Add message type
                    if (fun_msg.msg_type != null && fun_msg.msg_type.Length > 0 &&
                        fun_msg.message != null && fun_msg.msg_type != kAckNumberField)
                    {
                        if (encoding == FunEncoding.kJson)
                        {
                            json_helper_.SetStringField(fun_msg.message, kMessageTypeField, fun_msg.msg_type);
                        }
                        else if (encoding == FunEncoding.kProtobuf)
                        {
                            FunMessage msg = fun_msg.message as FunMessage;
                            if (fun_msg.msg_type.Contains(kIntMessageType))
                            {
                                msg.msgtype2 = Convert.ToInt32(fun_msg.msg_type.Substring(kIntMessageType.Length));
                            }
                            else
                            {
                                msg.msgtype = fun_msg.msg_type;
                            }
                        }
                    }

                    // Sending...
                    lock (sending_lock_)
                    {
                        fun_msg.buffer = new ArraySegment<byte>(fun_msg.GetBytes(encoding_));
                        pending_.Add(fun_msg);

                        if (Started && isSendable)
                        {
                            sendPendingMessages();
                        }
                    }
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = string.Format("{0} failure in SendMessage: {1}", str_protocol_, e.ToString());
                    event_.Add(onFailure);
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
            public HostAddr address
            {
                get { return ip_list_.GetCurAddress(); }
            }

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

            public abstract bool Started { get; }

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
                    if (protocol == TransportProtocol.kTcp)
                    {
                        if (Started && HasUnsentMessages)
                            return true;
                    }

                    return false;
                }
            }

            // If the transport has unsent messages..
            public bool HasUnsentMessages
            {
                get { lock (sending_lock_) { return sending_.Count > 0 || pending_.Count > 0; } }
            }

            public bool SequenceValidation
            {
                get { return option_.SequenceValidation; }
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


            protected abstract void setAddress (HostAddr addr);

            // Creates a socket.
            protected abstract void onStart ();

            // Closes a socket
            protected abstract void onClose ();

            // Sends a packet.
            protected abstract void wireSend ();

            // Is able to sending?
            protected virtual bool isSendable
            {
                get { lock (sending_lock_) { return sending_.Count == 0; } }
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

            protected void initAddress (string hostname, UInt16 port)
            {
                ip_list_.Add(hostname, port);
                setNextAddress();
            }

            protected void initAddress (string hostname, UInt16 port, bool https)
            {
                ip_list_.Add(hostname, port, https);
                setNextAddress();
            }

            bool setNextAddress ()
            {
                HostAddr addr = ip_list_.GetNextAddress();
                if (addr != null)
                {
                    setAddress(addr);
                }
                else
                {
                    Log("{0} setNextAddress - There's no available address.", str_protocol_);
                    return false;
                }

                return true;
            }

            protected void onStarted ()
            {
                if (StartedCallback != null)
                    StartedCallback(protocol_);
            }

            protected void onDisconnected ()
            {
                Stop();

                if (DisconnectedCallback != null)
                    DisconnectedCallback(protocol_);
            }

            void onConnectionTimedout ()
            {
                if (state_ == State.kUnknown || state_ == State.kEstablished)
                    return;

                Log("{0} Connection waiting time has been exceeded.", str_protocol_);

                checkReconnect();

                if (ConnectionTimeoutCallback != null)
                    ConnectionTimeoutCallback(protocol_);
            }

            void onConnectionFailed ()
            {
                ip_list_.SetFirst();

                if (ConnectionFailedCallback != null)
                    ConnectionFailedCallback(protocol_);
            }


            //---------------------------------------------------------------------
            // auto-reconnect-related functions
            //---------------------------------------------------------------------
            void checkReconnect ()
            {
                if (!auto_reconnect_ || Reconnecting)
                    return;

                cstate_ = ConnectState.kReconnecting;
                exponential_time_ = 1f;
            }

            bool startReconnect ()
            {
                if (reconnect_count_ >= 0)
                {
                    if (tryToReconnect())
                        return true;
                }

                if (!ip_list_.IsNextAvailable)
                    return false;

                event_.Add (delegate
                {
                    Log("'{0}' Try to connect to server.", str_protocol_);
                    exponential_time_ = 1f;
                    reconnect_count_ = 0;
                    setNextAddress();
                    Start();
                });

                return true;
            }

            bool tryToReconnect ()
            {
                ++reconnect_count_;
                if (reconnect_count_ > kReconnectCountMax)
                    return false;

                float delay_time = exponential_time_;
                exponential_time_ *= 2f;

                Log("Wait {0} seconds for reconnect to {1} transport.", delay_time, str_protocol_);

                event_.Add (delegate
                    {
                        Log("'{0}' Try to reconnect to server.", str_protocol_);
                        Start();
                    },
                    delay_time
                );

                return true;
            }

            void onReconnecting ()
            {
                if (!startReconnect())
                {
                    cstate_ = ConnectState.kUnknown;
                    exponential_time_ = 1f;
                    reconnect_count_ = 0;

                    onConnectionFailed();
                }
            }


            //---------------------------------------------------------------------
            // Message-related functions
            //---------------------------------------------------------------------
            protected void sendPendingMessages ()
            {
                try
                {
                    lock (sending_lock_)
                    {
                        if (sending_.Count > 0)
                        {
                            // If we have more segments to send, we process more.
                            Log("{0} retrying unsent messages.", str_protocol_);
                            wireSend();
                        }
                        else if (isSendable && pending_.Count > 0)
                        {
                            // Otherwise, try to process pending messages.
                            List<FunapiMessage> tmp = sending_;
                            sending_ = pending_;
                            pending_ = tmp;

                            if (!buildingMessages())
                                return;

                            wireSend();
                        }
                    }
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = string.Format("{0} failure in sendPendingMessages: {1}", str_protocol_, e.ToString());
                    event_.Add(onFailure);
                }
            }

            bool buildingMessages ()
            {
                for (int i = 0; i < sending_.Count; i += 2)
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
                            last_error_code_ = TransportError.Type.kEncryptionFailed;
                            last_error_message_ = string.Format("Encrypt message failed. type:{0}", (int)type);
                            event_.Add(onFailure);
                            return false;
                        }
                    }

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
                                        message.buffer.Count, kHeaderDelimeter);
                    if (type != EncryptionType.kNoneEncryption)
                    {
                        header.AppendFormat("{0}{1}{2}", kEncryptionHeaderField, kHeaderFieldDelimeter, Convert.ToInt32(type));
                        header.AppendFormat("-{0}{1}", enc_header, kHeaderDelimeter);
                    }
                    header.Append(kHeaderDelimeter);

                    FunapiMessage header_buffer = new FunapiMessage(protocol_, message.msg_type + " (header)", header);
                    header_buffer.buffer = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(header.ToString()));
                    sending_.Insert(i, header_buffer);

                    DebugLog2("{0} built a message - '{1}' ({2}bytes + {3}bytes)", str_protocol_,
                              message.msg_type, header_buffer.buffer.Count, message.buffer.Count);
                }

                return true;
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
                    DebugLog2("{0} compacting the receive buffer to save {1} bytes.",
                              str_protocol_, next_decoding_offset_);
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0,
                                     received_size_ - next_decoding_offset_);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    DebugLog2("{0} increasing the receive buffer to {1} bytes.", str_protocol_, new_length);
                    Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                    receive_buffer_ = new_buffer;
                }
            }

            // Decoding a messages
            protected void tryToDecodeMessage ()
            {
                if (protocol == TransportProtocol.kTcp)
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
                DebugLog2("{0} trying to decode header fields.", str_protocol_);
                int length = 0;

                for (; next_decoding_offset_ < received_size_; )
                {
                    ArraySegment<byte> haystack = new ArraySegment<byte>(
                        receive_buffer_, next_decoding_offset_, received_size_ - next_decoding_offset_);
                    int offset = bytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                    if (offset < 0)
                    {
                        // Not enough bytes. Wait for more bytes to come.
                        DebugLog2("{0} need more bytes for a header field. Waiting.", str_protocol_);
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
                        DebugLog2("{0} read {1} bytes for header.", str_protocol_, length);
                        return true;
                    }

                    string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                    if (tuple.Length == 2)
                    {
                        tuple[0] = tuple[0].ToUpper();
                        header_fields_[tuple[0]] = tuple[1];
                        DebugLog2("  > {0} header '{1} : {2}'", str_protocol_, tuple[0], tuple[1]);
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
                    DebugLog2("{0} message body is {1} bytes. Buffer has {2} bytes.",
                              str_protocol_, body_length, received_size_ - next_decoding_offset_);
                }
                else
                {
                    DebugLog2("{0} {1} bytes left in buffer.",
                              str_protocol_, received_size_ - next_decoding_offset_);
                }

                if (received_size_ - next_decoding_offset_ < body_length)
                {
                    // Need more bytes.
                    DebugLog2("{0} need more bytes for a message body. Waiting.", str_protocol_);
                    return false;
                }

                // Encryption
                string encryption_type = "";
                string encryption_header = "";
                if (header_fields_.TryGetValue(kEncryptionHeaderField, out encryption_header))
                    parseEncryptionHeader(ref encryption_type, ref encryption_header);

                if (state_ == State.kHandshaking)
                {
                    FunDebug.Assert(body_length == 0);

                    if (doHandshaking(encryption_type, encryption_header))
                    {
                        state_ = State.kConnected;
                        DebugLog1("{0} handshaking is complete.", str_protocol_);

                        // Send public key (Do not change this order)
                        if (hasEncryption(EncryptionType.kAes128Encryption))
                            sendPublicKey(EncryptionType.kAes128Encryption);

                        if (hasEncryption(EncryptionType.kChaCha20Encryption))
                            sendPublicKey(EncryptionType.kChaCha20Encryption);

                        event_.Add(onHandshakeComplete);
                    }
                }

                if (body_length > 0)
                {
                    if (!Started)
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

                    if (first_receiving_)
                    {
                        first_receiving_ = false;
                        cstate_ = ConnectState.kConnected;

                        timer_.Remove(connect_timer_id_);
                        connect_timer_id_ = 0;
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
                FunapiMessage fun_msg = new FunapiMessage(protocol, kEncryptionPublicKey, null, type);

                // Add a message to front of pending list...
                lock (sending_lock_)
                {
                    fun_msg.buffer = new ArraySegment<byte>(fun_msg.GetBytes(encoding_));
                    pending_.Insert(0, fun_msg);
                }

                DebugLog1("{0} sending a {1}-pubkey message.", str_protocol_, (int)type);
            }

            // Sends messages & Calls start callback
            void onHandshakeComplete ()
            {
                lock (sending_lock_)
                {
                    if (Started && isSendable)
                    {
                        sendPendingMessages();
                        onStarted();
                    }
                }
            }

            void onReceived (Dictionary<string, string> header, ArraySegment<byte> body)
            {
                if (body.Count <= 0)
                {
                    if (ReceivedCallback != null)
                        ReceivedCallback(new FunapiMessage(protocol_, ""));

                    return;
                }

                // Deserializing a message
                object message = FunapiMessage.Deserialize(body, encoding_);
                if (message == null)
                {
                    LogWarning("{0} failed to deserialize a message.", str_protocol_);
                    return;
                }

                // Gets message type
                string msg_type = "";
                if (encoding_ == FunEncoding.kJson)
                {
                    if (json_helper_.HasField(message, kMessageTypeField))
                    {
                        msg_type = json_helper_.GetStringField(message, kMessageTypeField);
                        json_helper_.RemoveField(message, kMessageTypeField);
                    }
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    FunMessage funmsg = (FunMessage)message;

                    if (funmsg.msgtypeSpecified)
                    {
                        msg_type = funmsg.msgtype;
                    }
                    else if (funmsg.msgtype2Specified)
                    {
                        msg_type = MessageTable.Lookup((MessageType)funmsg.msgtype2);
                    }
                }

                // Checks ping messages
                if (msg_type.Length > 0)
                {
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

                if (ReceivedCallback != null)
                    ReceivedCallback(new FunapiMessage(protocol_, msg_type, message));
            }

            //---------------------------------------------------------------------
            // Ping-related functions
            //---------------------------------------------------------------------
            void startPingTimer ()
            {
                if (protocol_ != TransportProtocol.kTcp)
                    return;

                if (ping_timer_id_ != 0)
                    timer_.Remove(ping_timer_id_);

                ping_timer_id_ = timer_.Add (() => onPingTimerEvent(), true, ping_interval_);
                ping_wait_time_ = 0f;

                Log("Start ping - interval seconds: {0}, timeout seconds: {1}",
                    ping_interval_, ping_timeout_);
            }

            void stopPingTimer ()
            {
                if (protocol_ != TransportProtocol.kTcp)
                    return;

                timer_.Remove(ping_timer_id_);
                ping_timer_id_ = 0;
                ping_time_ = 0;
            }

            void onPingTimerEvent ()
            {
                if (ping_wait_time_ > ping_timeout_)
                {
                    LogWarning("Network seems disabled. Stopping the transport.");
                    onDisconnected();
                    return;
                }

                sendPingMessage();
            }

            void sendPingMessage ()
            {
                if (!session_id_.IsValid)
                    return;

                long timestamp = DateTime.Now.Ticks;

                if (encoding_ == FunEncoding.kJson)
                {
                    object msg = FunapiMessage.Deserialize("{}");
                    json_helper_.SetIntegerField(msg, kPingTimestampField, timestamp);
                    SendMessage(new FunapiMessage(protocol_, kClientPingMessageType, msg));
                }
                else if (encoding_ == FunEncoding.kProtobuf)
                {
                    FunPingMessage ping = new FunPingMessage();
                    ping.timestamp = timestamp;
                    FunMessage msg = FunapiMessage.CreateFunMessage(ping, MessageType.cs_ping);
                    SendMessage(new FunapiMessage(protocol_, kClientPingMessageType, msg));
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

                    SendMessage(new FunapiMessage(protocol_, kServerPingMessageType, json_helper_.Clone(body)));
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
                    SendMessage(new FunapiMessage(protocol_, kServerPingMessageType, send_msg));
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


            //---------------------------------------------------------------------
            // Transport error
            //---------------------------------------------------------------------
            protected virtual void onFailure ()
            {
                LogError("{0} : onFailure - state: {1}, error: {2}\n{3}\n",
                         str_protocol_, state_, last_error_code_, last_error_message_);

                if (state_ != State.kEstablished)
                {
                    checkReconnect();
                    Stop();

                    if (auto_reconnect_ && Reconnecting)
                        return;

                    onConnectionFailed();
                }
                else
                {
                    if (TransportErrorCallback != null)
                        TransportErrorCallback(protocol_);
                }
            }


            public enum State
            {
                kUnknown = 0,
                kConnecting,
                kHandshaking,
                kConnected,
                kWaitForSessionId,
                kWaitForAck,
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

            // Buffer-related constants.
            protected const int kUnitBufferSize = 65536;

            // Funapi header-related constants.
            protected const string kHeaderDelimeter = "\n";
            protected const string kHeaderFieldDelimeter = ":";
            protected const string kVersionHeaderField = "VER";
            protected const string kPluginVersionHeaderField = "PVER";
            protected const string kLengthHeaderField = "LEN";
            protected const string kEncryptionHeaderField = "ENC";

            // message-related constants.
            const string kEncryptionPublicKey = "_pub_key";
            const string kServerPingMessageType = "_ping_s";
            const string kClientPingMessageType = "_ping_c";
            const string kPingTimestampField = "timestamp";
            const string kAckNumberField = "_ack";

            // for speed-up.
            static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(System.Text.Encoding.ASCII.GetBytes(kHeaderDelimeter));
            static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

            // Event handlers
            public event EventNotifyHandler StartedCallback;
            public event EventNotifyHandler StoppedCallback;
            public event MessageNotifyHandler ReceivedCallback;
            public event EventNotifyHandler TransportErrorCallback;

            public event EventNotifyHandler ConnectionFailedCallback;
            public event EventNotifyHandler ConnectionTimeoutCallback;
            public event EventNotifyHandler DisconnectedCallback;

            // member variables.
            protected State state_;
            protected SessionId session_id_ = new SessionId();
            protected TransportProtocol protocol_;
            protected string str_protocol_;
            protected FunEncoding encoding_ = FunEncoding.kNone;
            protected TransportOption option_ = null;
            protected ThreadSafeEventList event_ = new ThreadSafeEventList();
            protected ThreadSafeEventList timer_ = new ThreadSafeEventList();

            // Connect-related member variables.
            ConnectState cstate_ = ConnectState.kUnknown;
            ConnectList ip_list_ = new ConnectList();
            bool auto_reconnect_ = false;
            int connect_timer_id_ = 0;
            float exponential_time_ = 0f;
            int reconnect_count_ = 0;

            // Ping-related variables.
            bool enable_ping_ = false;
            bool enable_ping_log_ = false;
            int ping_time_ = 0;
            int ping_timer_id_ = 0;
            int ping_interval_ = 0;
            float ping_timeout_ = 0f;
            float ping_wait_time_ = 0f;

            // Message-related variables.
            bool first_sending_ = true;
            bool first_receiving_ = true;
            bool header_decoded_ = false;
            bool send_session_id_only_once_ = false;
            bool session_id_has_been_sent = false;
            Dictionary<string, string> header_fields_ = new Dictionary<string, string>();

            protected int received_size_ = 0;
            protected int next_decoding_offset_ = 0;
            protected object sending_lock_ = new object();
            protected object receive_lock_ = new object();
            protected byte[] receive_buffer_ = new byte[kUnitBufferSize];
            protected List<FunapiMessage> pending_ = new List<FunapiMessage>();
            protected List<FunapiMessage> sending_ = new List<FunapiMessage>();

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

                initAddress(hostname_or_ip, port);
            }

            public override bool Started
            {
                get { return sock_ != null && sock_.Connected && state_ >= State.kConnected; }
            }

            protected override void setAddress (HostAddr addr)
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

                TcpTransportOption tcp_option = (TcpTransportOption)option_;
                Log("TCP connect - {0}:{1}\n {2}, {3}, Sequence:{4}, Timeout:{5}, Reconnect:{6}, Nagle:{7}, Ping:{8}",
                    ip, addr.port, convertString(encoding_), convertString(tcp_option.Encryption),
                    tcp_option.SequenceValidation, tcp_option.ConnectionTimeout,
                    tcp_option.AutoReconnect, !tcp_option.DisableNagle, tcp_option.EnablePing);
            }

            protected override void onStart ()
            {
                state_ = State.kConnecting;
                sock_ = new Socket(ip_af_, SocketType.Stream, ProtocolType.Tcp);

                bool disable_nagle = (option_ as TcpTransportOption).DisableNagle;
                if (disable_nagle)
                    sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                sock_.BeginConnect(connect_ep_, new AsyncCallback(this.startCb), this);
            }

            protected override void onClose ()
            {
                if (sock_ != null)
                {
                    sock_.Close();
                    sock_ = null;
                }
            }

            protected override void wireSend ()
            {
                List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();
                int length = 0;

                lock (sending_lock_)
                {
                    foreach (FunapiMessage message in sending_)
                    {
                        if (message.buffer.Count > 0)
                        {
                            list.Add(message.buffer);
                            length += message.buffer.Count;
                        }
                    }
                }

                DebugLog2("TCP sending {0} bytes.", length);

                sock_.BeginSend(list, 0, new AsyncCallback(this.sendBytesCb), this);
            }

            void startCb (IAsyncResult ar)
            {
                try
                {
                    if (sock_ == null)
                        return;

                    sock_.EndConnect(ar);
                    if (sock_.Connected == false)
                    {
                        last_error_code_ = TransportError.Type.kConnectingFailed;
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
                        sock_.BeginReceive(buffer, 0, new AsyncCallback(this.receiveBytesCb), this);
                    }
                }
                catch (ObjectDisposedException)
                {
                    DebugLog1("TCP BeginConnect operation has been cancelled.");
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kConnectingFailed;
                    last_error_message_ = "TCP failure in startCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void sendBytesCb (IAsyncResult ar)
            {
                try
                {
                    if (sock_ == null)
                        return;

                    int nSent = sock_.EndSend(ar);
                    FunDebug.Assert(nSent > 0, "TCP failed to transfer messages.");

                    DebugLog2("TCP sent {0} bytes.", nSent);

                    lock (sending_lock_)
                    {
                        // Removes any segment fully sent.
                        while (nSent > 0)
                        {
                            if (sending_.Count <= 0)
                            {
                                string error = string.Format("TCP couldn't find the sending buffers that sent messages.\n" +
                                                             "Sent {0} more bytes but there are no sending buffers.", nSent);
                                FunDebug.Assert(false, error);
                            }

                            if (sending_[0].buffer.Count > nSent)
                            {
                                // partial data
                                DebugLog2("TCP partially sent. Will resume. (buffer:{0}, nSent:{1})",
                                          sending_[0].buffer.Count, nSent);
                                break;
                            }
                            else
                            {
                                DebugLog2("TCP remove buffer - '{0}' ({1}bytes)",
                                          sending_[0].msg_type, sending_[0].buffer.Count);

                                // fully sent.
                                nSent -= sending_[0].buffer.Count;
                                sending_.RemoveAt(0);
                            }

                            // for empty body.
                            if (sending_.Count > 0 && sending_[0].buffer.Count <= 0)
                            {
                                DebugLog2("TCP remove buffer - '{0}' (0bytes)", sending_[0].msg_type);
                                sending_.RemoveAt(0);
                            }
                        }

                        // If the first segment has been sent partially, we need to reconstruct the first segment.
                        if (nSent > 0)
                        {
                            FunDebug.Assert(sending_.Count > 0);
                            ArraySegment<byte> original = sending_[0].buffer;

                            FunDebug.Assert(sending_[0].buffer.Count > nSent);
                            ArraySegment<byte> adjusted = new ArraySegment<byte>(
                                original.Array, original.Offset + nSent, original.Count - nSent);
                            sending_[0].buffer = adjusted;
                            DebugLog2("TCP partially sending {0} bytes. {1} bytes already sent.", adjusted.Count, nSent);
                        }

                        sendPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    DebugLog1("TCP beginSend operation has been cancelled.");
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
                    if (sock_ == null)
                        return;

                    lock (receive_lock_)
                    {
                        int nRead = sock_.EndReceive(ar);
                        if (nRead > 0)
                        {
                            received_size_ += nRead;
                            DebugLog2("TCP received {0} bytes. Buffer has {1} bytes.",
                                      nRead, received_size_ - next_decoding_offset_);
                        }

                        // Decoding a messages
                        tryToDecodeMessage();

                        if (nRead > 0)
                        {
                            // Checks buffer space
                            checkReceiveBuffer();

                            // Starts another async receive
                            ArraySegment<byte> residual = new ArraySegment<byte>(
                                receive_buffer_, received_size_, receive_buffer_.Length - received_size_);

                            List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                            buffer.Add(residual);

                            sock_.BeginReceive(buffer, 0, new AsyncCallback(this.receiveBytesCb), this);
                            DebugLog2("TCP ready to receive more. We can receive upto {0} more bytes.",
                                      receive_buffer_.Length - received_size_);
                        }
                        else
                        {
                            LogWarning("TCP socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                LogWarning("TCP buffer has {0} bytes but they failed to decode. Discarding.",
                                           receive_buffer_.Length - received_size_);
                            }

                            last_error_code_ = TransportError.Type.kDisconnected;
                            last_error_message_ = "TCP can't receive messages. Maybe the socket is closed.";
                            event_.Add(onDisconnected);
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
            AddressFamily ip_af_;
            IPEndPoint connect_ep_;
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

                initAddress(hostname_or_ip, port);
            }

            public override bool Started
            {
                get { return sock_ != null && state_ >= State.kConnected; }
            }

            protected override void setAddress (HostAddr addr)
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

                Log("UDP connect - {0}:{1}\n {2}, {3}, Sequence:{4}, Timeout:{5}",
                    ip, addr.port, convertString(encoding_), convertString(option_.Encryption),
                    option_.SequenceValidation, option_.ConnectionTimeout);
            }

            protected override void onStart ()
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
                                           ref receive_ep_, new AsyncCallback(this.receiveBytesCb), this);
                }

                onStarted();
            }

            protected override void onClose ()
            {
                if (sock_ != null)
                {
                    sock_.Close();
                    sock_ = null;
                }
            }

            // Send a packet.
            protected override void wireSend ()
            {
                int offset = 0;

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count >= 2);

                    // one header + one body
                    int length = sending_[0].buffer.Count + sending_[1].buffer.Count;
                    if (length > kUdpBufferSize)
                    {
                        string error = string.Format("'{0}' message's length is {1} bytes " +
                                                     "but UDP single message can't bigger than {2} bytes.",
                                                     sending_[1].msg_type, length, kUdpBufferSize);
                        FunDebug.Assert(false, error);
                    }

                    for (int i = 0; i < 2; ++i)
                    {
                        ArraySegment<byte> item = sending_[i].buffer;
                        if (item.Count > 0)
                        {
                            Buffer.BlockCopy(item.Array, 0, send_buffer_, offset, item.Count);
                            offset += item.Count;
                        }
                    }

                    DebugLog2("UDP sending {0} bytes.", length);
                }

                if (offset > 0)
                {
                    sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                      send_ep_, new AsyncCallback(this.sendBytesCb), this);
                }
            }

            void sendBytesCb (IAsyncResult ar)
            {
                try
                {
                    if (sock_ == null)
                        return;

                    lock (sending_lock_)
                    {
                        int nSent = sock_.EndSend(ar);
                        FunDebug.Assert(nSent > 0, "UDP failed to transfer messages.");

                        FunDebug.Assert(sending_.Count >= 2);
                        DebugLog2("UDP sent a message - '{0}' ({1}bytes)", sending_[1].msg_type, nSent);

                        // Removes header and body segment
                        int nLength = 0;
                        for (int i = 0; i < 2; ++i)
                        {
                            nLength += sending_[0].buffer.Count;
                            sending_.RemoveAt(0);
                        }

                        if (nSent != nLength)
                        {
                            string error = string.Format("UDP failed to sending a whole message. " +
                                                         "buffer:{0} sent:{1}", nLength, nSent);
                            FunDebug.Assert(false, error);
                        }

                        sendPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    DebugLog1("UDP BeginSendTo operation has been cancelled.");
                }
                catch (Exception e)
                {
                    last_error_code_ = TransportError.Type.kSendingFailed;
                    last_error_message_ = "UDP failure in sendBytesCb: " + e.ToString();
                    event_.Add(onFailure);
                }
            }

            void receiveBytesCb (IAsyncResult ar)
            {
                try
                {
                    if (sock_ == null)
                        return;

                    lock (receive_lock_)
                    {
                        int nRead = sock_.EndReceive(ar);
                        if (nRead > 0)
                        {
                            received_size_ += nRead;
                            DebugLog2("UDP received {0} bytes. Buffer has {1} bytes.",
                                      nRead, received_size_ - next_decoding_offset_);
                        }

                        // Decoding a message
                        tryToDecodeMessage();

                        if (nRead > 0)
                        {
                            // Resets buffer
                            received_size_ = 0;
                            next_decoding_offset_ = 0;

                            // Starts another async receive
                            sock_.BeginReceiveFrom(receive_buffer_, received_size_,
                                                   receive_buffer_.Length - received_size_,
                                                   SocketFlags.None, ref receive_ep_,
                                                   new AsyncCallback(this.receiveBytesCb), this);

                            DebugLog2("UDP ready to receive more. We can receive upto {0} more bytes",
                                      receive_buffer_.Length);
                        }
                        else
                        {
                            LogWarning("UDP socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                LogWarning("UDP buffer has {0} bytes but they failed to decode. Discarding.",
                                           receive_buffer_.Length - received_size_);
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


            Socket sock_;
            AddressFamily ip_af_;
            IPEndPoint send_ep_;
            EndPoint receive_ep_;

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

                initAddress(hostname_or_ip, port, https);

                if (https)
                    MozRoots.LoadRootCertificates();
            }

            public MonoBehaviour mono { private get; set; }

            public override bool Started
            {
                get { return state_ >= State.kConnected; }
            }

            protected override void setAddress (HostAddr addr)
            {
                FunDebug.Assert(addr is HostHttp);
                HostHttp http = addr as HostHttp;

                // Sets host url
                host_url_ = string.Format("{0}://{1}:{2}/v{3}/",
                                          (http.https ? "https" : "http"), http.host, http.port,
                                          FunapiVersion.kProtocolVersion);

                HttpTransportOption http_option = (HttpTransportOption)option_;
                Log("HTTP connect - {0}\n {1}, {2}, Sequence:{3}, Timeout:{4}, WWW:{5}",
                    host_url_, convertString(encoding_), convertString(http_option.Encryption),
                    http_option.SequenceValidation, http_option.ConnectionTimeout, http_option.UseWWW);
            }

            protected override void onStart ()
            {
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
#if !NO_UNITY
                    if (cur_www_ != null)
                        return false;
#endif
                    if (web_request_ != null || web_response_ != null)
                        return false;

                    return true;
                }
            }

            protected override void wireSend ()
            {
                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count >= 2);
                    DebugLog2("HTTP Host Url: {0}", host_url_);

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

                    DebugLog2("HTTP sending {0} bytes.", header.buffer.Count + body.buffer.Count);

#if !NO_UNITY
                    // Sending a message
                    if (using_www_)
                    {
                        sendWWWRequest(headers, body);
                    }
                    else
#endif
                    {
                        sendHttpWebRequest(headers, body);
                    }
                }
            }

#if !NO_UNITY
            void sendWWWRequest (Dictionary<string, string> headers, FunapiMessage body)
            {
                cancel_www_ = false;

                if (body.buffer.Count > 0)
                {
                    mono.StartCoroutine(wwwPost(new WWW(host_url_, body.buffer.Array, headers)));
                }
                else
                {
                    mono.StartCoroutine(wwwPost(new WWW(host_url_, null, headers)));
                }
            }
#endif

            void sendHttpWebRequest (Dictionary<string, string> headers, FunapiMessage body)
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

                web_request_.BeginGetRequestStream(new AsyncCallback(requestStreamCb), body);
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
                            DebugLog2("HTTP set-cookie : {0}", str_cookie_);
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
                    FunapiMessage body = (FunapiMessage)ar.AsyncState;

                    Stream stream = web_request_.EndGetRequestStream(ar);
                    if (body.buffer.Count > 0)
                        stream.Write(body.buffer.Array, 0, body.buffer.Count);
                    stream.Close();

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count >= 2);
                        DebugLog2("HTTP sent a message - '{0}' ({1}bytes)",
                                  sending_[1].msg_type, sending_[0].buffer.Count + sending_[1].buffer.Count);

                        // Removes header and body segment
                        sending_.RemoveAt(0);
                        sending_.RemoveAt(0);
                    }

                    web_request_.BeginGetResponse(new AsyncCallback(responseCb), null);
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
                    if (was_aborted_)
                    {
                        Log("HTTP responseCb - request aborted.");
                        return;
                    }

                    web_response_ = (HttpWebResponse)web_request_.EndGetResponse(ar);
                    web_request_ = null;

                    if (web_response_.StatusCode == HttpStatusCode.OK)
                    {
                        lock (receive_lock_)
                        {
                            byte[] header = web_response_.Headers.ToByteArray();
                            string str_header = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
                            onReceiveHeader(str_header);

                            read_stream_ = web_response_.GetResponseStream();
                            read_stream_.BeginRead(receive_buffer_, received_size_,
                                                   receive_buffer_.Length - received_size_,
                                                   new AsyncCallback(readCb), null);
                        }
                    }
                    else
                    {
                        last_error_code_ = TransportError.Type.kReceivingFailed;
                        last_error_message_ = string.Format("Failed response. status:{0}",
                                                            web_response_.StatusDescription);
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
                    int nRead = read_stream_.EndRead(ar);
                    if (nRead > 0)
                    {
                        lock (receive_lock_)
                        {
                            received_size_ += nRead;
                            read_stream_.BeginRead(receive_buffer_, received_size_,
                                                   receive_buffer_.Length - received_size_,
                                                   new AsyncCallback(readCb), null);
                        }
                    }
                    else
                    {
                        if (web_response_ == null)
                        {
                            last_error_code_ = TransportError.Type.kReceivingFailed;
                            last_error_message_ = "Response instance is null.";
                            event_.Add(onFailure);
                            return;
                        }

                        DebugLog2("HTTP received {0} bytes.", received_size_);

                        lock (receive_lock_)
                        {
                            // Decoding a message
                            tryToDecodeMessage();
                        }

                        read_stream_.Close();
                        web_response_.Close();

                        clearRequest();

                        // Sends unsent messages
                        sendPendingMessages();
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
                        DebugLog2("HTTP sent a message - '{0}' ({1}bytes)",
                                  sending_[1].msg_type, sending_[0].buffer.Count + sending_[1].buffer.Count);

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
                        onReceiveHeader(headers.ToString());

                        Buffer.BlockCopy(www.bytes, 0, receive_buffer_, received_size_, www.bytes.Length);
                        received_size_ += www.bytes.Length;

                        DebugLog2("HTTP received {0} bytes.", received_size_);

                        // Decoding a message
                        tryToDecodeMessage();
                    }

                    clearRequest();

                    // Sends unsent messages
                    sendPendingMessages();
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
                    web_response_.Close();

                if (read_stream_ != null)
                    read_stream_.Close();

                clearRequest();
            }

            void clearRequest ()
            {
#if !NO_UNITY
                cur_www_ = null;
#endif
                web_request_ = null;
                web_response_ = null;
                read_stream_ = null;
            }

            protected override void onFailure ()
            {
                cancelRequest();
                base.onFailure();
            }


            // Funapi header-related constants.
            const string kEncryptionHttpHeaderField = "X-iFun-Enc";
            const string kCookieHeaderField = "Cookie";

            static readonly string[] kHeaderSeparator = { kHeaderFieldDelimeter, kHeaderDelimeter };

            // member variables.
            string host_url_;
            string str_cookie_;

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
        public delegate void EventNotifyHandler (TransportProtocol protocol);
        public delegate void MessageNotifyHandler (FunapiMessage message);
    }

}  // namespace Fun
