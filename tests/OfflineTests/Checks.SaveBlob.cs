using System;
using System.IO;
using System.Text;
using DSPQualitySystem.Save;

namespace DSPQualitySystem.OfflineTests
{
    /// <summary>
    /// v0.11.11: offline checks for the .dsv trailer codec. The codec is what makes the quality data
    /// travel inside the player's save file, so these checks pin the exact byte layout, the behaviour
    /// on a save that has no trailer (vanilla or pre-v0.11.11 save) and on damaged trailers - the mod
    /// must never guess and must never consume vanilla bytes.
    /// </summary>
    internal static partial class Program
    {
        private static void CheckSaveBlobRoundTrip()
        {
            int[] lengths = { 1, 7, 8, 12, 1000, 65536 };
            var rng = new Random(20261026);
            for (int i = 0; i < lengths.Length; i++)
            {
                int length = lengths[i];
                byte[] prefix = new byte[5];
                rng.NextBytes(prefix);
                byte[] payload = new byte[length];
                rng.NextBytes(payload);

                byte[] file = BlobAppendTo(prefix, payload);
                Assert(file.Length == prefix.Length + length + QualityBlobCodec.TrailerOverhead,
                    "trailer size must be payload + 12 bytes, len=" + length + " got " + file.Length);
                for (int k = 0; k < prefix.Length; k++)
                {
                    Assert(file[k] == prefix[k], "prefix byte " + k + " changed, len=" + length);
                }

                using (var stream = new MemoryStream(file))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    stream.Position = prefix.Length;
                    byte[] read;
                    string reason;
                    Assert(QualityBlobCodec.TryRead(reader, out read, out reason),
                        "trailer must be readable, len=" + length + " reason=" + reason);
                    Assert(BlobBytesEqual(payload, read), "payload must round trip, len=" + length);
                    Assert(stream.Position == stream.Length, "reading a trailer must consume up to EOF");
                }
            }
        }

