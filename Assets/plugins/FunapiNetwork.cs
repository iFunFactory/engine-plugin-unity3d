// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
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
    public enum FunMsgType
    {
        kJson,
        kProtobuf
    }

    // Event handler delegate
    public delegate void ReceivedEventHandler(Dictionary<string, string> header, ArraySegment<byte> body);
    public delegate void StoppedEventHandler();

    // Container to hold json-related functions.
    public abstract class JsonAccessor
    {
        public abstract string Serialize(object json_obj);
        public abstract object Deserialize(string json_str);
        public abstract string GetStringField(object json_obj, string field_name);
        public abstract void SetStringField(object json_obj, string field_name, string value);
        public abstract void RemoveStringField(object json_obj, string field_name);
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

        public override void RemoveStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d.Remove(field_name);
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
        public event ReceivedEventHandler ReceivedCallback;
        public event StoppedEventHandler StoppedCallback;
        #endregion

        // Encoding/Decoding related
        public JsonAccessor JsonHelper
        {
            get { return json_accessor_; }
            set { json_accessor_ = value; }
        }

        #region internal implementation
        protected void OnReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            ReceivedCallback(header, body);
        }

        protected void OnStopped ()
        {
            StoppedCallback();
        }


        //
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

        // Funapi Version
        protected static readonly int kCurrentFunapiProtocolVersion = 1;

        protected State state_ = State.kDisconnected;
        protected Mutex mutex_ = new Mutex();
        protected JsonAccessor json_accessor_ = new DictionaryJsonAccessor();
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
            mutex_.WaitOne();

            bool failed = false;
            try
            {
                // Resets states.
                header_decoded_ = false;
                received_size_ = 0;
                next_decoding_offset_ = 0;
                header_fields_.Clear();
                sending_.Clear();

                Init();
            }
            catch (Exception e)
            {
                Debug.Log("Failure in Start: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        // Stops a socket.
        public override void Stop()
        {
            mutex_.WaitOne();

            try
            {
                if (state_ == State.kDisconnected)
                    return;

                state_ = State.kDisconnected;

                if (sock_ != null)
                {
                    sock_.Close();
                    sock_ = null;
                }

                OnStopped();
            }
            finally
            {
                mutex_.ReleaseMutex();
            }
        }

        public override bool Started
        {
            get { return sock_ != null && sock_.Connected && state_ == State.kConnected; }
        }


        // Sends a JSON message through a socket.
        public override void SendMessage (object json_message, EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            string str = this.JsonHelper.Serialize(json_message);
            byte[] body = Encoding.Default.GetBytes(str);

            Debug.Log("JSON to send : " + str);

            SendMessage(body, encryption);
        }

        public override void SendMessage (FunMessage message, EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            MemoryStream stream = new MemoryStream();
            Serializer.Serialize(stream, message);

            byte[] body = new byte[stream.Length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(body, 0, body.Length);

            SendMessage(body, encryption);
        }
        #endregion

        #region internal implementation
        private void SendMessage (byte[] body, EncryptionType encryption)
        {
            mutex_.WaitOne();

            bool failed = false;
            bool sendable = false;

            try
            {
                pending_.Add(new SendingBuffer(new ArraySegment<byte>(body), encryption));

                if (state_ == State.kConnected && sending_.Count == 0)
                {
                    List<SendingBuffer> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;
                    sendable = true;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in SendMessage: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (sendable)
            {
                if (!EncryptThenSendMessage())
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
                        Debug.LogWarning("Unknown encryption: " + encryption);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kEstablished)
                    {
                        Debug.LogWarning("'" + encryptor.name + "' is invalid encryption type. Check out the encryption type of server.");
                        return false;
                    }

                    Int64 nSize = encryptor.Encrypt(buffer.data, buffer.data, ref encryption_header);
                    if (nSize <= 0)
                    {
                        Debug.LogWarning("Encrypt failure: " + encryptor.name);
                        return false;
                    }

                    DebugUtils.Assert(nSize == buffer.data.Count);
                }

                string header = "";
                header += kVersionHeaderField + kHeaderFieldDelimeter + kCurrentFunapiProtocolVersion + kHeaderDelimeter;
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

                Debug.Log("Header to send: " + header + " body length: " + buffer.data.Count);
            }

            WireSend(sending_);

            return true;
        }

        protected bool TryToDecodeHeader()
        {
            Debug.Log("Trying to decode header fields.");

            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    Debug.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }
                string line = Encoding.ASCII.GetString(receive_buffer, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    Debug.Log("End of header reached. Will decode body from now.");
                    return true;
                }

                Debug.Log("Header line: " + line);
                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                tuple[0] = tuple[0].ToUpper();
                Debug.Log("Decoded header field '" + tuple[0] + "' => '" + tuple[1] + "'");
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
            DebugUtils.Assert(version == kCurrentFunapiProtocolVersion);

            // Header length
            DebugUtils.Assert(header_fields_.ContainsKey(kLengthHeaderField));
            int body_length = Convert.ToUInt16(header_fields_[kLengthHeaderField]);
            Debug.Log("We need " + body_length + " bytes for a message body. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");

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
                else
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
                        Debug.Log("Set default encryption: " + default_encryptor_);
                    }

                    // Create encryptors
                    foreach (EncryptionType type in encryption_list)
                    {
                        Encryptor encryptor = Encryptor.Create(type);
                        if (encryptor == null)
                        {
                            Debug.LogWarning("Failed to create encryptor: " + type);
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
                        Debug.LogWarning("Unknown encryption: " + encryption_str);
                        return false;
                    }

                    if (encryptor.state != Encryptor.State.kHandshaking)
                    {
                        Debug.LogWarning("Unexpected handshake message: " + encryptor.name);
                        return false;
                    }

                    string out_header = "";
                    if (!encryptor.Handshake(encryption_header, ref out_header))
                    {
                        Debug.LogWarning("Encryption handshake failure: " + encryptor.name);
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
                    Debug.Log("Ready to receive.");

                    // Starts to process if there any data already queue.
                    if (pending_.Count > 0)
                    {
                        Debug.Log("Flushing pending messages.");
                        List<SendingBuffer> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                        if (!EncryptThenSendMessage())
                            return false;
                    }
                }
            }

            if (body_length > 0)
            {
                DebugUtils.Assert(state_ == State.kConnected);

                if (state_ != State.kConnected)
                {
                    Debug.Log("Unexpected message.");
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

                    ArraySegment<byte> body_bytes = new ArraySegment<byte>(receive_buffer, next_decoding_offset_, body_length);
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
        private static readonly string kHeaderDelimeter = "\n";
        private static readonly string kHeaderFieldDelimeter = ":";
        private static readonly string kVersionHeaderField = "VER";
        private static readonly string kLengthHeaderField = "LEN";
        private static readonly string kEncryptionHeaderField = "ENC";

        // Encryption-releated constants.
        private static readonly string kEncryptionHandshakeBegin = "HELLO!";
        private static readonly int kNoneEncryption = 0;
        private static readonly char kDelim1 = '-';
        private static readonly char kDelim2 = ',';

        // for speed-up.
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // State-related.
        protected Socket sock_;
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
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnecting;
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
            mutex_.WaitOne();
            Debug.Log("StartCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    Debug.Log("Failed to connect.");
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    Debug.Log("Failed to connect.");
                    return;
                }
                Debug.Log("Connected.");

                state_ = State.kEncryptionHandshaking;

                  // Wait for encryption handshaking message.
                ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer, 0, receive_buffer.Length);
                List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                buffer.Add(wrapped);
                sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
            }
            catch (Exception e)
            {
                Debug.Log("Failure in StartCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("SendBytesCb called.");

            bool failed = false;
            bool sendable = false;

            try
            {
                if (sock_ == null)
                    return;

                int nSent = sock_.EndSend(ar);
                Debug.Log("Sent " + nSent + "bytes");

                // Removes any segment fully sent.
                while (nSent > 0)
                {
                    if (sending_[0].data.Count > nSent)
                    {
                        // partial data
                        Debug.Log("Partially sent. Will resume.");
                        break;
                    }
                    else
                    {
                        // fully sent.
                        Debug.Log("Discarding a fully sent message.");
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
                }

                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    Debug.Log("Retrying unsent messages.");
                    WireSend(sending_);
                }
                else if (pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<SendingBuffer> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;
                    sendable = true;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in SendBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (sendable)
            {
                if (!EncryptThenSendMessage())
                    failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                    return;

                int nRead = sock_.EndReceive(ar);
                if (nRead > 0)
                {
                    received_size_ += nRead;
                    Debug.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
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
                            Debug.Log("Compacting a receive buffer to save " + next_decoding_offset_ + " bytes.");
                            Buffer.BlockCopy(receive_buffer, next_decoding_offset_, receive_buffer, 0, received_size_ - next_decoding_offset_);
                            received_size_ -= next_decoding_offset_;
                            next_decoding_offset_ = 0;
                        }
                        else
                        {
                            Debug.Log("Increasing a receive buffer to " + (receive_buffer.Length + kUnitBufferSize) + " bytes.");
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
                    Debug.Log("Ready to receive more. We can receive upto " + (receive_buffer.Length - received_size_) + " more bytes");
                }
                else
                {
                    Debug.Log("Socket closed");
                    if (received_size_ - next_decoding_offset_ > 0)
                    {
                        Debug.Log("Buffer has " + (receive_buffer.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                    }
                    failed = true;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in ReceiveBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        private IPEndPoint connect_ep_;
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

        public void SetEncryption (EncryptionType encryption)
        {
            Encryptor encryptor = Encryptor.Create(encryption);
            if (encryptor == null)
            {
                Debug.LogWarning("Failed to create encryptor: " + encryption);
                return;
            }

            default_encryptor_ = (int)encryption;
            encryptors_[encryption] = encryptor;
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
                    Debug.LogWarning("Message is greater than 64KB. It will be truncated.");
                    DebugUtils.Assert(false);
                }

                sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                  send_ep_, new AsyncCallback(this.SendBytesCb), this);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("SendBytesCb called.");

            bool failed = false;
            bool sendable = false;

            try
            {
                if (sock_ == null)
                    return;

                int nSent = sock_.EndSend(ar);
                Debug.Log("Sent " + nSent + "bytes");

                // Removes header and body segment
                int nToSend = 0;
                for (int i = 0; i < 2; ++i)
                {
                    nToSend += sending_[0].data.Count;
                    sending_.RemoveAt(0);
                }

                if (nSent > 0 && nSent < nToSend)
                {
                    Debug.LogWarning("Failed to transfer hole messages.");
                    DebugUtils.Assert(false);
                }

                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    Debug.Log("Retrying unsent messages.");
                    WireSend(sending_);
                }
                else if (pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<SendingBuffer> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;
                    sendable = true;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in SendBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (sendable)
            {
                if (!EncryptThenSendMessage())
                    failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                    return;

                int nRead = sock_.EndReceive(ar);
                if (nRead > 0)
                {
                    received_size_ += nRead;
                    Debug.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
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
                    // Prepares a next message.
                    received_size_ = 0;
                    next_decoding_offset_ = 0;
                    header_fields_.Clear();

                    // Starts another async receive
                    sock_.BeginReceiveFrom(receive_buffer, 0, receive_buffer.Length, SocketFlags.None,
                                           ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

                    Debug.Log("Ready to receive more. We can receive upto " + receive_buffer.Length + " more bytes");
                }
                else
                {
                    Debug.Log("Socket closed");
                    if (received_size_ - next_decoding_offset_ > 0)
                    {
                        Debug.Log("Buffer has " + (receive_buffer.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                    }

                    failed = true;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in ReceiveBytesCb: " + e.ToString ());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }


        private IPEndPoint send_ep_;
        private EndPoint receive_ep_;
        #endregion
    }


    // HTTP transport layer
    public class FunapiHttpTransport : FunapiTransport
    {
        #region public interface
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port, bool https = false)
        {
            // Url
            if (https)
                host_url_ = "https://";
            else
                host_url_ = "http://";

            host_url_ += hostname_or_ip + ":" + port;

            // Version
            host_url_ += "/v" + kCurrentFunapiProtocolVersion + "/";
        }

        public override void Start()
        {
            Debug.Log("Started.");
            state_ = State.kConnected;
        }

        public override void Stop()
        {
            mutex_.WaitOne();

            try
            {
                if (state_ == State.kDisconnected)
                    return;

                Debug.Log("Stopped.");
                state_ = State.kDisconnected;

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

                OnStopped();
            }
            finally
            {
                mutex_.ReleaseMutex();
            }
        }

        public override bool Started
        {
            get { return state_ == State.kConnected; }
        }

        public override void SendMessage(object json_message, EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            string str = this.JsonHelper.Serialize(json_message);
            byte[] body = Encoding.Default.GetBytes(str);

            Debug.Log("JSON to send: " + str);

            SendMessage(body, encryption);
        }

        public override void SendMessage(FunMessage message, EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            MemoryStream stream = new MemoryStream();
            Serializer.Serialize(stream, message);

            byte[] body = new byte[stream.Length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(body, 0, body.Length);

            SendMessage(body, encryption);
        }
        #endregion

        #region internal implementation
        private void SendMessage (byte[] body, EncryptionType encryption)
        {
            mutex_.WaitOne();
            Debug.Log("Send a Message.");

            bool failed = false;
            try
            {
                ArraySegment<byte> content = new ArraySegment<byte>(body);

                Debug.Log("Host Url: " + host_url_);

                // Request
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = content.Count;

                // Response
                WebState state = new WebState();
                state.request = request;
                state.sending = content;
                list_.Add(state);

                request.BeginGetRequestStream(new AsyncCallback(RequestStreamCb), state);
            }
            catch (Exception e)
            {
                Debug.Log("Failure in SendMessage: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        private void RequestStreamCb (IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("RequestStreamCb called.");

            bool failed = false;
            try
            {
                WebState state = (WebState)ar.AsyncState;
                HttpWebRequest request = state.request;

                Stream stream = request.EndGetRequestStream(ar);
                stream.Write(state.sending.Array, 0, state.sending.Count);
                stream.Close();
                Debug.Log("Sent " + state.sending.Count + "bytes");

                request.BeginGetResponse(new AsyncCallback(ResponseCb), state);
            }
            catch (Exception e)
            {
                Debug.Log("Failure in RequestStreamCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ResponseCb (IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("ResponseCb called.");

            bool failed = false;
            try
            {
                WebState state = (WebState)ar.AsyncState;
                if (state.aborted)
                    return;

                HttpWebResponse response = (HttpWebResponse)state.request.EndGetResponse(ar);
                state.request = null;

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
                    Debug.Log("Failed response. status:" + response.StatusDescription);
                    DebugUtils.Assert(false);
                    list_.Remove(state);
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in ResponseCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReadCb (IAsyncResult ar)
        {
            mutex_.WaitOne();
            Debug.Log("ReadCb called.");

            bool failed = false;
            try
            {
                WebState state = (WebState)ar.AsyncState;
                int nRead = state.stream.EndRead(ar);

                if (nRead > 0)
                {
                    Debug.Log("We need more bytes for response. Waiting.");
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
                    ArraySegment<byte> body = new ArraySegment<byte>(state.read_data, 0, state.read_offset);

                    // The network module eats the fields and invoke registered handler.
                    OnReceived(null, body);

                    state.stream.Close();
                    state.stream = null;
                    list_.Remove(state);
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in ReadCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }
        #endregion


        // Response-related.
        class WebState
        {
            public HttpWebRequest request = null;
            public Stream stream = null;
            public byte[] buffer = null;
            public byte[] read_data = null;
            public int read_offset = 0;
            public bool aborted = false;
            public ArraySegment<byte> sending;
        }

        // Buffer-related constants.
        private static readonly int kUnitBufferSize = 65536;

        // member variables.
        private string host_url_;
        private List<WebState> list_ = new List<WebState>();
    }


    // Driver to use Funapi network plugin.
    public class FunapiNetwork
    {
        #region Handler delegate definition
        public delegate void MessageHandler(string msg_type, object body);
        public delegate void OnSessionInitiated(string session_id);
        public delegate void OnSessionClosed();
        #endregion

        #region public interface
        public FunapiNetwork(FunapiTransport transport, FunMsgType type,
                             OnSessionInitiated on_session_initiated, OnSessionClosed on_session_closed)
        {
            transport_ = transport;
            msg_type_ = type;
            on_session_initiated_ = on_session_initiated;
            on_session_closed_ = on_session_closed;
            transport_.ReceivedCallback += new ReceivedEventHandler(OnTransportReceived);
            transport_.StoppedCallback += new StoppedEventHandler(OnTransportStopped);
        }

        public void Start()
        {
            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            Debug.Log("Starting a network module.");
            transport_.Start();
            started_ = true;
        }

        public void Stop()
        {
            Debug.Log("Stopping a network module.");
            started_ = false;

            if (transport_.Started)
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

        public FunMsgType MsgType
        {
            get { return msg_type_; }
        }

        public void SendMessage(FunMessage message, EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                Debug.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                message.sid = session_id_;
            }

            transport_.SendMessage(message, encryption);
        }

        public void SendMessage(string msg_type, object body, EncryptionType encryption = EncryptionType.kDefaultEncryption)
        {
            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                Debug.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            // Encodes a messsage type.
            transport_.JsonHelper.SetStringField(body, kMsgTypeBodyField, msg_type);

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                transport_.JsonHelper.SetStringField(body, kSessionIdBodyField, session_id_);
            }

            transport_.SendMessage(body, encryption);
        }

        public void RegisterHandler(string type, MessageHandler handler)
        {
            Debug.Log("New handler for message type '" + type + "'");
            message_handlers_[type] = handler;
        }
        #endregion

        #region internal implementation
        private void OnTransportReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            Debug.Log("OnTransportReceived invoked.");
            last_received_ = DateTime.Now;

            string msg_type = "";
            string session_id = "";

            if (msg_type_ == FunMsgType.kJson)
            {
                string str = Encoding.Default.GetString(body.Array, body.Offset, body.Count);
                object json = transport_.JsonHelper.Deserialize(str);
                Debug.Log("Parsed json: " + str);

                DebugUtils.Assert(transport_.JsonHelper.GetStringField(json, kMsgTypeBodyField) is string);
                string msg_type_node = transport_.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                msg_type = msg_type_node;
                transport_.JsonHelper.RemoveStringField(json, kMsgTypeBodyField);

                DebugUtils.Assert(transport_.JsonHelper.GetStringField(json, kSessionIdBodyField) is string);
                string session_id_node = transport_.JsonHelper.GetStringField(json, kSessionIdBodyField) as string;
                session_id = session_id_node;
                transport_.JsonHelper.RemoveStringField(json, kSessionIdBodyField);

                if (message_handlers_.ContainsKey(msg_type))
                    message_handlers_[msg_type](msg_type, json);
            }
            else if (msg_type_ == FunMsgType.kProtobuf)
            {
                MemoryStream stream = new MemoryStream(body.Array, body.Offset, body.Count, false);
                FunMessage message = Serializer.Deserialize<FunMessage>(stream);

                msg_type = message.msgtype;
                session_id = message.sid;

                if (message_handlers_.ContainsKey(msg_type))
                    message_handlers_[msg_type](msg_type, message);
            }
            else
            {
                Debug.LogWarning("Invalid message type. type: " + msg_type_);
                DebugUtils.Assert(false);
                return;
            }

            if (session_id_.Length == 0)
            {
                session_id_ = session_id;
                Debug.Log("New session id: " + session_id);
                if (on_session_initiated_ != null)
                {
                    on_session_initiated_(session_id_);
                }
            }

            if (session_id_ != session_id)
            {
                Debug.Log("Session id changed: " + session_id_ + " => " + session_id);
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
                Debug.Log("No handler for message '" + msg_type + "'. Ignoring.");
            }
        }

        private void OnTransportStopped()
        {
            Debug.Log("Transport terminated. Stopping. You may restart again.");
            Stop();
        }

        #region Funapi system message handlers
        private void OnNewSession(string msg_type, object body)
        {
            // ignore.
        }

        private void OnSessionTimedout(string msg_type, object body)
        {
            Debug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");
            session_id_ = "";

            if (on_session_closed_ != null)
                on_session_closed_();
        }
        #endregion

        // Funapi message-related constants.
        private static readonly float kFunapiSessionTimeout = 3600.0f;
        private static readonly string kMsgTypeBodyField = "_msgtype";
        private static readonly string kSessionIdBodyField = "_sid";
        private static readonly string kNewSessionMessageType = "_session_opened";
        private static readonly string kSessionClosedMessageType = "_session_closed";

        // member variables.
        private FunMsgType msg_type_;
        private bool started_ = false;
        private FunapiTransport transport_;
        private OnSessionInitiated on_session_initiated_;
        private OnSessionClosed on_session_closed_;
        private string session_id_ = "";
        private Dictionary<string, MessageHandler> message_handlers_ = new Dictionary<string, MessageHandler>();
        private DateTime last_received_ = DateTime.Now;
        #endregion
    }
}  // namespace Fun
