"""Small standard-library OpenCL binding. GPU failures never select a CPU fallback."""
from __future__ import annotations
import ctypes as C
import os

GPU = 4
PLATFORM_INFO = {"name":0x0902,"vendor":0x0903,"version":0x0901}
DEVICE_INFO = {"name":0x102B,"vendor":0x102C,"driver":0x102D,"version":0x102F,
               "globalMemoryBytes":0x101F,"maxAllocationBytes":0x1010,
               "hostUnifiedMemory":0x1035,"computeUnits":0x1002,"maxWorkGroupSize":0x1004}

KERNEL_SOURCE = r"""
uint word_shape(uint seed, ulong index) {
  uint x = seed ^ (uint)index ^ (uint)(index >> 32);
  x = (x ^ (x >> 16)) * 0x7feb352dU;
  x = (x ^ (x >> 15)) * 0x846ca68bU;
  return x ^ (x >> 16);
}
__kernel void initialize(__global uint *state, uint seed, ulong base,
                         uint offset, uint count, ulong last, uint tail) {
  uint i = (uint)get_global_id(0); if (i >= count) return;
  ulong index = base + offset + i;
  uint x = word_shape(seed,index); if(index == last) x &= tail;
  state[offset+i] = x;
}
__kernel void product(__global uint *state,__global uint *scratch,uint mask,
                      ulong base,uint offset,uint count,ulong last,uint tail) {
  uint i = (uint)get_global_id(0); if (i >= count) return;
  uint x = state[offset+i] & mask;
  if(base+offset+i == last) x &= tail;
  scratch[offset+i] = x;
}
__kernel void answer(__global uint *scratch,__global uint *state,uint mask,
                     ulong base,uint offset,uint count,ulong last,uint tail) {
  uint i = (uint)get_global_id(0); if (i >= count) return;
  uint x = scratch[offset+i] ^ mask;
  if(base+offset+i == last) x &= tail;
  state[offset+i] = x;
}
__kernel void samples(__global uint *previous,__global uint *observed,
                      __global uint *traces,uint a,uint b,uint count) {
  uint i=(uint)get_global_id(0);if(i>=count)return;
  uint product=a*previous[i];uint sum=product+b;uint result=sum&1U;
  traces[4*i]=product;traces[4*i+1]=sum;traces[4*i+2]=result;
  traces[4*i+3]=(uint)(result==observed[i]);
}
"""

class OpenCLError(RuntimeError): pass

class OpenCL:
    def __init__(self):
        self.lib = C.WinDLL("OpenCL.dll") if os.name == "nt" else C.CDLL("libOpenCL.so.1")
        p=C.c_void_p; u=C.c_uint; q=C.c_ulonglong; z=C.c_size_t; i=C.c_int
        signatures={
          "clGetPlatformIDs":(i,[u,C.POINTER(p),C.POINTER(u)]),
          "clGetPlatformInfo":(i,[p,u,z,p,C.POINTER(z)]),
          "clGetDeviceIDs":(i,[p,q,u,C.POINTER(p),C.POINTER(u)]),
          "clGetDeviceInfo":(i,[p,u,z,p,C.POINTER(z)]),
          "clCreateContext":(p,[p,u,C.POINTER(p),p,p,C.POINTER(i)]),
          "clCreateCommandQueue":(p,[p,p,q,C.POINTER(i)]),
          "clCreateProgramWithSource":(p,[p,u,C.POINTER(C.c_char_p),C.POINTER(z),C.POINTER(i)]),
          "clBuildProgram":(i,[p,u,C.POINTER(p),C.c_char_p,p,p]),
          "clGetProgramBuildInfo":(i,[p,p,u,z,p,C.POINTER(z)]),
          "clCreateKernel":(p,[p,C.c_char_p,C.POINTER(i)]),
          "clCreateBuffer":(p,[p,q,z,p,C.POINTER(i)]),
          "clSetKernelArg":(i,[p,u,z,p]),
          "clEnqueueNDRangeKernel":(i,[p,p,u,C.POINTER(z),C.POINTER(z),C.POINTER(z),u,C.POINTER(p),C.POINTER(p)]),
          "clWaitForEvents":(i,[u,C.POINTER(p)]),
          "clGetEventProfilingInfo":(i,[p,u,z,p,C.POINTER(z)]),
          "clEnqueueReadBuffer":(i,[p,p,u,z,z,p,u,C.POINTER(p),C.POINTER(p)]),
          "clEnqueueWriteBuffer":(i,[p,p,u,z,z,p,u,C.POINTER(p),C.POINTER(p)]),
          "clFinish":(i,[p]),
          "clReleaseMemObject":(i,[p]),"clReleaseKernel":(i,[p]),
          "clReleaseProgram":(i,[p]),"clReleaseCommandQueue":(i,[p]),
          "clReleaseContext":(i,[p]),"clReleaseEvent":(i,[p])}
        for name,(restype,args) in signatures.items():
            function=getattr(self.lib,name);function.restype=restype;function.argtypes=args;setattr(self,name,function)
    @staticmethod
    def check(code,operation):
        if code != 0: raise OpenCLError(f"{operation} failed with OpenCL code {code}.")
    def text(self,function,handle,key):
        size=C.c_size_t();self.check(function(handle,key,0,None,C.byref(size)),"info size")
        output=C.create_string_buffer(size.value);self.check(function(handle,key,size.value,output,None),"info")
        return output.value.decode("utf-8","replace")
    def devices(self):
        count=C.c_uint();self.check(self.clGetPlatformIDs(0,None,C.byref(count)),"platform enumeration")
        platforms=(C.c_void_p*count.value)();self.check(self.clGetPlatformIDs(count.value,platforms,None),"platform list")
        result=[]
        for platform in platforms:
            platform_info={name:self.text(self.clGetPlatformInfo,platform,key) for name,key in PLATFORM_INFO.items()}
            number=C.c_uint();code=self.clGetDeviceIDs(platform,GPU,0,None,C.byref(number))
            if code == -1:continue
            self.check(code,"GPU enumeration");devices=(C.c_void_p*number.value)();self.check(self.clGetDeviceIDs(platform,GPU,number.value,devices,None),"GPU list")
            for device in devices:
                info={"platform":platform_info,"handle":device}
                for name,key in DEVICE_INFO.items():
                    if name in ("name","vendor","driver","version"):info[name]=self.text(self.clGetDeviceInfo,device,key)
                    else:
                        value=C.c_ulonglong() if name in ("globalMemoryBytes","maxAllocationBytes") else C.c_size_t() if name=="maxWorkGroupSize" else C.c_uint()
                        self.check(self.clGetDeviceInfo(device,key,C.sizeof(value),C.byref(value),None),"device integer info");info[name]=value.value
                info["hostUnifiedMemory"]=bool(info["hostUnifiedMemory"]);result.append(info)
        return result

