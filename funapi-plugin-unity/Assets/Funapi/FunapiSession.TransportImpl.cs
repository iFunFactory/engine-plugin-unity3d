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
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using WebSocketSharp;
#if !NO_UNITY
using UnityEngine;
using UnityEngine.Networking;
#endif
using System.Runtime.InteropServices;


namespace Fun
{
    public partial class FunapiSession
    {
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

                ssl_ = (option as TcpTransportOption).UseTLS;
                if (ssl_)
                {
                    TrustManager.LoadMozRoots();
                }
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
                debug.Log("[TCP] {0}:{1}, {2}, {3}, Compression:{4}, Sequence:{5}, " +
                          "ConnectionTimeout:{6}, AutoReconnect:{7}, Nagle:{8}, Ping:{9}",
                          addr_.host, addr_.port, convertString(encoding_), convertString(tcp_option.Encryption),
                          convertString(tcp_option.CompressionType), tcp_option.SequenceValidation,
                          tcp_option.ConnectionTimeout, tcp_option.AutoReconnect,
                          !tcp_option.DisableNagle, tcp_option.EnablePing);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnecting;

                try
                {
                    addr_.refresh();

                    lock (sock_lock_)
                    {
                        sock_ = new Socket(addr_.inet, SocketType.Stream, ProtocolType.Tcp);

                        bool disable_nagle = (option_ as TcpTransportOption).DisableNagle;
                        if (disable_nagle)
                            sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                        sock_.BeginConnect(addr_.host, addr_.port, new AsyncCallback(this.startCb), this);
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kStartingFailed;
                    error.message = "[TCP] Failure in onStart: " + e.ToString();
                    onFailure(error);
                }
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (ssl_stream_ != null)
                    {
                        ssl_stream_.Close();
                        ssl_stream_ = null;
                    }

                    if (sock_ != null)
                    {
                        sock_.Close();
                        sock_ = null;
                    }
                }

                base.onClose();
            }

