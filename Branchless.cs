using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BurstEnums
{
    /// <summary>
    /// Library of branchless arithmetic operations for integers
    /// </summary>
    public static partial class Branchless
    {
        /// <summary>
        /// Union of bool and byte with inbuild safety ensuring that false == 0 and true == 1
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        public readonly ref struct binary
        {
            [FieldOffset(0)]
            public readonly bool boole;
            [FieldOffset(0)]
            public readonly byte value;

            public unsafe binary(bool b)
            {
                value = 0;
                boole = b;
                value = (byte)NotZero(value);
            }

            public binary(byte b)
            {
                boole = false;
                value = b;
                value = (byte)NotZero(value);
            }

            public binary(int i)
            {
                boole = false;
                value = (byte)NotZero(i);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int NotZero(int i) => ((i | -i) >> 31) & 1;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override string ToString() => boole ? "True(1)" : "False(0)";

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode() => value;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override bool Equals(object other) => new binary(other == null);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(binary other) => this.boole == other.boole;
            public static bool operator ==(binary a, binary b) => a.boole == b.boole;
            public static bool operator !=(binary a, binary b) => !(a == b);
            public static binary operator !(binary a) => new binary(1 - a);

            public static implicit operator binary(bool value) => new binary(value);
            public static implicit operator bool(binary value) => value.boole;
            public static implicit operator binary(byte value) => new binary(value);
            public static implicit operator byte(binary value) => value.value;
            public static implicit operator binary(int value) => new binary(value);
            public static implicit operator int(binary value) => value.value;
        }

        /// <summary>
        /// Without branching convert integer to bool
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool ToBool(this int i) => *(bool*)&i;

        /// <summary>
        /// Without branching convert bool to integer ensuring that False == 0 and True == 1
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int ToInt(this bool b) => IsNotZero(*(byte*)&b);

        /// <summary>
        /// Without branching check if the integer is even
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEven(this int i)
        {
            return (i & 1) == 0;
        }

        /// <summary>
        /// Without branching check if the integer is even
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary Even(this int i)
        {
            return 1 - (i & 1);
        }

        /// <summary>
        /// Without branching check if the integer is zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsZero(this int i)
        {
            return (((i | -i) >> 31) + 1);
        }

        /// <summary>
        /// Without branching check if the integer is zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsZero(this long i)
        {
            return (int)(((i | -i) >> 63) + 1);
        }

        /// <summary>
        /// Without branching check if the integer is not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsNotZero(this int i)
        {
            return (((i | -i) >> 31) & 1);
        }

        /// <summary>
        /// Without branching check if the integer is not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsNotZero(this long i)
        {
            return (int)(((i | -i) >> 63) & 1);
        }

        /// <summary>
        /// Without branching check if the integer is not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsNotZero(this ulong u)
        {
            return (int)(((u | (~u + 1)) >> 63) & 1);
        }

        /// <summary>
        /// Without branching check if integer is greater or equal to zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsPositive(this int i)
        {
            return (i >> 31) + 1;
        }

        /// <summary>
        /// Without branching check if integer is greater then other integer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsGreaterThan(this int i, int other)
        {
            return IsPositive(i - other - 1);
        }

        /// <summary>
        /// Without branching check if integer is greater or equal to other integer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsGreaterOrEqualThan(this int i, int other)
        {
            return IsPositive(i - other);
        }

        /// <summary>
        /// Without branching return One or Zero integer depending on corresponding binary value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IfElse(this binary binary, int True, int False)
        {
            return (True * binary) + (False * !binary);
        }

        /// <summary>
        /// Without branching return One or Zero integer depending on corresponding binary value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong IfElse(this binary binary, ulong True, ulong False)
        {
            return (True * binary) + (False * !binary);
        }

        /// <summary>
        /// Without branching return One or Zero enum depending on corresponding binary value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T IfElse<T>(this binary binary, T True, T False) where T : unmanaged, Enum
        {
            return ((True.ToMask() * binary) + (False.ToMask() * !binary)).ToEnum<T>();
        }

        /// <summary>
        /// Without branching return absolute value of an integer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(this int i)
        {
            int m = i >> 31;
            return (i + m) ^ m;//alternative: (i ^ m) - m;
        }

        /// <summary>
        /// Without branching return absolute value of an integer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Abs(this long i)
        {
            long m = i >> 63;
            return (i + m) ^ m;//alternative: (i ^ m) - m;
        }

        /// <summary>
        /// Without branching return value sign (1 for > 0; 0 for 0; -1 for < 0) of integer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(this int i)
        {
            return (i >> 31) - (-i >> 31);
        }

        /// <summary>
        /// Without branching change magnitude of an integer while preserving its sign, positive scale is increment, negative scale is decrement
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scale(this int i, int scale)
        {
            int sign = i.Sign();
            return i + (sign * scale) + (sign.IsZero() * scale);
        }

        /// <summary>
        /// Without branching find lesser value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int x, int y)
        {
            return (x + y - Abs(x - y)) >> 1;
        }

        /// <summary>
        /// Without branching find greater value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Max(int x, int y)
        {
            return (x + y + Abs(x - y)) >> 1;
        }

        /// <summary>
        /// Without branching find greater value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Max(uint x, uint y)
        {
            return (uint)(x + y + Abs(x - y)) >> 1;
        }

        /// <summary>
        /// Without branching clamp value between min and max
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int x, int min, int max)
        {
            max = x + max - Abs(x - max);
            min *= 2;
            return (max + min + Abs(max - min)) >> 2;
        }

        /// <summary>
        /// Without branching check if the integer value is power of two, zero is true as well
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary IsPowerOfTwo(this int i)
        {
            return IsZero(i & (i - 1));
        }

        /// <summary>
        /// Without branching return closest power of two greater than the integer
        /// </summary>
        public static int GetClosestPowerOfTwoGreaterThan(this int i)
        {
            i--;
            i |= i >> 1;
            i |= i >> 2;
            i |= i >> 4;
            i |= i >> 8;
            i |= i >> 16;
            i++;
            return i;
        }

        /// <summary>
        /// Without branching return closest integer that is divisible by given number
        /// </summary>
        public static int GetClosestDivisibleBy(this int value, int divisor)
        {
            int quotient = value / divisor;
            int a = divisor * quotient;
            var binary = (value * divisor).IsGreaterThan(0);
            int b = divisor * binary.IfElse(quotient + 1, quotient - 1);
            binary = (Abs(value - b) - Abs(value - a)).IsGreaterThan(0);
            return binary.IfElse(a, b);
        }

        /// <summary>
        /// Without branching check if the integer value is within interval (a,b) or (b,a)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static binary WithinRange(this int value, int a, int b)
        {
            return (((value - a) * (b - value) - 1) >> 31) + 1;
        }

        /// <summary>
        /// Without branching remap an integer from one range to another
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Remap(this int value, int in_min, int in_max, int out_min, int out_max)
        {
            return (value - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
        }
    }
}