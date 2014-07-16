#define DEBUG

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SimpleJSON;


namespace Fun
{
    // Abstract class to represent Transport used by Funapi
    // There are 3 transport types at the moment (though this plugin implements only TCP one.)
    // TCP, UDP, and HTTP.
    public abstract class FunapiTransport
    {
        // Start connecting
        public abstract void Start();

        // Disconnection
        public abstract void Stop();

        // Check connection
        public abstract bool Started { get; }

        // Send a message
        public abstract void SendMessage(JSONClass message);

        // Socket-level registers handlers for the event. (i.e., received bytes and closed)
        public void RegisterEventHandlers(OnReceived on_received, OnStopped on_stopped)
        {
            mutex_.WaitOne();
            on_received_ = on_received;
            on_stopped_ = on_stopped;
            mutex_.ReleaseMutex();
        }


        protected enum State
        {
            kDisconnected = 0,
            kConnecting,
            kConnected
        };


        // Event handler delegate
        public delegate void OnReceived(Dictionary<string, string> header, JSONClass body);
        public delegate void OnStopped();

        // Funapi Version
        protected static readonly int kCurrentFunapiProtocolVersion = 1;

        // Registered event handlers.
        protected OnReceived on_received_;
        protected OnStopped on_stopped_;

        protected State state_ = State.kDisconnected;
        protected Mutex mutex_ = new Mutex();
    }


    // Transport class for socket
    public abstract class FunapiDecodedTransport : FunapiTransport
    {
        // Create a socket.
        protected abstract void Init();

        // Sends a packet.
        protected abstract void WireSend (List<ArraySegment<byte>> sending);

        #region public interface
        // Starts a socket.
        public override void Start()
        {
            mutex_.WaitOne();

            bool failed = false;
            try
            {
                // Resets states.
                header_decoded_ = false;
                received_size_ = 0;
                next_decoding_offset_ = 0;
                header_fields_ = new Dictionary<string, string>();
                sending_.Clear();

                Init();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failred in Start: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }

        // Stops a socket.
        public override void Stop()
        {
            mutex_.WaitOne();

            state_ = State.kDisconnected;

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }

            mutex_.ReleaseMutex();
        }

        public override bool Started
        {
            get { return sock_ != null && sock_.Connected && state_ == State.kConnected; }
        }


        // Sends a JSON message through a socket.
        public override void SendMessage (JSONClass message)
        {
            string body = message.ToString();
            ArraySegment<byte> body_as_bytes = new ArraySegment<byte>(Encoding.Default.GetBytes(body));

            string header = "";
            header += kVersionHeaderField + kHeaderFieldDelimeter + kCurrentFunapiProtocolVersion + kHeaderDelimeter;
            header += kLengthHeaderField + kHeaderFieldDelimeter + body.Length + kHeaderDelimeter;
            header += kHeaderDelimeter;
            ArraySegment<byte> header_as_bytes = new ArraySegment<byte>(Encoding.ASCII.GetBytes(header));

            UnityEngine.Debug.Log("Header to send: " + header);
            UnityEngine.Debug.Log("JSON to send: " + body);

            mutex_.WaitOne();
            bool failed = false;
            try
            {
                pending_.Add(header_as_bytes);
                pending_.Add(body_as_bytes);
                if (state_ == State.kConnected && sending_.Count == 0)
                {
                    List<ArraySegment<byte>> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;
                    WireSend(sending_);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failure in SendMessage: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }
        #endregion

        #region internal implementation
        protected bool TryToDecodeHeader()
        {
            UnityEngine.Debug.Log("Trying to decode header fields.");
            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    UnityEngine.Debug.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }
                string line = Encoding.ASCII.GetString(receive_buffer, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    UnityEngine.Debug.Log("End of header reached. Will decode body from now.");
                    return true;
                }

                UnityEngine.Debug.Log("Header line: " + line);
                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                tuple[0] = tuple[0].ToUpper();
                UnityEngine.Debug.Log("Decoded header field '" + tuple[0] + "' => '" + tuple[1] + "'");
                DebugUtils.Assert(tuple.Length == 2);
                header_fields_[tuple[0]] = tuple[1];
            }
            return false;
        }

