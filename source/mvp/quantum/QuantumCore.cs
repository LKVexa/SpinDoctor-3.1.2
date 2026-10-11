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
// Procedural product-state engine. Compatible with .NET Framework / C# 5.
// This file contains the actual CPU evaluator and a real FP64 OpenCL GPU kernel.
// A descriptor represents independent single-qubit states; it is not a dense
// entangled statevector and does not credit unexecuted operations as hardware work.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QuettaQuantum
{
    public static class QuantumMath
    {
        public const ulong MaximumExactStateCount = 9007199254740991UL;
        public const ulong MaximumAxis = 1000000000UL;
        public const int StateStride = 9;
        public static double[] Identity
        {
            get { return new double[] { 1, 0, 0, 0, 0, 0, 1, 0 }; }
        }

        public static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        public static ulong ValidateGrid(ulong m, ulong l)
        {
            if (m == 0 || l == 0 || m > MaximumAxis || l > MaximumAxis)
                throw new ArgumentOutOfRangeException("grid", "Each grid axis must be from 1 to 1,000,000,000.");
            if (m > MaximumExactStateCount / l)
                throw new ArgumentOutOfRangeException("grid", "The grid must contain at most 2^53-1 states for exact address conversion.");
            return m * l;
        }

        public static void ValidateMigration(double t)
        {
            if (!IsFinite(t) || t < 0 || t > 1)
                throw new ArgumentOutOfRangeException("t", "Migration t must be a finite value from 0 to 1.");
        }

        public static void ValidateMatrix(double[] matrix)
        {
            if (matrix == null || matrix.Length != 8)
                throw new ArgumentException("A matrix needs eight row-major interleaved real/imaginary components.", "matrix");
            for (int i = 0; i < matrix.Length; ++i)
                if (!IsFinite(matrix[i])) throw new ArgumentException("Matrix components must be finite.", "matrix");
            if (UnitarityError(matrix) > 1e-9)
                throw new ArgumentException("The cumulative matrix is not unitary within 1e-9.", "matrix");
        }

        // Format: [ar, ai, br, bi, Bloch x, Bloch y, Bloch z, norm, P(1)].
        public static double[] State(ulong id, ulong m, ulong l, double t, double[] matrix)
        {
            ulong count = ValidateGrid(m, l);
            if (id >= count) throw new ArgumentOutOfRangeException("id", "Address is outside the prepared grid.");
            ValidateMigration(t);
            ValidateMatrix(matrix);
            return StateUnchecked(id, m, l, t, matrix);
        }

        internal static double[] StateUnchecked(ulong id, ulong m, ulong l, double t, double[] matrix)
        {
            ulong k = id % m;
            ulong ell = id / m;
            double q = ((double)k + 0.5) / (double)m;
            double theta = 2.0 * Math.Asin(Math.Sqrt(q));
            double phi = 2.0 * Math.PI * (double)ell / (double)l;
            double ar = Math.Cos(t * theta * 0.5);
            double amplitude = Math.Sin(t * theta * 0.5);
            double br = amplitude * Math.Cos(t * phi);
            double bi = amplitude * Math.Sin(t * phi);
            double outAr = matrix[0] * ar + matrix[2] * br - matrix[3] * bi;
            double outAi = matrix[1] * ar + matrix[2] * bi + matrix[3] * br;
            double outBr = matrix[4] * ar + matrix[6] * br - matrix[7] * bi;
            double outBi = matrix[5] * ar + matrix[6] * bi + matrix[7] * br;
            double pa = outAr * outAr + outAi * outAi;
            double pb = outBr * outBr + outBi * outBi;
            return new double[] {
                outAr, outAi, outBr, outBi,
                2.0 * (outAr * outBr + outAi * outBi),
                2.0 * (outAr * outBi - outAi * outBr),
                pa - pb, pa + pb, pb
            };
        }

        public static double[] States(ulong[] ids, ulong m, ulong l, double t, double[] matrix)
        {
            if (ids == null) throw new ArgumentNullException("ids");
            ulong count = ValidateGrid(m, l);
            ValidateMigration(t);
            ValidateMatrix(matrix);
            double[] result = new double[checked(ids.Length * StateStride)];
            for (int i = 0; i < ids.Length; ++i)
            {
                if (ids[i] >= count) throw new ArgumentOutOfRangeException("ids", "Address is outside the prepared grid.");
                double[] one = StateUnchecked(ids[i], m, l, t, matrix);
                Array.Copy(one, 0, result, i * StateStride, StateStride);
            }
            return result;
        }

        public static double[] MatrixForGate(string gate, double[] parameters)
        {
            if (gate == null) throw new ArgumentNullException("gate");
            if (parameters == null) parameters = new double[0];
            for (int i = 0; i < parameters.Length; ++i)
                if (!IsFinite(parameters[i]) || Math.Abs(parameters[i]) > 1e12)
                    throw new ArgumentException("Gate angles must be finite and have absolute value at most 1e12 radians.", "parameters");
            string name = gate.ToLowerInvariant();
            double c, s;
            if (name == "u" || name == "u3")
            {
                RequireParameters(parameters, 3, name);
                double theta = parameters[0], phi = parameters[1], lambda = parameters[2];
                c = Math.Cos(theta / 2.0); s = Math.Sin(theta / 2.0);
                return new double[] {
                    c, 0, -s * Math.Cos(lambda), -s * Math.Sin(lambda),
                    s * Math.Cos(phi), s * Math.Sin(phi),
                    c * Math.Cos(phi + lambda), c * Math.Sin(phi + lambda)
                };
            }
            if (name == "ry")
            {
                RequireParameters(parameters, 1, name);
                c = Math.Cos(parameters[0] / 2.0); s = Math.Sin(parameters[0] / 2.0);
                return new double[] { c, 0, -s, 0, s, 0, c, 0 };
            }
            if (name == "rz")
            {
                RequireParameters(parameters, 1, name);
                c = Math.Cos(parameters[0] / 2.0); s = Math.Sin(parameters[0] / 2.0);
                return new double[] { c, -s, 0, 0, 0, 0, c, s };
            }
            RequireParameters(parameters, 0, name);
            if (name == "h")
            {
                c = 1.0 / Math.Sqrt(2.0);
                return new double[] { c, 0, c, 0, c, 0, -c, 0 };
            }
            if (name == "x") return new double[] { 0, 0, 1, 0, 1, 0, 0, 0 };
            if (name == "y") return new double[] { 0, 0, 0, -1, 0, 1, 0, 0 };
            if (name == "z") return new double[] { 1, 0, 0, 0, 0, 0, -1, 0 };
            if (name == "s") return new double[] { 1, 0, 0, 0, 0, 0, 0, 1 };
            if (name == "t")
            {
                c = 1.0 / Math.Sqrt(2.0);
                return new double[] { 1, 0, 0, 0, 0, 0, c, c };
            }
            if (name == "i" || name == "identity") return Identity;
            throw new ArgumentException("Supported gates are U, RY, RZ, H, X, Y, Z, S, T and I.", "gate");
        }

        private static void RequireParameters(double[] parameters, int wanted, string gate)
        {
            if (parameters.Length != wanted)
                throw new ArgumentException(gate.ToUpperInvariant() + " requires " + wanted + " angle parameter(s).", "parameters");
        }

        // New shared gates compose on the left: cumulative' = gate * cumulative.
        public static double[] Multiply(double[] left, double[] right)
        {
            ValidateMatrix(left); ValidateMatrix(right);
            double[] result = new double[8];
            for (int row = 0; row < 2; ++row)
                for (int col = 0; col < 2; ++col)
                {
                    int output = 2 * (2 * row + col);
                    for (int k = 0; k < 2; ++k)
                    {
                        int a = 2 * (2 * row + k), b = 2 * (2 * k + col);
                        result[output] += left[a] * right[b] - left[a + 1] * right[b + 1];
                        result[output + 1] += left[a] * right[b + 1] + left[a + 1] * right[b];
                    }
                }
            ValidateMatrix(result);
            return result;
        }

        public static double UnitarityError(double[] matrix)
        {
            if (matrix == null || matrix.Length != 8)
                throw new ArgumentException("A matrix needs eight components.", "matrix");
            double maximum = 0;
            for (int row = 0; row < 2; ++row)
                for (int col = 0; col < 2; ++col)
                {
                    double real = 0, imaginary = 0;
                    for (int k = 0; k < 2; ++k)
                    {
                        int a = 2 * (2 * k + row), b = 2 * (2 * k + col);
                        real += matrix[a] * matrix[b] + matrix[a + 1] * matrix[b + 1];
                        imaginary += matrix[a] * matrix[b + 1] - matrix[a + 1] * matrix[b];
                    }
                    real -= row == col ? 1.0 : 0.0;
                    double error = Math.Sqrt(real * real + imaginary * imaginary);
                    if (!IsFinite(error)) return Double.PositiveInfinity;
                    maximum = Math.Max(maximum, error);
                }
            return maximum;
        }
    }

    public sealed class QuantumGpu : IDisposable
    {
        public const int MaximumBatchStates = 16384;
        private IntPtr context, queue, program, kernel, device;
        private readonly object sync = new object();
        private bool disposed;
        private static int activeResources;
        public static int ActiveNativeResources { get { return Interlocked.CompareExchange(ref activeResources, 0, 0); } }
        public bool IsDisposed { get { lock (sync) { return disposed; } } }
        public string DeviceName { get; private set; }
        public string DriverVersion { get; private set; }
        public string DeviceVersion { get; private set; }

        private const string OpenClLibrary = "OpenCL.dll";
        private const ulong DeviceTypeGpu = 4;
        private const ulong MemoryReadOnly = 4;
        private const ulong MemoryWriteOnly = 2;
        private const ulong MemoryCopyHostPointer = 32;

        // At t=1 the GPU evaluates the amplitudes by direct square roots. The CPU
        // reference uses asin/sin/cos. The general-t GPU uses acos instead of asin.
        // All nine returned components, including absolute complex phase, are checked.
        private const string KernelSource = @"
#pragma OPENCL EXTENSION cl_khr_fp64 : enable
__kernel void quantum_states(__global const ulong *ids,
                             __global const double *g,
                             __global double *result,
                             ulong m, ulong l, double t, uint count) {
    uint n = (uint)get_global_id(0);
    if (n >= count) return;
    ulong id = ids[n];
    ulong k = id % m, ell = id / m;
    double q = ((double)k + 0.5) / (double)m;
    double phi = 6.283185307179586476925286766559 * (double)ell / (double)l;
    double a, amplitude;
    if (t == 1.0) {
        a = sqrt(1.0 - q); amplitude = sqrt(q);
    } else if (t == 0.0) {
        a = 1.0; amplitude = 0.0;
    } else {
        double theta = acos(1.0 - 2.0*q);
        a = cos(t * theta * 0.5); amplitude = sin(t * theta * 0.5);
    }
    double b = amplitude * cos(t * phi), bi = amplitude * sin(t * phi);
    double ar = g[0]*a + g[2]*b - g[3]*bi;
    double ai = g[1]*a + g[2]*bi + g[3]*b;
    double br = g[4]*a + g[6]*b - g[7]*bi;
    double outBi = g[5]*a + g[6]*bi + g[7]*b;
    double pa = ar*ar + ai*ai, pb = br*br + outBi*outBi;
    uint start = n*9;
    result[start] = ar; result[start+1] = ai;
    result[start+2] = br; result[start+3] = outBi;
    result[start+4] = 2.0*(ar*br + ai*outBi);
    result[start+5] = 2.0*(ar*outBi - ai*br);
    result[start+6] = pa-pb; result[start+7] = pa+pb;
    result[start+8] = pb;
}
";

        public QuantumGpu()
        {
            try
            {
                device = FindFp64Gpu();
                DeviceName = DeviceText(device, 0x102B);
                DriverVersion = DeviceText(device, 0x102D);
                DeviceVersion = DeviceText(device, 0x102F);
                int error;
                context = clCreateContext(IntPtr.Zero, 1, new IntPtr[] { device }, IntPtr.Zero, IntPtr.Zero, out error);
                Register(context); Check(error, "OpenCL context creation"); EnsureHandle(context, "context");
                queue = clCreateCommandQueue(context, device, 2, out error);
                Register(queue); Check(error, "OpenCL profiling queue creation"); EnsureHandle(queue, "queue");
                IntPtr source = Marshal.StringToHGlobalAnsi(KernelSource);
                try
                {
                    program = clCreateProgramWithSource(context, 1, new IntPtr[] { source }, new UIntPtr[] { Bytes(KernelSource.Length) }, out error);
                    Register(program); Check(error, "OpenCL program creation"); EnsureHandle(program, "program");
                }
                finally { Marshal.FreeHGlobal(source); }
                error = clBuildProgram(program, 1, new IntPtr[] { device }, "-cl-std=CL1.2", IntPtr.Zero, IntPtr.Zero);
                if (error != 0)
                    throw new InvalidOperationException("OpenCL FP64 kernel compilation failed (" + error + "): " + BuildLog(program, device));
                kernel = clCreateKernel(program, "quantum_states", out error);
                Register(kernel); Check(error, "OpenCL kernel creation"); EnsureHandle(kernel, "kernel");
            }
            catch { Dispose(); throw; }
        }

        public double[] Compute(ulong[] ids, ulong m, ulong l, double t, double[] matrix, out double kernelMs)
        {
            if (ids == null) throw new ArgumentNullException("ids");
            if (ids.Length > MaximumBatchStates)
                throw new ArgumentOutOfRangeException("ids", "An actual GPU batch is limited to 16,384 sampled states.");
            ulong total = QuantumMath.ValidateGrid(m, l);
            QuantumMath.ValidateMigration(t); QuantumMath.ValidateMatrix(matrix);
            for (int i = 0; i < ids.Length; ++i)
                if (ids[i] >= total) throw new ArgumentOutOfRangeException("ids", "Address is outside the prepared grid.");
            kernelMs = 0;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("QuantumGpu");
                if (ids.Length == 0) return new double[0];
                IntPtr idBuffer = IntPtr.Zero, matrixBuffer = IntPtr.Zero, resultBuffer = IntPtr.Zero, completion = IntPtr.Zero;
                try
                {
                    idBuffer = CopyBuffer(ids, checked(ids.Length * 8));
                    matrixBuffer = CopyBuffer(matrix, 8 * 8);
                    int error;
                    resultBuffer = clCreateBuffer(context, MemoryWriteOnly, Bytes(checked(ids.Length * QuantumMath.StateStride * 8)), IntPtr.Zero, out error);
                    Register(resultBuffer); Check(error, "GPU result allocation"); EnsureHandle(resultBuffer, "result buffer");
                    SetPointer(0, idBuffer); SetPointer(1, matrixBuffer); SetPointer(2, resultBuffer);
                    Check(clSetKernelArgULong(kernel, 3, Bytes(8), ref m), "GPU M argument");
                    Check(clSetKernelArgULong(kernel, 4, Bytes(8), ref l), "GPU L argument");
                    Check(clSetKernelArgDouble(kernel, 5, Bytes(8), ref t), "GPU migration argument");
                    uint number = checked((uint)ids.Length);
                    Check(clSetKernelArgUInt(kernel, 6, Bytes(4), ref number), "GPU sample count argument");
                    error = clEnqueueNDRangeKernel(queue, kernel, 1, null, new UIntPtr[] { Bytes(ids.Length) }, null, 0, null, out completion);
                    Register(completion); Check(error, "actual GPU dispatch"); EnsureHandle(completion, "completion event");
                    Check(clWaitForEvents(1, new IntPtr[] { completion }), "actual GPU completion");
                    ulong start, finish;
                    Check(clGetEventProfilingInfo(completion, 0x1282, Bytes(8), out start, IntPtr.Zero), "GPU event start time");
                    Check(clGetEventProfilingInfo(completion, 0x1283, Bytes(8), out finish, IntPtr.Zero), "GPU event end time");
                    kernelMs = finish >= start ? (finish - start) / 1000000.0 : 0;
                    double[] result = new double[checked(ids.Length * QuantumMath.StateStride)];
                    GCHandle pinned = GCHandle.Alloc(result, GCHandleType.Pinned);
                    try
                    {
                        Check(clEnqueueReadBuffer(queue, resultBuffer, 1, UIntPtr.Zero, Bytes(checked(result.Length * 8)), pinned.AddrOfPinnedObject(), 0, null, IntPtr.Zero), "actual GPU amplitude readback");
                    }
                    finally { pinned.Free(); }
                    return result;
                }
                finally
                {
                    ReleaseEvent(ref completion);
                    ReleaseBuffer(ref resultBuffer); ReleaseBuffer(ref matrixBuffer); ReleaseBuffer(ref idBuffer);
                }
            }
        }

        private IntPtr CopyBuffer(Array values, int byteCount)
        {
            GCHandle pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int error;
                buffer = clCreateBuffer(context, MemoryReadOnly | MemoryCopyHostPointer, Bytes(byteCount), pinned.AddrOfPinnedObject(), out error);
                Register(buffer); Check(error, "GPU input allocation and upload"); EnsureHandle(buffer, "input buffer");
                return buffer;
            }
            catch { ReleaseBuffer(ref buffer); throw; }
            finally { pinned.Free(); }
        }

        private void SetPointer(uint position, IntPtr value)
        {
            Check(clSetKernelArgPointer(kernel, position, Bytes(IntPtr.Size), ref value), "GPU buffer argument");
        }

        private static UIntPtr Bytes(int size)
        {
            return new UIntPtr(checked((uint)size));
        }

        private static void Check(int code, string action)
        {
            if (code != 0) throw new InvalidOperationException(action + " failed with OpenCL code " + code + ".");
        }

        private static void EnsureHandle(IntPtr handle, string kind)
        {
            if (handle == IntPtr.Zero) throw new InvalidOperationException("OpenCL returned a null " + kind + ".");
        }

        private static void Register(IntPtr handle)
        {
            if (handle != IntPtr.Zero) Interlocked.Increment(ref activeResources);
        }

        private static IntPtr FindFp64Gpu()
        {
            uint count;
            Check(clGetPlatformIDs(0, null, out count), "OpenCL platform enumeration");
            if (count == 0) throw new InvalidOperationException("No OpenCL platform is available; no GPU fallback was used.");
            IntPtr[] platforms = new IntPtr[count];
            Check(clGetPlatformIDs(count, platforms, out count), "OpenCL platform list");
            for (int p = 0; p < platforms.Length; ++p)
            {
                uint number;
                int code = clGetDeviceIDs(platforms[p], DeviceTypeGpu, 0, null, out number);
                if (code == -1 || number == 0) continue;
                Check(code, "OpenCL GPU enumeration");
                IntPtr[] devices = new IntPtr[number];
                Check(clGetDeviceIDs(platforms[p], DeviceTypeGpu, number, devices, out number), "OpenCL GPU list");
                for (int d = 0; d < devices.Length; ++d)
                {
                    ulong configuration;
                    code = clGetDeviceInfoULong(devices[d], 0x1032, Bytes(8), out configuration, IntPtr.Zero);
                    if (code == 0 && configuration != 0) return devices[d];
                }
            }
            throw new InvalidOperationException("No FP64-capable OpenCL GPU is available; the GPU evaluator did not run.");
        }

        private static string DeviceText(IntPtr handle, uint key)
        {
            UIntPtr bytes;
            Check(clGetDeviceInfo(handle, key, UIntPtr.Zero, IntPtr.Zero, out bytes), "OpenCL device information size");
            int length = checked((int)bytes.ToUInt64());
            IntPtr storage = Marshal.AllocHGlobal(Math.Max(length, 1));
            try
            {
                Check(clGetDeviceInfo(handle, key, bytes, storage, out bytes), "OpenCL device information");
                return Marshal.PtrToStringAnsi(storage) ?? "";
            }
            finally { Marshal.FreeHGlobal(storage); }
        }

        private static string BuildLog(IntPtr programHandle, IntPtr deviceHandle)
        {
            UIntPtr bytes;
            Check(clGetProgramBuildInfo(programHandle, deviceHandle, 0x1183, UIntPtr.Zero, IntPtr.Zero, out bytes), "OpenCL build log size");
            IntPtr storage = Marshal.AllocHGlobal(Math.Max(checked((int)bytes.ToUInt64()), 1));
            try
            {
                Check(clGetProgramBuildInfo(programHandle, deviceHandle, 0x1183, bytes, storage, out bytes), "OpenCL build log");
                return Marshal.PtrToStringAnsi(storage) ?? "";
            }
            finally { Marshal.FreeHGlobal(storage); }
        }

        private static void ReleaseBuffer(ref IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                clReleaseMemObject(handle); handle = IntPtr.Zero; Interlocked.Decrement(ref activeResources);
            }
        }

        private static void ReleaseEvent(ref IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                clReleaseEvent(handle); handle = IntPtr.Zero; Interlocked.Decrement(ref activeResources);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (queue != IntPtr.Zero) clFinish(queue);
                if (kernel != IntPtr.Zero) { clReleaseKernel(kernel); kernel = IntPtr.Zero; Interlocked.Decrement(ref activeResources); }
                if (program != IntPtr.Zero) { clReleaseProgram(program); program = IntPtr.Zero; Interlocked.Decrement(ref activeResources); }
                if (queue != IntPtr.Zero) { clReleaseCommandQueue(queue); queue = IntPtr.Zero; Interlocked.Decrement(ref activeResources); }
                if (context != IntPtr.Zero) { clReleaseContext(context); context = IntPtr.Zero; Interlocked.Decrement(ref activeResources); }
            }
            GC.SuppressFinalize(this);
        }

        ~QuantumGpu()
        {
            try { Dispose(); } catch { }
        }

        [DllImport(OpenClLibrary)] private static extern int clGetPlatformIDs(uint num_entries, [Out] IntPtr[] platforms, out uint num_platforms);
        [DllImport(OpenClLibrary)] private static extern int clGetDeviceIDs(IntPtr platform, ulong device_type, uint num_entries, [Out] IntPtr[] devices, out uint num_devices);
        [DllImport(OpenClLibrary)] private static extern int clGetDeviceInfo(IntPtr device, uint key, UIntPtr size, IntPtr value, out UIntPtr size_ret);
        [DllImport(OpenClLibrary, EntryPoint = "clGetDeviceInfo")] private static extern int clGetDeviceInfoULong(IntPtr device, uint key, UIntPtr size, out ulong value, IntPtr size_ret);
        [DllImport(OpenClLibrary)] private static extern IntPtr clCreateContext(IntPtr properties, uint num_devices, IntPtr[] devices, IntPtr notify, IntPtr user_data, out int errcode_ret);
        [DllImport(OpenClLibrary)] private static extern IntPtr clCreateCommandQueue(IntPtr context, IntPtr device, ulong properties, out int errcode_ret);
        [DllImport(OpenClLibrary)] private static extern IntPtr clCreateProgramWithSource(IntPtr context, uint count, IntPtr[] strings, UIntPtr[] lengths, out int errcode_ret);
        [DllImport(OpenClLibrary, CharSet = CharSet.Ansi)] private static extern int clBuildProgram(IntPtr program, uint num_devices, IntPtr[] devices, string options, IntPtr notify, IntPtr user_data);
        [DllImport(OpenClLibrary)] private static extern int clGetProgramBuildInfo(IntPtr program, IntPtr device, uint key, UIntPtr size, IntPtr value, out UIntPtr size_ret);
        [DllImport(OpenClLibrary, CharSet = CharSet.Ansi)] private static extern IntPtr clCreateKernel(IntPtr program, string name, out int errcode_ret);
        [DllImport(OpenClLibrary)] private static extern IntPtr clCreateBuffer(IntPtr context, ulong flags, UIntPtr size, IntPtr host_ptr, out int errcode_ret);
        [DllImport(OpenClLibrary, EntryPoint = "clSetKernelArg")] private static extern int clSetKernelArgPointer(IntPtr kernel, uint index, UIntPtr size, ref IntPtr value);
        [DllImport(OpenClLibrary, EntryPoint = "clSetKernelArg")] private static extern int clSetKernelArgULong(IntPtr kernel, uint index, UIntPtr size, ref ulong value);
        [DllImport(OpenClLibrary, EntryPoint = "clSetKernelArg")] private static extern int clSetKernelArgUInt(IntPtr kernel, uint index, UIntPtr size, ref uint value);
        [DllImport(OpenClLibrary, EntryPoint = "clSetKernelArg")] private static extern int clSetKernelArgDouble(IntPtr kernel, uint index, UIntPtr size, ref double value);
        [DllImport(OpenClLibrary)] private static extern int clEnqueueNDRangeKernel(IntPtr queue, IntPtr kernel, uint work_dim, UIntPtr[] global_offset, UIntPtr[] global_size, UIntPtr[] local_size, uint num_events, IntPtr[] event_list, out IntPtr result_event);
        [DllImport(OpenClLibrary)] private static extern int clWaitForEvents(uint num_events, IntPtr[] events);
        [DllImport(OpenClLibrary)] private static extern int clGetEventProfilingInfo(IntPtr event_handle, uint key, UIntPtr size, out ulong value, IntPtr size_ret);
        [DllImport(OpenClLibrary)] private static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, IntPtr destination, uint num_events, IntPtr[] event_list, IntPtr result_event);
        [DllImport(OpenClLibrary)] private static extern int clFinish(IntPtr queue);
        [DllImport(OpenClLibrary)] private static extern int clReleaseMemObject(IntPtr memory);
        [DllImport(OpenClLibrary)] private static extern int clReleaseKernel(IntPtr kernel);
        [DllImport(OpenClLibrary)] private static extern int clReleaseProgram(IntPtr program);
        [DllImport(OpenClLibrary)] private static extern int clReleaseCommandQueue(IntPtr queue);
        [DllImport(OpenClLibrary)] private static extern int clReleaseContext(IntPtr context);
        [DllImport(OpenClLibrary)] private static extern int clReleaseEvent(IntPtr event_handle);
    }
}
