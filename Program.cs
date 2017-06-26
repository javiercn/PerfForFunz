using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.WebUtilities;

namespace PerfForFunz
{
    [MemoryDiagnoser]
    public class Base64EncodingBenchmarks
    {
        private readonly byte[] _data;
        // private readonly Base64Encoder _myEncoder = new Base64Encoder('-', '_');
        private readonly Base64Encoder _myEncoder = new Base64Encoder('+', '/', '=');
        public Base64EncodingBenchmarks()
        {
            var rnd = System.Security.Cryptography.RandomNumberGenerator.Create();
            _data = new byte[1024 * 1024];
            rnd.GetBytes(_data);
        }

        // [Benchmark(Baseline = true)]
        // public string CurrentAspNetCore()
        // {
        //     return Base64UrlTextEncoder.Encode(_data);
        // }

        [Benchmark(Baseline = true)]
        public string DotNetImplementation()
        {
            return Convert.ToBase64String(_data);
        }

        [Benchmark]
        public string MySuperAwesomeImplementation()
        {
            return _myEncoder.Encode(_data, _data.Length);
        }
    }

    class Program
    {
        unsafe static void Main(string[] args)
        {
            // var str = "hello world";
            // fixed (char* hptr = str)
            // {
            //     var bytePtr = (byte*)hptr;
            //     var byteCount = UnicodeEncoding.Unicode.GetByteCount(hptr, str.Length);
            //     for (var i = 0; i < byteCount; i++)
            //     {
            //         Console.Write($"'{bytePtr[i]}', ");
            //     }
            // }

            // var base64Encoder = new Base64Encoder('+', '/', '=');
            // Console.WriteLine(base64Encoder.Encode(new string('a', 1024)));
            // Console.WriteLine(base64Encoder.Encode("aaa"));
            // Console.WriteLine(base64Encoder.Encode("aaaa"));
            // Console.WriteLine(base64Encoder.Encode("aaaaa"));
            // Console.WriteLine(base64Encoder.Encode(new string('a', 2048)));

            // var base64UrlEncoder = new Base64Encoder('-', '_');
            // Console.WriteLine(base64UrlEncoder.Encode("hello world"));
            // Console.WriteLine(base64UrlEncoder.Encode("aaa"));
            // Console.WriteLine(base64UrlEncoder.Encode("aaaa"));
            // Console.WriteLine(base64UrlEncoder.Encode("aaaaa"));

            // Console.WriteLine(base64Encoder.Decode(base64Encoder.Encode("hello world")));
            // Console.WriteLine(base64Encoder.Decode(base64Encoder.Encode("aaa")));
            // Console.WriteLine(base64Encoder.Decode(base64Encoder.Encode("aaaa")));
            // Console.WriteLine(base64Encoder.Decode(base64Encoder.Encode("aaaaa")));

            // Console.WriteLine("Hello World!");

            var summary = BenchmarkRunner.Run<Base64EncodingBenchmarks>();
        }
    }

    public class Base64Encoder
    {
        private readonly short[] _characters;
        private readonly byte[] _values;
        private readonly byte _padding;
        private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;

        public const int SourceBlockBytes = 3;
        public const int TargetBlockBytes = 4;

        public static Vector<int> mask0 = new Vector<int>(0b00111111_00000000_00000000_00000000);
        public static Vector<int> mask1 = new Vector<int>(0b00000000_00111111_00000000_00000000);
        public static Vector<int> mask2 = new Vector<int>(0b00000000_00000000_00111111_00000000);
        public static Vector<int> mask3 = new Vector<int>(0b00000000_00000000_00000000_00111111);

        public Base64Encoder(char character62, char character63, char padding = default(char))
        {
            var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                "abcdefghijklmnopqrstuvwxyz" +
                $"0123456789{character62}{character63}";
            _characters = string.Concat(alphabet, alphabet, alphabet, alphabet).Select(c => (short)c).ToArray();

            _values = new byte[128];

            for (var i = 0; i < alphabet.Length; i++)
            {
                _values[alphabet[i]] = (byte)i;
            };

            _padding = (byte)padding;
        }

