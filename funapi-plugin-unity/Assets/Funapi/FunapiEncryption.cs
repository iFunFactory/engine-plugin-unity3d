// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;


namespace Fun
{
    public enum EncryptionType
    {
        kNoneEncryption = 0,
        kDefaultEncryption = 100,
        kDummyEncryption,
        kIFunEngine1Encryption,
        kIFunEngine2Encryption
    }


    // Abstract class
    abstract class Encryptor
    {
        public static Encryptor Create (EncryptionType type)
        {
            if (type == EncryptionType.kDummyEncryption)
                return new Encryptor0();
            else if (type == EncryptionType.kIFunEngine1Encryption)
                return new Encryptor1();
            else if (type == EncryptionType.kIFunEngine2Encryption)
                return new Encryptor2();

            FunDebug.LogWarning("Unknown encryptor: {0}", type);
            FunDebug.Assert(false);

            return null;
        }

        public static Encryptor Create (string name)
        {
            if (name == Encryptor0.kName)
                return new Encryptor0();
            else if (name == Encryptor1.kName)
                return new Encryptor1();
            else if (name == Encryptor2.kName)
                return new Encryptor2();

            FunDebug.LogWarning("Unknown encryptor: {0}", name);
            FunDebug.Assert(false);

            return null;
        }

        protected Encryptor (EncryptionType encryption, string encryption_name, State initial_state)
        {
            encryption_ = encryption;
            name_ = encryption_name;
            state_ = initial_state;
        }

        public virtual bool Handshake (string in_header, ref string out_header)
        {
            FunDebug.Assert(false);
            return true;
        }

        public abstract Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header);
        public abstract Int64 Decrypt (ArraySegment<byte> src, ArraySegment<byte> dst, string in_header);

        public EncryptionType encryption
        {
            get { return encryption_; }
        }

        public string name
        {
            get { return name_; }
        }

        public State state
        {
            get { return state_; }
        }

        protected void SetState (State state)
        {
            state_ = state;
        }

        protected static byte CircularLeftShift (byte value, int shift_len)
        {
            return (byte)((value << shift_len) | (value >> (sizeof(byte) * 8 - shift_len)));
        }

        protected static UInt32 CircularLeftShift (UInt32 value, int shift_len)
        {
            return (value << shift_len) | (value >> (sizeof(UInt32) * 8 - shift_len));
        }


        public enum State
        {
            kHandshaking = 0,
            kEstablished
        }

        protected static readonly int kBlockSize = sizeof(UInt32);

