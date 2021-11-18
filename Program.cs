using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Diagnostics.Debug;

namespace UnsafeMessagePipe
{
    public interface ISubscriberWrapper
    {
        void Execute(ReadOnlySpan<byte> bytes);
        int GetSize();
    }
    public interface ISubscriber<T> : ISubscriberWrapper where T : unmanaged 
    {
        void Execute(in T data);
    }
    public abstract class Subscriber<T> : ISubscriber<T> where T : unmanaged
    {
        public abstract void Execute(in T data);

        // These have to either come from a base class or be generated via the code generator
        void ISubscriberWrapper.Execute(ReadOnlySpan<byte> bytes)
        {
            Execute(in MemoryMarshal.Cast<byte, T>(bytes)[0]);
        }
        public int GetSize()
        {
            return Marshal.SizeOf<T>();
        }
    }

    public struct MessageIdentifier<T>
    {
        public int id;
        public MessageIdentifier(int id)
        {
            this.id = id;
        }
    }

    public class Registry
    {
        private int _currentId;
        public int NextId() { return _currentId++; }
        public int Count => _currentId;
    }

    public unsafe class UnsafeMessagePipe
    {
        private Memory<byte> _rawData;
        private int _currentIndex;

        public UnsafeMessagePipe(int maxSize)
        {
            _rawData = GC.AllocateArray<byte>(maxSize, false);
            _currentIndex = 0;
        }

        public void AddMessage<T>(MessageIdentifier<T> messageType, in T message) where T : unmanaged
        {
            using (var handle = _rawData.Pin())
            {
                Assert(_currentIndex + 4 + Marshal.SizeOf<T>() <= _rawData.Length);

                *(int*)((byte*) handle.Pointer + _currentIndex) = messageType.id;
                _currentIndex += 4;

                *(T*)((byte*) handle.Pointer + _currentIndex) = message;
                _currentIndex += Marshal.SizeOf<T>();
            }
        }

        public void Reset() { _currentIndex = 0; }
        public bool IsEmpty => _currentIndex == 0;

        public ref T GetMessageAt<T>(MessageIdentifier<T> messageType, int index) where T : unmanaged
        {
            using (var handle = _rawData.Pin())
            {
                Assert(index < _rawData.Length);
                Assert(*(int*)((byte*) handle.Pointer + index) == messageType.id);
                return ref Unsafe.AsRef<T>((T*)((byte*) handle.Pointer + index + 4));
            }
        }

        public void Flush(ReadOnlySpan<ISubscriberWrapper> eventHandlers)
        {
            using (var handle = _rawData.Pin())
            {
                int offset = 0;

                while (offset < _currentIndex)
                {
                    int rawMessageType = *(int*)((byte*) handle.Pointer + offset);
                    Assert(rawMessageType < eventHandlers.Length);
                    offset += 4; // the int
                    int size = eventHandlers[rawMessageType].GetSize();
                    var span = new Span<byte>((byte*) handle.Pointer + offset, size);
                    eventHandlers[rawMessageType].Execute(span);
                    offset += size;
                }

                _currentIndex = 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct DataA
    {
        public int a;
        public int b;
    }
    public class DataA_PlaybackSubscriber : Subscriber<DataA>
    {
        public override void Execute(in DataA data)
        {
            Console.WriteLine("Data A playing back");
            Console.WriteLine("a:" + data.a);
            Console.WriteLine("b:" + data.b);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct DataB
    {
        public int a;
        public int b;
    }
    public class DataB_PlaybackSubscriber : Subscriber<DataB>
    {
        public override void Execute(in DataB data)
        {
            Console.WriteLine("Data B playing back");
            Console.WriteLine("a:" + data.a);
            Console.WriteLine("b:" + data.b);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Registry reg = new Registry();

            MessageIdentifier<DataA> a_index;
            a_index.id = reg.NextId();
            MessageIdentifier<DataB> b_index;
            b_index.id = reg.NextId();
            
            DataA data1;
            data1.a = 1;
            data1.b = 2;

            DataB data2;
            data2.a = 3;
            data2.b = 4;

            // This gets the final size. It will never grow.
            // TODO: maybe make it a circular buffer.
            var pipe = new UnsafeMessagePipe(64 * 1024);
            pipe.AddMessage(a_index, data1);
            pipe.AddMessage(b_index, data2);
            pipe.AddMessage(a_index, data1);

            var subs = new ISubscriberWrapper[reg.Count];
            subs[a_index.id] = new DataA_PlaybackSubscriber();
            subs[b_index.id] = new DataB_PlaybackSubscriber();

            pipe.Flush(subs);
        }
    }
}