        public unsafe string Encode(string data)
        {
            var encoder = Encoding.UTF8.GetEncoder();
            var sourceBuffer = _bytePool.Rent(1024);
            var remainingChars = data.Length;
            fixed (char* dataPtr = data)
            {
                var length = GetTargetLength(Encoding.UTF8.GetByteCount(dataPtr, data.Length));
                var result = new string('\0', length);
                fixed (char* resultPtr = result)
                {
                    var target = resultPtr;
                    fixed (byte* source = sourceBuffer)
                    {
                        var dataCursor = dataPtr;
                        var completed = false;
                        while (!completed)
                        {
                            encoder.Convert(
                                dataCursor,
                                remainingChars,
                                source,
                                1020,
                                false,
                                out var charsRead,
                                out var bytesWritten,
                                out completed);

                            if (!completed)
                            {
                                EncodeBlock(source, bytesWritten, (byte*)target);
                                target = target + bytesWritten / 3 * 4;
                            }
                            else
                            {
                                EncodeFinal(source, bytesWritten, (byte*)target);
                            }

                            dataCursor = dataCursor + charsRead;
                            remainingChars = remainingChars - charsRead;
                        }
                    }
                }

                return result;
            }
            // var sourceLength = Encoding.UTF8.GetByteCount(data);
            // if (sourceLength < 1024)
            // {
            //     var bufferLength = Encoding.UTF8.GetBytes(data, 0, data.Length, sourceBuffer, 0);

            //     var final = Encode(sourceBuffer, bufferLength);
            //     _bytePool.Return(sourceBuffer);
            //     return final;
            // }

            // var result = new string('\0', GetTargetLength(sourceLength));
            // fixed (char* resultBuffer = result)
            // {
            //     var targetBuffer = (byte*)resultBuffer;
            //     var remainingBytes = sourceLength;
            //     var blockBuffer = _bytePool.Rent(1024);
            //     var dataIndex = 0;
            //     var blockLength = 0;
            //     var blockLeftOvers = 0;
            //     var targetIndex = 0;
            //     while (remainingBytes > 1024)
            //     {
            //         Array.Copy(blockBuffer, blockBuffer.Length - blockLeftOvers - 1, blockBuffer, 0, blockLeftOvers);
            //         (dataIndex, blockLength) = GetNextBlock(blockBuffer, data, dataIndex, blockLeftOvers);
            //         blockLeftOvers = blockLength % 3;
            //         var transformLength = blockLength - blockLeftOvers;
            //         fixed (byte* buffer = blockBuffer)
            //         {
            //             EncodeBlock(buffer, transformLength, targetBuffer);
            //         }
            //         targetIndex = targetIndex + ((transformLength / 3 * 4) * 2);
            //         remainingBytes = remainingBytes - transformLength;
            //     }

            //     Array.Copy(blockBuffer, blockBuffer.Length - blockLeftOvers - 1, blockBuffer, 0, blockLeftOvers);
            //     var finalBytes = Encoding.UTF8.GetBytes(data, dataIndex, data.Length - dataIndex, blockBuffer, blockLeftOvers);

            //     fixed (byte* buffer = blockBuffer)
            //     {
            //         EncodeFinal(buffer, 0, targetBuffer);
            //     }

            //     _bytePool.Return(blockBuffer);

            // return result;
            // }
        }

        private int GetTargetLength(int sourceLength)
        {
            var remainingBytes = sourceLength % 3;
            var targetLength = sourceLength / 3 * 4;

            if (remainingBytes != 0)
            {
                if (_padding != default(char))
                {
                    targetLength = targetLength + 4;
                }
                else
                {
                    targetLength = targetLength + remainingBytes + 1;
                }
            }

            return targetLength;
        }

