using System;
using System.IO;

namespace DSPQualitySystem.Save
{
    /// <summary>
    /// Trailer codec for the quality block that is appended to the end of a vanilla .dsv file.
    /// Layout right after the last vanilla byte: [int32 payloadLength][payload UTF-8 JSON][8 B magic].
    /// The length field is deliberately written as an int32 and the magic sits at the END of the
    /// block: a vanilla reader stops reading before this block, so it never has to understand it,
    /// while our reader can locate and verify the block by seeking from the end of the stream
    /// instead of having to know where the vanilla data ends.
    /// This file must stay free of BepInEx/Unity references: the offline test project links it.
    /// </summary>
    public static class QualityBlobCodec
    {
        /// <summary>ASCII "DSPQSQ1\0": a marker that will not appear by accident in our own JSON.</summary>
        public static readonly byte[] Magic = { 0x44, 0x53, 0x50, 0x51, 0x53, 0x51, 0x31, 0x00 };

        private const int LengthFieldSize = 4;

        /// <summary>Bytes a trailer adds on top of the payload (length field + magic).</summary>
        public static int TrailerOverhead
        {
            get { return LengthFieldSize + Magic.Length; }
        }

        /// <summary>
        /// Append one trailer at the writer's current position. Empty payloads are rejected here
        /// because a zero-length block cannot be distinguished from a corrupt one on read; callers
        /// that have nothing to say must still write a valid (tiny) JSON document.
        /// </summary>
        public static void Append(BinaryWriter writer, byte[] payload)
        {
            if (writer == null)
            {
                throw new ArgumentNullException("writer");
            }
            if (payload == null || payload.Length == 0)
            {
                throw new ArgumentException("payload must not be null or empty", "payload");
            }

            // Little-endian int32 (BinaryWriter) so the trailer survives a save/load round trip on
            // any platform the game itself supports.
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Write(Magic);
            // Deliberately no Flush/Dispose here: the writer and its FileStream are owned by
            // GameSave.SaveCurrentGame, which back-fills offset 6 (the file size) from
            // fileStream.Position right after GameData.Export returns. Leaving both alone keeps that
            // bookkeeping in the vanilla code's hands - and the stream's own length already covers
            // what we just wrote.
        }

        /// <summary>
        /// Read the newest trailer that starts at (or after) the reader's current position. Success
        /// means a complete [length][payload][magic] frame was found somewhere in the wake of the
        /// vanilla data and the stream position is left at the end of the frame.
        /// The frame is located by scanning the last <see cref="MaxSearchBytes"/> bytes backwards for
        /// the payload anchor ("{&quot;version&quot;:"), then validating the length field and the magic that
        /// follows the payload. Scanning (instead of only looking at EOF) is what lets us coexist
        /// with other mods that append their own trailer to the same .dsv after ours.
        /// On failure the stream position is restored, so a caller can safely fall back to treating
        /// the data as a plain vanilla save.
        /// </summary>
        public static bool TryRead(BinaryReader reader, out byte[] payload)
        {
            string reason;
            return TryRead(reader, out payload, out reason);
        }

        /// <summary>
        /// Same as <see cref="TryRead(BinaryReader, out byte[])"/> but also reports why a trailer was
        /// rejected, so the mod log can tell "old save without quality data" (expected, quiet) apart
        /// from "something wrote garbage here" (worth a warning).
        /// </summary>
        public static bool TryRead(BinaryReader reader, out byte[] payload, out string reason)
        {
            payload = null;

            Stream stream = reader != null ? reader.BaseStream : null;
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                // Without Length/Position any size check would be a guess, and guessing here means
                // consuming vanilla data that the game still needs.
                reason = "stream cannot seek";
                return false;
            }

            long start;
            long length;
            try
            {
                start = stream.Position;
                length = stream.Length;
            }
            catch (Exception)
            {
                reason = "stream cannot seek";
                return false;
            }

            long remaining = length - start;
            if (remaining <= 0)
            {
                reason = "no trailing data";
                return false;
            }
            if (remaining < TrailerOverhead)
            {
                reason = "truncated";
                return false;
            }

            long windowStart = length - remaining;
            if (length - windowStart > MaxSearchBytes)
            {
                windowStart = length - MaxSearchBytes;
            }

            byte[] window;
            try
            {
                stream.Position = windowStart;
                int windowLength = (int)(length - windowStart);
                window = new byte[windowLength];
                int read = 0;
                while (read < windowLength)
                {
                    int got = stream.Read(window, read, windowLength - read);
                    if (got <= 0)
                    {
                        break;
                    }
                    read += got;
                }
                if (read != windowLength)
                {
                    Restore(stream, start);
                    reason = "truncated";
                    return false;
                }
            }
            catch (Exception)
            {
                Restore(stream, start);
                reason = "truncated";
                return false;
            }

            // Walk backwards so the newest frame wins when several of ours were appended over time.
            int payloadStart;
            int payloadLength;
            int frameEnd = FindFrame(window, out payloadStart, out payloadLength);
            if (frameEnd > 0)
            {
                payload = new byte[payloadLength];
                Buffer.BlockCopy(window, payloadStart, payload, 0, payloadLength);
                try
                {
                    stream.Position = windowStart + frameEnd;
                }
                catch (Exception)
                {
                    // Position is informative only; the payload above is already validated.
                }
                reason = "ok";
                return true;
            }

            Restore(stream, start);
            reason = "no trailer found";
            return false;
        }

        /// <summary>
        /// Number of trailing bytes searched for a frame. Our payload is a few hundred bytes for a
        /// normal save and grows with the number of recorded entities, so a generous window keeps us
        /// correct while still bounding the work done on a 24 MB save.
        /// </summary>
        private const int MaxSearchBytes = 4 << 20;

        /// <summary>Position of the newest complete frame in the window, or -1.</summary>
        /// <remarks>
        /// Frames are found by scanning the window backwards for our magic (a 4 MB scan of a byte
        /// comparison is cheap and only happens once per load) and then walking backwards from each
        /// candidate magic to the length field that describes it. That is what makes the block
        /// readable even when another mod appended its own trailer after ours.
        /// </remarks>
        private static int FindFrame(byte[] window, out int payloadStart, out int payloadLength)
        {
            payloadStart = -1;
            payloadLength = 0;

            int magicLength = Magic.Length;
            for (int magicStart = window.Length - magicLength; magicStart >= LengthFieldSize; magicStart--)
            {
                if (window[magicStart] != Magic[0])
                {
                    continue;
                }
                if (!SameBytes(window, magicStart, Magic))
                {
                    continue;
                }

                int frameStart = magicStart - LengthFieldSize;
                for (; frameStart >= 0; frameStart--)
                {
                    int declared = BitConverter.ToInt32(window, frameStart);
                    if (declared <= 0)
                    {
                        continue;
                    }
                    int actual = magicStart - (frameStart + LengthFieldSize);
                    if (declared != actual)
                    {
                        continue;
                    }

                    payloadStart = frameStart + LengthFieldSize;
                    payloadLength = declared;
                    return magicStart + magicLength;
                }
            }

            return -1;
        }

        private static bool SameBytes(byte[] window, int position, byte[] expected)
        {
            if (position < 0 || position + expected.Length > window.Length)
            {
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (window[position + i] != expected[i])
                {
                    return false;
                }
            }
            return true;
        }


        private static void Restore(Stream stream, long position)
        {
            try
            {
                stream.Position = position;
            }
            catch (Exception)
            {
                // A stream that cannot seek back is already the "nothing we can do" case; the caller
                // only ever calls this after the pre-checks proved the stream is seekable.
            }
        }
    }
}
