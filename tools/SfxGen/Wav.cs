using System.Text;

namespace Bloom.SfxGen;

/// <summary>Reads and writes the one kind of file the tool makes: 16-bit PCM WAV, mono.</summary>
internal static class Wav
{
    public static byte[] Encode(short[] samples, int sampleRate = Synth.OutputRate)
    {
        const int HeaderSize = 44;
        var bytes = new byte[HeaderSize + (samples.Length * 2)];
        using var stream = new MemoryStream(bytes);
        using var w = new BinaryWriter(stream, Encoding.ASCII);
        w.Write("RIFF"u8);
        w.Write(bytes.Length - 8);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (short s in samples)
        {
            w.Write(s);
        }

        return bytes;
    }

    public static short[] Decode(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII);
        if (bytes.Length < 12 || Tag(r) != "RIFF")
        {
            throw new InvalidDataException("Not a RIFF file.");
        }

        r.ReadInt32();
        if (Tag(r) != "WAVE")
        {
            throw new InvalidDataException("Not a WAVE file.");
        }

        bool sawFormat = false;
        while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
        {
            string chunk = Tag(r);
            int size = r.ReadInt32();
            long next = r.BaseStream.Position + size + (size & 1);
            if (chunk == "fmt ")
            {
                short format = r.ReadInt16();
                short channels = r.ReadInt16();
                int rate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                short bits = r.ReadInt16();
                if (format != 1 || channels != 1 || bits != 16 || rate != Synth.OutputRate)
                {
                    throw new InvalidDataException($"Expected 16-bit mono PCM at {Synth.OutputRate} Hz; found format {format}, {channels} channel(s), {bits} bits, {rate} Hz.");
                }

                sawFormat = true;
            }
            else if (chunk == "data")
            {
                if (!sawFormat)
                {
                    throw new InvalidDataException("The data chunk comes before the format chunk.");
                }

                var samples = new short[size / 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = r.ReadInt16();
                }

                return samples;
            }

            r.BaseStream.Position = next;
        }

        throw new InvalidDataException("The file has no data chunk.");
    }

    private static string Tag(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(4));
}
