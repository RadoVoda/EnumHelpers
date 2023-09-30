using Unity.Collections.LowLevel.Unsafe;
using System;

namespace Unity.Collections
{
    public static unsafe class UnityCollectionsExtension
    {
        public static UnsafeParallelHashMap<TKey, TValue> AsUnsafe<TKey, TValue>(this ref NativeParallelHashMap<TKey, TValue> safeMap)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return safeMap.m_HashMapData;
        }

        public static IntPtr GetUnsafePtr<TKey, TValue>(this ref UnsafeParallelHashMap<TKey, TValue> unsafeMap)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return new IntPtr(unsafeMap.m_Buffer);
        }

        public static void SetUnsafePtr<TKey, TValue>(this ref UnsafeParallelHashMap<TKey, TValue> unsafeMap, void* ptr)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (ptr != null)
            {
                unsafeMap.m_Buffer = (UnsafeParallelHashMapData*)ptr;
            }
        }
    }
}