        public unsafe string Encode(byte[] sourceBuffer, int length)
        {
            var targetLength = GetTargetLength(length);

            var result = new string('\0', targetLength);
            fixed (byte* buffer = sourceBuffer)
            {
                fixed (char* targetBuffer = result)
                {
                    Encode(buffer, length, (byte*)targetBuffer);
                }
            }

            return result;
        }

        public unsafe void Encode(byte* buffer, int length, byte* target)
        {
            EncodeFinal(buffer, length, target);
        }

        private unsafe void EncodeFinal(byte* sourceBuffer, int length, byte* targetBuffer)
        {
            var remainingBytes = length % 3;
            var fullByteLength = length - length % 3;

            EncodeBlock(sourceBuffer, fullByteLength, targetBuffer);

            var targetFinalIndex = ((short*)targetBuffer) + (fullByteLength / 3) * TargetBlockBytes;
            var sourceFinalIndex = sourceBuffer + fullByteLength;
            if (remainingBytes == 1)
            {
                *targetFinalIndex++ = _characters[(byte)(*sourceFinalIndex >> 2)];
                *targetFinalIndex++ = _characters[(byte)(*sourceFinalIndex << 4)];

                if (_padding != default(char))
                {
                    *targetFinalIndex++ = _padding;
                    *targetFinalIndex = _padding;
                }
            }

            if (remainingBytes == 2)
            {
                *targetFinalIndex++ = _characters[(byte)(*sourceFinalIndex >> 2)];
                *targetFinalIndex++ = _characters[(byte)(*sourceFinalIndex << 4) | (byte)(sourceFinalIndex[1] >> 4)];
                *targetFinalIndex++ = _characters[(byte)(sourceFinalIndex[1] << 2)];

                if (_padding != default(char))
                {
                    *targetFinalIndex = _padding;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void EncodeBlock(byte* sourceBuffer, int length, byte* targetBuffer)
        {
            var target = (short*)targetBuffer;
            Debug.Assert(target[0] == 0);
            var numberOfFullBlocks = length / (SourceBlockBytes * Vector<int>.Count);
            var fullBlocksLength = numberOfFullBlocks * SourceBlockBytes * Vector<int>.Count;
            ComputeBlock(sourceBuffer, fullBlocksLength, targetBuffer);

            sourceBuffer = sourceBuffer + fullBlocksLength;
            target = target + fullBlocksLength / SourceBlockBytes * TargetBlockBytes;

            var remaining = length - fullBlocksLength;
            fixed (short* characters = _characters)
            {
                for (var i = 0; i < remaining; i = i + 3)
                {
                    *target++ = _characters[(byte)(sourceBuffer[i] >> 2)];
                    *target++ = _characters[(byte)(sourceBuffer[i] << 4) | (byte)(sourceBuffer[i + 1] >> 4)];
                    *target++ = _characters[(byte)(sourceBuffer[i + 1] << 2) | (byte)(sourceBuffer[i + 2] >> 6)];
                    *target++ = _characters[(byte)sourceBuffer[i + 2]];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void ComputeBlock(byte* source, int sourceLength, byte* destination)
        {
            Debug.Assert(sourceLength % (SourceBlockBytes * Vector<int>.Count) == 0);

            var targetLength = sourceLength / SourceBlockBytes * TargetBlockBytes;
            var blockLength = Vector<int>.Count * sizeof(int);
            var tmp = stackalloc byte[blockLength];
            var passes = targetLength / blockLength;

            var tmpCursor = tmp;
            var srcCursor = source + SourceBlockBytes - 1;
            var dstCursor = (short*)destination;

            fixed (short* characters = _characters)
            {
                for (int i = 0; i < passes; i++, tmpCursor = tmp)
                {
                    const int blockStep = SourceBlockBytes * 2;
                    for (var j = 0; j < blockLength; j = j + TargetBlockBytes)
                    {
                        *tmpCursor++ = *srcCursor--;
                        *tmpCursor++ = *srcCursor--;
                        *tmpCursor++ = *srcCursor--;
                        tmpCursor++;
                        srcCursor = srcCursor + blockStep;
                    }

                    var initMem = Unsafe.Read<Vector<int>>(tmp);
                    var result = initMem * 64 & mask0;
                    var tmpVect = initMem * 16 & mask1;
                    result = result | tmpVect;
                    tmpVect = initMem * 4 & mask2;
                    result = result | tmpVect;
                    result = result | initMem & mask3;
                    Unsafe.Write(tmp, result);

                    tmpCursor = tmp + TargetBlockBytes - 1;

                    const int destinationStep = TargetBlockBytes * 2;

                    for (var j = 0; j < blockLength; j = j + TargetBlockBytes)
                    {
                        *dstCursor++ = characters[*tmpCursor--];
                        *dstCursor++ = characters[*tmpCursor--];
                        *dstCursor++ = characters[*tmpCursor--];
                        *dstCursor++ = characters[*tmpCursor--];
                        tmpCursor = tmpCursor + destinationStep;
                    }
                }
            }
        }

        public string Decode(string encoded)
        {
            var totalBytes = 0;
            var paddingCharacters = 0;
            if (string.IsNullOrEmpty(encoded))
            {
                throw new InvalidOperationException("Invalid encoded data.");
            }

            if (_padding != default(char))
            {
                if (encoded.Length % 4 != 0)
                {
                    throw new InvalidOperationException("Invalid encoded data.");
                }
                var firstPaddingCharacterIndex = encoded.IndexOf((char)_padding, encoded.Length - 4);

                if (firstPaddingCharacterIndex == encoded.Length - 1)
                {
                    paddingCharacters = 1;
                }
                else if (firstPaddingCharacterIndex == encoded.Length - 2)
                {
                    paddingCharacters = 2;
                }
                else if (encoded.Length % 4 == 0)
                {
                    paddingCharacters = 0;
                }
                else
                {
                    throw new InvalidOperationException("Invalid encoded data.");
                }

                totalBytes = ((encoded.Length / 4) * 3) - paddingCharacters;
            }
            else
            {
                paddingCharacters = 4 - encoded.Length % 4;
                if (paddingCharacters == 3)
                {
                    throw new InvalidOperationException("Invalid encoded data.");
                }

                var additionalBytes = paddingCharacters == 1 ? 2 : paddingCharacters == 2 ? 1 : 0;

                totalBytes = (((encoded.Length - encoded.Length % 4) / 4) * 3) + additionalBytes;
            }

            var buffer = _bytePool.Rent(totalBytes);

            var fullTransformLength = totalBytes - totalBytes % 3;
            for (int i = 0, j = 0; i < fullTransformLength; i = i + 3, j = j + 4)
            {
                buffer[i] = (byte)(_values[encoded[j]] << 2 | (byte)(_values[encoded[j + 1]] >> 4));
                buffer[i + 1] = (byte)(_values[encoded[j + 1]] << 4 | (byte)(_values[encoded[j + 2]] >> 2));
                buffer[i + 2] = (byte)(_values[encoded[j + 2]] << 6 | (byte)(_values[encoded[j + 3]]));
            }

            var remainingBytes = totalBytes % 3;

            if (remainingBytes == 1)
            {
                var charIndex = fullTransformLength * 4 / 3;
                buffer[fullTransformLength] = (byte)(_values[encoded[charIndex]] << 2 | (_values[encoded[charIndex + 1]] >> 4));
            }

            if (remainingBytes == 2)
            {
                var charIndex = fullTransformLength * 4 / 3;
                buffer[fullTransformLength] = (byte)(_values[encoded[charIndex]] << 2 | (_values[encoded[charIndex + 1]] >> 4));
                buffer[fullTransformLength + 1] = (byte)(_values[encoded[charIndex + 1]] << 4 | (_values[encoded[charIndex + 2]] >> 2));
            }

            return Encoding.UTF8.GetString(buffer, 0, totalBytes);
        }
    }
}
