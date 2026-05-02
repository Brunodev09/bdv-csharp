using System.Buffers.Binary;

namespace BdvEngine;

internal static class WavDecoder
{
    public sealed class Pcm
    {
        public byte[] Data = Array.Empty<byte>();
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
    }

    public static Pcm Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44) throw new InvalidDataException($"WAV too small: {path}");

        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
            throw new InvalidDataException($"Not a RIFF file: {path}");
        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
            throw new InvalidDataException($"Not a WAVE file: {path}");

        int p = 12;
        short audioFormat = 0, channels = 0, bits = 0;
        int sampleRate = 0;
        byte[] data = Array.Empty<byte>();

        while (p + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, p, 4);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p + 4, 4));
            p += 8;
            if (id == "fmt ")
            {
                audioFormat  = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(p,      2));
                channels     = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(p + 2,  2));
                sampleRate   = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p + 4,  4));
                bits         = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(p + 14, 2));
            }
            else if (id == "data")
            {
                data = new byte[chunkSize];
                Array.Copy(bytes, p, data, 0, chunkSize);
                break;
            }
            p += chunkSize + (chunkSize & 1); // chunks are word-aligned
        }

        if (audioFormat != 1) throw new InvalidDataException($"WAV must be PCM (got format {audioFormat}): {path}");
        if (bits != 8 && bits != 16) throw new InvalidDataException($"WAV must be 8 or 16-bit (got {bits}): {path}");
        if (channels != 1 && channels != 2) throw new InvalidDataException($"WAV must be mono or stereo (got {channels} ch): {path}");

        return new Pcm { Data = data, Channels = channels, SampleRate = sampleRate, BitsPerSample = bits };
    }
}