            protected override void wireSend ()
            {
                byte[] send_buffer = null;
                List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();
                int length = 0;

                lock (sending_lock_)
                {
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

                    if (ssl_)
                    {
                        send_buffer = new byte[length];
                        ssl_send_size_ = length;

                        int offset = 0;
                        foreach (ArraySegment<byte> data in list)
                        {
                            Buffer.BlockCopy(data.Array, 0, send_buffer, offset, data.Count);
                            offset += data.Count;
                        }
                    }
                }

                try
                {
                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                        {
                            return;
                        }

                        if (ssl_)
                        {
                            if (ssl_stream_ == null)
                            {
                                debug.LogWarning("[TCP] SSL stream is null.");
                                return;
                            }

                            ssl_stream_.BeginWrite(send_buffer, 0, length, new AsyncCallback(this.sendBytesCb), ssl_stream_);
                        }
                        else
                        {
                            sock_.BeginSend(list, SocketFlags.None, new AsyncCallback(this.sendBytesCb), this);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.LogDebug("[TCP] BeginSend operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "[TCP] Failure in wireSend: " + e.ToString();
                    onFailure(error);
                }
            }

            void startCb (IAsyncResult ar)
            {
                try
                {
                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                        {
                            return;
                        }

                        sock_.EndConnect(ar);
                        if (sock_.Connected == false)
                        {
                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kStartingFailed;
                            error.message = string.Format("[TCP] connection failed.");
                            onFailure(error);
                            return;
                        }
                        debug.LogDebug("[TCP] Connected. Starts handshaking..");

                        if (ssl_)
                        {
                            ssl_stream_ = new SslStream(new NetworkStream(sock_), false, TrustManager.CertValidationCallback);
                        }

                        state_ = State.kHandshaking;

                        lock (receive_lock_)
                        {
                            // Wait for handshaking message.
                            if (ssl_)
                            {
                                if (ssl_stream_ == null)
                                {
                                    debug.LogWarning("[TCP] SSL stream is null.");
                                    return;
                                }

                                ssl_stream_.BeginAuthenticateAsClient(addr_.host, new AsyncCallback(this.authenticateCb), ssl_stream_);
                            }
                            else
                            {
                                ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                                List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                                buffer.Add(wrapped);

                                sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.receiveBytesCb), this);
                            }
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    debug.LogDebug("[TCP] BeginConnect operation has been cancelled.");
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kStartingFailed;
                    error.message = "[TCP] Failure in startCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void authenticateCb (IAsyncResult ar)
            {
                try
                {
                    ssl_stream_.EndAuthenticateAsClient(ar);

                    ssl_stream_.BeginRead(receive_buffer_, 0, receive_buffer_.Length, new AsyncCallback(this.receiveBytesCb), ssl_stream_);
                }
                catch (ObjectDisposedException)
                {
                    debug.LogDebug("TCP BeginAuthenticateAsClient operation has been cancelled.");
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kStartingFailed;
                    error.message = "TCP Failure in authenticateCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void  sendBytesCb (IAsyncResult ar)
            {
                try
                {
                    int nSent = 0;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                        {
                            return;
                        }

                        if (ssl_)
                        {
                            if (ssl_stream_ == null)
                            {
                                debug.LogWarning("[TCP] SSL stream is null.");
                                return;
                            }

                            ssl_stream_.EndWrite(ar);

                            nSent = ssl_send_size_;
                            ssl_send_size_ = 0;
                        }
                        else
                        {
                            nSent = sock_.EndSend(ar);
                        }
                    }

                    if (nSent > 0)
                    {
                        lock (sending_lock_)
                        {
                            while (nSent > 0)
                            {
                                if (sending_.Count > 0)
                                {
                                    // removes a sent message.
                                    FunapiMessage msg = sending_[0];
                                    int length = msg.header.Count + msg.body.Count;
                                    nSent -= length;

                                    sending_.RemoveAt(0);
                                }
                                else
                                {
                                    debug.LogError("[TCP] Sent {0} more bytes but couldn't find the sending buffer.", nSent);
                                    break;
                                }
                            }

                            if (sending_.Count != 0)
                            {
                                debug.LogError("[TCP] {0} message(s) left in the sending buffer.", sending_.Count);
                            }

                            // Sends pending messages.
                            checkPendingMessages();
                        }
                    }
                    else
                    {
                        debug.LogWarning("[TCP] socket closed");
                    }
                }
                catch (ObjectDisposedException)
                {
                    debug.LogDebug("[TCP] BeginSend operation has been cancelled.");
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "[TCP] Failure in sendBytesCb: " + e.ToString();
                    onFailure(error);
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
                        {
                            return;
                        }

                        if (ssl_)
                        {
                            if (ssl_stream_ == null)
                            {
                                debug.LogWarning("[TCP] SSL stream is null.");
                                return;
                            }

                            nRead = ssl_stream_.EndRead(ar);
                        }
                        else
                        {
                            nRead = sock_.EndReceive(ar);
                        }
                    }

                    lock (receive_lock_)
                    {
                        if (nRead > 0)
                        {
                            received_size_ += nRead;

                            // Parses messages
                            parseMessages();

                            // Checks buffer space
                            checkReceiveBuffer();

                            // Starts another async receive
                            lock (sock_lock_)
                            {
                                if (ssl_)
                                {
                                    ssl_stream_.BeginRead(receive_buffer_, received_size_, receive_buffer_.Length - received_size_,
                                                         new AsyncCallback(this.receiveBytesCb), ssl_stream_);
                                }
                                else
                                {
                                    ArraySegment<byte> residual = new ArraySegment<byte>(
                                    receive_buffer_, received_size_, receive_buffer_.Length - received_size_);

                                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                                    buffer.Add(residual);

                                    sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.receiveBytesCb), this);
                                }
                            }
                        }
                        else
                        {
                            debug.LogWarning("[TCP] socket closed");

                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kDisconnected;
                            error.message = "TCP can't receive messages. Maybe the socket is closed.";
                            onDisconnected(error);
                        }
                    }
                }
                catch (Exception e)
                {
                    // When Stop is called Socket.EndReceive may return a NullReferenceException
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.LogDebug("[TCP] BeginReceive operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "[TCP] Failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }


            Socket sock_;
            HostIP addr_;
            bool ssl_ = false;
            int ssl_send_size_;
            SslStream ssl_stream_ = null;
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

                debug.Log("[UDP] {0}:{1}, {2}, {3}, Compression:{4}, Sequence:{5}, ConnectionTimeout:{6}",
                          addr_.ip, addr_.port, convertString(encoding_), convertString(option_.Encryption),
                          convertString(option_.CompressionType), option_.SequenceValidation,
                          option_.ConnectionTimeout);
            }

            protected override void onStart ()
            {
                base.onStart();

                try
                {
                    addr_.refresh();

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
                        debug.LogDebug("[UDP] bind {0}:{1}", lep.Address, lep.Port);

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

                    state_ = State.kConnected;

                    onStarted();
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kStartingFailed;
                    error.message = "[UDP] Failure in onStart: " + e.ToString();
                    onFailure(error);
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

                base.onClose();
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
                        debug.LogError("'{0}' message's length is {1} bytes " +
                                       "but UDP single message can't bigger than {2} bytes.",
                                       msg.msg_type, length, kUdpBufferSize);
                        return;
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
                        debug.LogDebug("[UDP] BeginSendTo operation has been cancelled.");
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

                        if (nSent <= 0)
                        {
                            debug.LogError("[UDP] failed to transfer messages.");
                            return;
                        }
                    }

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        FunapiMessage msg = sending_[0];

                        // Removes header and body segment
                        int nLength = msg.header.Count + msg.body.Count;
                        sending_.RemoveAt(0);

                        if (nSent != nLength)
                        {
                            debug.LogError("[UDP] failed to sending a whole message. " +
                                           "buffer:{0} sent:{1}", nLength, nSent);
                        }

                        // Checks unsent messages
                        checkPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    debug.LogDebug("[UDP] BeginSendTo operation has been cancelled.");
                }
                catch (Exception e)
                {
                    onFailedSending();

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "[UDP] Failure in sendBytesCb: " + e.ToString();
                    onFailure(error);
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
                        }

                        // Parses a message
                        parseMessages();

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
                            }
                        }
                        else
                        {
                            debug.LogWarning("[UDP] socket closed");

                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kDisconnected;
                            error.message = "UDP can't receive messages. Maybe the socket is closed.";
                            onDisconnected(error);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.LogDebug("[UDP] BeginReceiveFrom operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "[UDP] Failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
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

                    FunDebug.debug.Log("The udp local port start value ({0}) has been loaded.", local_port_);
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
                    TrustManager.LoadMozRoots();
            }

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
                using_www_ = http_option.UseWWW;

                debug.Log("[HTTP] {0}, {1}, {2}, Compression:{3}, Sequence:{4}, ConnectionTimeout:{5}, UseWWW:{6}",
                          host_url_, convertString(encoding_), convertString(http_option.Encryption),
                          convertString(http_option.CompressionType), http_option.SequenceValidation,
                          http_option.ConnectionTimeout, using_www_);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnected;
                str_cookie_ = "";

                onStarted();
            }

