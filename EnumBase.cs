using UnityEngine;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace BurstEnums
{
    public class EnumFlagAttribute : PropertyAttribute
    {
        public EnumFlagAttribute() { }
    }
    /// <summary>
    /// Burst compatible static class to handle enum data
    /// </summary>
    public static unsafe class EnumBase
    {
        [NativeDisableUnsafePtrRestriction]
        private static readonly UnsafeList<EnumDataRecord>* entries;

        [NativeDisableUnsafePtrRestriction]
        private static readonly UnsafeParallelHashMap<long, int> typeHashToTypeIndexMap;

        private const int defaultAllocationLength = 1024;

        [Flags]
        public enum EnumFlags : byte
        {
            None = 0,
            Flag = 1 << 0,
            Sign = 1 << 1,
            Zero = 1 << 2,
            Gaps = 1 << 3
        }

        private struct EnumBaseContext { }

        /// <summary>
        /// Shared Static pointer to enum data record array
        /// </summary>
        private struct EnumSharedStaticEnumDataArrayPointer
        {
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<EnumBaseContext, EnumSharedStaticEnumDataArrayPointer>();
        }

        /// <summary>
        /// Shared Static pointer to type hash to type index map. Must be passed to UnsafeParallelHashMap.SetUnsafePtr(void*) before use
        /// </summary>
        private struct EnumSharedStaticTypeIndexMapPointer
        {
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<EnumBaseContext, EnumSharedStaticTypeIndexMapPointer>();
        }

        static EnumBase()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
#endif
            typeHashToTypeIndexMap = new UnsafeParallelHashMap<long, int>(defaultAllocationLength, Allocator.Persistent);
            entries = UnsafeList<EnumDataRecord>.Create(defaultAllocationLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            entries->Add(default);
            EnumSharedStaticTypeIndexMapPointer.Ref.Data = typeHashToTypeIndexMap.GetUnsafePtr();
            EnumSharedStaticEnumDataArrayPointer.Ref.Data = new IntPtr(entries->Ptr);
        }

        /// <summary>
        /// Cleanup before assembly reload
        /// </summary>
        private static void Cleanup()
        {
            if (entries != null)
            {
                for (int i = 0; i < entries->Length; ++i)
                {
                    ref var entry = ref entries->ElementAt(i);

                    if (entry.IsNull == false)
                    {
                        Marshal.FreeHGlobal(entry.Pointer);
                    }
                }

                typeHashToTypeIndexMap.Dispose();
                UnsafeList<EnumDataRecord>.Destroy(entries);
                EnumSharedStaticEnumDataArrayPointer.Ref.Data = IntPtr.Zero;
                EnumSharedStaticTypeIndexMapPointer.Ref.Data = IntPtr.Zero;
            }

#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
#endif
        }

        /// <summary>
        /// Get enum type index in the enum data record array from type hash. Returns 0 when type with this hash is not yet initialized
        /// </summary>
        public static int GetTypeIndexFromTypeHash(long hash)
        {
            UnsafeParallelHashMap<long, int> map = default;
            IntPtr ptr = EnumSharedStaticTypeIndexMapPointer.Ref.Data;

            if (ptr != IntPtr.Zero)
            {
                map.SetUnsafePtr(ptr.ToPointer());

                if (map.TryGetValue(hash, out var index))
                    return index;
            }

            return 0;
        }

        /// <summary>
        /// Get enum type index in the enum data record array for generic enum type. Returns 0 when type is not yet initialized
        /// </summary>
        public static int GetEnumIndex<T>() where T : unmanaged, Enum
        {
            long hash = BurstRuntime.GetHashCode64<T>();
            int index = GetTypeIndexFromTypeHash(hash);
            return index;
        }

        /// <summary>
        /// Get enum data record for generic enum type. Returns default invalid record when type is not yet initialized
        /// </summary>
        public static ref readonly EnumDataRecord GetEnumData<T>() where T : unmanaged, Enum
        {
            IntPtr ptr = EnumSharedStaticEnumDataArrayPointer.Ref.Data;
            return ref UnsafeUtility.ArrayElementAsRef<EnumDataRecord>(ptr.ToPointer(), GetEnumIndex<T>());
        }

        /// <summary>
        /// Get static enum data record for given type. Returns default invalid record when type is not enum
        /// </summary>
        [ExcludeFromBurstCompatTesting("Takes a managed Type")]
        public static ref readonly EnumDataRecord GetEnumData(Type type)
        {
            Initialize(type);
            long hash = BurstRuntime.GetHashCode64(type);
            int index = GetTypeIndexFromTypeHash(hash);
            IntPtr ptr = EnumSharedStaticEnumDataArrayPointer.Ref.Data;
            return ref UnsafeUtility.ArrayElementAsRef<EnumDataRecord>(ptr.ToPointer(), index);
        }

        /// <summary>
        /// Initialize type to create static enum data record. If type is not enum or the record for this type was already created it does nothing
        /// </summary>
        [ExcludeFromBurstCompatTesting("Takes a managed Type")]
        public static void Initialize(Type type)
        {
            if (type == null || type.IsEnum == false)
                return;

            long hash = BurstRuntime.GetHashCode64(type);
            int index = GetTypeIndexFromTypeHash(hash);

            if (index > 0)
                return;

            Type uType = Enum.GetUnderlyingType(type);
            Array values = Enum.GetValues(type);
            Array.Sort(values);
            int size = UnsafeUtility.SizeOf(type);
            int length = values.Length;
            ulong sum = 0;
            EnumFlags flags = 0;
            flags.Toggle(EnumFlags.Flag, type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0);
            flags.Toggle(EnumFlags.Sign, uType == typeof(sbyte) || uType == typeof(short) || uType == typeof(int) || uType == typeof(long));

            int arrayByteLength = sizeof(long) * length;
            IntPtr arrayPtr = Marshal.AllocHGlobal(arrayByteLength);
            void* targetPtr = arrayPtr.ToPointer();
            void* sourcePtr = UnsafeUtility.PinGCArrayAndGetDataAddress(values, out var gcSourceHandle);
            UnsafeUtility.MemClear(targetPtr, arrayByteLength);
            UnsafeUtility.MemCpyStride(targetPtr, sizeof(long), sourcePtr, size, size, length);
            
            long min = UnsafeUtility.ReadArrayElement<long>(targetPtr, 0);
            long max = UnsafeUtility.ReadArrayElement<long>(targetPtr, length - 1);
            flags.Toggle(EnumFlags.Gaps, flags.Match(EnumFlags.Flag) ? min << (length - 1) != max : length - 1 != max - min);

            for (int i = 0; i < length; ++i)
            {
                var value = UnsafeUtility.ReadArrayElementWithStride<long>(targetPtr, i, sizeof(long));
                sum |= (ulong)value;

                if (value == 0)
                    flags |= EnumFlags.Zero;
            }

            typeHashToTypeIndexMap.TryAdd(hash, entries->Length);
            entries->Add(new EnumDataRecord(arrayPtr, sum, hash, length, size, flags));
            UnsafeUtility.ReleaseGCObject(gcSourceHandle);
        }
    }

    /// <summary>
    /// Burst compatible static generic class to handle enum data
    /// </summary>
    public static class EnumBase<T> where T : unmanaged, Enum
    {
        public static ref readonly EnumDataRecord data => ref EnumBase.GetEnumData<T>();
        public static T All => data.Sum.ToEnum<T>();
        public static T Default => ((ulong)data.Default).ToEnum<T>();
        public static Enumerator GetEnumerator() => new Enumerator();

        static EnumBase()
        {
            EnumBase.Initialize(typeof(T));
        }

        public ref struct Enumerator
        {
            private int index;

            public T this[int i]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return data.ValueAt<T>(i);
                }
            }

            public unsafe bool MoveNext() => ++index < data.Length;
            public void Reset() => index = -1;
            public T Current { get { return data.ValueAt<T>(index); } }
            public Enumerator GetEnumerator() => new Enumerator();
        }
    }

    /// <summary>
    /// Burst compatible enum data container
    /// </summary>
    public readonly unsafe struct EnumDataRecord
    {
        [NativeDisableUnsafePtrRestriction]
        public readonly IntPtr Pointer;
        public readonly ulong Sum;
        public readonly long Hash;
        public readonly int Length;
        public readonly byte Size;
        public readonly EnumBase.EnumFlags Flags;

        public long Default => IsZeroDefined ? 0 : Min;
        public long Min => UnsafeUtility.ReadArrayElement<long>(Pointer.ToPointer(), 0);
        public long Max => UnsafeUtility.ReadArrayElement<long>(Pointer.ToPointer(), Length - 1);
        public bool IsNull => Pointer == null;
        public bool IsFlag => Flags.Match(EnumBase.EnumFlags.Flag);
        public bool Signed => Flags.Match(EnumBase.EnumFlags.Sign);
        public bool HasGaps => Flags.Match(EnumBase.EnumFlags.Gaps);
        public bool IsZeroDefined => Flags.Match(EnumBase.EnumFlags.Zero);

        public EnumDataRecord(IntPtr pointer, ulong sum, long hash, int length, int size, EnumBase.EnumFlags flags)
        {
            Pointer = pointer;
            Sum = sum;
            Hash = hash;
            Length = length;
            Size = (byte)size;
            Flags = flags;
        }

        /// <summary>
        /// Check if given enum value or a combination of valid values for flag enum is defined.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid<T>(T check) where T : unmanaged, Enum
        {
            if (IsMatchingType<T>() == false)
                return false;

            var value = check.ToMask();

            if (IsFlag)
                return (Sum & value) == value;

            if (HasGaps == false)
            {
                return Min < (long)value && (long)value < Max;
            }

            return BinarySearch(Pointer.ToPointer(), Length, (long)value) != -1;
        }

        /// <summary>
        /// Returns index of given enum value in the definition.
        /// Combined flags returns index of first bit.
        /// Invalid enum returns -1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf<T>(T check) where T : unmanaged, Enum
        {
            if (IsMatchingType<T>() == false)
                return -1;

            var value = check.ToMask();

            if (HasGaps == false)
            {
                return IsFlag ? math.tzcnt(value) : (int)((long)value - Min);
            }

            return BinarySearch(Pointer.ToPointer(), Length, (long)value);
        }

        // <summary>
        /// Returns Enum value at given index within its definition.
        /// Index is clamped to avoid reading out of range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ValueAt<T>(int i) where T : unmanaged, Enum
        {
            i = Branchless.Clamp(i, 0, Length - 1);
            return UnsafeUtility.ReadArrayElementWithStride<T>(Pointer.ToPointer(), i, sizeof(long));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMatchingSize<T>() where T : unmanaged, Enum => UnsafeUtility.SizeOf<T>() == Size;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMatchingType<T>() where T : unmanaged, Enum => EnumBase.GetEnumData<T>().Hash == Hash;

        // <summary>
        /// Recursively find index of given key.
        /// Returns -1 if it does not exist.
        /// </summary>
        private static int BinarySearch(void* ptr, int length, long key)
        {
            int find = 0;
            
            while (length > 1)
            {
                int half = length >> 1;
                length -= half;
#if UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC
                int quarter = length >> 1;
                Unity.Burst.Intrinsics.Common.Prefetch(ptr + find + quarter - 1);
                Unity.Burst.Intrinsics.Common.Prefetch(ptr + find + half + quarter - 1);
#endif
                var element = UnsafeUtility.ReadArrayElement<long>(ptr, find + half - 1);
                find += Branchless.IfElse(element < key, half, 0);
            }

            var found = UnsafeUtility.ReadArrayElement<long>(ptr, find);
            find = Branchless.IfElse(found == key, find, -1);
            return find;
        }
    }

    /// <summary>
    /// Struct representing a collection of bit flag integers without allocating any managed memory
    /// </summary>
    public ref struct BitFlags
    {
        private ulong mask;
        private ulong current;

        public BitFlags(ulong m)
        {
            mask = m;
            current = 0;
        }

        public void Toggle(ulong flag, bool toggle) => mask = Branchless.IfElse(toggle, mask | flag, mask & ~flag);

        public void Toggle<T>(T flag, bool toggle) where T : unmanaged, Enum => Toggle(flag.ToMask(), toggle);

        public ulong this[int i]
        {
            get
            {
                ulong flag = 0;
                ulong temp = mask;

                if (i >= Count || i < 0)
                    i = -1;

                while (temp != 0 && i >= 0)
                {
                    flag = flag == 0 ? 1 : flag <<= 1;

                    ulong check = temp & flag;
                    temp -= check;

                    if (check != 0)
                    {
                        i--;
                    }
                }

                return flag;
            }
        }

        public bool MoveNext()
        {
            bool check = false;

            while (current <= mask && check == false)
            {
                current = current == 0 ? 1 : current <<= 1;
                check = (mask & current) != 0;
            }

            return check;
        }

        public void Reset()
        {
            current = 0;
        }

        public bool TryDequeue(out ulong next)
        {
            bool move = MoveNext();
            next = current;
            return move;
        }

        public bool Contains<T>(T item) where T : unmanaged, Enum => (mask & item.ToMask()) != 0;
        public BitFlags GetEnumerator() => new BitFlags(mask);
        public ulong Current { get { return current; } }
        public int Count { get { return math.countbits(mask); } }
    }

    public static partial class Extensions
    {
        /// <summary>
        /// Return ulong mask value of an Enum without boxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ToMask<T>(this T value) where T : Enum => UnsafeUtility.As<T, ulong>(ref value);

        /// <summary>
        /// Return Enum value from ulong mask without boxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ToEnum<T>(this ulong mask) where T : unmanaged, Enum => UnsafeUtility.As<ulong, T>(ref mask);

        /// <summary>
        /// Return collection of set bits, does not allocate any managed memory
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitFlags BitFlags<T>(this T value) where T : unmanaged, Enum => new BitFlags(value.ToMask());

        /// <summary>
        /// Return count of set bits
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BitCount<T>(this T value) where T : unmanaged, Enum => math.countbits(value.ToMask());

        /// <summary>
        /// Check if any flag bit is set without boxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Match<T>(this T mask, T flag) where T : unmanaged, Enum => (mask.ToMask() & flag.ToMask()) != 0;

        /// <summary>
        /// Check how many flags are set without boxing, zero is no match
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchCount<T>(this T mask, T flag) where T : unmanaged, Enum => math.countbits(mask.ToMask() & flag.ToMask());

        /// <summary>
        /// Check if all flag bits are set without boxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Exact<T>(this T mask, T flag) where T : unmanaged, Enum
        {
            var check = flag.ToMask();
            return (mask.ToMask() & check) == check;
        }

        /// <summary>
        /// Toggle given flag on/off on the target mask
        /// </summary>
        public static void Toggle<T>(this ref T mask, T flag, bool toggle) where T : unmanaged, Enum
        {
            var m = mask.ToMask();
            var f = flag.ToMask();
            mask = Branchless.IfElse(toggle, m | f, m & ~f).ToEnum<T>();
        }

        /// <summary>
        /// Check if flags are equal without boxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Equal<T>(this T a, T b) where T : unmanaged, Enum => a.ToMask() == b.ToMask();

        /// <summary>
        /// Compare two enum without boxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Compare<T>(this T a, T b) where T : unmanaged, Enum => a.ToMask().CompareTo(b.ToMask());

        /// <summary>
        /// Get highest set flag
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetMaxSetFlag<T>(this T value, out T maxFlag) where T : unmanaged, Enum
        {
            var flags = value.BitFlags();
            var count = flags.Count;
            maxFlag = flags[count - 1].ToEnum<T>();
            return count > 0;
        }

        /// <summary>
        /// Get lowest set flag
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetMinSetFlag<T>(this T value, out T minFlag) where T : unmanaged, Enum
        {
            var flags = value.BitFlags();
            var count = flags.Count;
            minFlag = flags[0].ToEnum<T>();
            return count > 0;
        }

        /// <summary>
        /// Return next enum value after this one
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Next<T>(this T check) where T : unmanaged, Enum => EnumBase<T>.data.ValueAt<T>(check.IndexOf() + 1);

        /// <summary>
        /// Return previous enum value before this one
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Last<T>(this T check) where T : unmanaged, Enum => EnumBase<T>.data.ValueAt<T>(check.IndexOf() - 1);

        /// <summary>
        /// Fast check if given enum is defined, works for valid flag combinations as well.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValid<T>(this T check) where T : unmanaged, Enum => EnumBase<T>.data.IsValid(check);

        /// <summary>
        /// Get relative index of this enum entry in its definition
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf<T>(this T check) where T : unmanaged, Enum => EnumBase<T>.data.IndexOf(check);

        /// <summary>
        /// Fast check if given enum is a flag enum
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFlag<T>(this T check) where T : unmanaged, Enum => EnumBase<T>.data.IsFlag;

        /// <summary>
        /// Fast check if given enum is a signed enum
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSigned<T>(this T check) where T : unmanaged, Enum => EnumBase<T>.data.Signed;

        /// <summary>
        /// Get EnumStaticData for type. Will be invalid if type is not enum
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly EnumDataRecord GetEnumData(this Type type) => ref EnumBase.GetEnumData(type);

        /// <summary>
        /// Get EnumStaticData for generic type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly EnumDataRecord GetEnumData<T>() where T : unmanaged, Enum => ref EnumBase<T>.data;
    }

#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(EnumFlagAttribute))]
    public class EnumFlagsAttributeDrawer : PropertyDrawer
    {
        const float mininumWidth = 70.0f;

        int enumLength;
        float enumWidth;

        int numBtns;
        int numRows;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SetDimensions(property);
            return numRows * EditorGUIUtility.singleLineHeight + (numRows - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        void SetDimensions(SerializedProperty property)
        {
            enumLength = property.enumNames.Length;
            enumWidth = (EditorGUIUtility.currentViewWidth - EditorGUIUtility.labelWidth - 30);

            numBtns = Mathf.FloorToInt(enumWidth / mininumWidth);
            numRows = Mathf.CeilToInt((float)enumLength / (float)numBtns);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SetDimensions(property);

            int buttonsIntValue = 0;
            bool[] buttonPressed = new bool[enumLength];
            float buttonWidth = enumWidth / Mathf.Min(numBtns, enumLength);

            EditorGUI.LabelField(new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height), label);

            EditorGUI.BeginChangeCheck();

            for (int row = 0; row < numRows; row++)
            {
                for (int btn = 0; btn < numBtns; btn++)
                {
                    int i = btn + row * numBtns;

                    if (i >= enumLength)
                    {
                        break;
                    }

                    // Check if the button is/was pressed
                    if ((property.intValue & (1 << i)) == 1 << i)
                    {
                        buttonPressed[i] = true;
                    }

                    Rect buttonPosition = new Rect(position.x + EditorGUIUtility.labelWidth + buttonWidth * btn, position.y + row * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing), buttonWidth, EditorGUIUtility.singleLineHeight);
                    buttonPressed[i] = GUI.Toggle(buttonPosition, buttonPressed[i], property.enumDisplayNames[i], EditorStyles.toolbarButton);

                    if (buttonPressed[i])
                        buttonsIntValue += 1 << i;

                    FieldInfo fieldInfo = property.serializedObject.targetObject.GetType().GetField(property.propertyPath);

                    if (fieldInfo != null)
                    {
                        foreach (MemberInfo field in fieldInfo.FieldType.GetMembers())
                        {
                            if (field.Name == property.enumNames[i])
                            {
                                var tooltipAttributes = field.GetCustomAttributes(typeof(TooltipAttribute), false);
                                var tooltipAttribute = (tooltipAttributes.Length > 0) ? (TooltipAttribute)tooltipAttributes[0] : null;

                                if (tooltipAttribute != null)
                                    EditorGUI.LabelField(buttonPosition, new GUIContent("", tooltipAttribute.tooltip));
                            }
                        }
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = buttonsIntValue;
            }
        }
    }

    [CustomPropertyDrawer(typeof(Enum))]
    public class EnumTooltipDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            FieldInfo fieldInfo = property.serializedObject.targetObject.GetType().GetField(property.propertyPath);

            if (fieldInfo != null)
            {
                foreach (MemberInfo field in fieldInfo.FieldType.GetMembers())
                {
                    var tooltipAttributes = field.GetCustomAttributes(typeof(TooltipAttribute), false);
                    var tooltipAttribute = (tooltipAttributes.Length > 0) ? (TooltipAttribute)tooltipAttributes[0] : null;

                    if (tooltipAttribute != null)
                        EditorGUI.LabelField(position, new GUIContent("", tooltipAttribute.tooltip));
                }
            }
        }
    }

#endif

}