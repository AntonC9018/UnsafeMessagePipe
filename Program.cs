using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static System.Diagnostics.Debug;

namespace UnsafeMessagePipe
{
    enum MessageType { A, B }
    struct MessageA
    {
        public int a;
        public int b;
    }
    struct MessageB
    {
        public int a;
        public int b;
    }
    struct MessageWithPtr
    {
        public int a;
    }

    struct Message<MessageBodyType> where MessageBodyType : struct
    {
        public MessageType messageType;
        public MessageBodyType messageBody;
        public static int GetSize() => Marshal.SizeOf<Message<MessageBodyType>>();
    }

    class Program
    {
        static int GetMessageSize(MessageType m)
        {
            return m switch
            {
                MessageType.A => Marshal.SizeOf<MessageA>(),
                MessageType.B => Marshal.SizeOf<MessageB>(),
                _ => throw new Exception("Impossible")
            };
        }

        unsafe void Set<T>(ref byte* memory, in T message, MessageType messageType) where T : unmanaged
        {
            *(int*) memory = (int) messageType;
            memory += sizeof(int);
            *(T*) memory = message;
            memory += Marshal.SizeOf<T>();
        } 
        
        static void Main(string[] args)
        {
            unsafe
            {
                byte[] array = GC.AllocateArray<byte>(32, pinned: true);
                {
                    int offset = 0;
                    byte* basePointer = (byte*) Marshal.UnsafeAddrOfPinnedArrayElement(array, 0);
                    Span<int> i = new Span<int>(basePointer, 32 / 4);
                    *(int*) (basePointer + offset) = (int) MessageType.A; // 4
                    offset += 4;
                    int asize = GetMessageSize(MessageType.A); // 8
                    Assert(asize == 8);
                    MessageA* aptr = (MessageA*) (basePointer + offset);
                    aptr->a = 1;
                    aptr->b = 2;
                    offset += asize;

                    *(int*)(basePointer + offset) = (int) MessageType.B; // 4
                    offset += 4;
                    int bsize = GetMessageSize(MessageType.B); // 8
                    Assert(bsize == 8);
                    MessageA* bptr = (MessageA*) (basePointer + offset);
                    bptr->a = 3;
                    bptr->b = 4;
                    offset += bsize;
                }
                
                {
                    int offset;
                    byte* basePointer = (byte*) Marshal.UnsafeAddrOfPinnedArrayElement(array, 0);

                    for (offset = 0; offset < array.Length;)
                    {
                        int rawMessageType = * (int*) ((byte*) basePointer + offset);
                        if (rawMessageType == -1)
                            break;
                        int size = GetMessageSize((MessageType) rawMessageType);
                        offset += 4; // the int

                        switch ((MessageType) rawMessageType)
                        {
                            case MessageType.A:
                            {
                                MessageA* a = (MessageA*) ((byte*) basePointer + offset);
                                MessageA acopy = *a;
                                Console.WriteLine(acopy.a);
                                Console.WriteLine(acopy.b);
                                break;
                            }
                            
                            case MessageType.B:
                            {
                                MessageB* b = (MessageB*) ((byte*) basePointer + offset);
                                MessageB bcopy = *b;
                                Console.WriteLine(bcopy.a);
                                Console.WriteLine(bcopy.b);
                                break;
                            }

                            default: Assert(false, "Should never happen"); break;
                        }

                        offset += size;
                    }
                }
            }
        }
    }
}
