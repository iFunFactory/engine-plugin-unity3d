// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;

#if !NO_UNITY
using UnityEngine;
#endif


namespace Fun
{
    public enum EncryptionType
    {
        kDefaultEncryption = 100,
        kDummyEncryption,
        kIFunEngine1Encryption,
        kIFunEngine2Encryption
    }


    // Abstract class
    public abstract class Encryptor
    {
        public static Encryptor Create (EncryptionType type)
        {
            if (type == EncryptionType.kDummyEncryption)
                return new Encryptor0();
            else if (type == EncryptionType.kIFunEngine1Encryption)
                return new Encryptor1();
            else if (type == EncryptionType.kIFunEngine2Encryption)
                return new Encryptor2();

            DebugUtils.LogWarning("Unknown encryptor: {0}", type);
            DebugUtils.Assert(false);

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

            DebugUtils.LogWarning("Unknown encryptor: {0}", name);
            DebugUtils.Assert(false);

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
            DebugUtils.Assert(false);
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

        private EncryptionType encryption_;
        private string name_;
        private State state_;
    }


    // encryption - dummy
    public class Encryptor0 : Encryptor
    {
        public Encryptor0 () : base(EncryptionType.kDummyEncryption, kName, State.kEstablished)
        {
        }

        public override Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header)
        {
            DebugUtils.Assert(state == State.kEstablished);

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
                DebugUtils.LogWarning("Wrong encryptor header.");
                return -1;
            }

            string out_header = "";
            return Encrypt(src, dst, ref out_header);
        }

        internal static readonly string kName = "dummy";
    }


    // encryption - ife1
    public class Encryptor1 : Encryptor
    {
        public Encryptor1 () : base(EncryptionType.kIFunEngine1Encryption, kName, State.kHandshaking)
        {
            enc_key_ = 0;
            dec_key_ = 0;
        }

        public override bool Handshake (string in_header, ref string out_header)
        {
            DebugUtils.Assert(state == State.kHandshaking);

            enc_key_ = Convert.ToUInt32(in_header);
            dec_key_ = enc_key_;

            SetState(State.kEstablished);

            return true;
        }

        public override Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header)
        {
            DebugUtils.Assert(state == State.kEstablished);

            return Encrypt(src, dst, ref enc_key_);
        }

        public override Int64 Decrypt (ArraySegment<byte> src, ArraySegment<byte> dst, string in_header)
        {
            DebugUtils.Assert(state == State.kEstablished);

            if (in_header.Length > 0)
            {
                DebugUtils.LogWarning("Wrong encryptor header.");
                return -1;
            }

            return Encrypt(src, dst, ref dec_key_);
        }

        private static Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref UInt32 key)
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
                DebugUtils.Assert(d.Length == kBlockSize);

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

        internal static readonly string kName = "ife1";

        private UInt32 enc_key_;
        private UInt32 dec_key_;
    }


    // encryption - ife2
    public class Encryptor2 : Encryptor
    {
        public Encryptor2 () : base(EncryptionType.kIFunEngine2Encryption, kName, State.kEstablished)
        {
        }

        public override Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, ref string out_header)
        {
            DebugUtils.Assert(state == State.kEstablished);

            return Encrypt(src, dst, true);
        }

        public override Int64 Decrypt (ArraySegment<byte> src, ArraySegment<byte> dst, string in_header)
        {
            DebugUtils.Assert(state == State.kEstablished);

            if (in_header.Length > 0)
            {
                DebugUtils.LogWarning("Wrong encryptor header.");
                return -1;
            }

            return Encrypt(src, dst, false);
        }

        private static Int64 Encrypt (ArraySegment<byte> src, ArraySegment<byte> dst, bool encrypt)
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

        internal static readonly string kName = "ife2";
    }
}
