// Carrier rebuild: overlays changed mvp/ payload entries onto the preserved baseline carrier using the
// package's own TIFF-GIF-HOLO/4 codec (CarrierCodec archive + page encoding). Frame zero (cover)
// of both TIFF and GIF is preserved exactly, as the 3.0.2 build did. Compiled together with the
// unmodified harness source so the internal codec is used rather than a reimplementation.
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging; using System.IO;
using System.Linq; using System.Reflection; using System.Security.Cryptography; using System.Text;
namespace TIFFHolo
{
    public static class ReleaseCarrierBuilder
    {
        static MethodInfo M(string n) { MethodInfo m = typeof(CarrierCodec).GetMethod(n, BindingFlags.NonPublic | BindingFlags.Static); if (m == null) throw new MissingMethodException(n); return m; }
        static byte[] H(byte[] b) { using (SHA256 s = SHA256.Create()) return s.ComputeHash(b); }
        static string X(byte[] b) { return Program.Hex(b); }
        static void P16(byte[] b, int p, ushort v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 2); }
        static void P32(byte[] b, int p, int v) { Buffer.BlockCopy(BitConverter.GetBytes((uint)v), 0, b, p, 4); }
        static void P64(byte[] b, int p, ulong v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 8); }
        static int GifCoverPrefixLength(byte[] g)
        {
            int p = 13; if ((g[10] & 0x80) != 0) p += 3 * (1 << ((g[10] & 7) + 1));
            while (p < g.Length)
            {
                byte b = g[p];
                if (b == 0x21) { p += 2; while (g[p] != 0) p += g[p] + 1; p++; }
                else if (b == 0x2C) { int flags = g[p + 9]; p += 10; if ((flags & 0x80) != 0) p += 3 * (1 << ((flags & 7) + 1)); p++; while (g[p] != 0) p += g[p] + 1; p++; return p; }
                else throw new InvalidDataException("Unexpected GIF block before the cover frame");
            }
            throw new InvalidDataException("GIF cover frame not found");
        }
        static string PixelHash(Image img, int frame)
        {
            FrameDimension dim = new FrameDimension(img.FrameDimensionsList[0]); img.SelectActiveFrame(dim, frame);
            using (Bitmap src = new Bitmap(img)) using (Bitmap bmp = src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format24bppRgb))
            {
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try { int stride = Math.Abs(d.Stride); byte[] all = new byte[bmp.Width * 3 * bmp.Height]; byte[] row = new byte[stride];
                    for (int y = 0; y < bmp.Height; y++) { System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), row, 0, stride); Buffer.BlockCopy(row, 0, all, y * bmp.Width * 3, bmp.Width * 3); }
                    return X(H(all)); }
                finally { bmp.UnlockBits(d); }
            }
        }
        public static string Run(string baseTiff, string baseGif, string tree, string immutable, string outTiff, string outGif, string newEntryBank)
        {
            StringBuilder log = new StringBuilder();
            Carrier c = CarrierCodec.Load(baseTiff, true); Carrier cg = CarrierCodec.Load(baseGif, true);
            if (!c.StreamHash.SequenceEqual(cg.StreamHash)) throw new InvalidDataException("Baseline TIFF and GIF carriers differ");
            if (File.Exists(outTiff) || File.Exists(outGif)) throw new IOException("Carrier build requires new output paths");
            Dictionary<string, byte[]> want = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string mvp = Path.Combine(tree, "mvp");
            foreach (string f in Directory.GetFiles(mvp, "*", SearchOption.AllDirectories))
            {
                string rel = "mvp/" + f.Substring(mvp.Length + 1).Replace('\\', '/');
                if (rel == "mvp/_HOLOGRAM_MOUNT_PROVENANCE.txt" || rel.IndexOf("/__pycache__/", StringComparison.Ordinal) >= 0 || rel.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase)) continue;
                want[rel] = File.ReadAllBytes(f);
            }
            want["integrity/immutable.json"] = File.ReadAllBytes(immutable);
            int unchanged = 0, overlaid = 0, added = 0; List<object> manifest = new List<object>();
            foreach (string name in c.Entries.Keys.ToList()) if (!want.ContainsKey(name)) throw new InvalidDataException("Entry missing from updated source tree: " + name);
            foreach (KeyValuePair<string, byte[]> kv in want.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                CarrierEntry e; string state;
                if (c.Entries.TryGetValue(kv.Key, out e)) { if (e.Data.SequenceEqual(kv.Value)) { unchanged++; state = "unchanged"; } else { e.Data = kv.Value; overlaid++; state = "overlay"; } }
                else { e = new CarrierEntry { Name = kv.Key, Bank = newEntryBank, Data = kv.Value }; c.Entries.Add(kv.Key, e); added++; state = "added"; }
                if (state != "unchanged") log.AppendLine(state + "\t" + e.Bank + "\t" + kv.Value.Length + "\t" + kv.Key);
            }
            CarrierCodec.VerifyImmutable(c);
            byte[] stream = (byte[])M("BuildArchive").Invoke(null, new object[] { c }); byte[] sh = H(stream);
            int pageSize = CarrierCodec.PageSize, header = CarrierCodec.HeaderSize, chunk = CarrierCodec.ChunkMax;
            int count = (stream.Length + chunk - 1) / chunk; long tick = c.TickHeader;
            if (count < 1 || count > UInt16.MaxValue) throw new InvalidDataException("Temporal page count exceeds format limit");
            Func<int, int, long, byte> mask = (Func<int, int, long, byte>)Delegate.CreateDelegate(typeof(Func<int, int, long, byte>), M("PhaseMask"));
            List<byte[]> frames = new List<byte[]>(); byte[] prev = new byte[32]; byte[] spec = H(Encoding.ASCII.GetBytes("TIFF-GIF-HOLO/4 triangle-xor-v1"));
            for (int idx = 0; idx < count; idx++)
            {
                int off = idx * chunk, plen = Math.Min(chunk, stream.Length - off); byte[] payload = new byte[plen]; Buffer.BlockCopy(stream, off, payload, 0, plen);
                byte[] s = new byte[pageSize * pageSize]; Buffer.BlockCopy(new byte[] { (byte)'T', (byte)'G', (byte)'P', (byte)'G', (byte)'4', 0, 0, 0 }, 0, s, 0, 8);
                P16(s, 8, 4); P16(s, 10, (ushort)idx); P16(s, 12, (ushort)count); P16(s, 14, 1); P32(s, 16, pageSize); P32(s, 20, stream.Length); P32(s, 24, off); P32(s, 28, plen);
                Buffer.BlockCopy(sh, 0, s, 32, 32); byte[] ph = H(payload); Buffer.BlockCopy(ph, 0, s, 64, 32);
                byte[] cid = c.CartridgeId == null || c.CartridgeId.Length != 16 ? new byte[16] : c.CartridgeId; Buffer.BlockCopy(cid, 0, s, 96, 16); P64(s, 112, (ulong)Math.Max(0, tick));
                Buffer.BlockCopy(Encoding.ASCII.GetBytes("TGIFv4\0\0"), 0, s, 120, 8); Buffer.BlockCopy(prev, 0, s, 128, 32); Buffer.BlockCopy(spec, 0, s, 160, 32);
                byte[] tag = Encoding.ASCII.GetBytes("TIFF-GIF-HOLO/4"); Buffer.BlockCopy(tag, 0, s, 192, tag.Length);
                for (int pos = header; pos < s.Length; pos++) { byte raw = pos < header + plen ? payload[pos - header] : (byte)0; s[pos] = (byte)(raw ^ mask(pos, idx, tick)); }
                frames.Add(s); prev = ph;
            }
            // TIFF: original frame-zero cover pixels + newly encoded data pages, LZW like CarrierCodec.
            string coverTiffHash, coverTiffHashNew, coverGifHash, coverGifHashNew;
            List<Bitmap> pages = new List<Bitmap>();
            byte[] baseTiffBytes = File.ReadAllBytes(baseTiff);
            using (MemoryStream ms = new MemoryStream(baseTiffBytes, false)) using (Image img = Image.FromStream(ms, true, true))
            {
                coverTiffHash = PixelHash(img, 0); FrameDimension dim = new FrameDimension(img.FrameDimensionsList[0]); img.SelectActiveFrame(dim, 0);
                using (Bitmap src = new Bitmap(img)) pages.Add(src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format24bppRgb));
            }
            MethodInfo toBmp = M("SymbolsToBitmap"); foreach (byte[] f in frames) pages.Add((Bitmap)toBmp.Invoke(null, new object[] { f }));
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(x => x.MimeType == "image/tiff");
            string tmp = outTiff + ".write"; if (File.Exists(tmp)) File.Delete(tmp);
            try { M("SaveTiffPages").Invoke(null, new object[] { tmp, pages, codec, EncoderValue.CompressionLZW }); } finally { foreach (Bitmap b in pages) b.Dispose(); }
            File.Move(tmp, outTiff);
            // GIF: exact baseline header/palette/loop/cover-frame bytes, then data frames encoded like CarrierCodec.
            byte[] g0 = File.ReadAllBytes(baseGif); int prefix = GifCoverPrefixLength(g0); MethodInfo lzw = M("LiteralGifLzw");
            using (MemoryStream ms = new MemoryStream(g0, false)) using (Image img = Image.FromStream(ms, true, true)) coverGifHash = PixelHash(img, 0);
            string tmpg = outGif + ".write";
            using (FileStream fs = File.Create(tmpg)) using (BinaryWriter wr = new BinaryWriter(fs))
            {
                wr.Write(g0, 0, prefix);
                foreach (byte[] f in frames)
                {
                    wr.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00 }); wr.Write((ushort)11); wr.Write((byte)0); wr.Write((byte)0);
                    wr.Write((byte)0x2C); wr.Write((ushort)0); wr.Write((ushort)0); wr.Write((ushort)pageSize); wr.Write((ushort)pageSize); wr.Write((byte)0);
                    wr.Write((byte)8); byte[] comp = (byte[])lzw.Invoke(null, new object[] { f });
                    int p = 0; while (p < comp.Length) { int n = Math.Min(255, comp.Length - p); wr.Write((byte)n); wr.Write(comp, p, n); p += n; } wr.Write((byte)0);
                }
                wr.Write((byte)0x3B);
            }
            File.Move(tmpg, outGif);
            // Verify both with the original reader and compare every payload byte.
            Carrier vt = CarrierCodec.Load(outTiff, true), vg = CarrierCodec.Load(outGif, true);
            bool same = vt.StreamHash.SequenceEqual(vg.StreamHash) && vt.StreamHash.SequenceEqual(sh) && vt.Entries.Count == want.Count && vg.Entries.Count == want.Count;
            foreach (KeyValuePair<string, byte[]> kv in want) { same &= vt.Entries[kv.Key].Data.SequenceEqual(kv.Value) && vg.Entries[kv.Key].Data.SequenceEqual(kv.Value); }
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(outTiff), false)) using (Image img = Image.FromStream(ms, true, true)) coverTiffHashNew = PixelHash(img, 0);
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(outGif), false)) using (Image img = Image.FromStream(ms, true, true)) coverGifHashNew = PixelHash(img, 0);
            byte[] g1 = File.ReadAllBytes(outGif); bool prefixSame = true; for (int i = 0; i < prefix; i++) if (g1[i] != g0[i]) { prefixSame = false; break; }
            bool checkpointSame = vt.TickHeader == c.TickHeader && vg.TickHeader == c.TickHeader && vt.CartridgeId.SequenceEqual(c.CartridgeId) && vg.CartridgeId.SequenceEqual(c.CartridgeId);
            if (!same || !checkpointSame || coverTiffHash != coverTiffHashNew || coverGifHash != coverGifHashNew || !prefixSame)
                throw new InvalidDataException("Carrier round-trip, checkpoint, or cover preservation failed");
            var r = new Dictionary<string, object> {
                {"entries", vt.Entries.Count}, {"dataPages", vt.DataPageCount}, {"tiffFrames", vt.FrameCount}, {"gifFrames", vg.FrameCount},
                {"streamSha256", X(vt.StreamHash)}, {"baselineStreamSha256", X(c.StreamHash)}, {"tiffSha256", X(CarrierCodec.FileHash(outTiff))}, {"gifSha256", X(CarrierCodec.FileHash(outGif))},
                {"immutableSha256", X(H(want["integrity/immutable.json"]))},
                {"bothCarriersVerifiedWithHardenedReader", true}, {"allPayloadBytesRoundTripIdentically", same}, {"immutablePayloadBankVerified", true},
                {"checkpointHeaderUnchanged", checkpointSame},
                {"unchangedPayloadEntries", unchanged}, {"overlaidPayloadEntries", overlaid}, {"addedPayloadEntries", added},
                {"tiffCoverDecodedPixelsUnchanged", coverTiffHash == coverTiffHashNew}, {"gifCoverDecodedPixelsUnchanged", coverGifHash == coverGifHashNew},
                {"tiffCoverPixelSha256", coverTiffHashNew}, {"gifCoverPixelSha256", coverGifHashNew}, {"gifCoverPrefixBytes", prefix}, {"gifCoverPrefixBytesUnchanged", prefixSame}
            };
            return JsonUtil.Text(r) + "\n" + log.ToString();
        }
    }
}