        protected bool TryToDecodeBody()
        {
            DebugUtils.Assert(header_fields_.ContainsKey(kVersionHeaderField));
            int version = Convert.ToUInt16(header_fields_[kVersionHeaderField]);
            DebugUtils.Assert(version == kCurrentFunapiProtocolVersion);

            DebugUtils.Assert(header_fields_.ContainsKey(kLengthHeaderField));
            int body_length = Convert.ToUInt16(header_fields_[kLengthHeaderField]);
            UnityEngine.Debug.Log("We need " + body_length + " bytes for a message body. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");

            if (received_size_ - next_decoding_offset_ < body_length)
            {
                // Need more bytes.
                UnityEngine.Debug.Log("We need more bytes for a message body. Waiting.");
                return false;
            }

            if (body_length > 0)
            {
                string body = Encoding.Default.GetString(receive_buffer, next_decoding_offset_, body_length);
                next_decoding_offset_ += body_length;

                UnityEngine.Debug.Log(">>> " + body);

                JSONNode json = JSON.Parse(body);
                DebugUtils.Assert(json is JSONClass);
                UnityEngine.Debug.Log("Parsed json: " + json.ToString());

                // Parsed json message should have reserved fields.
                // The network module eats the fields and invoke registered handler with a remaining json body.
                UnityEngine.Debug.Log("Invoking a receive handler.");
                if (on_received_ != null)
                {
                    on_received_(header_fields_, json.AsObject);
                }
            }

            // Prepares a next message.
            header_decoded_ = false;
            header_fields_ = new Dictionary<string, string>();
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


        // Buffer-related constants.
        protected static readonly int kUnitBufferSize = 65536;

        // Funapi header-related constants.
        private static readonly string kHeaderDelimeter = "\n";
        private static readonly string kHeaderFieldDelimeter = ":";
        private static readonly string kVersionHeaderField = "VER";
        private static readonly string kLengthHeaderField = "LEN";

        // for speed-up.
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // State-related.
        protected Socket sock_;
        protected byte[] receive_buffer = new byte[kUnitBufferSize];
        protected byte[] send_buffer_ = new byte[kUnitBufferSize];
        protected bool header_decoded_ = false;
        protected int received_size_ = 0;
        protected int next_decoding_offset_ = 0;
        protected Dictionary<string, string> header_fields_;
        protected List<ArraySegment<byte>> pending_ = new List<ArraySegment<byte>>();
        protected List<ArraySegment<byte>> sending_ = new List<ArraySegment<byte>>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiDecodedTransport
    {
        #region public interface
        public FunapiTcpTransport(string hostname_or_ip, UInt16 port)
        {
            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            connect_ep_ = new IPEndPoint(address, port);
        }
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnecting;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sock_.BeginConnect(connect_ep_, new AsyncCallback(this.StartCb), this);
        }

        protected override void WireSend(List<ArraySegment<byte>> sending)
        {
            sock_.BeginSend(sending, 0, new AsyncCallback(this.SendBytesCb), this);
        }

        private void StartCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            UnityEngine.Debug.Log("StartCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    UnityEngine.Debug.Log("Failed to connect.");
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    UnityEngine.Debug.Log("Failed to connect.");
                    return;
                }
                UnityEngine.Debug.Log("Connected.");

                state_ = State.kConnected;

                // Wait for message.
                ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer, 0, receive_buffer.Length);
                List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                buffer.Add(wrapped);
                sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failrued in StartCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            UnityEngine.Debug.Log("SendBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                    return;

                int nSent = sock_.EndSend(ar);
                UnityEngine.Debug.Log("Sent " + nSent + "bytes");

                // Removes any segment fully sent.
                while (nSent > 0)
                {
                    if (sending_[0].Count > nSent)
                    {
                        // partial data
                        UnityEngine.Debug.Log("Partially sent. Will resume.");
                        break;
                    }
                    else
                    {
                        // fully sent.
                        UnityEngine.Debug.Log("Discarding a fully sent message.");
                        nSent -= sending_[0].Count;
                        sending_.RemoveAt(0);
                    }
                }

                // If the first segment has been sent partially, we need to reconstruct the first segment.
                if (nSent > 0)
                {
                    DebugUtils.Assert(sending_.Count > 0);
                    ArraySegment<byte> original = sending_[0];

                    DebugUtils.Assert(nSent <= sending_[0].Count);
                    ArraySegment<byte> adjusted = new ArraySegment<byte>(original.Array, original.Offset + nSent, original.Count - nSent);
                    sending_[0] = adjusted;
                }

                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    UnityEngine.Debug.Log("Retrying unsent messages.");
                    WireSend(sending_);
                }
                else if (pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<ArraySegment<byte>> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;
                    WireSend(sending_);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failure in SendBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            UnityEngine.Debug.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                    return;

                int nRead = sock_.EndReceive(ar);
                if (nRead > 0)
                {
                    received_size_ += nRead;
                    UnityEngine.Debug.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
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
                    // Checks buffer space before starting another async receive.
                    if (receive_buffer.Length - received_size_ == 0)
                    {
                        // If there are space can be collected, compact it first.
                        // Otherwise, increase the receiving buffer size.
                        if (next_decoding_offset_ > 0)
                        {
                            UnityEngine.Debug.Log("Compacting a receive buffer to save " + next_decoding_offset_ + " bytes.");
                            Buffer.BlockCopy(receive_buffer, next_decoding_offset_, receive_buffer, 0, received_size_ - next_decoding_offset_);
                            received_size_ -= next_decoding_offset_;
                            next_decoding_offset_ = 0;
                        }
                        else
                        {
                            UnityEngine.Debug.Log("Increasing a receive buffer to " + (receive_buffer.Length + kUnitBufferSize) + " bytes.");
                            byte[] new_buffer = new byte[receive_buffer.Length + kUnitBufferSize];
                            Buffer.BlockCopy(receive_buffer, 0, new_buffer, 0, received_size_);
                            receive_buffer = new_buffer;
                        }
                    }

                    // Starts another async receive
                    ArraySegment<byte> residual = new ArraySegment<byte>(receive_buffer, received_size_, receive_buffer.Length - received_size_);
                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                    buffer.Add(residual);
                    sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                    UnityEngine.Debug.Log("Ready to receive more. We can receive upto " + (receive_buffer.Length - received_size_) + " more bytes");
                }
                else
                {
                    UnityEngine.Debug.Log("Socket closed");
                    if (received_size_ - next_decoding_offset_ > 0)
                    {
                        UnityEngine.Debug.Log("Buffer has " + (receive_buffer.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                    }
                    failed = true;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failure in ReceiveBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }

        private IPEndPoint connect_ep_;
        #endregion
    }


    // UDP transport layer
    public class FunapiUdpTransport : FunapiDecodedTransport
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
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnected;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock_.BeginReceiveFrom(receive_buffer, 0, receive_buffer.Length, SocketFlags.None,
                                   ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

            UnityEngine.Debug.Log("Connected.");
        }

        // Send a packet.
        protected override void WireSend(List<ArraySegment<byte>> sending)
        {
            DebugUtils.Assert(sending.Count >= 2);

            int length = sending[0].Count + sending[1].Count;
            if (length > send_buffer_.Length)
            {
                send_buffer_ = new byte[length];
            }

            int offset = 0;

            // one header + one body
            for (int i = 0; i < 2; ++i)
            {
                ArraySegment<byte> item = sending[i];
                Buffer.BlockCopy(item.Array, 0, send_buffer_, offset, item.Count);
                offset += item.Count;
            }

            if (offset > 0)
            {
                if (offset > kUnitBufferSize)
                {
                    UnityEngine.Debug.LogWarning("Packet is greater than 64KB. It will be truncated.");
                    DebugUtils.Assert(false);
                }

                sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                  send_ep_, new AsyncCallback(this.SendBytesCb), this);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            UnityEngine.Debug.Log("SendBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                    return;

                int nSent = sock_.EndSend(ar);
                UnityEngine.Debug.Log("Sent " + nSent + "bytes");

                // Removes header and body segment
                int nToSend = 0;
                for (int i = 0; i < 2; ++i)
                {
                    nToSend += sending_[0].Count;
                    sending_.RemoveAt(0);
                }

                if (nSent > 0 && nSent < nToSend)
                {
                    UnityEngine.Debug.LogWarning("Failed to transfer hole messages.");
                    DebugUtils.Assert(false);
                }

                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    UnityEngine.Debug.Log("Retrying unsent messages.");
                    WireSend(sending_);
                }
                else if (pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<ArraySegment<byte>> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;
                    WireSend(sending_);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failure in SendBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            UnityEngine.Debug.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                    return;

                int nRead = sock_.EndReceive(ar);
                if (nRead > 0)
                {
                    received_size_ += nRead;
                    UnityEngine.Debug.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
                }

                // Decoding a message
                if (TryToDecodeHeader())
                {
                    if (TryToDecodeBody() == false)
                    {
                        UnityEngine.Debug.LogWarning("Failed to decode body.");
                        DebugUtils.Assert(false);
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning("Failed to decode header.");
                    DebugUtils.Assert(false);
                }

                if (nRead > 0)
                {
                    // Prepares a next message.
                    received_size_ = 0;
                    next_decoding_offset_ = 0;
                    header_fields_ = new Dictionary<string, string>();

                    // Starts another async receive
                    sock_.BeginReceiveFrom(receive_buffer, 0, receive_buffer.Length, SocketFlags.None,
                                           ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

                    UnityEngine.Debug.Log("Ready to receive more. We can receive upto " + receive_buffer.Length + " more bytes");
                }
                else
                {
                    UnityEngine.Debug.Log("Socket closed");
                    if (received_size_ - next_decoding_offset_ > 0)
                    {
                        UnityEngine.Debug.Log("Buffer has " + (receive_buffer.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                    }

                    failed = true;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Failure in ReceiveBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
                on_stopped_();
            }
        }


        private IPEndPoint send_ep_;
        private EndPoint receive_ep_;
        #endregion
    }


    // HTTP transport layer
    /*public class FunapiHttpTransport : FunapiTransport
    {
        #region public interface
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port)
        {
        }

        public override void Start()
        {
        }

        public override void Stop()
        {
        }

        public override bool Started
        {
            get { return false; }
        }

        public override void SendMessage(JSONClass message)
        {
        }
        #endregion

        #region internal implementation
        #endregion
    }*/


    // Driver to use Funapi network plugin.
    public class FunapiNetwork
    {
        #region Handler delegate definition
        public delegate void MessageHandler(string msg_type, JSONClass body);
        public delegate void OnSessionInitiated(string session_id);
        public delegate void OnSessionClosed();
        #endregion

        #region public interface
        public FunapiNetwork(FunapiTransport transport, OnSessionInitiated on_session_initiated, OnSessionClosed on_session_closed)
        {
            transport_ = transport;
            on_session_initiated_ = on_session_initiated;
            on_session_closed_ = on_session_closed;
            transport_.RegisterEventHandlers(this.OnTransportReceived, this.OnTransportStopped);
        }

        public void Start()
        {
            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            UnityEngine.Debug.Log("Starting a network module.");
            transport_.Start();
            started_ = true;
        }

        public void Stop()
        {
            UnityEngine.Debug.Log("Stopping a network module.");
            started_ = false;
            transport_.Stop();
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

        public void SendMessage(string msg_type, JSONClass body)
        {
            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                UnityEngine.Debug.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            // Encodes a messsage type.
            body[kMsgTypeBodyField] = msg_type;

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                body[kSessionIdBodyField] = session_id_;
            }
            transport_.SendMessage(body);
        }

        public void RegisterHandler(string type, MessageHandler handler)
        {
            UnityEngine.Debug.Log("New handler for message type '" + type + "'");
            message_handlers_[type] = handler;
        }
        #endregion

        #region internal implementation
        private void OnTransportReceived(Dictionary<string, string> header, JSONClass body)
        {
            UnityEngine.Debug.Log("OnReceived invoked.");
            last_received_ = DateTime.Now;

            JSONNode msg_type_node = body[kMsgTypeBodyField];
            DebugUtils.Assert(msg_type_node is JSONData);
            DebugUtils.Assert(msg_type_node.Value is string);
            string msg_type = msg_type_node.Value;
            body.Remove(msg_type_node);

            JSONNode session_id_node = body[kSessionIdBodyField];
            DebugUtils.Assert(session_id_node is JSONData);
            DebugUtils.Assert(session_id_node.Value is String);
            string session_id = session_id_node.Value;
            body.Remove(session_id_node);

            if (session_id_.Length == 0)
            {
                session_id_ = session_id;
                UnityEngine.Debug.Log("New session id: " + session_id);
                if (on_session_initiated_ != null)
                {
                    on_session_initiated_(session_id_);
                }
            }

            if (session_id_ != session_id)
            {
                UnityEngine.Debug.Log("Session id changed: " + session_id_ + " => " + session_id);
                session_id_ = session_id;
                if (on_session_closed_ != null)
                {
                    on_session_closed_();
                }
                if (on_session_initiated_ != null)
                {
                    on_session_initiated_(session_id_);
                }
            }

            if (!message_handlers_.ContainsKey(msg_type))
            {
                UnityEngine.Debug.Log("No handler for message '" + msg_type + "'. Ignoring.");
            } else {
                message_handlers_[msg_type](msg_type, body);
            }
        }

        private void OnTransportStopped()
        {
            UnityEngine.Debug.Log("Transport terminated. Stopping. You may restart again.");
            Stop();
        }

        #region Funapi system message handlers
        private void OnNewSession(string msg_type, JSONClass body)
        {
            // ignore.
        }

        private void OnSessionTimedout(string msg_type, JSONClass body)
        {
            UnityEngine.Debug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");
            session_id_ = "";
            on_session_closed_();
        }
        #endregion

        // Funapi message-related constants.
        private static readonly float kFunapiSessionTimeout = 3600.0f;
        private static readonly string kMsgTypeBodyField = "msgtype";
        private static readonly string kSessionIdBodyField = "sid";
        private static readonly string kNewSessionMessageType = "_session_opened";
        private static readonly string kSessionClosedMessageType = "_session_closed";

        // member variables.
        private bool started_ = false;
        private FunapiTransport transport_;
        private OnSessionInitiated on_session_initiated_;
        private OnSessionClosed on_session_closed_;
        private string session_id_ = "";
        private Dictionary<string, MessageHandler> message_handlers_ = new Dictionary<string, MessageHandler>();
        private DateTime last_received_ = DateTime.Now;
        #endregion
    }


    // Utility class
    public class DebugUtils {
        [Conditional("DEBUG")]
        public static void Assert(bool condition)
        {
            if (!condition) throw new Exception();
        }
    }
}  // namespace Fun