        EncryptionType encryption_;
        string name_;
        State state_;
    }


    // encryption - dummy
    class Encryptor0 : Encryptor
    {
        public Encryptor0 () : base(EncryptionType.kDummyEncryption, kName, State.kEstablished)
        {
        }

        public override Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header)
        {
            FunDebug.Assert(state == State.kEstablished);

            if (dst.Count < src.Count)
                return -1;

            if (!src.Equals(dst))
                dst = new ArraySegment<byte>(src.Array, 0, src.Count);

            return src.Count;
        }

        public override Int64 Decrypt (ArraySegment<byte> src, ArraySegment<byte> dst, string in_header)
        {
            if (in_header.Length > 0)
            {
                FunDebug.LogWarning("Wrong encryptor header.");
                return -1;
            }

            string out_header = "";
            return Encrypt(src, dst, ref out_header);
        }

        public static readonly string kName = "dummy";
    }


    // encryption - ife1
    class Encryptor1 : Encryptor
    {
        public Encryptor1 () : base(EncryptionType.kIFunEngine1Encryption, kName, State.kHandshaking)
        {
            enc_key_ = 0;
            dec_key_ = 0;
        }

        public override bool Handshake (string in_header, ref string out_header)
        {
            FunDebug.Assert(state == State.kHandshaking);

            enc_key_ = Convert.ToUInt32(in_header);
            dec_key_ = enc_key_;

            SetState(State.kEstablished);

            return true;
        }

        public override Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header)
        {
            FunDebug.Assert(state == State.kEstablished);

            return Encrypt(src, dst, ref enc_key_);
        }

        public override Int64 Decrypt (ArraySegment<byte> src, ArraySegment<byte> dst, string in_header)
        {
            FunDebug.Assert(state == State.kEstablished);

            if (in_header.Length > 0)
            {
                FunDebug.LogWarning("Wrong encryptor header.");
                return -1;
            }

            return Encrypt(src, dst, ref dec_key_);
        }

        static Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref UInt32 key)
        {
            if (dst.Count < src.Count)
                return -1;

            // update key
            key = 8253729 * key + 2396403;

            int shift_len = (int)(key & 0x0F);
            UInt32 key32 = CircularLeftShift(key, shift_len);

            // Encrypted in kBlockSize
            for (int i = 0; i < (src.Count / kBlockSize); ++i)
            {
                UInt32 s = BitConverter.ToUInt32(src.Array, src.Offset + i * kBlockSize);
                byte[] d = BitConverter.GetBytes(s ^ key32);
                FunDebug.Assert(d.Length == kBlockSize);

                for (int j = 0; j < d.Length; ++j) {
                    dst.Array[dst.Offset + i * kBlockSize + j] = d[j];
                }
            }

            byte key8 = 0;
            byte[] k = BitConverter.GetBytes(key);
            if (BitConverter.IsLittleEndian)
                key8 = CircularLeftShift(k[0], shift_len);
            else
                key8 = CircularLeftShift(k[3], shift_len);

            // The remaining values are encrypted in units of 1byte
            for (int i = 0; i < (src.Count % kBlockSize); ++i)
            {
                int idx = src.Count - 1 - i;
                dst.Array[dst.Offset + idx] = (byte)(src.Array[src.Offset + idx] ^ key8);
            }

            return src.Count;
        }

        public static readonly string kName = "ife1";

        UInt32 enc_key_;
        UInt32 dec_key_;
    }


    // encryption - ife2
    class Encryptor2 : Encryptor
    {
        public Encryptor2 () : base(EncryptionType.kIFunEngine2Encryption, kName, State.kEstablished)
        {
        }

        public override Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header)
        {
            FunDebug.Assert(state == State.kEstablished);

            return Encrypt(src, dst, true);
        }

        public override Int64 Decrypt (ArraySegment<byte> src, ArraySegment<byte> dst, string in_header)
        {
            FunDebug.Assert(state == State.kEstablished);

            if (in_header.Length > 0)
            {
                FunDebug.LogWarning("Wrong encryptor header.");
                return -1;
            }

            return Encrypt(src, dst, false);
        }

        static Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, bool encrypt)
        {
            byte key = (byte)src.Count;
            int shift_len = 0;

            if (encrypt)
                shift_len = key % (sizeof(byte) * 8);
            else
                shift_len = (sizeof(byte) * 8) - (key % (sizeof(byte) * 8));

            for (Int64 i = 0; i < src.Count; ++i)
            {
                if (encrypt)
                {
                    dst.Array[dst.Offset + i] = CircularLeftShift((byte)(src.Array[src.Offset + i] ^ key), shift_len);
                }
                else
                {
                    dst.Array[dst.Offset + i] = (byte)(CircularLeftShift(src.Array[src.Offset + i], shift_len) ^ key);
                }
            }

            return src.Count;
        }

        public static readonly string kName = "ife2";
    }


    public class FunapiEncryptor
    {
        bool CreateEncryptor (EncryptionType type)
        {
            if (encryptors_.ContainsKey(type))
                return true;

            Encryptor encryptor = Encryptor.Create(type);
            if (encryptor == null)
            {
                FunDebug.LogWarning("Failed to create encryptor: {0}", type);
                return false;
            }

            encryptors_[type] = encryptor;

            if (default_encryptor_ == EncryptionType.kNoneEncryption)
                SetDefaultEncryption(type);

            return true;
        }

        void SetDefaultEncryption (EncryptionType type)
        {
            if (default_encryptor_ == type)
                return;

            default_encryptor_ = type;
            FunDebug.Log("Set default encryption: {0}", (int)type);
        }

        protected void SetEncryption (EncryptionType type)
        {
            if (!CreateEncryptor(type))
                return;

            SetDefaultEncryption(type);
        }

        protected EncryptionType GetEncryption (FunapiMessage message)
        {
            if (message.enc_type != EncryptionType.kDefaultEncryption)
                return message.enc_type;

            return default_encryptor_;
        }

        protected void ParseEncryptionHeader (ref string encryption_type, ref string encryption_header)
        {
            int index = encryption_header.IndexOf(kDelim1);
            if (index != -1)
            {
                encryption_type = encryption_header.Substring(0, index);
                encryption_header = encryption_header.Substring(index + 1);
            }
            else if (encryption_header != " ") // for HTTP header's blank
            {
                encryption_type = encryption_header;
            }
        }

        protected bool Handshake (string encryption_type, string encryption_header)
        {
            if (encryption_type == kEncryptionHandshakeBegin)
            {
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

                // Create encryptors
                foreach (EncryptionType type in encryption_list)
                {
                    if (!CreateEncryptor(type))
                        return false;
                }
            }
            else
            {
                // Encryption handshake message
                EncryptionType encryption = (EncryptionType)Convert.ToInt32(encryption_type);
                Encryptor encryptor = encryptors_[encryption];
                if (encryptor == null)
                {
                    FunDebug.Log("Unknown encryption: {0}", encryption_type);
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

                FunDebug.Assert(encryptor.state == Encryptor.State.kEstablished);
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

            return handshake_complete;
        }

        protected bool EncryptMessage (FunapiMessage message, EncryptionType type, ref string header)
        {
            if (!encryptors_.ContainsKey(type))
            {
                FunDebug.Log("Unknown encryption: {0}", type);
                return false;
            }

            Encryptor encryptor = encryptors_[type];
            if (encryptor == null || encryptor.state != Encryptor.State.kEstablished)
            {
                FunDebug.Log("Invalid encryption: {0}", type);
                return false;
            }

            if (message.buffer.Count > 0)
            {
                Int64 nSize = encryptor.Encrypt(message.buffer, message.buffer, ref header);
                if (nSize <= 0)
                {
                    FunDebug.Log("Failed to encrypt.");
                    return false;
                }

                FunDebug.Assert(nSize == message.buffer.Count);
            }

            return true;
        }

        protected bool DecryptMessage (ArraySegment<byte> buffer, string encryption_type, string encryption_header)
        {
            EncryptionType type = (EncryptionType)Convert.ToInt32(encryption_type);
            if (!encryptors_.ContainsKey(type))
            {
                FunDebug.Log("Unknown encryption: {0}", type);
                return false;
            }

            Encryptor encryptor = encryptors_[type];
            if (encryptor == null)
            {
                FunDebug.Log("Invalid encryption: {0}", type);
                return false;
            }

            Int64 nSize = encryptor.Decrypt(buffer, buffer, encryption_header);
            if (nSize <= 0)
            {
                FunDebug.Log("Failed to decrypt.");
                return false;
            }

            return true;
        }


        static readonly string kEncryptionHandshakeBegin = "HELLO!";
        static readonly char kDelim1 = '-';
        static readonly char kDelim2 = ',';

        EncryptionType default_encryptor_ = EncryptionType.kNoneEncryption;
        Dictionary<EncryptionType, Encryptor> encryptors_ = new Dictionary<EncryptionType, Encryptor>();
    }
}