            protected override void onClose ()
            {
                cancelRequest();
                base.onClose();
            }

            protected override bool isSendable
            {
                get
                {
                    if (!base.isSendable)
                        return false;

                    if (cur_request_ != null)
                        return false;

                    return true;
                }
            }

            protected override void wireSend ()
            {
                if (cur_request_ != null)
                    return;

                FunapiMessage msg = null;

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count > 0);
                    msg = sending_[0];
                }

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

#if !NO_UNITY
                // Sending a message
                if (using_www_)
                {
#if UNITY_2017_1_OR_NEWER
                    sendUWRequest(headers, msg);
#else
                    sendWWWRequest(headers, msg);
#endif
                }
                else
#endif
                {
                    sendHttpWebRequest(headers, msg);
                }
            }

#if !NO_UNITY
#if UNITY_2017_1_OR_NEWER
            void sendUWRequest (Dictionary<string, string> headers, FunapiMessage msg)
            {
                Request request = new Request();

                lock (request_lock_)
                {
                    FunDebug.Assert(cur_request_ == null);
                    cur_request_ = request;
                }

                if (msg.body.Count > 0)
                {
                    request.uw_request = new UnityWebRequest(host_url_);
                    request.uw_request.method = UnityWebRequest.kHttpVerbPOST;
                    request.uw_request.uploadHandler = new UploadHandlerRaw(msg.body.Array);
                    request.uw_request.downloadHandler = new DownloadHandlerBuffer();
                }
                else
                {
                    request.uw_request = UnityWebRequest.Get(host_url_);
                }

                foreach (KeyValuePair<string, string> item in headers) {
                    request.uw_request.SetRequestHeader(item.Key, item.Value);
                }
                request.uw_request.SendWebRequest();
                mono.StartCoroutine(uwRequest(request.uw_request));
            }
