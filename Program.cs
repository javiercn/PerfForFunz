using System;
using System.Buffers;
using System.Linq;
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
        private readonly Base64Encoder _myEncoder = new Base64Encoder('-', '_');
        public Base64EncodingBenchmarks()
        {
            var rnd = System.Security.Cryptography.RandomNumberGenerator.Create();
            _data = new byte[1024 * 1024];
            rnd.GetBytes(_data);
        }

        [Benchmark(Baseline = true)]
        public string CurrentAspNetCore()
        {
            return Base64UrlTextEncoder.Encode(_data);
        }

        [Benchmark]
        public string MySuperAwesomeImplementation()
        {
            return _myEncoder.Encode(_data, _data.Length);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // var base64Encoder = new Base64Encoder('+', '/', '=');
            // Console.WriteLine(base64Encoder.Decode(base64Encoder.Encode(new string('a', 1024 * 1024))));
            // Console.WriteLine(base64Encoder.Encode("hello world"));
            // Console.WriteLine(base64Encoder.Encode("aaa"));
            // Console.WriteLine(base64Encoder.Encode("aaaa"));
            // Console.WriteLine(base64Encoder.Encode("aaaaa"));

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
        private readonly byte[] _characters;
        private readonly byte[] _values;
        private readonly byte _padding;
        private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
        private static readonly ArrayPool<byte> _targetBytePool = ArrayPool<byte>.Create(2 * 1024 * 1024, 20);

        public Base64Encoder(char character62, char character63, char padding = default(char))
        {
            var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                "abcdefghijklmnopqrstuvwxyz" +
                $"0123456789{character62}{character63}";
            _characters = string.Concat(alphabet, alphabet, alphabet, alphabet).Select(c => (byte)c).ToArray();

            _values = new byte[128];
            for (var i = 0; i < alphabet.Length; i++)
            {
                _values[alphabet[i]] = (byte)i;
            };

            _padding = (byte)padding;
        }

        public string Encode(string data)
        {
            var sourceLength = Encoding.UTF8.GetByteCount(data);
            if (sourceLength < 1024)
            {
                var sourceBuffer = _bytePool.Rent(Encoding.UTF8.GetByteCount(data));
                var bufferLength = Encoding.UTF8.GetBytes(data, 0, data.Length, sourceBuffer, 0);

                var final = Encode(sourceBuffer, bufferLength);
                _bytePool.Return(sourceBuffer);
                return final;
            }

            var targetBuffer = _targetBytePool.Rent(GetTargetLength(sourceLength));
            var remainingBytes = sourceLength;
            var blockBuffer = _bytePool.Rent(1024);
            var dataIndex = 0;
            var blockLength = 0;
            var blockLeftOvers = 0;
            var targetIndex = 0;
            while (remainingBytes > 1024)
            {
                Array.Copy(blockBuffer, blockBuffer.Length - blockLeftOvers - 1, blockBuffer, 0, blockLeftOvers);
                (dataIndex, blockLength) = GetNextBlock(blockBuffer, data, dataIndex, blockLeftOvers);
                blockLeftOvers = blockLength % 3;
                var transformLength = blockLength - blockLeftOvers;
                EncodeBlock(blockBuffer, 0, transformLength, targetBuffer, targetIndex);
                targetIndex = targetIndex + transformLength;
                remainingBytes = remainingBytes - transformLength;
            }

            Array.Copy(blockBuffer, blockBuffer.Length - blockLeftOvers - 1, blockBuffer, 0, blockLeftOvers);
            var finalBytes = Encoding.UTF8.GetBytes(data, dataIndex, data.Length - dataIndex, blockBuffer, blockLeftOvers);
            EncodeFinal(blockBuffer, 0, finalBytes + blockLeftOvers, targetBuffer, targetIndex);

            var result = Encoding.UTF8.GetString(targetBuffer, 0, GetTargetLength(sourceLength));
            _bytePool.Return(blockBuffer);
            _targetBytePool.Return(targetBuffer);

            return result;
        }

        private (int dataIndex, int blockLength) GetNextBlock(byte[] buffer, string data, int index, int blockStart)
        {
            var ptrIndex = index;
            var count = blockStart;
            while (true)
            {
                var charBytes = Encoding.UTF8.GetByteCount(data, ptrIndex, 1);
                if (count + charBytes < 1024)
                {
                    count = count + charBytes;
                    ptrIndex++;
                }
                else
                {
                    return (ptrIndex, count);
                }
            }
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

        public string Encode(byte[] sourceBuffer, int length)
        {
            var targetLength = GetTargetLength(length);

            var targetBuffer = _targetBytePool.Rent(targetLength);

            EncodeFinal(sourceBuffer, 0, length, targetBuffer, 0);

            var result = Encoding.UTF8.GetString(targetBuffer, 0, targetLength);
            _targetBytePool.Return(targetBuffer);

            return result;
        }

        private void EncodeFinal(byte[] sourceBuffer, int start, int length, byte[] targetBuffer, int targetStart)
        {
            var remainingBytes = length % 3;
            var fullByteLength = length - length % 3;

            EncodeBlock(sourceBuffer, start, fullByteLength, targetBuffer, targetStart);

            var targetFinalIndex = targetStart + fullByteLength / 3 * 4;
            var sourceFinalIndex = start + fullByteLength;
            if (remainingBytes == 1)
            {
                targetBuffer[targetFinalIndex] = _characters[(byte)(sourceBuffer[sourceFinalIndex] >> 2)];
                targetBuffer[targetFinalIndex + 1] = _characters[(byte)(sourceBuffer[sourceFinalIndex] << 4)];

                if (_padding != default(char))
                {
                    targetBuffer[targetFinalIndex + 2] = _padding;
                    targetBuffer[targetFinalIndex + 3] = _padding;
                }
            }

            if (remainingBytes == 2)
            {
                targetBuffer[targetFinalIndex] = _characters[(byte)(sourceBuffer[sourceFinalIndex] >> 2)];
                targetBuffer[targetFinalIndex + 1] = _characters[(byte)(sourceBuffer[sourceFinalIndex] << 4) | (byte)(sourceBuffer[sourceFinalIndex + 1] >> 4)];
                targetBuffer[targetFinalIndex + 2] = _characters[(byte)(sourceBuffer[sourceFinalIndex + 1] << 2)];

                if (_padding != default(char))
                {
                    targetBuffer[targetFinalIndex + 3] = _padding;
                }
            }
        }

        private void EncodeBlock(byte[] sourceBuffer, int start, int length, byte[] targetBuffer, int targetStart)
        {
            for (int i = start, j = targetStart; i < length; i = i + 3, j = j + 4)
            {
                targetBuffer[j] = _characters[(byte)(sourceBuffer[i] >> 2)];
                targetBuffer[j + 1] = _characters[(byte)(sourceBuffer[i] << 4) | (byte)(sourceBuffer[i + 1] >> 4)];
                targetBuffer[j + 2] = _characters[(byte)(sourceBuffer[i + 1] << 2) | (byte)(sourceBuffer[i + 2] >> 6)];
                targetBuffer[j + 3] = _characters[(byte)sourceBuffer[i + 2]];
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
