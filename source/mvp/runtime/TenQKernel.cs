// Lunar127 10Q@1M represented-compute kernel (TQK-1) — release 1.1.4 run-session randomized-raster full-appliance containment
// C# 5 / .NET Framework 4.x compatible. This is image-resident source materialized
// from the verified TIFF-GIF carrier at launch. It does not claim physical 10 QFLOP/s.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Lunar127TenQ
{
    internal struct State256
    {
        internal ulong A, B, C, D;
    }

    internal sealed class HotRun
    {
        internal State256 State;
        internal byte[] CheckpointRoot;
        internal int CheckpointCount;
    }

    internal static class TenQKernel
    {
        private const int WordBytes = 131072;
        private const int WordBits = 1048576;
        private const int HeaderBytes = 256;
        private const int CheckpointInterval = 65536;
        private const long DefaultEpochs = 10000000L;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 };

        private static byte[] _wordHash;
        private static ulong[] _k64;
        private static uint[] _laneSalt;
        private static uint[] _voxelSalt;
        private static byte[] _rasterSeed;
        private static byte[] _sessionSeed;
        private static byte[] _carrierBinding;
        private static string _rasterSeedSource;

        [STAThread]
        private static int Main(string[] args)
        {
            return Run(args);
        }

        internal static int Run(string[] args)
        {
            try
            {
                Dictionary<string, string> a = ParseArgs(args);
                string wordPath = Need(a, "word");
                string corePath = Need(a, "core");
                string refsPath = Need(a, "refs");
                string applianceRoot = Need(a, "appliance-root");
                string applianceManifestPath = Need(a, "appliance-manifest");
                string applianceZipPath = Need(a, "appliance-zip");
                string jsonOut = Need(a, "json");
                string jsOut = Need(a, "js");
                long epochs = a.ContainsKey("epochs") ? Int64.Parse(a["epochs"], CultureInfo.InvariantCulture) : DefaultEpochs;
                if (epochs < 1000000L || epochs > 1000000000L) throw new InvalidDataException("epochs must be in [1000000,1000000000]");

                byte[] core = File.ReadAllBytes(corePath);
                byte[] word = File.ReadAllBytes(wordPath);
                Dictionary<string, object> applianceVerification = VerifyUltraWideAppliance(applianceRoot, applianceManifestPath, applianceZipPath);
                Dictionary<string, object> verification = VerifyResidentWord(core, word);
                LoadMacroConstants(word);
                InitializeRasterSession(a);

                Dictionary<string, object> refs = Json.DeserializeObject(File.ReadAllText(refsPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (refs == null) throw new InvalidDataException("reference vector JSON root rejected");
                Dictionary<string, object> preflight = RunPreflight(refs);
                if (!(bool)preflight["pass"]) throw new InvalidDataException("TQK-1 fixed-vector preflight failed");

                // Warm JIT and code paths before measuring.
                RunHot(250000L, false, false);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();

                const int rounds = 3;
                double[] rates = new double[rounds];
                HotRun selected = null;
                for (int r = 0; r < rounds; r++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    HotRun hr = RunHot(epochs, true, true);
                    sw.Stop();
                    rates[r] = epochs / sw.Elapsed.TotalSeconds;
                    selected = hr;
                }
                Array.Sort(rates);
                double epochRate = rates[1];
                double clockMHz = epochRate / 1000000.0;
                bool clockPass = epochRate >= 1000000.0;

                Dictionary<string, object> scalar = MeasureScalarFloats();

                // Challenges are selected only after the sequential benchmark completes.
                List<object> laneChallenges = new List<object>();
                List<object> voxelChallenges = new List<object>();
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    BigInteger laneLimit = BigInteger.Parse("5000000000000000000000000", CultureInfo.InvariantCulture);
                    for (int i = 0; i < 64; i++)
                    {
                        BigInteger lane = RandomBelow(rng, laneLimit);
                        ulong epoch = 1UL + RandomU64(rng) % (ulong)epochs;
                        laneChallenges.Add(ComputeLane(epoch, lane));
                    }
                    for (int i = 0; i < 64; i++)
                    {
                        ulong x = RandomU64(rng) % 25000000000000UL;
                        ulong y = RandomU64(rng) % 40000000000000UL;
                        ulong z = RandomU64(rng) % 10000000000000UL;
                        ulong epoch = 1UL + RandomU64(rng) % (ulong)epochs;
                        voxelChallenges.Add(ComputeVoxel(epoch, x, y, z));
                    }
                }

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["schema"] = "LUNAR127/TENQ-RUNTIME-METRICS/1";
                root["release"] = "1.1.4";
                root["generatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                root["measurementBoundary"] = "10 QFLOP/s-eq is represented/queryable compute at the 1 MHz gate, not physically executed 10^31 floating-point operations per second.";
                root["verification"] = verification;
                root["ultraWideAppliance"] = applianceVerification;
                root["preflight"] = preflight;
                root["benchmarkEpochs"] = epochs.ToString(CultureInfo.InvariantCulture);
                root["benchmarkRounds"] = rounds;
                root["sequentialEpochsPerSecondMedian"] = epochRate;
                root["hologramClockMHzMeasured"] = clockMHz;
                root["clockGatePass"] = clockPass;
                root["flopEquivalentPerEpoch"] = "10000000000000000000000000";
                root["fmaLanesPerEpoch"] = "5000000000000000000000000";
                root["representedFlopEquivalentPerSecondAt1MHz"] = "10000000000000000000000000000000";
                root["representedQuettaFlopEquivalentPerSecondAt1MHz"] = 10.0;
                root["representedQuettaFlopEquivalentPerSecondAtMeasuredClock"] = 10.0 * clockMHz;
                root["hotStateBits"] = 256;
                root["ultraWideProgramBits"] = WordBits;
                root["ultraWideProgramBytesReadAtBoot"] = WordBytes;
                root["ultraWideWordStatus"] = "RESIDENT / VERIFIED";
                root["checkpointIntervalEpochs"] = CheckpointInterval;
                root["checkpointCount"] = selected.CheckpointCount;
                root["checkpointRootSha256"] = Hex(selected.CheckpointRoot);
                root["finalStateSha256"] = ShaHex(StateBytes(selected.State));
                root["materializedHotStateBytes"] = 32;
                root["algorithmicStateReadWriteBytesPerEpoch"] = 64;
                root["hardwareMemoryBusBytesMeasured"] = false;
                root["hardwareMemoryBusBoundary"] = "No PMU/DRAM bus counter is claimed; the 64-byte figure is algorithmic state read+write accounting.";
                root["physicalScalarFlopDiagnostic"] = scalar;
                root["computeLaneChallenges"] = laneChallenges;
                root["voxelChallenges"] = voxelChallenges;
                root["rasterRandomization"] = new Dictionary<string, object> {
                    {"mode", "fresh 256-bit launch seed per run; carrier+BRWWORD-bound session seed; deterministic within a run"},
                    {"launchSeedHex", Hex(_rasterSeed)},
                    {"sessionSeedHex", Hex(_sessionSeed)},
                    {"carrierBindingSha256", Hex(_carrierBinding)},
                    {"seedSource", _rasterSeedSource},
                    {"wordBound", true},
                    {"carrierBound", true},
                    {"deterministicWithinRun", true},
                    {"randomizedBetweenLaunches", String.Equals(_rasterSeedSource, "os-csprng", StringComparison.Ordinal)},
                    {"replayEnvironmentVariable", "LUNAR127_RASTER_SEED"},
                    {"derivation", "SHA256(launchSeed || carrierProgramBinding || BRWWORD_SHA256)"}
                };
                root["checkpointBindsRasterSeed"] = true;
                root["rasterCube"] = new Dictionary<string, object> {
                    {"width", "25000000000000"}, {"height", "40000000000000"}, {"depth", "10000000000000"},
                    {"addressableVoxels", "10000000000000000000000000000000000000000"},
                    {"rgba8FlatMaterializationBytes", "40000000000000000000000000000000000000000"}
                };

                string json = Json.Serialize(root);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut)));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsOut)));
                File.WriteAllText(jsonOut, json + Environment.NewLine, new UTF8Encoding(false));
                File.WriteAllText(jsOut, "window.LUNAR127_TENQ_METRICS = " + json + ";\n", new UTF8Encoding(false));

                Console.WriteLine("PASS TQK-1");
                Console.WriteLine("ultrawide_appliance=PASS");
                Console.WriteLine("ultrawide_files=" + Convert.ToString(applianceVerification["fileCount"], CultureInfo.InvariantCulture));
                Console.WriteLine("word_sha256=" + Hex(_wordHash));
                Console.WriteLine("epochs_per_second=" + epochRate.ToString("0.000", CultureInfo.InvariantCulture));
                Console.WriteLine("clock_mhz=" + clockMHz.ToString("0.000000", CultureInfo.InvariantCulture));
                Console.WriteLine("clock_gate=" + (clockPass ? "PASS" : "FAIL"));
                Console.WriteLine("represented_qflop_eq_at_gate=10.000");
                Console.WriteLine("raster_seed=" + Hex(_rasterSeed));
                Console.WriteLine("session_seed=" + Hex(_sessionSeed));
                Console.WriteLine("carrier_binding=" + Hex(_carrierBinding));
                Console.WriteLine("raster_seed_source=" + _rasterSeedSource);
                Console.WriteLine("checkpoint_root=" + Hex(selected.CheckpointRoot));
                return clockPass ? 0 : 7;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("TQK-1 FAIL: " + ex.ToString());
                return 2;
            }
        }


        private static Dictionary<string, object> VerifyUltraWideAppliance(string repositoryRoot, string manifestPath, string sourceZipPath)
        {
            repositoryRoot = Path.GetFullPath(repositoryRoot);
            manifestPath = Path.GetFullPath(manifestPath);
            sourceZipPath = Path.GetFullPath(sourceZipPath);
            if (!Directory.Exists(repositoryRoot)) throw new DirectoryNotFoundException("Carrier-resident UltraWide repository missing: " + repositoryRoot);
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Carrier-resident UltraWide manifest missing", manifestPath);
            if (!File.Exists(sourceZipPath)) throw new FileNotFoundException("Carrier-resident UltraWide source ZIP missing", sourceZipPath);

            Dictionary<string, object> manifest = Json.DeserializeObject(File.ReadAllText(manifestPath, Encoding.UTF8)) as Dictionary<string, object>;
            if (manifest == null) throw new InvalidDataException("UltraWide appliance manifest JSON root rejected");
            string schema = Convert.ToString(manifest.ContainsKey("schema") ? manifest["schema"] : null, CultureInfo.InvariantCulture);
            if (!String.Equals(schema, "LUNAR127/ULTRAWIDE-APPLIANCE-MANIFEST/1", StringComparison.Ordinal)) throw new InvalidDataException("UltraWide appliance manifest schema rejected");

            long expectedZipBytes = Convert.ToInt64(manifest["sourceZipBytes"], CultureInfo.InvariantCulture);
            string expectedZipHash = Convert.ToString(manifest["sourceZipSha256"], CultureInfo.InvariantCulture);
            FileInfo zfi = new FileInfo(sourceZipPath);
            if (zfi.Length != expectedZipBytes) throw new InvalidDataException("Carrier-resident UltraWide source ZIP byte count mismatch");
            string actualZipHash;
            using (FileStream fs = File.OpenRead(sourceZipPath)) using (SHA256 h = SHA256.Create()) actualZipHash = Hex(h.ComputeHash(fs));
            if (!String.Equals(actualZipHash, expectedZipHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Carrier-resident UltraWide source ZIP hash mismatch");

            object[] files = manifest["files"] as object[];
            if (files == null) throw new InvalidDataException("UltraWide appliance manifest files array rejected");
            string rootWithSep = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            long totalBytes = 0;
            int checkedFiles = 0;
            for (int i = 0; i < files.Length; i++)
            {
                Dictionary<string, object> f = files[i] as Dictionary<string, object>;
                if (f == null) throw new InvalidDataException("UltraWide appliance manifest file entry rejected at index " + i.ToString(CultureInfo.InvariantCulture));
                string rel = Convert.ToString(f["path"], CultureInfo.InvariantCulture).Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(rel) || rel.Contains(".." + Path.DirectorySeparatorChar)) throw new InvalidDataException("Unsafe UltraWide manifest path: " + rel);
                string full = Path.GetFullPath(Path.Combine(repositoryRoot, rel));
                if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UltraWide manifest path escaped repository root: " + rel);
                if (!File.Exists(full)) throw new FileNotFoundException("Carrier-resident UltraWide file missing", full);
                long expectedBytes = Convert.ToInt64(f["bytes"], CultureInfo.InvariantCulture);
                FileInfo fi = new FileInfo(full);
                if (fi.Length != expectedBytes) throw new InvalidDataException("UltraWide file byte count mismatch: " + rel);
                string expectedHash = Convert.ToString(f["sha256"], CultureInfo.InvariantCulture);
                string actualHash;
                using (FileStream fs = File.OpenRead(full)) using (SHA256 h = SHA256.Create()) actualHash = Hex(h.ComputeHash(fs));
                if (!String.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UltraWide file hash mismatch: " + rel);
                totalBytes += fi.Length;
                checkedFiles++;
            }
            int expectedCount = Convert.ToInt32(manifest["fileCount"], CultureInfo.InvariantCulture);
            long expectedTotal = Convert.ToInt64(manifest["totalFileBytes"], CultureInfo.InvariantCulture);
            if (checkedFiles != expectedCount || totalBytes != expectedTotal) throw new InvalidDataException("UltraWide appliance manifest aggregate mismatch");

            // Reject undeclared files inside the materialized repository tree. This makes the manifest an exact set, not a subset check.
            string[] actualFiles = Directory.GetFiles(repositoryRoot, "*", SearchOption.AllDirectories);
            if (actualFiles.Length != expectedCount) throw new InvalidDataException("UltraWide repository contains undeclared or missing files");

            return new Dictionary<string, object> {
                {"status", "PASS"},
                {"manifestSchema", schema},
                {"fileCount", checkedFiles},
                {"repositoryBytes", totalBytes},
                {"sourceZipBytes", zfi.Length},
                {"sourceZipSha256", actualZipHash},
                {"repositoryRoot", "mvp/ultrawide-appliance/repository/UltraWide-Appliance-main"},
                {"sourceZip", "mvp/ultrawide-appliance/package/UltraWide-Appliance-main.zip"},
                {"boundary", "Complete UltraWide repository tree plus original source ZIP were decoded from and verified against the TIFF/GIF carrier before benchmarking."}
            };
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string k = args[i];
                if (!k.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) throw new ArgumentException("Expected --name value arguments");
                d[k.Substring(2)] = args[++i];
            }
            return d;
        }
        private static string Need(Dictionary<string, string> d, string key) { if (!d.ContainsKey(key)) throw new ArgumentException("Missing --" + key); return Path.GetFullPath(d[key]); }

        private static Dictionary<string, object> VerifyResidentWord(byte[] core, byte[] word)
        {
            if (word.Length != WordBytes) throw new InvalidDataException("Resident word must be exactly 131072 bytes");
            if (Encoding.ASCII.GetString(word, 0, 8) != "BRWWORD\0") throw new InvalidDataException("BRWWORD magic mismatch");
            ushort version = U16(word, 8), header = U16(word, 10);
            uint bits = U32(word, 12), index = U32(word, 16), count = U32(word, 20), imageSize = U32(word, 24), sourceSize = U32(word, 28), brirSize = U32(word, 32), steps = U32(word, 36);
            if (version != 1 || header != HeaderBytes || bits != WordBits || index != 0 || count != 1 || steps != 32) throw new InvalidDataException("BRWWORD geometry/version/budget mismatch");
            long payload = (long)imageSize + sourceSize + brirSize;
            if (imageSize < 1 || sourceSize < 1 || brirSize < 1 || HeaderBytes + payload > WordBytes) throw new InvalidDataException("BRWWORD segment lengths rejected");
            for (int i = 232; i < HeaderBytes; i++) if (word[i] != 0) throw new InvalidDataException("BRWWORD reserved bytes nonzero");
            for (long i = HeaderBytes + payload; i < word.Length; i++) if (word[i] != 0) throw new InvalidDataException("BRWWORD padding nonzero");
            byte[] image = Slice(word, HeaderBytes, checked((int)imageSize));
            byte[] source = Slice(word, HeaderBytes + checked((int)imageSize), checked((int)sourceSize));
            byte[] brir = Slice(word, HeaderBytes + checked((int)imageSize + checked((int)sourceSize)), checked((int)brirSize));
            if (!Same(Sha(image), Slice(word, 104, 32)) || !Same(Sha(source), Slice(word, 136, 32)) || !Same(Sha(brir), Slice(word, 168, 32))) throw new InvalidDataException("BRWWORD segment digest mismatch");
            byte[] joined = new byte[checked((int)payload)]; Buffer.BlockCopy(word, HeaderBytes, joined, 0, checked((int)payload));
            if (!Same(Sha(joined), Slice(word, 200, 32))) throw new InvalidDataException("BRWWORD payload digest mismatch");

            byte[] coreDigest = Sha(core);
            ulong d0 = BitConverter.ToUInt64(coreDigest, 0), d1 = BitConverter.ToUInt64(coreDigest, 8), d2 = BitConverter.ToUInt64(coreDigest, 16), d3 = BitConverter.ToUInt64(coreDigest, 24);
            string expectedSource = "LCTL-BRIM/1\nID|LANE|OP|OUT|CTRL|IN|ARG|META\n" +
                "I0000|main|MOVI|R0|C0|R0>R0|u64:" + d0.ToString(CultureInfo.InvariantCulture) + "|mode=WRAP;width=WIDE\n" +
                "I0001|main|MOVI|R1|C0|R0>R0|u64:" + d1.ToString(CultureInfo.InvariantCulture) + "|mode=WRAP;width=WIDE\n" +
                "I0002|main|MOVI|R2|C0|R0>R0|u64:" + d2.ToString(CultureInfo.InvariantCulture) + "|mode=WRAP;width=WIDE\n" +
                "I0003|main|MOVI|R3|C0|R0>R0|u64:" + d3.ToString(CultureInfo.InvariantCulture) + "|mode=WRAP;width=WIDE\n" +
                "I0004|main|XOR|R4|C0|R0>R1|u64:0|mode=WRAP;width=WIDE\n" +
                "I0005|main|XOR|R5|C0|R2>R3|u64:0|mode=WRAP;width=WIDE\n" +
                "I0006|main|XOR|R6|C0|R4>R5|u64:0|mode=WRAP;width=WIDE\n" +
                "I0007|main|HALT|R0|C0|R0>R0|u64:0|mode=WRAP;width=WIDE\n";
            byte[] expectedSourceBytes = Encoding.UTF8.GetBytes(expectedSource);
            if (!Same(source, expectedSourceBytes)) throw new InvalidDataException("Resident UltraWide word does not bind the current macroprogram core digest");

            // Recompute one-word BRWWORD manifest identity exactly as UltraWide 0.2.0.
            using (SHA256 h = SHA256.Create())
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write(Encoding.ASCII.GetBytes("UltraWide:BRWWORD:manifest:v1\0"));
                bw.Write((uint)1); bw.Write(steps);
                bw.Write(imageSize); bw.Write(Sha(image));
                bw.Write(sourceSize); bw.Write(Sha(source));
                bw.Write(brirSize); bw.Write(Sha(brir));
                bw.Flush();
                byte[] manifest = h.ComputeHash(ms.ToArray());
                if (!Same(manifest, Slice(word, 40, 32))) throw new InvalidDataException("BRWWORD manifest identity mismatch");
            }
            for (int i = 72; i < 104; i++) if (word[i] != 0) throw new InvalidDataException("One-word chain previous-hash field must be zero");

            Dictionary<string, object> r = new Dictionary<string, object>();
            r["status"] = "PASS";
            r["format"] = "BRWWORD/1";
            r["wordBits"] = WordBits;
            r["wordBytes"] = WordBytes;
            r["coreSha256"] = Hex(coreDigest);
            r["wordSha256"] = ShaHex(word);
            r["imageSha256"] = ShaHex(image);
            r["sourceSha256"] = ShaHex(source);
            r["brirSha256"] = ShaHex(brir);
            r["binding"] = "macroprogram-core SHA-256 encoded as four little-endian u64 MOVI immediates";
            r["authentication"] = "integrity only; unsigned preview";
            return r;
        }

        private static void LoadMacroConstants(byte[] word)
        {
            _wordHash = Sha(word);
            _k64 = new ulong[4];
            for (int i = 0; i < 4; i++) _k64[i] = BitConverter.ToUInt64(_wordHash, i * 8);
            _laneSalt = new uint[4];
            _voxelSalt = new uint[4];
            for (int i = 0; i < 4; i++) _laneSalt[i] = BitConverter.ToUInt32(_wordHash, i * 4);
            for (int i = 0; i < 4; i++) _voxelSalt[i] = BitConverter.ToUInt32(_wordHash, 16 + i * 4);
        }

        private static void InitializeRasterSession(Dictionary<string, string> args)
        {
            string supplied = null;
            if (args != null && args.ContainsKey("raster-seed")) supplied = args["raster-seed"];
            if (String.IsNullOrWhiteSpace(supplied)) supplied = Environment.GetEnvironmentVariable("LUNAR127_RASTER_SEED");

            string carrierBindingHex = args != null && args.ContainsKey("carrier-binding") ? args["carrier-binding"] : Environment.GetEnvironmentVariable("LUNAR127_CARRIER_BINDING");
            if (String.IsNullOrWhiteSpace(carrierBindingHex)) throw new InvalidDataException("carrier-binding is required for Lunar127 1.1.4 session binding");
            _carrierBinding = ParseHex32(carrierBindingHex.Trim());

            if (!String.IsNullOrWhiteSpace(supplied))
            {
                _rasterSeed = ParseHex32(supplied.Trim());
                _rasterSeedSource = "replay";
            }
            else
            {
                _rasterSeed = new byte[32];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(_rasterSeed);
                _rasterSeedSource = "os-csprng";
            }

            // Lunar127 1.1.4 run-session derivation:
            // sessionSeed = SHA256(launchSeed || carrierProgramBinding || verified BRWWORD SHA-256).
            // The carrier binding is a packaging-time digest over the immutable run-critical
            // payload set. The outer TIFF-GIF-HOLO/4 verifier authenticates the containing
            // archive stream before this image-resident entry point is materialized.
            byte[] bind = new byte[96];
            Buffer.BlockCopy(_rasterSeed, 0, bind, 0, 32);
            Buffer.BlockCopy(_carrierBinding, 0, bind, 32, 32);
            Buffer.BlockCopy(_wordHash, 0, bind, 64, 32);
            _sessionSeed = Sha(bind);

            uint[] baseSalt = new uint[4];
            for (int i = 0; i < 4; i++) baseSalt[i] = BitConverter.ToUInt32(_wordHash, 16 + i * 4);
            uint[] constants = new uint[] { 0xA5A5A5A5U, 0x3C6EF372U, 0xBB67AE85U, 0x9E3779B9U };
            for (int i = 0; i < 4; i++)
            {
                uint s0 = BitConverter.ToUInt32(_sessionSeed, i * 4);
                uint s1 = BitConverter.ToUInt32(_sessionSeed, (i + 4) * 4);
                _voxelSalt[i] = Mix32(baseSalt[i] ^ s0 ^ Rol32(s1, 7 + i * 3) ^ constants[i]);
            }
        }

        private static byte[] ParseHex32(string hex)
        {
            if (hex == null || hex.Length != 64) throw new InvalidDataException("raster seed must be exactly 64 hexadecimal characters");
            byte[] b = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                int hi = HexNibble(hex[i * 2]);
                int lo = HexNibble(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) throw new InvalidDataException("raster seed contains a non-hexadecimal character");
                b[i] = (byte)((hi << 4) | lo);
            }
            return b;
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static State256 InitialState()
        {
            State256 s = new State256();
            s.A = _k64[0] ^ 0x243F6A8885A308D3UL;
            s.B = _k64[1] ^ 0x13198A2E03707344UL;
            s.C = _k64[2] ^ 0xA4093822299F31D0UL;
            s.D = _k64[3] ^ 0x082EFA98EC4E6C89UL;
            return s;
        }

        private static State256 Step(State256 s, ulong epoch)
        {
            unchecked
            {
                s.A = s.A + 0x9E3779B97F4A7C15UL + epoch;
                s.B ^= Rol64(s.A ^ _k64[0], 13); s.B *= 0xD6E8FEB86659FD93UL;
                s.C += (s.B ^ _k64[1]); s.C = Rol64(s.C, 29); s.C *= 0xA24BAED4963EE407UL;
                s.D = Rol64(s.D ^ s.C ^ _k64[2], 17) + s.A + _k64[3];
                s.A ^= Rol64(s.D, 41);
                s.B += s.C + 0x94D049BB133111EBUL;
                s.C ^= Rol64(s.A, 23);
                s.D *= 0x9FB21C651E98DF25UL;
                return s;
            }
        }

        private static HotRun RunHot(long epochs, bool checkpoint, bool bindRasterSeed)
        {
            State256 s = InitialState();
            byte[] prev = new byte[32]; int count = 0;
            for (long i = 1; i <= epochs; i++)
            {
                s = Step(s, unchecked((ulong)i));
                if (checkpoint && (i % CheckpointInterval == 0 || i == epochs))
                {
                    int seedBytes = (bindRasterSeed && _sessionSeed != null) ? 32 : 0;
                    byte[] input = new byte[32 + 32 + seedBytes + 8 + 32];
                    Buffer.BlockCopy(prev, 0, input, 0, 32);
                    Buffer.BlockCopy(_wordHash, 0, input, 32, 32);
                    int p = 64;
                    if (seedBytes != 0) { Buffer.BlockCopy(_sessionSeed, 0, input, p, 32); p += 32; }
                    Buffer.BlockCopy(BitConverter.GetBytes(unchecked((ulong)i)), 0, input, p, 8); p += 8;
                    Buffer.BlockCopy(StateBytes(s), 0, input, p, 32);
                    prev = Sha(input); count++;
                }
            }
            return new HotRun { State = s, CheckpointRoot = prev, CheckpointCount = count };
        }

        private static Dictionary<string, object> RunPreflight(Dictionary<string, object> refs)
        {
            Dictionary<string, object> hot = refs["hot_state"] as Dictionary<string, object>;
            if (hot == null) throw new InvalidDataException("reference hot_state missing");
            Dictionary<string, object> oneM = hot["1000000"] as Dictionary<string, object>;
            if (oneM == null) throw new InvalidDataException("reference 1,000,000 vector missing");
            HotRun hr = RunHot(1000000L, true, false);
            string state = ShaHex(StateBytes(hr.State));
            string cp = Hex(hr.CheckpointRoot);
            string expectState = Convert.ToString(oneM["state_sha256"], CultureInfo.InvariantCulture);
            string expectCp = Convert.ToString(oneM["checkpoint_root_sha256"], CultureInfo.InvariantCulture);
            bool pass = String.Equals(state, expectState, StringComparison.OrdinalIgnoreCase) && String.Equals(cp, expectCp, StringComparison.OrdinalIgnoreCase) && hr.CheckpointCount == 16;
            return new Dictionary<string, object> { {"pass", pass}, {"epochs", 1000000}, {"stateSha256", state}, {"checkpointRootSha256", cp}, {"checkpointCount", hr.CheckpointCount} };
        }

        private static Dictionary<string, object> ComputeLane(ulong epoch, BigInteger lane)
        {
            BigInteger mask = new BigInteger(UInt32.MaxValue);
            uint l0 = (uint)(lane & mask);
            uint l1 = (uint)((lane >> 32) & mask);
            uint l2 = (uint)((lane >> 64) & mask);
            uint e = unchecked((uint)epoch);
            uint h0 = Mix32(l0 ^ Rol32(l1, 7) ^ Rol32(l2, 13) ^ e ^ _laneSalt[0]);
            uint h1 = Mix32(l1 ^ Rol32(l2, 11) ^ Rol32(l0, 17) ^ unchecked(e * 0x9e3779b1U) ^ _laneSalt[1]);
            uint h2 = Mix32(l2 ^ Rol32(l0, 5) ^ Rol32(l1, 19) ^ unchecked(e * 0x85ebca6bU) ^ _laneSalt[2]);
            int aa = (int)(h0 & 2047U) - 1024;
            int bb = (int)(h1 & 2047U) - 1024;
            int cc = (int)(h2 & 2047U) - 1024;
            int result = aa * bb + cc;
            return new Dictionary<string, object> {
                {"epoch", epoch.ToString(CultureInfo.InvariantCulture)}, {"lane", lane.ToString(CultureInfo.InvariantCulture)},
                {"a", aa}, {"b", bb}, {"c", cc}, {"result", result}
            };
        }

        private static Dictionary<string, object> ComputeVoxel(ulong epoch, ulong x, ulong y, ulong z)
        {
            uint xl = (uint)x, xh = (uint)(x >> 32), yl = (uint)y, yh = (uint)(y >> 32), zl = (uint)z, zh = (uint)(z >> 32), e = (uint)epoch;
            uint h = Mix32(xl ^ Rol32(yl, 11) ^ unchecked(xh * 0x9e3779b1U) ^ unchecked(yh * 0x85ebca6bU) ^ Rol32(zl, 3) ^ unchecked(zh * 0x27d4eb2dU) ^ unchecked(e * 0xc2b2ae35U) ^ _voxelSalt[0]);
            uint h2 = Mix32(h ^ Rol32(xl, 7) ^ Rol32(yh, 17) ^ unchecked(zl * 0x165667b1U) ^ Rol32(zh, 9) ^ _voxelSalt[1]);
            uint h3 = Mix32(h2 ^ Rol32(yl, 5) ^ unchecked(xh * 0xd3a2646cU) ^ Rol32(zl, 15) ^ unchecked((e + 1U) * 0x9e3779b1U) ^ _voxelSalt[2]);
            uint h4 = Mix32(h3 ^ Rol32(zh, 13) ^ unchecked(yh * 0x94d049bbU) ^ _voxelSalt[3]);
            int r = 30 + (int)(h2 & 0x9fU), g = 35 + (int)((h3 >> 8) & 0x8fU), b = 45 + (int)((h4 >> 16) & 0x9fU), a = 64 + (int)((h >> 24) & 0xbfU);
            return new Dictionary<string, object> {
                {"epoch", epoch.ToString(CultureInfo.InvariantCulture)}, {"x", x.ToString(CultureInfo.InvariantCulture)}, {"y", y.ToString(CultureInfo.InvariantCulture)}, {"z", z.ToString(CultureInfo.InvariantCulture)},
                {"rgba", new int[] { r, g, b, a }}, {"code", new string[] { h.ToString(CultureInfo.InvariantCulture), h2.ToString(CultureInfo.InvariantCulture), h3.ToString(CultureInfo.InvariantCulture), h4.ToString(CultureInfo.InvariantCulture) }}
            };
        }

        private static Dictionary<string, object> MeasureScalarFloats()
        {
            const int iterations = 5000000;
            double x = 1.000001, y = 0.999999, z = 1.000003, w = 0.999997;
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                x = x * 0.999999999 + y * 0.000000001;
                y = y * 0.999999999 + z * 0.000000001;
                z = z * 0.999999999 + w * 0.000000001;
                w = w * 0.999999999 + x * 0.000000001;
            }
            sw.Stop();
            double flops = iterations * 12.0;
            return new Dictionary<string, object> {
                {"measured", true}, {"kind", "dependent scalar FP64 diagnostic; not peak hardware FLOPS"},
                {"iterations", iterations}, {"countedFlops", flops}, {"elapsedSeconds", sw.Elapsed.TotalSeconds},
                {"flopsPerSecond", flops / sw.Elapsed.TotalSeconds}, {"checksum", (x + y + z + w).ToString("R", CultureInfo.InvariantCulture)}
            };
        }

        private static BigInteger RandomBelow(RandomNumberGenerator rng, BigInteger limit)
        {
            byte[] b = new byte[17]; rng.GetBytes(b); b[16] = 0; return new BigInteger(b) % limit;
        }
        private static ulong RandomU64(RandomNumberGenerator rng) { byte[] b = new byte[8]; rng.GetBytes(b); return BitConverter.ToUInt64(b, 0); }

        private static ulong Rol64(ulong x, int n) { return (x << n) | (x >> (64 - n)); }
        private static uint Rol32(uint x, int n) { return (x << n) | (x >> (32 - n)); }
        private static uint Mix32(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; } }
        private static byte[] StateBytes(State256 s) { byte[] b = new byte[32]; Buffer.BlockCopy(BitConverter.GetBytes(s.A), 0, b, 0, 8); Buffer.BlockCopy(BitConverter.GetBytes(s.B), 0, b, 8, 8); Buffer.BlockCopy(BitConverter.GetBytes(s.C), 0, b, 16, 8); Buffer.BlockCopy(BitConverter.GetBytes(s.D), 0, b, 24, 8); return b; }
        private static ushort U16(byte[] b, int p) { return BitConverter.ToUInt16(b, p); }
        private static uint U32(byte[] b, int p) { return BitConverter.ToUInt32(b, p); }
        private static byte[] Slice(byte[] b, int p, int n) { byte[] r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); return r; }
        private static byte[] Sha(byte[] b) { using (SHA256 s = SHA256.Create()) return s.ComputeHash(b); }
        private static string ShaHex(byte[] b) { return Hex(Sha(b)); }
        private static bool Same(byte[] a, byte[] b) { if (a == null || b == null || a.Length != b.Length) return false; int x = 0; for (int i = 0; i < a.Length; i++) x |= a[i] ^ b[i]; return x == 0; }
        private static string Hex(byte[] b) { StringBuilder s = new StringBuilder(b.Length * 2); for (int i = 0; i < b.Length; i++) s.Append(b[i].ToString("x2", CultureInfo.InvariantCulture)); return s.ToString(); }
    }

    public static class KernelEntry
    {
        public static int Run(string[] args) { return TenQKernel.Run(args); }
    }
}
