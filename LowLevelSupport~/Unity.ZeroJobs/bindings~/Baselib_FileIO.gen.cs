//
// File autogenerated from Include/C/Baselib_FileIO.h
//

using System;
using System.Runtime.InteropServices;
using size_t = System.UIntPtr;

namespace Unity.Baselib.LowLevel
{
    internal static unsafe partial class Binding
    {
        /// <summary>Event queue handle.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_EventQueue
        {
            public IntPtr handle;
        }
        /// <summary>Async file handle.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_AsyncFile
        {
            public IntPtr handle;
        }
        /// <summary>Sync file handle.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_SyncFile
        {
            public IntPtr handle;
        }
        public enum Baselib_FileIO_OpenFlags : UInt64 // Baselib_FileIO_OpenFlags_t
        {
            Baselib_FileIO_FileFlags_Read = 0x1,
            Baselib_FileIO_FileFlags_Write = 0x2,
            /// <summary>when specifying this flag, Baselib_FileIO_FileFlags_Write must be specified</summary>
            Baselib_FileIO_FileFlags_CreateIfDoesntExist = 0x4,
            Baselib_FileIO_FileFlags_CreateAlways = 0x8,
        }
        /// <summary>File IO read request.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_ReadRequest
        {
            /// <summary>Offset in a file to read from.</summary>
            public UInt64 offset;
            /// <summary>Buffer to read to, must be available for duration of operation.</summary>
            public IntPtr buffer;
            /// <summary>Size of requested read, please note this it's 32 bit value.</summary>
            public UInt32 size;
        }
        /// <summary>
        /// File IO priorities.
        /// First we process all requests with high priority, then with normal priority.
        /// There's no round-robin, and high priority can starve normal priority.
        /// </summary>
        public enum Baselib_FileIO_Priority : Int32
        {
            Normal = 0x0,
            High = 0x1,
        }
        public enum Baselib_FileIO_EventQueue_ResultType : Int32
        {
            /// <summary>Upon receiving this event, please call the provided callback with provided data argument.</summary>
            Baselib_FileIO_EventQueue_Callback = 0x1,
            /// <summary>Result of open file operation.</summary>
            Baselib_FileIO_EventQueue_OpenFile = 0x2,
            /// <summary>Result of read file operation.</summary>
            Baselib_FileIO_EventQueue_ReadFile = 0x3,
            /// <summary>Result of close file operation.</summary>
            Baselib_FileIO_EventQueue_CloseFile = 0x4,
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void EventQueueCallback(UInt64 arg0);
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_EventQueue_Result_Callback
        {
            /// <summary>Please invoke this callback with userdata from the event.</summary>
            // Delegates are managed types, putting it here directly would prevent us from creating pointers to this struct.
            // Use System.Runtime.InteropServices.GetFunctionPointerForDelegate to generate IntPtr from the original delegate.
            public IntPtr callback; // EventQueueCallback
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_EventQueue_Result_OpenFile
        {
            /// <summary>Size of the file as seen on during open.</summary>
            public UInt64 fileSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct Baselib_FileIO_EventQueue_Result_ReadFile
        {
            /// <summary>Bytes transfered during read, please note it's 32 bit value.</summary>
            public UInt32 bytesTransfered;
        }
        /// <summary>Event queue result.</summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct Baselib_FileIO_EventQueue_Result
        {
            /// <summary>Event type.</summary>
            [FieldOffset(0)]
            public Baselib_FileIO_EventQueue_ResultType type;
            /// <summary>Userdata as provided to the request.</summary>
            [FieldOffset(8)]
            public UInt64 userdata;
            /// <summary>Error state of the operation.</summary>
            [FieldOffset(16)]
            public Baselib_ErrorState errorState;
            [FieldOffset(56)]
            public Baselib_FileIO_EventQueue_Result_Callback callback;
            [FieldOffset(56)]
            public Baselib_FileIO_EventQueue_Result_OpenFile openFile;
            [FieldOffset(56)]
            public Baselib_FileIO_EventQueue_Result_ReadFile readFile;
        }
        /// <summary>Creates event queue.</summary>
        /// <returns>Event queue.</returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern Baselib_FileIO_EventQueue Baselib_FileIO_EventQueue_Create();
        /// <summary>Frees event queue.</summary>
        /// <param name="eq">event queue to free.</param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Baselib_FileIO_EventQueue_Free(Baselib_FileIO_EventQueue eq);
        /// <summary>Dequeue events from event queue.</summary>
        /// <remarks>
        /// File operations errors are reported via Baselib_FileIO_EventQueue_Result::errorState
        /// Possible error codes:
        /// - InvalidPathname:             Requested pathname is invalid (not found, a directory, etc).
        /// - RequestedAccessIsNotAllowed: Access to requested pathname is not allowed.
        /// - IOError:                     IO error occured.
        /// </remarks>
        /// <param name="eq">Event queue to dequeue from.</param>
        /// <param name="results">
        /// Results array to dequeue elements into.
        /// If null will return 0.
        /// </param>
        /// <param name="count">
        /// Amount of elements in results array.
        /// If equals 0 will return 0.
        /// </param>
        /// <param name="timeoutInMilliseconds">
        /// If no elements are present in the queue,
        /// waits for any elements to be appear for specified amount of time.
        /// If 0 is passed, wait is omitted.
        /// If elements are present, dequeues up-to-count elements, and wait is omitted.
        /// </param>
        /// <returns>Amount of results filled.</returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UInt64 Baselib_FileIO_EventQueue_Dequeue(Baselib_FileIO_EventQueue eq, Baselib_FileIO_EventQueue_Result* results, UInt64 count, UInt32 timeoutInMilliseconds);
        /// <summary>Asynchronously opens a file.</summary>
        /// <remarks>
        /// Please note errors are reported via Baselib_FileIO_EventQueue_Result::errorState
        /// Possible error codes:
        /// - InvalidPathname:             Requested pathname is invalid (not found, a directory, etc).
        /// - RequestedAccessIsNotAllowed: Access to requested pathname is not allowed.
        /// - IOError:                     IO error occured.
        /// </remarks>
        /// <param name="eq">
        /// Event queue to associate file with.
        /// File can only be associated with one event queue,
        /// but one event queue can be associated with multiple files.
        /// If invalid event queue is passed, will return invalid file handle.
        /// </param>
        /// <param name="pathname">
        /// Platform defined pathname of a file.
        /// Can be freed after this function returns.
        /// If null is passed will return invalid file handle.
        /// </param>
        /// <param name="userdata">Userdata to be set in the completion event.</param>
        /// <param name="priority">Priority for file opening operation.</param>
        /// <returns>
        /// Async file handle, which can be used immediately for scheduling other operations.
        /// In case if file opening fails, all scheduled operations will fail as well.
        /// In case if invalid arguments are passed, might return invalid file handle (see args descriptions).
        /// </returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern Baselib_FileIO_AsyncFile Baselib_FileIO_AsyncOpen(Baselib_FileIO_EventQueue eq, byte* pathname, UInt64 userdata, Baselib_FileIO_Priority priority);
        /// <summary>Asynchronously reads data from a file.</summary>
        /// <remarks>
        /// Note scheduling reads on closed file is undefined.
        ///
        /// Please note errors are reported via Baselib_FileIO_EventQueue_Result::errorState
        /// If file is invalid handle, error can not be reported because event queue is not known.
        /// Possible error codes:
        /// - IOError:                     IO error occured.
        /// </remarks>
        /// <param name="file">
        /// Async file to read from.
        /// If invalid file handle is passed, will no-op.
        /// If file handle was already closed, behavior is undefined.
        /// </param>
        /// <param name="requests">
        /// Requests to schedule.
        /// If more than 1 provided,
        /// will provide completion event per individual request in the array.
        /// If null is passed, will no-op.
        /// </param>
        /// <param name="count">
        /// Amount of requests in requests array.
        /// If 0 is passed, will no-op.
        /// </param>
        /// <param name="userdata">Userdata to be set in the completion event(s).</param>
        /// <param name="priority">Priority for file reading operation(s).</param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Baselib_FileIO_AsyncRead(Baselib_FileIO_AsyncFile file, Baselib_FileIO_ReadRequest* requests, UInt64 count, UInt64 userdata, Baselib_FileIO_Priority priority);
        /// <summary>Asynchronously closes a file.</summary>
        /// <remarks>
        /// Will wait for all pending operations to complete,
        /// after that will close a file and put a completion event.
        ///
        /// Please note errors are reported via Baselib_FileIO_EventQueue_Result::errorState
        /// If file is invalid handle, error can not be reported because event queue is not known.
        /// Possible error codes:
        /// - IOError:                     IO error occured.
        /// </remarks>
        /// <param name="file">
        /// Async file to close.
        /// If invalid file handle is passed, will no-op.
        /// </param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Baselib_FileIO_AsyncClose(Baselib_FileIO_AsyncFile file);
        /// <summary>Synchronously opens a file.</summary>
        /// <remarks>
        /// Will try use the most open access permissions options that are available for each platform.
        /// If you require more strict options, please use our platform specific SyncOpenFromXXX functions.
        ///
        /// Possible error codes:
        /// - InvalidArgument:            Invalid argument was passed.
        /// - IOError:                    Generic IO error occured.
        /// </remarks>
        /// <param name="pathname">Platform defined pathname to open.</param>
        /// <param name="openFlags">Open flags.</param>
        /// <param name="createFileSize">When file needs to be created, because of a create flag is passed, will use this file size.</param>
        /// <param name="outFileSize">Opened file size.</param>
        /// <returns>SyncFile handle.</returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern Baselib_FileIO_SyncFile Baselib_FileIO_SyncOpen(byte* pathname, Baselib_FileIO_OpenFlags openFlags, UInt64 createFileSize, UInt64* outFileSize, Baselib_ErrorState* errorState);
        /// <summary>Synchronously reads data from a file.</summary>
        /// <remarks>
        /// Possible error codes:
        /// - InvalidArgument:            Invalid argument was passed.
        /// - IOError:                    Generic IO error occured.
        /// </remarks>
        /// <param name="file">
        /// File to read from.
        /// If invalid file handle is passed, will no-op.
        /// </param>
        /// <param name="offset">Offset in the file to read data at.</param>
        /// <param name="buffer">Pointer to data to read into.</param>
        /// <param name="size">
        /// Size of data to read.
        /// Note size is uint32_t which means we can read at most 4GB in one operation.
        /// </param>
        /// <returns>Amount of bytes read.</returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UInt32 Baselib_FileIO_SyncRead(Baselib_FileIO_SyncFile file, UInt64 offset, IntPtr buffer, UInt32 size, Baselib_ErrorState* errorState);
        /// <summary>Synchronously writes data to a file.</summary>
        /// <remarks>
        /// Possible error codes:
        /// - InvalidArgument:            Invalid argument was passed.
        /// - IOError:                    Generic IO error occured.
        /// </remarks>
        /// <param name="file">
        /// File to write to.
        /// If invalid file handle is passed, will no-op.
        /// </param>
        /// <param name="offset">
        /// Offset in the file to write data at.
        /// if offset+size goes past end-of-file, then file will be resized.
        /// </param>
        /// <param name="buffer">Pointer to data to write.</param>
        /// <param name="size">
        /// Size of data to write.
        /// Note size is uint32_t which means we can write at most 4GB in one operation.
        /// </param>
        /// <returns>Amount of bytes written.</returns>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UInt32 Baselib_FileIO_SyncWrite(Baselib_FileIO_SyncFile file, UInt64 offset, IntPtr buffer, UInt32 size, Baselib_ErrorState* errorState);
        /// <summary>Synchronously flushes file buffers.</summary>
        /// <remarks>
        /// Operating system might buffer some write operations.
        /// Flushing buffers is required to guarantee (best effort) writing data to disk.
        ///
        /// Possible error codes:
        /// - InvalidArgument:            Invalid argument was passed.
        /// - IOError:                    Generic IO error occured.
        /// </remarks>
        /// <param name="file">
        /// File to flush.
        /// If invalid file handle is passed, will no-op.
        /// </param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Baselib_FileIO_SyncFlush(Baselib_FileIO_SyncFile file, Baselib_ErrorState* errorState);
        /// <summary>
        /// Synchronously closes a file.
        /// Please note that closing a file does not guarantee file buffers flush.
        /// </summary>
        /// <remarks>
        /// Possible error codes:
        /// - InvalidArgument:            Invalid argument was passed.
        /// - IOError:                    Generic IO error occured.
        /// </remarks>
        /// <param name="file">
        /// File to close.
        /// If invalid file handle is passed, will no-op.
        /// </param>
        [DllImport(BaselibNativeLibrary.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Baselib_FileIO_SyncClose(Baselib_FileIO_SyncFile file, Baselib_ErrorState* errorState);
    }
}
