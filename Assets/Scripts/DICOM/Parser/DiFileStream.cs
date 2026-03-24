using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DICOMParser
{
    public class DiFileStream : Stream//ahora hereda de Stream
    {
        private Stream _stream;  // <-- puede ser FileStream o MemoryStream

        public bool MetaGroup, BeforeMetaGroup;

        public int VrFormat { get; set; }
        public int Endianess { get; set; }

        // ---------- CONSTRUCTOR PARA ARCHIVO (PC) ----------
        public DiFileStream(string fName)//si se inicializa con string
        {
            _stream = new FileStream(fName, FileMode.Open, FileAccess.Read, FileShare.Read);
            Endianess = DiFile.EndianUnknown;
            VrFormat = DiFile.VrUnknown;
            BeforeMetaGroup = true;
            MetaGroup = false;
        }

        // ---------- CONSTRUCTOR PARA BYTES (ANDROID) ----------
        public DiFileStream(byte[] data)//si se inicializa con byte[]
        {
            _stream = new MemoryStream(data);
            Endianess = DiFile.EndianUnknown;
            VrFormat = DiFile.VrUnknown;
            BeforeMetaGroup = true;
            MetaGroup = false;
        }

        // ============ OVERRIDES OBLIGATORIOS DE STREAM ============

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => _stream.Position = value; }
        public override void Flush() => _stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
        public override void SetLength(long value) => _stream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // ============ MÉTODOS PERSONALIZADOS (TU CÓDIGO) ============

        public uint ReadUShort(int endianess)
        {
            var val = new byte[2];
            Read(val, 0, val.Length);
            if (endianess == DiFile.EndianBig)
                Array.Reverse(val);
            return BitConverter.ToUInt16(val, 0);
        }

        public int ReadShort(int endianess)
        {
            var val = new byte[2];
            Read(val, 0, val.Length);
            if (endianess == DiFile.EndianBig)
                Array.Reverse(val);
            return BitConverter.ToInt16(val, 0);
        }

        public int ReadInt(int endianess)
        {
            var val = new byte[4];
            Read(val, 0, val.Length);
            if (endianess == DiFile.EndianBig)
                Array.Reverse(val);
            return BitConverter.ToInt32(val, 0);
        }

        public bool SkipHeader()
        {
            if (!CanSeek || Length < 128 || Seek(128, SeekOrigin.Begin) <= 0)
                return false;

            byte[] dicm = new byte[4];
            return Read(dicm, 0, 4) == 4 &&
                   Encoding.ASCII.GetString(dicm) == "DICM";
        }
    }
}
