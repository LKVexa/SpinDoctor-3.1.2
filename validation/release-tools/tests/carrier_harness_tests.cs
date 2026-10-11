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
namespace TIFFHolo
{
    public static class CarrierAuditTests
    {
        private static int checks;
        private static void Check(bool value, string name) { if (!value) throw new System.Exception(name); checks++; System.Console.WriteLine("PASS " + name); }
        private static object Call(string name, params object[] args)
        {
            try { return typeof(CarrierCodec).GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, args); }
            catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException; }
        }
        private static void Rejected(System.Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (System.IO.InvalidDataException) { rejected = true; } catch (System.IO.EndOfStreamException) { rejected = true; } catch (System.Text.DecoderFallbackException) { rejected = true; }
            Check(rejected, name);
        }
        private static byte[] Archive(string[] names, uint flags)
        {
            using (var ms = new System.IO.MemoryStream()) using (var writer = new System.IO.BinaryWriter(ms))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("THARCH3\0")); writer.Write((uint)names.Length);
                foreach (string name in names)
                {
                    byte[] nb = System.Text.Encoding.UTF8.GetBytes(name); byte[] data = new byte[] { 1 };
                    writer.Write((ushort)nb.Length); writer.Write((ushort)1); writer.Write(flags); writer.Write((ulong)1); writer.Write((ulong)1);
                    using (var hash = System.Security.Cryptography.SHA256.Create()) writer.Write(hash.ComputeHash(data));
                    writer.Write(nb); writer.Write((byte)'B'); writer.Write(data);
                }
                return ms.ToArray();
            }
        }
        public static void Run(string source, string scratch)
        {
            System.IO.Directory.CreateDirectory(scratch);
            Carrier valid = CarrierCodec.Load(source, true); Check(valid.Entries.Count > 200, "existing carrier verification");
            Check(CarrierMount.ValidateMvp(valid).StartsWith("mvp/", System.StringComparison.Ordinal), "MVP smoke validates entrypoint and native sources");
            CarrierEntry metadata = valid.Get("mvp/meta/ENTRYPOINT.json"); byte[] originalMetadata = metadata.Data;
            metadata.Data = System.Text.Encoding.UTF8.GetBytes("{\"entrypoint\":\"mvp/missing.cmd\"}");
            Rejected(delegate { CarrierMount.ValidateMvp(valid); }, "MVP smoke rejects missing entrypoint");
            metadata.Data = System.Text.Encoding.UTF8.GetBytes("{\"entrypoint\":\"mvp/README.md\"}");
            Rejected(delegate { CarrierMount.ValidateMvp(valid); }, "MVP smoke rejects nonlaunchable entrypoint");
            metadata.Data = originalMetadata;
            foreach (string path in new string[] { "mvp/../escape", "mvp/.. ", "mvp/CON.txt", "mvp/COM\u00b9.txt", "mvp/NUL .txt", "mvp/file:secret", "mvp/a\\b", "mvp/a.", "/absolute", "mvp//x", "mvp/\u0000bad" })
                Rejected(delegate { CarrierMount.ValidateEntryName(path); }, "unsafe path " + path.Replace('\0', '_'));
            CarrierMount.ValidateEntryName("mvp/Quantum Computation Equations Overview.txt"); Check(true, "legitimate spaced path");
            Rejected(delegate { Call("ParseArchive", new byte[8]); }, "truncated archive count");
            Rejected(delegate { Call("ParseArchive", Archive(new string[] { "mvp/file" }, 2)); }, "unknown archive flags");
            Rejected(delegate { Call("ParseArchive", Archive(new string[] { "mvp/A", "mvp/a" }, 0)); }, "Windows case collision");
            byte[] gz = (byte[])Call("Gzip", new byte[1024 * 1024]);
            Rejected(delegate { Call("Gunzip", gz, 1); }, "inflate cannot exceed declared size");
            Rejected(delegate { Call("Gunzip", gz, 0); }, "nonempty inflate with zero declaration");
            Check(((byte[])Call("Gunzip", gz, 1024 * 1024)).Length == 1024 * 1024, "valid bounded inflate");
            Carrier bad = new Carrier(); bad.Entries.Add("bad", new CarrierEntry { Name = "mvp/bad:stream", Bank = "B", Data = new byte[] { 1 } });
            string mount = System.IO.Path.Combine(scratch, "bad-mount");
            Rejected(delegate { CarrierMount.MaterializePrefix(bad, "mvp/", mount); }, "invalid mount preflight");
            Check(!System.IO.Directory.Exists(mount), "failed mount writes nothing");
            string output = System.IO.Path.Combine(scratch, "roundtrip.tiff");
            CarrierCodec.Save(output, valid, valid.TickHeader); Carrier first = CarrierCodec.Load(output, true);
            CarrierCodec.Save(output, valid, valid.TickHeader + 1); Carrier second = CarrierCodec.Load(output, true);
            Check(first.Entries.Count == second.Entries.Count && second.TickHeader == valid.TickHeader + 1, "verified atomic replacement roundtrip");
            Check(System.IO.Directory.GetFiles(scratch, "*.write-*").Length == 0, "temporary writeback cleanup");
            byte[] symbols = new byte[CarrierCodec.PageSize * CarrierCodec.PageSize]; System.Array.Copy(System.Text.Encoding.ASCII.GetBytes("TGPG4\0\0\0"), symbols, 8);
            System.Array.Copy(System.BitConverter.GetBytes((ushort)4), 0, symbols, 8, 2);
            System.Array.Copy(System.BitConverter.GetBytes((ushort)1), 0, symbols, 12, 2);
            System.Array.Copy(System.BitConverter.GetBytes(512), 0, symbols, 16, 4);
            System.Array.Copy(System.BitConverter.GetBytes(uint.MaxValue), 0, symbols, 20, 4);
            string hostile = System.IO.Path.Combine(scratch, "hostile.gif");
            Call("SaveAnimatedGifCore", hostile, new byte[symbols.Length], new System.Collections.Generic.List<byte[]> { symbols });
            Rejected(delegate { CarrierCodec.Load(hostile, false); }, "hostile stream length rejected before allocation");
            System.Array.Copy(System.BitConverter.GetBytes(12), 0, symbols, 20, 4);
            System.Array.Copy(System.BitConverter.GetBytes(1), 0, symbols, 24, 4);
            System.Array.Copy(System.BitConverter.GetBytes(12), 0, symbols, 28, 4);
            Call("SaveAnimatedGifCore", hostile, new byte[symbols.Length], new System.Collections.Generic.List<byte[]> { symbols });
            Rejected(delegate { CarrierCodec.Load(hostile, false); }, "overlapping or shifted page range rejected");
            System.Console.WriteLine("Carrier audit checks=" + checks);
        }
    }
}
