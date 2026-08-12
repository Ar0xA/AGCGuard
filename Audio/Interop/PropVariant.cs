using System;
using System.Runtime.InteropServices;

namespace HamstuffAgcGuard.Audio.Interop
{
    /// <summary>
    /// Minimal PROPVARIANT covering only the value types this app actually reads or
    /// writes (strings and 32-bit integers). Always call <see cref="Clear"/> after a
    /// successful IPropertyStore.GetValue to release any native memory it holds.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public short DataType;
        [FieldOffset(2)] public short Reserved1;
        [FieldOffset(4)] public short Reserved2;
        [FieldOffset(6)] public short Reserved3;
        [FieldOffset(8)] public IntPtr PointerValue;
        [FieldOffset(8)] public byte ByteValue;
        [FieldOffset(8)] public long LongValue;
        [FieldOffset(8)] public uint UIntValue;

        public static PropVariant FromUInt32(uint value)
        {
            return new PropVariant
            {
                DataType = (short)VarEnum.VT_UI4,
                UIntValue = value,
            };
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
                case VarEnum.VT_UI1:
                    return ByteValue;
                case VarEnum.VT_BOOL:
                    return UIntValue != 0;
                default:
                    return null;
            }
        }

        [DllImport("Ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        public void Clear() => PropVariantClear(ref this);
    }
}
