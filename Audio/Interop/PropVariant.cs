using System;
using System.Runtime.InteropServices;

namespace HamstuffAgcGuard.Audio.Interop
{
    /// <summary>
    /// PROPVARIANT covering the value types this app reads or writes: strings,
    /// 16/32-bit integers, and binary blobs (VT_BLOB / VT_VECTOR|VT_UI1 - same
    /// {count, pointer} shape used for both). Always call <see cref="Clear"/>
    /// after a successful GetValue call to release any native memory it holds.
    ///
    /// Explicitly sized to 24 bytes (8-byte header + a full 16-byte union slot)
    /// rather than the 16 bytes a simple-scalar-only variant would need: some
    /// properties turn out to be blob-shaped ({UInt32 length; IntPtr data;} is
    /// itself 16 bytes on x64 once pointer-aligned), and this struct is used for
    /// properties whose real type isn't known in advance - undersizing it would
    /// risk letting native code write past the end of it.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public short DataType;
        [FieldOffset(2)] public short Reserved1;
        [FieldOffset(4)] public short Reserved2;
        [FieldOffset(6)] public short Reserved3;
        [FieldOffset(8)] public IntPtr PointerValue;
        [FieldOffset(8)] public byte ByteValue;
        [FieldOffset(8)] public short ShortValue;
        [FieldOffset(8)] public ushort UShortValue;
        [FieldOffset(8)] public long LongValue;
        [FieldOffset(8)] public uint UIntValue;
        // BLOB { ULONG cbSize; BYTE *pBlobData; } / CAUB { ULONG cElems; BYTE *pElems; } -
        // identical layout, used by both VT_BLOB and VT_VECTOR|VT_UI1.
        [FieldOffset(8)] public uint BlobLength;
        [FieldOffset(16)] public IntPtr BlobData;

        public static PropVariant FromUInt32(uint value)
        {
            return new PropVariant
            {
                DataType = (short)VarEnum.VT_UI4,
                UIntValue = value,
            };
        }

        /// <summary>
        /// Builds a blob-shaped PROPVARIANT reusing the given VARTYPE (must be
        /// VT_BLOB or VT_VECTOR|VT_UI1 - whichever a prior GetValue reported).
        /// The caller owns the returned struct's unmanaged memory and must
        /// free it (see <see cref="FreeBlob"/>) once the native call using it
        /// has returned.
        /// </summary>
        public static PropVariant FromBytes(short vartype, byte[] bytes)
        {
            var handle = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            return new PropVariant
            {
                DataType = vartype,
                BlobLength = (uint)bytes.Length,
                BlobData = handle,
            };
        }

        /// <summary>Frees unmanaged memory allocated by <see cref="FromBytes"/>. Safe to call even if BlobData is IntPtr.Zero.</summary>
        public void FreeBlob()
        {
            if (BlobData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(BlobData);
            }
        }

        public object? GetValue()
        {
            switch ((VarEnum)DataType)
            {
                case VarEnum.VT_EMPTY:
                case VarEnum.VT_NULL:
                    return null;
                case VarEnum.VT_LPWSTR:
                    return Marshal.PtrToStringUni(PointerValue);
                case VarEnum.VT_UI4:
                case VarEnum.VT_UINT:
                    return UIntValue;
                case VarEnum.VT_UI8:
                    return (ulong)LongValue;
                case VarEnum.VT_I4:
                case VarEnum.VT_INT:
                    return (int)UIntValue;
                case VarEnum.VT_I2:
                    return ShortValue;
                case VarEnum.VT_UI2:
                    return UShortValue;
                case VarEnum.VT_UI1:
                    return ByteValue;
                case VarEnum.VT_BOOL:
                    return UIntValue != 0;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the raw bytes for a blob-shaped value (VT_BLOB or
        /// VT_VECTOR|VT_UI1), or null if this isn't one of those types.
        /// </summary>
        public byte[]? GetBytesIfBlob()
        {
            const short VT_BLOB = 65;
            const short VT_VECTOR_UI1 = (short)(VarEnum.VT_VECTOR | VarEnum.VT_UI1);

            if (DataType != VT_BLOB && DataType != VT_VECTOR_UI1)
            {
                return null;
            }

            if (BlobData == IntPtr.Zero || BlobLength == 0)
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[BlobLength];
            Marshal.Copy(BlobData, bytes, 0, (int)BlobLength);
            return bytes;
        }

        [DllImport("Ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        public void Clear() => PropVariantClear(ref this);
    }
}
