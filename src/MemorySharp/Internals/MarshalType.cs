﻿/*
 * MemorySharp Library
 * http://www.binarysharp.com/
 *
 * Copyright (C) 2012-2016 Jämes Ménétrey (a.k.a. ZenLulz).
 * This library is released under the MIT License.
 * See the file LICENSE for more information.
*/

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Binarysharp.MemoryManagement.Memory;

namespace Binarysharp.MemoryManagement.Internals
{
    /// <summary>
    /// Static class providing tools for extracting information related to types.
    /// </summary>
    /// <typeparam name="T">Type to analyze.</typeparam>
    public static class MarshalType<T>
    {
        #region Properties

        /// <summary>
        /// Gets if the type can be stored in a registers (for example ACX, ECX, ...).
        /// </summary>
        public static bool CanBeStoredInRegisters { get; private set; }

        /// <summary>
        /// State if the type is <see cref="IntPtr"/>.
        /// </summary>
        public static bool IsIntPtr { get; private set; }

        /// <summary>
        /// The real type.
        /// </summary>
        public static Type RealType { get; private set; }

        /// <summary>
        /// The size of the type.
        /// </summary>
        public static int Size { get; private set; }

        /// <summary>
        /// The size of the type as a <see cref="IntPtr"/>.
        /// </summary>
        public static IntPtr SizeAsPointer { get; private set; }

        /// <summary>
        /// The typecode of the type.
        /// </summary>
        public static TypeCode TypeCode { get; private set; }

        public static bool TypeRequiresMarshal { get; private set; }

        public static GetUnsafePtrDelegate GetUnsafePtr { get; private set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes static information related to the specified type.
        /// </summary>
        static MarshalType()
        {
            // Gather information related to the provided type
            IsIntPtr = typeof(T) == typeof(IntPtr);
            
            if (typeof (T) == typeof (bool))
            {
                Size = 1;
                RealType = typeof (T);
            }
            else if (typeof (T).IsEnum)
            {
                Type underlying = typeof (T).GetEnumUnderlyingType();
                Size = Marshal.SizeOf(underlying);
                RealType = underlying;
            }
            else
            {
                Size = GetSizeOf(typeof(T));
                RealType = typeof (T);
            }

            SizeAsPointer = new IntPtr(Size);
            TypeCode = Type.GetTypeCode(RealType);
            
            // Check if the type can be stored in registers
            CanBeStoredInRegisters = IsIntPtr ||
                                     TypeCode == TypeCode.Int64 ||
                                     TypeCode == TypeCode.UInt64 ||
                                     TypeCode == TypeCode.Boolean ||
                                     TypeCode == TypeCode.Byte ||
                                     TypeCode == TypeCode.Char ||
                                     TypeCode == TypeCode.Int16 ||
                                     TypeCode == TypeCode.Int32 ||
                                     TypeCode == TypeCode.Int64 ||
                                     TypeCode == TypeCode.SByte ||
                                     TypeCode == TypeCode.Single ||
                                     TypeCode == TypeCode.UInt16 ||
                                     TypeCode == TypeCode.UInt32;

            TypeRequiresMarshal = RealType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m => m.GetCustomAttributes(typeof(MarshalAsAttribute), true).Any());

            // Generate a method to get the address of a generic type. We'll be using this for RtlMoveMemory later for much faster structure reads.
            var method = new DynamicMethod(
                $"GetPinnedPtr<{typeof(T).FullName.Replace(".", "<>")}>",
                typeof(void*),
                new[] {typeof(T).MakeByRefType()},
                typeof(MarshalType<>).Module);

            ILGenerator generator = method.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Conv_U);
            generator.Emit(OpCodes.Ret);

            GetUnsafePtr = (GetUnsafePtrDelegate) method.CreateDelegate(typeof(GetUnsafePtrDelegate));
        }

        #endregion

        #region Methods

        #region ObjectToByteArray

        /// <summary>
        /// Marshals a managed object to an array of bytes.
        /// </summary>
        /// <param name="obj">The object to marshal.</param>
        /// <returns>A array of bytes corresponding to the managed object.</returns>
        public static byte[] ObjectToByteArray(T obj)
        {
            // We'll tried to avoid marshalling as it really slows the process
            // First, check if the type can be converted without marhsalling
            switch (TypeCode)
            {
                case TypeCode.Object:
                    if (IsIntPtr)
                    {
                        switch (Size)
                        {
                            case 4: return BitConverter.GetBytes(((IntPtr) (object) obj).ToInt32());
                            case 8: return BitConverter.GetBytes(((IntPtr) (object) obj).ToInt64());
                        }
                    }

                    break;

                case TypeCode.Boolean: return BitConverter.GetBytes((bool) (object) obj);
                case TypeCode.Char: return Encoding.UTF8.GetBytes(new[] {(char) (object) obj});
                case TypeCode.Double: return BitConverter.GetBytes((double) (object) obj);
                case TypeCode.Int16: return BitConverter.GetBytes((short) (object) obj);
                case TypeCode.Int32: return BitConverter.GetBytes((int) (object) obj);
                case TypeCode.Int64: return BitConverter.GetBytes((long) (object) obj);
                case TypeCode.Single: return BitConverter.GetBytes((float) (object) obj);
                case TypeCode.String: throw new InvalidCastException("This method doesn't support string conversion.");
                case TypeCode.UInt16: return BitConverter.GetBytes((ushort) (object) obj);
                case TypeCode.UInt32: return BitConverter.GetBytes((uint) (object) obj);
                case TypeCode.UInt64: return BitConverter.GetBytes((ulong) (object) obj);
            }

            // Check if it's not a common type
            // Allocate a block of unmanaged memory
            using (var unmanaged = new LocalUnmanagedMemory(Size, TypeRequiresMarshal))
            {
                // Write the object inside the unmanaged memory
                unmanaged.Write(obj);
                // Return the content of the block of unmanaged memory
                return unmanaged.Read();
            }
        }

