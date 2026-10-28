using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnRevive.Patcher
{
    // Minimal editor for the one field we need: BuildSettings.m_EnabledVRDevices inside
    // <Game>_Data/globalgamemanagers. That list is the "Virtual Reality SDKs" list from the
    // editor's Player Settings; Unity's legacy XR only ever tries the devices named in it,
    // in order, falling through to the next one when a device fails to initialize.
    //
    // Supports serialized-file formats 17-21 (Unity 2017.x-2019.x) without type trees, which
    // is what player builds ship. Anything unexpected throws rather than guessing.
    internal sealed class GlobalGameManagers
    {
        private const int BuildSettingsClassId = 141;
        private const int MonoBehaviourClassId = 114;
        private const int ObjectAlignment = 8;

        private sealed class ObjectInfo
        {
            public int TableOffset;   // file offset of this object's byteStart field
            public uint Start;        // relative to dataOffset
            public uint Size;
            public int ClassId;
        }

        private readonly byte[] _data;
        private readonly uint _dataOffset;
        private readonly List<ObjectInfo> _objects = new List<ObjectInfo>();
        private readonly ObjectInfo _buildSettings;
        private readonly int _vrListStart;  // absolute file offsets of m_EnabledVRDevices
        private readonly int _vrListEnd;

        public string UnityVersion { get; }
        public List<string> VRDevices { get; }

        public GlobalGameManagers(byte[] data)
        {
            _data = data;

            uint fileSize = ReadUInt32BE(4);
            uint version  = ReadUInt32BE(8);
            _dataOffset   = ReadUInt32BE(12);

            if (version < 17 || version > 21)
                throw new NotSupportedException($"serialized file format {version} (expected 17-21)");
            if (fileSize != data.Length)
                throw new InvalidDataException($"header says {fileSize} bytes, file is {data.Length}");
            if (data[16] != 0)
                throw new NotSupportedException("big-endian serialized file");

            int p = 20;
            UnityVersion = ReadCString(ref p);
            p += 4; // target platform
            if (data[p++] != 0)
                throw new NotSupportedException("serialized file has type trees");

            int typeCount = ReadInt32(p); p += 4;
            var classIds = new int[typeCount];
            for (int i = 0; i < typeCount; i++)
            {
                classIds[i] = ReadInt32(p); p += 4;
                p += 1; // isStrippedType
                p += 2; // scriptTypeIndex
                if (classIds[i] == MonoBehaviourClassId) p += 16; // scriptID
                p += 16; // oldTypeHash
            }

            int objectCount = ReadInt32(p); p += 4;
            for (int i = 0; i < objectCount; i++)
            {
                p = Align(p, 4);
                var obj = new ObjectInfo
                {
                    TableOffset = p + 8,
                    Start       = ReadUInt32(p + 8),
                    Size        = ReadUInt32(p + 12),
                    ClassId     = classIds[ReadInt32(p + 16)],
                };
                _objects.Add(obj);
                p += 20;
            }

            // We re-pack objects back-to-back on 8-byte boundaries after the edit, so refuse
            // any file that isn't already laid out exactly that way.
            uint cursor = 0;
            foreach (var obj in _objects)
            {
                if (obj.Start != Align(cursor, ObjectAlignment))
                    throw new InvalidDataException($"unexpected object layout at data offset {obj.Start}");
                cursor = obj.Start + obj.Size;
            }
            if (_dataOffset + cursor != data.Length)
                throw new InvalidDataException("trailing data after last object");

            _buildSettings = _objects.Find(o => o.ClassId == BuildSettingsClassId)
                ?? throw new InvalidDataException("no BuildSettings object");

            // BuildSettings starts with: m_Scenes, m_PreloadedPlugins, m_EnabledVRDevices
            int q = (int)(_dataOffset + _buildSettings.Start);
            int objEnd = q + (int)_buildSettings.Size;
            ReadStringArray(ref q, objEnd);
            ReadStringArray(ref q, objEnd);
            _vrListStart = q;
            VRDevices = ReadStringArray(ref q, objEnd);
            _vrListEnd = q;
        }

        // Returns a full copy of the file with m_EnabledVRDevices replaced.
        public byte[] WithVRDevices(IList<string> devices)
        {
            byte[] list = EncodeStringArray(devices);
            int delta = list.Length - (_vrListEnd - _vrListStart);

            var newStart = new uint[_objects.Count];
            var newSize  = new uint[_objects.Count];
            uint cursor = 0;
            for (int i = 0; i < _objects.Count; i++)
            {
                newSize[i]  = (uint)(_objects[i].Size + (_objects[i] == _buildSettings ? delta : 0));
                newStart[i] = Align(cursor, ObjectAlignment);
                cursor = newStart[i] + newSize[i];
            }

            var output = new byte[_dataOffset + cursor];
            Buffer.BlockCopy(_data, 0, output, 0, (int)_dataOffset); // header + metadata
            WriteUInt32BE(output, 4, (uint)output.Length);

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                WriteUInt32(output, obj.TableOffset,     newStart[i]);
                WriteUInt32(output, obj.TableOffset + 4, newSize[i]);

                int src = (int)(_dataOffset + obj.Start);
                int dst = (int)(_dataOffset + newStart[i]);
                if (obj != _buildSettings)
                {
                    Buffer.BlockCopy(_data, src, output, dst, (int)obj.Size);
                    continue;
                }
                int before = _vrListStart - src;
                int after  = src + (int)obj.Size - _vrListEnd;
                Buffer.BlockCopy(_data, src, output, dst, before);
                Buffer.BlockCopy(list, 0, output, dst + before, list.Length);
                Buffer.BlockCopy(_data, _vrListEnd, output, dst + before + list.Length, after);
            }
            return output;
        }

        private List<string> ReadStringArray(ref int p, int limit)
        {
            int count = ReadInt32(p); p += 4;
            if (count < 0 || count > 4096)
                throw new InvalidDataException($"implausible string array length {count}");
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                int len = ReadInt32(p); p += 4;
                if (len < 0 || p + len > limit)
                    throw new InvalidDataException($"implausible string length {len}");
                result.Add(Encoding.UTF8.GetString(_data, p, len));
                p = Align(p + len, 4);
            }
            if (p > limit) throw new InvalidDataException("string array overruns BuildSettings");
            return result;
        }

        private static byte[] EncodeStringArray(IList<string> strings)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(strings.Count);
                foreach (string s in strings)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(s);
                    w.Write(bytes.Length);
                    w.Write(bytes);
                    while (ms.Position % 4 != 0) w.Write((byte)0);
                }
                return ms.ToArray();
            }
        }

        private string ReadCString(ref int p)
        {
            int end = Array.IndexOf(_data, (byte)0, p);
            string s = Encoding.ASCII.GetString(_data, p, end - p);
            p = end + 1;
            return s;
        }

        private int  ReadInt32(int p)    => BitConverter.ToInt32(_data, p);
        private uint ReadUInt32(int p)   => BitConverter.ToUInt32(_data, p);
        private uint ReadUInt32BE(int p) => (uint)(_data[p] << 24 | _data[p + 1] << 16 | _data[p + 2] << 8 | _data[p + 3]);

        private static void WriteUInt32(byte[] b, int p, uint v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
        }

        private static void WriteUInt32BE(byte[] b, int p, uint v)
        {
            b[p] = (byte)(v >> 24); b[p + 1] = (byte)(v >> 16); b[p + 2] = (byte)(v >> 8); b[p + 3] = (byte)v;
        }

        private static int  Align(int v, int a)  => (v + a - 1) & ~(a - 1);
        private static uint Align(uint v, int a) => (uint)((v + a - 1) & ~(a - 1));
    }
}
