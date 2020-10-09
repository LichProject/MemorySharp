/*
 * MemorySharp Library
 * http://www.binarysharp.com/
 *
 * Copyright (C) 2012-2016 Jämes Ménétrey (a.k.a. ZenLulz).
 * This library is released under the MIT License.
 * See the file LICENSE for more information.
*/

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Binarysharp.MemoryManagement.Internals;

namespace Binarysharp.MemoryManagement.Memory
{
    /// <summary>
    /// Class representing a block of memory allocated in the local process.
    /// </summary>
    public class LocalUnmanagedMemory : IDisposable
    {
        [DllImport("Kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        static extern unsafe void MoveMemory(void* dest, void* src, int size);

        #region Properties

        /// <summary>
        /// The address where the data is allocated.
        /// </summary>
        public IntPtr Address { get; private set; }

        /// <summary>
        /// The size of the allocated memory.
        /// </summary>
        public int Size { get; private set; }

        public bool TypeRequiresMarshal { get; private set; }

        #endregion

        #region Constructor/Destructor

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalUnmanagedMemory"/> class, allocating a block of memory in the local process.
        /// </summary>
        /// <param name="size">The size to allocate.</param>
        public LocalUnmanagedMemory(int size, bool typeRequiresMarshal)
        {
            // Allocate the memory
            Size = size;
            Address = Marshal.AllocHGlobal(Size);
            TypeRequiresMarshal = typeRequiresMarshal;
        }

        /// <summary>
        /// Frees resources and perform other cleanup operations before it is reclaimed by garbage collection.
        /// </summary>
        ~LocalUnmanagedMemory()
        {
            Dispose();
        }

        #endregion

        #region Methods

        #region Dispose (implementation of IDisposable)

        /// <summary>
        /// Releases the memory held by the <see cref="LocalUnmanagedMemory"/> object.
        /// </summary>
        public virtual void Dispose()
        {
            // Free the allocated memory
            Marshal.FreeHGlobal(Address);
            // Remove the pointer
            Address = IntPtr.Zero;
            // Avoid the finalizer
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Read

        /// <summary>
        /// Reads data from the unmanaged block of memory.
        /// </summary>
        /// <typeparam name="T">The type of data to return.</typeparam>
        /// <returns>The return value is the block of memory casted in the specified type.</returns>
        [HandleProcessCorruptedStateExceptions]
        public unsafe T Read<T>()
        {
            try
            {
                if (Address == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Cannot retrieve a value at address 0");
                }

                object ret;
                switch (MarshalType<T>.TypeCode)
                {
                    case TypeCode.Object:
                        if (MarshalType<T>.IsIntPtr)
                        {
                            return (T) (object) *(IntPtr*) Address;
                        }

                        // If the type doesn't require an explicit Marshal call, then ignore it and memcpy the fuckin thing.
                        if (!MarshalType<T>.TypeRequiresMarshal)
                        {
                            T o = default;
                            void* ptr = MarshalType<T>.GetUnsafePtr(ref o);

                            MoveMemory(ptr, (void*) Address, MarshalType<T>.Size);

                            return o;
                        }

                        // All System.Object's require marshaling!
                        ret = Marshal.PtrToStructure(Address, typeof(T));
                        break;

                    case TypeCode.Boolean:
                        ret = *(byte*) Address != 0;
                        break;

                    case TypeCode.Char:
                        ret = *(char*) Address;
                        break;

                    case TypeCode.SByte:
                        ret = *(sbyte*) Address;
                        break;

                    case TypeCode.Byte:
                        ret = *(byte*) Address;
                        break;

                    case TypeCode.Int16:
                        ret = *(short*) Address;
                        break;

                    case TypeCode.UInt16:
                        ret = *(ushort*) Address;
                        break;

                    case TypeCode.Int32:
                        ret = *(int*) Address;
                        break;

                    case TypeCode.UInt32:
                        ret = *(uint*) Address;
                        break;

                    case TypeCode.Int64:
                        ret = *(long*) Address;
                        break;

                    case TypeCode.UInt64:
                        ret = *(ulong*) Address;
                        break;

                    case TypeCode.Single:
                        ret = *(float*) Address;
                        break;

                    case TypeCode.Double:
                        ret = *(double*) Address;
                        break;

                    default: throw new ArgumentOutOfRangeException();
                }

                return (T) ret;
            }
            catch (AccessViolationException ex)
            {
                Trace.WriteLine("Access Violation on " + Address + " with type " + typeof(T).Name);
                return default;
            }
        }

        /// <summary>
        /// Reads an array of bytes from the unmanaged block of memory.
        /// </summary>
        /// <returns>The return value is the block of memory.</returns>
        public byte[] Read()
        {
            // Allocate an array to store data
            var bytes = new byte[Size];
            // Copy the block of memory to the array
            Marshal.Copy(Address, bytes, 0, Size);
            // Return the array
            return bytes;
        }

        #endregion

        #region ToString (override)

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        public override string ToString()
        {
            return string.Format("Size = {0:X}", Size);
        }

        #endregion

        #region Write

        /// <summary>
        /// Writes an array of bytes to the unmanaged block of memory.
        /// </summary>
        /// <param name="byteArray">The array of bytes to write.</param>
        /// <param name="index">The start position to copy bytes from.</param>
        public void Write(byte[] byteArray, int index = 0)
        {
            // Copy the array of bytes into the block of memory
            Marshal.Copy(byteArray, index, Address, Size);
        }

        /// <summary>
        /// Write data to the unmanaged block of memory.
        /// </summary>
        /// <typeparam name="T">The type of data to write.</typeparam>
        /// <param name="data">The data to write.</param>
        public void Write<T>(T data)
        {
            // Marshal data from the managed object to the block of memory
            Marshal.StructureToPtr(data, Address, false);
        }

        #endregion

        #endregion
    }
}