        #endregion

        #region ByteArrayToObject

        /// <summary>
        /// Marshals an array of byte to a managed object.
        /// </summary>
        /// <param name="byteArray">The array of bytes corresponding to a managed object.</param>
        /// <param name="index">[Optional] Where to start the conversion of bytes to the managed object.</param>
        /// <returns>A managed object.</returns>
        public static T ByteArrayToObject(byte[] byteArray, int index = 0)
        {
            // We'll tried to avoid marshalling as it really slows the process
            // First, check if the type can be converted without marshalling
            switch (TypeCode)
            {
                case TypeCode.Object:
                    if (IsIntPtr)
                    {
                        switch (byteArray.Length)
                        {
                            case 1:
                                return (T) (object) new IntPtr(BitConverter.ToInt32(new byte[] {byteArray[index], 0x0, 0x0, 0x0}, index));

                            case 2:
                                return (T) (object) new IntPtr(
                                    BitConverter.ToInt32(new byte[] {byteArray[index], byteArray[index + 1], 0x0, 0x0}, index));

                            case 4: return (T) (object) new IntPtr(BitConverter.ToInt32(byteArray, index));
                            case 8: return (T) (object) new IntPtr(BitConverter.ToInt64(byteArray, index));
                        }
                    }

                    break;

                case TypeCode.Boolean: return (T) (object) BitConverter.ToBoolean(byteArray, index);
                case TypeCode.Byte: return (T) (object) byteArray[index];
                case TypeCode.Char: return (T) (object) Encoding.UTF8.GetChars(byteArray)[index];
                case TypeCode.Double: return (T) (object) BitConverter.ToDouble(byteArray, index);
                case TypeCode.Int16: return (T) (object) BitConverter.ToInt16(byteArray, index);
                case TypeCode.Int32: return (T) (object) BitConverter.ToInt32(byteArray, index);
                case TypeCode.Int64: return (T) (object) BitConverter.ToInt64(byteArray, index);
                case TypeCode.Single: return (T) (object) BitConverter.ToSingle(byteArray, index);
                case TypeCode.String: throw new InvalidCastException("This method doesn't support string conversion.");
                case TypeCode.UInt16: return (T) (object) BitConverter.ToUInt16(byteArray, index);
                case TypeCode.UInt32: return (T) (object) BitConverter.ToUInt32(byteArray, index);
                case TypeCode.UInt64: return (T) (object) BitConverter.ToUInt64(byteArray, index);
            }

            // Allocate a block of unmanaged memory
            using (var unmanaged = new LocalUnmanagedMemory(Size, TypeRequiresMarshal))
            {
                // Write the array of bytes inside the unmanaged memory
                unmanaged.Write(byteArray, index);

                // Return a managed object created from the block of unmanaged memory
                return unmanaged.Read<T>();
            }
        }

        #endregion

        #region PtrToObject

        /// <summary>
        /// Converts a pointer to a given type. This function converts the value of the pointer or the pointed value,
        /// according if the data type is primitive or reference.
        /// </summary>
        /// <param name="memorySharp">The concerned process.</param>
        /// <param name="pointer">The pointer to convert.</param>
        /// <returns>The return value is the pointer converted to the given data type.</returns>
        public static T PtrToObject(MemorySharp memorySharp, IntPtr pointer)
        {
            return ByteArrayToObject(
                CanBeStoredInRegisters
                    ? BitConverter.GetBytes(pointer.ToInt64())
                    : memorySharp.Read<byte>(pointer, Size, false));
        }

        #endregion

        static int GetSizeOf(Type t)
        {
            int size = 0;

            try
            {
                return Marshal.SizeOf(Activator.CreateInstance(t));
            }
            catch (Exception)
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var fieldInfo in fields)
                {
                    object[] customAttributes = fieldInfo.GetCustomAttributes(typeof(FixedBufferAttribute), false);
                    if (customAttributes.Length > 0)
                    {
                        var fixedBufferAttribute = (FixedBufferAttribute) customAttributes[0];
                        size = GetSizeOf(fixedBufferAttribute.ElementType) * fixedBufferAttribute.Length + size;
                    }
                    else
                    {
                        size = GetSizeOf(fieldInfo.FieldType) + size;
                    }
                }
            }

            return size;
        }

        #endregion

        #region Nested type: GetUnsafePtrDelegate

        public unsafe delegate void* GetUnsafePtrDelegate(ref T value);

        #endregion
    }
}