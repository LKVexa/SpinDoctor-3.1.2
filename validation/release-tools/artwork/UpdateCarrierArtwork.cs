// SPDX-License-Identifier: GPL-3.0-or-later
// SpinDoctor 3.1.2 - Quantum Virtual Terminal
// Copyright (C) 2026 RUSSELL PHILIP SMITHSON <russellsmithson77@outlook.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
// Artwork-only update. Compile with the unmodified carrier harness.
// Encoded data frames and the executable archive are preserved, never re-rendered.
namespace TIFFHolo
{
    public static class CarrierArtworkUpdate
    {
        static byte[] Hash(byte[] b) { using (var s = System.Security.Cryptography.SHA256.Create()) return s.ComputeHash(b); }
        static string Hex(byte[] b) { return Program.Hex(b); }
        static uint U32(byte[] b, int p) { return BitConverter.ToUInt32(b, p); }
        static ushort U16(byte[] b, int p) { return BitConverter.ToUInt16(b, p); }
        static void Put32(byte[] b, int p, uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 4); }
        static int[] GifFirstFrame(byte[] b)
        {
            if (Encoding.ASCII.GetString(b, 0, 6) != "GIF89a") throw new InvalidDataException("GIF89a required");
            int p = 13, global = 13, globalColors = (b[10] & 128) != 0 ? 1 << ((b[10] & 7) + 1) : 0;
            p += globalColors * 3;
            while (p < b.Length)
            {
                if (b[p] == 0x21) { p += 2; while (b[p] != 0) p += b[p] + 1; p++; }
                else if (b[p] == 0x2c)
                {
                    int start = p, flags = b[p + 9], colors = (flags & 128) != 0 ? 1 << ((flags & 7) + 1) : globalColors;
                    int palette = (flags & 128) != 0 ? p + 10 : global;
                    if (U16(b, p + 1) != 0 || U16(b, p + 3) != 0 || U16(b, p + 5) != 512 || U16(b, p + 7) != 512 || (flags & 64) != 0 || colors != 256)
                        throw new InvalidDataException("Expected noninterlaced 512x512 cover with 256 colors");
                    p += 10 + ((flags & 128) != 0 ? colors * 3 : 0); int data = p; p++;
                    while (b[p] != 0) p += b[p] + 1; p++;
                    return new int[] { start, data, p, palette, colors };
                }
                else throw new InvalidDataException("Unexpected GIF cover block");
            }
            throw new InvalidDataException("Cover missing");
        }
        static Bitmap BitmapFromBytes(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes, false)) using (var im = Image.FromStream(ms, true, true))
            { im.SelectActiveFrame(new FrameDimension(im.FrameDimensionsList[0]), 0); using (var src = new Bitmap(im)) return src.Clone(new Rectangle(0, 0, 512, 512), PixelFormat.Format24bppRgb); }
        }
        static byte[] Pixels(Bitmap b)
        {
            byte[] result = new byte[512 * 512 * 3]; int k = 0;
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++) { Color c = b.GetPixel(x, y); result[k++] = c.R; result[k++] = c.G; result[k++] = c.B; }
            return result;
        }
        static byte[] MatchPalette(Bitmap b, byte[] gif, int palette)
        {
            byte[] result = new byte[512 * 512]; var cache = new Dictionary<int, byte>();
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            {
                Color c = b.GetPixel(x, y); int rgb = c.ToArgb(); byte index;
                if (!cache.TryGetValue(rgb, out index))
                {
                    int best = int.MaxValue, choice = 0;
                    for (int i = 0; i < 256; i++) { int r = c.R - gif[palette + i * 3], g = c.G - gif[palette + i * 3 + 1], blue = c.B - gif[palette + i * 3 + 2]; int d = r * r + g * g + blue * blue; if (d < best) { best = d; choice = i; if (d == 0) break; } }
                    index = (byte)choice; cache[rgb] = index;
                }
                result[y * 512 + x] = index;
            }
            return result;
        }
        static Bitmap PaletteBitmap(byte[] indices, byte[] gif, int palette)
        {
            var b = new Bitmap(512, 512, PixelFormat.Format24bppRgb);
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++) { int p = palette + indices[y * 512 + x] * 3; b.SetPixel(x, y, Color.FromArgb(gif[p], gif[p + 1], gif[p + 2])); }
            return b;
        }
        static byte[] ReplaceGifCover(byte[] gif, int[] f, byte[] indices)
        {
            var lzw = typeof(CarrierCodec).GetMethod("LiteralGifLzw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            byte[] compressed = (byte[])lzw.Invoke(null, new object[] { indices });
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms))
            {
                w.Write(gif, 0, f[1]); w.Write((byte)8);
                for (int p = 0; p < compressed.Length; ) { int n = Math.Min(255, compressed.Length - p); w.Write((byte)n); w.Write(compressed, p, n); p += n; }
                w.Write((byte)0); w.Write(gif, f[2], gif.Length - f[2]); return ms.ToArray();
            }
        }
        // Append a relocated single-page cover IFD and redirect only the TIFF header.
        // The old data IFDs, strip bytes, metadata and link chain remain byte-identical.
        static byte[] ReplaceTiffCover(byte[] original, Bitmap cover)
        {
            if (original[0] != 'I' || original[1] != 'I' || U16(original, 2) != 42) throw new InvalidDataException("Classic little-endian TIFF required");
            byte[] page;
            using (var ms = new MemoryStream())
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(x => x.MimeType == "image/tiff");
                using (var parameters = new EncoderParameters(1)) { parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression, (long)EncoderValue.CompressionLZW); cover.Save(ms, codec, parameters); }
                page = ms.ToArray();
            }
            if (page[0] != 'I' || page[1] != 'I' || U16(page, 2) != 42) throw new InvalidDataException("Unexpected cover TIFF encoding");
            int oldIfd = checked((int)U32(original, 4)), oldCount = U16(original, oldIfd);
            uint dataIfd = U32(original, oldIfd + 2 + oldCount * 12);
            if (dataIfd == 0) throw new InvalidDataException("TIFF carrier has no data frames");
            int delta = (original.Length + 1) & ~1, ifd = checked((int)U32(page, 4)), count = U16(page, ifd);
            for (int i = 0; i < count; i++)
            {
                int p = ifd + 2 + i * 12, tag = U16(page, p), type = U16(page, p + 2); uint n = U32(page, p + 4);
                int[] sizes = { 0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8, 4, 8 };
                if (type < 1 || type >= sizes.Length || tag == 330 || tag == 34665 || tag == 34853 || tag == 40965) throw new InvalidDataException("Unsupported cover TIFF pointer/type");
                bool external = (ulong)n * (uint)sizes[type] > 4; uint offset = U32(page, p + 8);
                if (tag == 273)
                {
                    if (type != 4) throw new InvalidDataException("LONG strip offsets required");
                    for (uint j = 0; j < n; j++) { int entry = external ? checked((int)(offset + j * 4)) : p + 8; Put32(page, entry, checked(U32(page, entry) + (uint)delta)); }
                }
                if (external) Put32(page, p + 8, checked(offset + (uint)delta));
            }
            Put32(page, ifd + 2 + count * 12, dataIfd);
            byte[] result = new byte[delta + page.Length]; Buffer.BlockCopy(original, 0, result, 0, original.Length); Buffer.BlockCopy(page, 0, result, delta, page.Length);
            Put32(result, 4, checked((uint)(ifd + delta))); return result;
        }
        static void VerifyPayload(Carrier before, Carrier after)
        {
            if (!before.StreamHash.SequenceEqual(after.StreamHash) || before.Entries.Count != after.Entries.Count || before.DataPageCount != after.DataPageCount || before.FrameCount != after.FrameCount || before.TickHeader != after.TickHeader || !before.CartridgeId.SequenceEqual(after.CartridgeId)) throw new InvalidDataException("Carrier metadata/payload changed");
            foreach (var e in before.Entries) if (!after.Entries[e.Key].Data.SequenceEqual(e.Value.Data)) throw new InvalidDataException("Payload entry changed: " + e.Key);
        }
        public static string Run(string input, string output, string version)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(version ?? "", "^[0-9]+\\.[0-9]+\\.[0-9]+$")) throw new ArgumentException("A numeric release version is required");
            Directory.CreateDirectory(output); string stem = "Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE";
            byte[] gif = File.ReadAllBytes(Path.Combine(input, stem + ".gif")), tiff = File.ReadAllBytes(Path.Combine(input, stem + ".tiff"));
            var beforeGif = CarrierCodec.Load(Path.Combine(input, stem + ".gif"), true); var beforeTiff = CarrierCodec.Load(Path.Combine(input, stem + ".tiff"), true);
            VerifyPayload(beforeGif, beforeTiff); int[] first = GifFirstFrame(gif);
            using (var cover = BitmapFromBytes(gif))
            {
                byte[] oldPixels = Pixels(cover);
                // Retain the original title design, replacing only its version footer.
                for (int y = 475; y < 492; y++) { Color background = cover.GetPixel(507, y); for (int x = 458; x < 499; x++) cover.SetPixel(x, y, background); }
                using (var graphics = Graphics.FromImage(cover)) using (var font = new Font("Arial", 8.25f, FontStyle.Bold, GraphicsUnit.Point))
                { graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; float left = 492f - graphics.MeasureString(version, font, 512, StringFormat.GenericTypographic).Width; graphics.DrawString(version, font, Brushes.White, left, 476f, StringFormat.GenericTypographic); }
                byte[] indices = MatchPalette(cover, gif, first[3]);
                using (var quantized = PaletteBitmap(indices, gif, first[3]))
                {
                    byte[] expected = Pixels(quantized); bool outsideUnchanged = true;
                    for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++) if (x < 458 || x >= 499 || y < 475 || y >= 492) for (int k = 0; k < 3; k++) if (expected[(y * 512 + x) * 3 + k] != oldPixels[(y * 512 + x) * 3 + k]) outsideUnchanged = false;
                    if (!outsideUnchanged) throw new InvalidDataException("Cover changed outside version footer");
                    byte[] updatedGif = ReplaceGifCover(gif, first, indices), updatedTiff = ReplaceTiffCover(tiff, quantized);
                    string gp = Path.Combine(output, stem + ".gif"), tp = Path.Combine(output, stem + ".tiff");
                    if (File.Exists(gp) || File.Exists(tp)) throw new IOException("Fresh output paths required");
                    File.WriteAllBytes(gp, updatedGif); File.WriteAllBytes(tp, updatedTiff);
                    var afterGif = CarrierCodec.Load(gp, true); var afterTiff = CarrierCodec.Load(tp, true); VerifyPayload(beforeGif, afterGif); VerifyPayload(beforeTiff, afterTiff);
                    using (var g = BitmapFromBytes(updatedGif)) using (var t = BitmapFromBytes(updatedTiff)) if (!Pixels(g).SequenceEqual(expected) || !Pixels(t).SequenceEqual(expected)) throw new InvalidDataException("Cover pixels do not match");
                    int[] newFirst = GifFirstFrame(updatedGif); bool gifTail = gif.Skip(first[2]).SequenceEqual(updatedGif.Skip(newFirst[2]));
                    bool tiffBody = tiff.Take(4).SequenceEqual(updatedTiff.Take(4)) && tiff.Skip(8).SequenceEqual(updatedTiff.Skip(8).Take(tiff.Length - 8));
                    if (!gifTail || !tiffBody) throw new InvalidDataException("Original encoded data frames changed");
                    quantized.Save(Path.Combine(output, "SpinDoctor-" + version + "-Title-Card.png"), ImageFormat.Png);
                    return JsonUtil.Text(new Dictionary<string, object> {
                        {"status", "PASS"}, {"version", version}, {"entries", afterGif.Entries.Count}, {"dataPages", afterGif.DataPageCount}, {"gifFrames", afterGif.FrameCount}, {"tiffFrames", afterTiff.FrameCount},
                        {"streamSha256", Hex(afterGif.StreamHash)}, {"oldGifSha256", Hex(Hash(gif))}, {"oldTiffSha256", Hex(Hash(tiff))}, {"gifSha256", Hex(Hash(updatedGif))}, {"tiffSha256", Hex(Hash(updatedTiff))},
                        {"coverPixelRgbSha256", Hex(Hash(expected))}, {"gifArtworkOutsideVersionFooterUnchanged", outsideUnchanged}, {"matchingCoverDecodedPixels", true}, {"payloadStreamUnchanged", true},
                        {"allPayloadBytesRoundTripIdentically", true}, {"bothCarriersVerifiedWithHardenedReader", true}, {"immutablePayloadBankVerified", true}, {"checkpointHeaderUnchanged", true},
                        {"gifDataFrameBytesUnchanged", gifTail}, {"tiffOriginalBytesExceptFirstIfdPointerUnchanged", tiffBody}, {"gifLoopAndFrameDelaysUnchanged", true}
                    });
                }
            }
        }
    }
}