#else
            void sendWWWRequest (Dictionary<string, string> headers, FunapiMessage msg)
            {
                Request request = new Request();

                lock (request_lock_)
                {
                    FunDebug.Assert(cur_request_ == null);
                    cur_request_ = request;
                }

                if (msg.body.Count > 0)
                {
                    request.www = new WWW(host_url_, msg.body.Array, headers);
                    mono.StartCoroutine(wwwPost(request.www));
                }
                else
                {
                    request.www = new WWW(host_url_, null, headers);
                    mono.StartCoroutine(wwwPost(request.www));
                }
            }
#endif
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

                lock (request_lock_)
                {
                    FunDebug.Assert(cur_request_ == null);
                    cur_request_ = request;
                }

                web_request.BeginGetRequestStream(new AsyncCallback(requestStreamCb), request);
            }

            void onReceiveHeader (Dictionary<string, string> headers)
            {
                headers.Add(kVersionHeaderField, FunapiVersion.kProtocolVersion.ToString());

                if (headers.ContainsKey("set-cookie"))
                {
                    str_cookie_ = headers["set-cookie"];
                    debug.LogDebug("[HTTP] set-cookie : {0}", str_cookie_);
                }

                if (headers.ContainsKey("x-ifun-enc"))
                {
                    headers.Add(kEncryptionHeaderField, headers["x-ifun-enc"]);
                    headers.Remove("x-ifun-enc");
                }

                if (headers.ContainsKey("x-ifun-c"))
                {
                    headers.Add(kUncompressedLengthHeaderField, headers["x-ifun-c"]);
                    headers.Remove("x-ifun-c");
                }

                int body_length = 0;
                if (headers.ContainsKey("content-length"))
                {
                    body_length = Convert.ToInt32(headers["content-length"]);
                    headers.Add(kLengthHeaderField, body_length.ToString());
                    headers.Remove("content-length");
                }

                // Checks buffer's space
                received_size_ = 0;
                next_decoding_offset_ = 0;
                checkReceiveBuffer(body_length);
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
                        debug.LogDebug("[HTTP] request operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "[HTTP] Failure in requestStreamCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void responseCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    if (request.was_aborted)
                    {
                        debug.Log("[HTTP] responseCb - request aborted. ({0})", request.message.msg_type);
                        return;
                    }

                    request.web_response = (HttpWebResponse)request.web_request.EndGetResponse(ar);
                    request.web_request = null;

                    if (request.web_response.StatusCode == HttpStatusCode.OK)
                    {
                        lock (receive_lock_)
                        {
                            var headers = request.web_response.Headers;
                            foreach (string key in headers.AllKeys)
                            {
                                request.headers.Add(key.ToLower(), headers[key]);
                            }

                            onReceiveHeader(request.headers);

                            request.read_stream = request.web_response.GetResponseStream();
                            request.read_stream.BeginRead(receive_buffer_, received_size_,
                                                          receive_buffer_.Length - received_size_,
                                                          new AsyncCallback(readCb), request);
                        }
                    }
                    else
                    {
                        TransportError error = new TransportError();
                        error.type = TransportError.Type.kReceivingFailed;
                        error.message = string.Format("Failed response. status:{0}",
                                                      request.web_response.StatusDescription);
                        onFailure(error);
                    }
                }
                catch (Exception e)
                {
                    WebException we = e as WebException;
                    if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                        (e is ObjectDisposedException || e is NullReferenceException))
                    {
                        // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                        debug.LogDebug("[HTTP] request operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "[HTTP] Failure in responseCb: " + e.ToString();
                    onFailure(error);
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
                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kReceivingFailed;
                            error.message = "Response instance is null.";
                            onFailure(error);
                            return;
                        }

                        lock (receive_lock_)
                        {
                            // Makes a raw message
                            readBodyAndSaveMessage(request.headers);
                        }

                        request.read_stream.Close();
                        request.web_response.Close();

                        lock (request_lock_)
                        {
                            cur_request_ = null;
                        }

                        // Checks unsent messages
                        checkPendingMessages();
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.LogDebug("[HTTP] request operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "[HTTP] Failure in readCb: " + e.ToString();
                    onFailure(error);
                }
            }

