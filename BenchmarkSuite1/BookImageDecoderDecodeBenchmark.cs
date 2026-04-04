using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using DgRead.Chaek;

namespace DgRead.Benchmarks;
[CPUUsageDiagnoser]
public class BookImageDecoderDecodeBenchmark
{
    private byte[] _samplePng = null!;
    private MethodInfo _decode = null!;
    [GlobalSetup]
    public void Setup()
    {
        _samplePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAyAAAAJYCAYAAACadoJwAAAACXBIWXMAAAsSAAALEgHS3X78AAAAGXRFWHRTb2Z0d2FyZQBwYWludC5uZXQgNC4wLjEyQ0R0SAAAACdJREFUeF7twTEBAAAAwqD1T20ND6AAAAAAAAAAAAAAAAAAAAAA4N8GAAE3s2QhAAAAAElFTkSuQmCC");
        var type = typeof(BookBase).Assembly.GetType("DgRead.Chaek.BookImageDecoder", throwOnError: true)!;
        _decode = type.GetMethod("Decode", BindingFlags.Public | BindingFlags.Static)!;
    }

    [Benchmark]
    public void Decode_StaticPng()
    {
        if (_decode.Invoke(null, [_samplePng]) is IDisposable page)
            page.Dispose();
    }
}