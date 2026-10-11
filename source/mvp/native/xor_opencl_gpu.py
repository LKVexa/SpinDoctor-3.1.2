"""Real OpenCL XOR calculations on selected bits; no CPU GPU emulation."""
from __future__ import annotations
import ctypes as C
import numpy as np
from opencl_gpu import OpenCL, OpenCLError

SOURCE = r"""
uint shape(uint seed, ulong index) {
  uint x=seed ^ (uint)index ^ (uint)(index >> 32);
  x=(x ^ (x >> 16))*0x7feb352dU;
  x=(x ^ (x >> 15))*0x846ca68bU;
  return x ^ (x >> 16);
}
__kernel void xor_pairs(__global uint *base,__global ulong *words,
                        __global uint *offsets,__global uint *traces,
                        uint currentSeed,uint nextSeed,uint count) {
  uint i=(uint)get_global_id(0);if(i>=count)return;
  uint m0=(shape(currentSeed,words[i]) >> offsets[i]) & 1U;
  uint m1=(shape(nextSeed,words[i]) >> offsets[i]) & 1U;
  uint a=base[i] ^ m0;
  uint b=m0 ^ m1;
  uint answer=a ^ b; // One primary XOR per selected pair.
  uint expected=base[i] ^ m1;
  traces[8*i]=base[i];traces[8*i+1]=a;traces[8*i+2]=b;
  traces[8*i+3]=answer;traces[8*i+4]=expected;
  traces[8*i+5]=m0;traces[8*i+6]=m1;
  traces[8*i+7]=(uint)(answer==expected && answer<=1U);
}
"""

class XORGPU:
    def __init__(self,capacity,planned_device):
        self.cl=OpenCL();self.context=None;self.queue=None;self.program=None;self.kernel=None;self.buffers=[]
        self.capacity=capacity
        device=next((item for item in self.cl.devices() if item["name"]==planned_device["name"] and item["hostUnifiedMemory"]),None)
        if device is None:raise OpenCLError("The planned real integrated GPU is unavailable; no CPU fallback.")
        self.device=device;cl=self.cl;error=C.c_int()
        try:
            handles=(C.c_void_p*1)(device["handle"])
            self.context=cl.clCreateContext(None,1,handles,None,None,C.byref(error));cl.check(error.value,"XOR context creation")
            self.queue=cl.clCreateCommandQueue(self.context,device["handle"],2,C.byref(error));cl.check(error.value,"XOR queue creation")
            source=SOURCE.encode();sources=(C.c_char_p*1)(source);lengths=(C.c_size_t*1)(len(source))
            self.program=cl.clCreateProgramWithSource(self.context,1,sources,lengths,C.byref(error));cl.check(error.value,"XOR program creation")
            code=cl.clBuildProgram(self.program,1,handles,b"-cl-std=CL1.2",None,None)
            if code:
                size=C.c_size_t();cl.clGetProgramBuildInfo(self.program,device["handle"],0x1183,0,None,C.byref(size))
                log=C.create_string_buffer(max(size.value,1));cl.clGetProgramBuildInfo(self.program,device["handle"],0x1183,len(log),log,None)
                raise OpenCLError("Actual XOR kernel build failed: "+log.value.decode("utf-8","replace"))
            self.kernel=cl.clCreateKernel(self.program,b"xor_pairs",C.byref(error));cl.check(error.value,"XOR kernel creation")
            for byte_count in (capacity*4,capacity*8,capacity*4,capacity*32):
                if byte_count>device["maxAllocationBytes"]:raise OpenCLError("Selected staging exceeds GPU allocation limit.")
                buffer=cl.clCreateBuffer(self.context,1|16,byte_count,None,C.byref(error));cl.check(error.value,"XOR staging allocation");self.buffers.append(buffer)
        except Exception:self.release();raise

    def compute(self,base,words,offsets,current_seed,next_seed):
        count=len(base)
        if not 1<=count<=self.capacity:raise ValueError("Selected GPU count exceeds staging capacity.")
        cl=self.cl
        for buffer,array in zip(self.buffers[:3],(base,words,offsets)):
            cl.check(cl.clEnqueueWriteBuffer(self.queue,buffer,1,0,array.nbytes,C.c_void_p(array.ctypes.data),0,None,None),"actual selected XOR GPU staging")
        args=[C.c_void_p(buffer) for buffer in self.buffers]+[C.c_uint(current_seed),C.c_uint(next_seed),C.c_uint(count)]
        for index,value in enumerate(args):cl.check(cl.clSetKernelArg(self.kernel,index,C.sizeof(value),C.byref(value)),"XOR kernel argument")
        size=(C.c_size_t*1)(count);event=C.c_void_p()
        cl.check(cl.clEnqueueNDRangeKernel(self.queue,self.kernel,1,None,size,None,0,None,C.byref(event)),"actual selected XOR GPU dispatch")
        try:
            events=(C.c_void_p*1)(event.value);cl.check(cl.clWaitForEvents(1,events),"actual selected XOR GPU completion")
            start=C.c_ulonglong();end=C.c_ulonglong()
            cl.check(cl.clGetEventProfilingInfo(event,0x1282,8,C.byref(start),None),"XOR GPU event start")
            cl.check(cl.clGetEventProfilingInfo(event,0x1283,8,C.byref(end),None),"XOR GPU event end")
            event_ms=max(0,end.value-start.value)/1e6
        finally:cl.clReleaseEvent(event)
        traces=np.empty((count,8),dtype=np.uint32)
        cl.check(cl.clEnqueueReadBuffer(self.queue,self.buffers[3],1,0,traces.nbytes,C.c_void_p(traces.ctypes.data),0,None,None),"actual selected XOR GPU readback")
        return traces,event_ms

    def release(self):
        for buffer in self.buffers:self.cl.clReleaseMemObject(buffer)
        self.buffers=[]
        if self.kernel:self.cl.clReleaseKernel(self.kernel);self.kernel=None
        if self.program:self.cl.clReleaseProgram(self.program);self.program=None
        if self.queue:self.cl.clReleaseCommandQueue(self.queue);self.queue=None
        if self.context:self.cl.clReleaseContext(self.context);self.context=None