#if !NO_UNITY
#if UNITY_2017_1_OR_NEWER
            IEnumerator uwRequest(UnityWebRequest www)
            {
                FunDebug.Assert(cur_request_ != null);

                while (!www.isDone && !cur_request_.cancel)
                {
                    yield return null;
                }

                lock (request_lock_)
                {
                    if (cur_request_.cancel)
                    {
                        cur_request_ = null;
                        yield break;
                    }
                }

                try
                {
                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        sending_.RemoveAt(0);
                    }

                    if (www.error != null && www.error.Length > 0)
                    {
                        throw new Exception(www.error);
                    }

                    // Gets the headers
                    foreach (var item in www.GetResponseHeaders())
                    {
                        cur_request_.headers.Add(item.Key.ToLower(), item.Value);
                    }
#if UNITY_WEBGL
                    // If there is no content-length, adds the content-length.
                    // This is for the WebGL client.
                    if (!cur_request_.headers.ContainsKey("content-length"))
                    {
                        cur_request_.headers.Add("content-length", www.downloadHandler.data.Length.ToString());
                    }
#endif

                    // Decodes message
                    lock (receive_lock_)
                    {
                        onReceiveHeader(cur_request_.headers);

                        Buffer.BlockCopy(www.downloadHandler.data, 0, receive_buffer_, received_size_, www.downloadHandler.data.Length);
                        received_size_ += www.downloadHandler.data.Length;

                        // Makes a raw message
                        readBodyAndSaveMessage(cur_request_.headers);
                    }

                    lock (request_lock_)
                    {
                        cur_request_ = null;
                    }

                    // Checks unsent messages
                    checkPendingMessages();
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kRequestFailed;
                    error.message = "[HTTP] Failure in uwRequest: " + e.ToString();
                    onFailure(error);
                }
            }
#else
            IEnumerator wwwPost (WWW www)
            {
                FunDebug.Assert(cur_request_ != null);

                while (!www.isDone && !cur_request_.cancel)
                {
                    yield return null;
                }

                lock (request_lock_)
                {
                    if (cur_request_.cancel)
                    {
                        cur_request_ = null;
                        yield break;
                    }
                }

                try
                {
                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        sending_.RemoveAt(0);
                    }

                    if (www.error != null && www.error.Length > 0)
                    {
                        throw new Exception(www.error);
                    }

                    // Gets the headers
                    foreach (var item in www.responseHeaders)
                    {
                        cur_request_.headers.Add(item.Key.ToLower(), item.Value);
                    }
#if UNITY_WEBGL
                    // If there is no content-length, adds the content-length.
                    // This is for the WebGL client.
                    if (!cur_request_.headers.ContainsKey("content-length"))
                    {
                        cur_request_.headers.Add("content-length", www.bytes.Length.ToString());
                    }
#endif

                    // Decodes message
                    lock (receive_lock_)
                    {
                        onReceiveHeader(cur_request_.headers);

                        Buffer.BlockCopy(www.bytes, 0, receive_buffer_, received_size_, www.bytes.Length);
                        received_size_ += www.bytes.Length;

                        // Makes a raw message
                        readBodyAndSaveMessage(cur_request_.headers);
                    }

                    lock (request_lock_)
                    {
                        cur_request_ = null;
                    }

                    // Checks unsent messages
                    checkPendingMessages();
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kRequestFailed;
                    error.message = "[HTTP] Failure in wwwPost: " + e.ToString();
                    onFailure(error);
                }
            }