class GPUContext:
    def __init__(self,cl:OpenCL,device):
        self.cl=cl;self.device=device;self.context=None;self.queue=None;self.program=None;self.kernels={};self.buffers=[]
        try:
            handles=(C.c_void_p*1)(device["handle"]);error=C.c_int()
            self.context=cl.clCreateContext(None,1,handles,None,None,C.byref(error));cl.check(error.value,"context creation")
            self.queue=cl.clCreateCommandQueue(self.context,device["handle"],2,C.byref(error));cl.check(error.value,"profiling queue creation")
            encoded=KERNEL_SOURCE.encode();sources=(C.c_char_p*1)(encoded);lengths=(C.c_size_t*1)(len(encoded))
            self.program=cl.clCreateProgramWithSource(self.context,1,sources,lengths,C.byref(error));cl.check(error.value,"program creation")
            code=cl.clBuildProgram(self.program,1,handles,b"-cl-std=CL1.2",None,None)
            if code:
                size=C.c_size_t();cl.clGetProgramBuildInfo(self.program,device["handle"],0x1183,0,None,C.byref(size));log=C.create_string_buffer(max(size.value,1));cl.clGetProgramBuildInfo(self.program,device["handle"],0x1183,len(log),log,None)
                raise OpenCLError("GPU kernel build failed: "+log.value.decode("utf-8","replace"))
            for name in ("initialize","product","answer","samples"):
                kernel=cl.clCreateKernel(self.program,name.encode(),C.byref(error));cl.check(error.value,"kernel creation");self.kernels[name]=kernel
        except Exception:self.release();raise
    def allocate(self,byte_count):
        if byte_count>self.device["maxAllocationBytes"]:raise OpenCLError("GPU bank exceeds device maximum allocation.")
        error=C.c_int();buffer=self.cl.clCreateBuffer(self.context,1|16,byte_count,None,C.byref(error));self.cl.check(error.value,"host-backed GPU buffer allocation");self.buffers.append(buffer);return buffer
    def dispatch(self,name,args,count):
        kernel=self.kernels[name]
        for index,value in enumerate(args):self.cl.check(self.cl.clSetKernelArg(kernel,index,C.sizeof(value),C.byref(value)),"kernel argument")
        global_size=(C.c_size_t*1)(count);event=C.c_void_p()
        self.cl.check(self.cl.clEnqueueNDRangeKernel(self.queue,kernel,1,None,global_size,None,0,None,C.byref(event)),"kernel dispatch")
        try:
            events=(C.c_void_p*1)(event.value);self.cl.check(self.cl.clWaitForEvents(1,events),"GPU completion event")
            start=C.c_ulonglong();end=C.c_ulonglong();self.cl.check(self.cl.clGetEventProfilingInfo(event,0x1282,8,C.byref(start),None),"event start time");self.cl.check(self.cl.clGetEventProfilingInfo(event,0x1283,8,C.byref(end),None),"event end time")
            return max(0,end.value-start.value)/1e6
        finally:self.cl.clReleaseEvent(event)
    def read_word(self,buffer,index):
        result=C.c_uint();self.cl.check(self.cl.clEnqueueReadBuffer(self.queue,buffer,1,index*4,4,C.byref(result),0,None,None),"actual GPU readback");return result.value
    def release(self):
        for buffer in self.buffers:self.cl.clReleaseMemObject(buffer)
        self.buffers=[]
        for kernel in self.kernels.values():self.cl.clReleaseKernel(kernel)
        self.kernels={}
        if self.program:self.cl.clReleaseProgram(self.program);self.program=None
        if self.queue:self.cl.clReleaseCommandQueue(self.queue);self.queue=None
        if self.context:self.cl.clReleaseContext(self.context);self.context=None

if __name__ == "__main__":
    import json
    api=OpenCL();devices=api.devices();print(json.dumps([{key:value for key,value in device.items() if key!="handle"} for device in devices],indent=2))