        private static void CheckSaveBlobRejects()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"version\":1,\"gameName\":\"a\",\"planets\":{}}");
            byte[] valid = BlobAppendTo(new byte[0], payload);

            // A vanilla save (or one written before v0.11.11) simply ends where the vanilla data ends.
            using (var stream = new MemoryStream())
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                Assert(!QualityBlobCodec.TryRead(reader, out read, out reason), "empty stream must not read");
                Assert(read == null, "failed read must return null payload");
                Assert(reason == "no trailing data", "empty stream reason must be 'no trailing data', got " + reason);
                Assert(stream.Position == 0, "failed read must restore the stream position");
            }

            // Anything that does not contain one complete frame is "not ours": the codec scans for a
            // self-consistent [length][payload][magic] frame instead of trusting the last 12 bytes.
            BlobExpectReject(BlobCut(valid, valid.Length - 1, 1), "no trailer found", "one magic byte missing");
            BlobExpectReject(BlobCut(valid, 4 + 3, 5), "no trailer found", "part of the payload missing");
            BlobExpectReject(BlobRebuild(0, payload), "no trailer found", "payloadLength = 0");
            BlobExpectReject(BlobRebuild(-5, payload), "no trailer found", "payloadLength < 0");
            BlobExpectReject(BlobRebuild(payload.Length + 100, payload), "no trailer found", "payloadLength beyond EOF");

            byte[] badMagic = (byte[])valid.Clone();
            badMagic[badMagic.Length - 1] = (byte)(badMagic[badMagic.Length - 1] ^ 0xFF);
            BlobExpectReject(badMagic, "no trailer found", "magic byte edited");

            // Junk after a complete frame no longer hides the frame: v0.12.1 recovery.
            using (var stream = new MemoryStream(BlobAppendRaw(valid, new byte[] { 1 })))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                Assert(QualityBlobCodec.TryRead(reader, out read, out reason),
                    "a frame followed by junk must still be found: " + reason);
                Assert(BlobBytesEqual(payload, read), "recovered payload after junk");
            }

            // Reading twice must not loop: the second read starts at EOF.
            using (var stream = new MemoryStream(valid))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                Assert(QualityBlobCodec.TryRead(reader, out read, out reason), "valid trailer must read");
                Assert(!QualityBlobCodec.TryRead(reader, out read, out reason), "second read must fail");
                Assert(reason == "no trailing data", "second read reason: " + reason);
            }
        }

        private static void CheckSaveBlobVanillaPrefix()
        {
            // A "vanilla prefix": the real header starts with "VFSAVE" + long fileSize + int version,
            // and the game back-fills fileSize after the export. The trailer must therefore be
            // invisible to the size check, i.e. the back-filled value has to include it.
            var rng = new Random(1111);
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("VFSAVE"));
                writer.Write((long)0); // placeholder, filled in below
                writer.Write(7);
                byte[] body = new byte[4096];
                rng.NextBytes(body);
                writer.Write(body);
            }

            int trailerStart = (int)stream.Position;
            byte[] payload = new byte[200 * 1024];
            rng.NextBytes(payload);
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                QualityBlobCodec.Append(writer, payload);
            }

            long total = stream.Length;
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                stream.Position = 6;
                writer.Write(total); // what GameSave.SaveCurrentGame does after data.Export
            }
            Assert(total == stream.Length, "file size must cover the trailer");

            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                stream.Position = 6;
                Assert(reader.ReadInt64() == total, "back-filled fileSize must include the trailer");
                stream.Position = trailerStart;
                byte[] read;
                string reason;
                Assert(QualityBlobCodec.TryRead(reader, out read, out reason), "trailer after vanilla data: " + reason);
                Assert(BlobBytesEqual(payload, read), "200 KB payload must round trip");
                Assert(stream.Position == stream.Length, "trailer read must end at EOF");

                // The vanilla prefix must still be readable: the trailer never moves existing bytes.
                stream.Position = 0;
                byte[] magic = reader.ReadBytes(6);
                Assert(Encoding.ASCII.GetString(magic) == "VFSAVE", "vanilla header must be intact");
                Assert(reader.ReadInt64() == total, "vanilla size field must be intact");
                Assert(reader.ReadInt32() == 7, "vanilla header version must be intact");
            }
        }

        private static void CheckSaveBlobDoubleAppend()
        {
            // Two of our frames back to back (as if the same .dsv had been exported twice): the newest
            // frame wins because the codec scans the tail backwards.
            var rng = new Random(4242);
            byte[] first = new byte[64];
            rng.NextBytes(first);
            byte[] second = new byte[128];
            rng.NextBytes(second);

            byte[] file = BlobAppendTo(new byte[] { 9, 9 }, first);
            int secondStart = file.Length;
            file = BlobAppendTo(file, second);

            using (var stream = new MemoryStream(file))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                stream.Position = 2;
                Assert(QualityBlobCodec.TryRead(reader, out read, out reason),
                    "the newest frame must be found: " + reason);
                Assert(BlobBytesEqual(second, read), "the last trailer must win");
                Assert(stream.Position == stream.Length, "reading the last trailer must end at EOF");

                stream.Position = secondStart;
                Assert(QualityBlobCodec.TryRead(reader, out read, out reason), "the last trailer must read: " + reason);
                Assert(BlobBytesEqual(second, read), "the last trailer must win again");
            }
        }

        /// <summary>
        /// v0.12.1: another mod (e.g. DSP-MolecularHub with its "MHUBTRL1" marker) appends its own
        /// [length][payload][magic] trailer to the same .dsv after ours. Our frame is then no longer at
        /// EOF and must still be found - otherwise the quality data would silently disappear.
        /// </summary>
        private static void CheckSaveBlobSharedTrailer()
        {
            byte[] vanilla = Encoding.ASCII.GetBytes("VFSAVE");
            byte[] ours = Encoding.UTF8.GetBytes("{\"version\":1,\"gameName\":\"a\",\"planets\":{}}");
            byte[] otherPayload = Encoding.UTF8.GetBytes("{\"hub\":1}abcd");

            byte[] file = BlobAppendTo(vanilla, ours);
            file = BlobAppendForeignFrame(file, otherPayload, Encoding.ASCII.GetBytes("MHUBTRL1"));
            file = BlobAppendRaw(file, new byte[] { 7, 7, 7 }); // a little junk after everything

            using (var stream = new MemoryStream(file))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                stream.Position = vanilla.Length;
                Assert(QualityBlobCodec.TryRead(reader, out read, out reason),
                    "our frame buried by another mod must still be found: " + reason);
                Assert(BlobBytesEqual(ours, read), "the payload must be ours, not the foreign one");
                Assert(stream.Position > vanilla.Length + ours.Length,
                    "the reader must stop at the end of our own frame");
            }

            // A foreign frame alone is not ours: nothing to load, quiet vanilla fallback.
            byte[] foreignOnly = BlobAppendForeignFrame(vanilla, otherPayload, Encoding.ASCII.GetBytes("MHUBTRL1"));
            using (var stream = new MemoryStream(foreignOnly))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                stream.Position = vanilla.Length;
                Assert(!QualityBlobCodec.TryRead(reader, out read, out reason),
                    "a foreign trailer must not be loaded as quality data");
                Assert(reason == "no trailer found", "foreign only reason: " + reason);
            }
        }

        private static byte[] BlobAppendForeignFrame(byte[] file, byte[] payload, byte[] foreignMagic)
        {
            var stream = new MemoryStream();
            if (file != null && file.Length > 0)
            {
                stream.Write(file, 0, file.Length);
            }
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Write(foreignMagic);
            }
            return stream.ToArray();
        }

        private static void CheckSaveBlobArguments()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                bool threw = false;
                try
                {
                    QualityBlobCodec.Append(writer, null);
                }
                catch (ArgumentException)
                {
                    threw = true;
                }
                Assert(threw, "null payload must throw ArgumentException");

                threw = false;
                try
                {
                    QualityBlobCodec.Append(writer, new byte[0]);
                }
                catch (ArgumentException)
                {
                    threw = true;
                }
                Assert(threw, "empty payload must throw ArgumentException");
                Assert(stream.Length == 0, "a rejected payload must not write anything");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static byte[] BlobAppendRaw(byte[] file, byte[] raw)
        {
            var stream = new MemoryStream();
            if (file != null && file.Length > 0)
            {
                stream.Write(file, 0, file.Length);
            }
            stream.Write(raw, 0, raw.Length);
            return stream.ToArray();
        }

        private static byte[] BlobAppendTo(byte[] first, byte[] payload)
        {
            var stream = new MemoryStream();
            if (first != null && first.Length > 0)
            {
                stream.Write(first, 0, first.Length);
            }
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                QualityBlobCodec.Append(writer, payload);
            }
            return stream.ToArray();
        }

        private static byte[] BlobCut(byte[] source, int offset, int count)
        {
            var result = new byte[source.Length - count];
            Array.Copy(source, 0, result, 0, offset);
            Array.Copy(source, offset + count, result, offset, source.Length - offset - count);
            return result;
        }

        /// <summary>Rebuild a trailer with a forged length field, keeping payload and magic.</summary>
        private static byte[] BlobRebuild(int declaredLength, byte[] payload)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(declaredLength);
                writer.Write(payload);
                writer.Write(QualityBlobCodec.Magic);
            }
            return stream.ToArray();
        }

        private static void BlobExpectReject(byte[] file, string expectedReason, string what)
        {
            using (var stream = new MemoryStream(file))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] read;
                string reason;
                Assert(!QualityBlobCodec.TryRead(reader, out read, out reason), what + ": must be rejected");
                Assert(read == null, what + ": payload must stay null");
                Assert(reason == expectedReason, what + ": expected reason '" + expectedReason + "', got '" + reason + "'");
                Assert(stream.Position == 0, what + ": rejected read must restore the stream position");
            }
        }

        private static bool BlobBytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