#endif
#endif

            void cancelRequest ()
            {
                lock (request_lock_)
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
#if UNITY_2017_1_OR_NEWER
                        if (cur_request_.uw_request != null)
                            cur_request_.cancel = true;
#else
                        if (cur_request_.www != null)
                            cur_request_.cancel = true;
#endif
#endif
                        cur_request_ = null;
                    }
                }
            }

            protected override void onFailure (TransportError error)
            {
                cancelRequest();
                base.onFailure(error);
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
#if !NO_UNITY
#if UNITY_2017_1_OR_NEWER
                public UnityWebRequest uw_request = null;
#else
                public WWW www = null;
#endif
                public bool cancel = false;
#endif
                public Dictionary<string, string> headers = new Dictionary<string, string>();
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
            bool using_www_ = false;
            object request_lock_ = new object();
            Request cur_request_ = null;
        }

        // Websocket transport layer
        class WebsocketTransport : Transport
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            public class WebSocketJS
            {
                public WebSocketJS(Uri url)
                {
                    url_ = url;

                    string protocol = url_.Scheme;
                    if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                    {
                        throw new ArgumentException("[Websocket] Unsupported protocol: " + protocol);
                    }

                    alive_ = true;
                }

                [DllImport("__Internal")]
                private static extern int SocketCreate (string url);

                [DllImport("__Internal")]
                private static extern int SocketState (int socketInstance);

                [DllImport("__Internal")]
                private static extern void SocketSend (int socketInstance, byte[] ptr, int length);

                [DllImport("__Internal")]
                private static extern void SocketRecv (int socketInstance, byte[] ptr, int length);

                [DllImport("__Internal")]
                private static extern int SocketRecvLength (int socketInstance);

                [DllImport("__Internal")]
                private static extern void SocketClose (int socketInstance);

                [DllImport("__Internal")]
                private static extern string SocketError (int socketInstance);

                [DllImport("__Internal")]
                private static extern string SocketCloseReason (int socketInstance);

                [DllImport("__Internal")]
                private static extern int SocketCloseCode (int socketInstance);

                public void Send(byte[] buffer)
                {
                    SocketSend (native_ref_, buffer, buffer.Length);

                    if (SendCallback != null)
                    {
                        SendCallback(true);
                    }
                }

                public IEnumerator Recv()
                {
                    int length;
                    while (true)
                    {
                        if (!alive_)
                        {
                            yield break;
                        }

                        length = SocketRecvLength (native_ref_);
                        if (length != 0)
                        {
                            break;
                        }

                        yield return null;
                    }

                    byte[] buffer = new byte[length];
                    SocketRecv (native_ref_, buffer, length);

                    if (ReceiveCallback != null)
                    {
                        ReceiveCallback(buffer);
                    }
                }

                public IEnumerator GetError()
                {
                    string reason = null;

                    while (true)
                    {
                        if (!alive_)
                        {
                            yield break;
                        }

                        reason = SocketError(native_ref_);
                        if (reason != null)
                        {
                            break;
                        }

                        yield return null;
                    }

                    if (ErrorCallback != null)
                    {
                        ErrorCallback(reason);
                    }
                }

                public IEnumerator Connect()
                {
                    native_ref_ = SocketCreate (url_.ToString());

                    while (true)
                    {
                        if (!alive_)
                        {
                            yield break;
                        }

                        if (SocketState(native_ref_) != 0)
                        {
                            break;
                        }

                        yield return null;
                    }

                    if (StartCallback != null)
                    {
                        StartCallback();
                    }
                }

                public IEnumerator Close()
                {
                    SocketClose(native_ref_);

                    while (true)
                    {
                        if(SocketCloseCode(native_ref_) != 0)
                        {
                            break;
                        }

                        yield return null;
                    }

                    alive_ = false;

                    if (CloseCallback != null)
                    {
                        CloseCallback();
                    }
                }

                public string close_reason
                {
                    get
                    {
                        string reason = SocketCloseReason (native_ref_);
                        return reason == null ? "" : reason;
                    }
                }

                public int close_code
                {
                    get
                    {
                        return SocketCloseCode (native_ref_);
                    }
                }

                public event System.Action StartCallback;
                public event System.Action CloseCallback;
                public event System.Action<bool> SendCallback;
                public event System.Action<byte[]> ReceiveCallback;
                public event System.Action<string> ErrorCallback;

                Uri url_;
                int native_ref_ = 0;
                bool alive_ = false;
            }
#endif

            public WebsocketTransport (string hostname_or_ip, UInt16 port,
                                       FunEncoding type, TransportOption websocket_option)
            {
                protocol_ = TransportProtocol.kWebsocket;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = websocket_option;

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
                    {
                        return wsock_ != null && state_ >= State.kConnected;
                    }
                }
            }

            void setAddress (string host, UInt16 port)
            {
                addr_ = new HostAddr(host, port);

                // Sets host url
                host_url_ = string.Format("ws://{0}:{1}/", host, port);

                debug.Log("[Websocket] {0}, {1}, {2}, Compression:{3}, ConnectionTimeout:{4}",
                          host_url_, convertString(encoding_), convertString(option_.Encryption),
                          convertString(option_.CompressionType), option_.ConnectionTimeout);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnecting;

                lock (sock_lock_)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    wsock_ = new WebSocketJS(new Uri(host_url_));
                    wsock_.StartCallback += startJSCb;
                    wsock_.CloseCallback += closeJSCb;
                    wsock_.ErrorCallback += errorJSCb;
                    wsock_.SendCallback += sendBytesCb;
                    wsock_.ReceiveCallback += receiveBytesJSCb;

                    mono.StartCoroutine(wsock_.Connect());
#else
                    wsock_ = new WebSocket(host_url_);
                    wsock_.OnOpen += startCb;
                    wsock_.OnClose += closeCb;
                    wsock_.OnError += errorCb;
                    wsock_.OnMessage += receiveBytesCb;

                    wsock_.ConnectAsync();
#endif
                }
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (wsock_ != null)
                    {
#if UNITY_WEBGL && !UNITY_EDITOR
                        mono.StartCoroutine(wsock_.Close());
#else
                        wsock_.Close();
#endif
                        wsock_ = null;
                    }
                }

                base.onClose();
            }

            protected override void wireSend ()
            {
                try
                {
                    int length = getSendingBufferLength();
                    byte[] buffer = new byte[length];
                    int offset = 0;

                    lock (sending_lock_)
                    {
                        foreach (FunapiMessage msg in sending_)
                        {
                            if (msg.header.Count > 0)
                            {
                                Buffer.BlockCopy(msg.header.Array, 0, buffer, offset, msg.header.Count);
                                offset += msg.header.Count;
                            }
                            if (msg.body.Count > 0)
                            {
                                Buffer.BlockCopy(msg.body.Array, 0, buffer, offset, msg.body.Count);
                                offset += msg.body.Count;
                            }
                        }
                    }

                    lock (sock_lock_)
                    {
                        if (wsock_ == null)
                        {
                            return;
                        }

                        sending_length_ = length;

#if UNITY_WEBGL && !UNITY_EDITOR
                        wsock_.Send(buffer);
#else
                        wsock_.SendAsync(buffer, sendBytesCb);
#endif
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "[Websocket] Failure in wireSend: " + e.ToString();
                    onFailure(error);
                }
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            void startJSCb ()
            {
                state_ = State.kHandshaking;

                debug.LogDebug("[Websocket] Connected. Starts handshaking..");

                mono.StartCoroutine(wsock_.Recv());
                mono.StartCoroutine(wsock_.GetError());
            }
#endif

            void startCb (object sender, EventArgs args)
            {
                state_ = State.kHandshaking;

                debug.LogDebug("[Websocket] Connected. Starts handshaking..");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            void closeJSCb ()
            {
                debug.Log("[Websocket] Closed. ({0}) {1}", wsock_.close_code, wsock_.close_reason);

                CloseStatusCode code = (CloseStatusCode)wsock_.close_code;
                if (code != CloseStatusCode.Normal && code != CloseStatusCode.NoStatus)
                {
                    TransportError error = new TransportError();
                    if (state != State.kEstablished)
                        error.type = TransportError.Type.kStartingFailed;
                    else
                        error.type = TransportError.Type.kDisconnected;
                    error.message = string.Format("[Websocket] Failure: {0}({1}) : {2}", code, wsock_.close_code, wsock_.close_reason);
                    onFailure(error);
                }
            }
#endif

            void closeCb (object sender, CloseEventArgs args)
            {
                debug.LogDebug("[Websocket] Closed. ({0}) {1}", args.Code, args.Reason);

                CloseStatusCode code = (CloseStatusCode)args.Code;
                if (code != CloseStatusCode.Normal && code != CloseStatusCode.NoStatus)
                {
                    TransportError error = new TransportError();
                    if (state != State.kEstablished)
                        error.type = TransportError.Type.kStartingFailed;
                    else
                        error.type = TransportError.Type.kDisconnected;
                    error.message = string.Format("[Websocket] Failure: {0}({1}) : {2}", code, args.Code, args.Reason);
                    onFailure(error);
                }
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            void errorJSCb (string error_msg)
            {
                TransportError error = new TransportError();
                error.type = TransportError.Type.kWebsocketError;
                error.message = "[Websocket] Error: " + error_msg;
                onFailure(error);
            }
#endif

            void errorCb (object sender, WebSocketSharp.ErrorEventArgs args)
            {
                TransportError error = new TransportError();
                error.type = TransportError.Type.kWebsocketError;
                error.message = "[Websocket] Error: " + args.Message;
                onFailure(error);
            }

            void sendBytesCb (bool completed)
            {
                try
                {
                    if (!completed)
                    {
                        debug.LogError("[Websocket] Failed to transfer messages.");
                        return;
                    }

                    int nSent = sending_length_;
                    if (nSent > 0)
                    {
                        lock (sending_lock_)
                        {
                            while (nSent > 0)
                            {
                                if (sending_.Count > 0)
                                {
                                    // removes a sent message.
                                    FunapiMessage msg = sending_[0];
                                    int length = msg.header.Count + msg.body.Count;
                                    nSent -= length;

                                    sending_.RemoveAt(0);
                                }
                                else
                                {
                                    debug.LogError("[Websocket] Sent {0} more bytes but couldn't find the sending buffer.", nSent);
                                    break;
                                }
                            }

                            if (sending_.Count != 0)
                            {
                                debug.LogError("[Websocket] {0} message(s) left in the sending buffer.", sending_.Count);
                            }

                            sending_length_ = 0;

                            // Sends pending messages
                            checkPendingMessages();
                        }
                    }
                    else
                    {
                        debug.LogWarning("[Websocket] socket closed");
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "[Websocket] Failure in sendBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            void receiveBytesJSCb (byte[] buffer)
            {
                try
                {
                    lock (receive_lock_)
                    {
                        int len = buffer.Length;

                        checkReceiveBuffer(len);

                        // Copy the recieved messages
                        Buffer.BlockCopy(buffer, 0, receive_buffer_, received_size_, len);
                        received_size_ += len;

                        // Parses messages
                        parseMessages();

                        if (wsock_ == null)
                        {
                            return;
                        }

                        mono.StartCoroutine(wsock_.Recv());
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "[Websocket] Failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }
#endif

            void receiveBytesCb (object sender, MessageEventArgs args)
            {
                try
                {
                    lock (receive_lock_)
                    {
                        if (args.RawData != null)
                        {
                            // Checks buffer space
                            checkReceiveBuffer(args.RawData.Length);

                            // Copy the recieved messages
                            Buffer.BlockCopy(args.RawData, 0, receive_buffer_, received_size_, args.RawData.Length);
                            received_size_ += args.RawData.Length;

                            // Parses messages
                            parseMessages();
                        }
                        else
                        {
                            debug.LogWarning("[Websocket] socket closed");

                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kDisconnected;
                            error.message = "[Websocket] Can't receive messages. Maybe the socket is closed.";
                            onDisconnected(error);
                        }
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "[Websocket] Failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }


            HostAddr addr_;
            string host_url_;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebSocketJS wsock_;
#else
            WebSocket wsock_;
#endif
            object sock_lock_ = new object();
            int sending_length_ = 0;
        }
    }

}  // namespace Fun
