using Silk.NET.OpenGL;

namespace BdvEngine;

public sealed class GLAttrInfo
{
    public uint Location;
    public int Size;
    internal int Offset;
}

public sealed class GLBuffer : IDisposable
{
    private readonly GL _gl = Gfx.Gl;
    private readonly uint _vao;
    private readonly uint _bufferId;
    private readonly BufferTargetARB _target;
    private readonly VertexAttribPointerType _type;
    private readonly PrimitiveType _mode;
    private readonly int _typeSize;
    private readonly bool _isArrayBuffer;

    private readonly List<GLAttrInfo> _attrs = new();
    private readonly List<float> _floatData = new();
    private readonly List<uint> _intData = new();

    private int _elementSize;
    private int _stride;
    private bool _attrsConfigured;
    private int _uploadedCount;

    private BufferUsageARB Usage { get; set; } = BufferUsageARB.StaticDraw;

    public GLBuffer(
        VertexAttribPointerType dataType = VertexAttribPointerType.Float,
        BufferTargetARB target = BufferTargetARB.ArrayBuffer,
        PrimitiveType mode = PrimitiveType.Triangles,
        BufferUsageARB usage = BufferUsageARB.StaticDraw)
    {
        _type = dataType;
        _target = target;
        _isArrayBuffer = target == BufferTargetARB.ArrayBuffer;
        _mode = mode;
        Usage = usage;
        _typeSize = dataType switch
        {
            VertexAttribPointerType.Float => 4,
            VertexAttribPointerType.Int => 4,
            VertexAttribPointerType.UnsignedInt => 4,
            VertexAttribPointerType.Short => 2,
            VertexAttribPointerType.UnsignedShort => 2,
            VertexAttribPointerType.Byte => 1,
            VertexAttribPointerType.UnsignedByte => 1,
            _ => throw new ArgumentException($"Unsupported type {dataType}")
        };

        _vao = _gl.GenVertexArray();
        _bufferId = _gl.GenBuffer();
    }

    public void AddAttrLocation(GLAttrInfo info)
    {
        info.Offset = _elementSize;
        _attrs.Add(info);
        _elementSize += info.Size;
        _stride = _elementSize * _typeSize;
    }

    public void SetData(IEnumerable<float> data)
    {
        _floatData.Clear();
        _floatData.AddRange(data);
    }

    public void SetData(IEnumerable<uint> data)
    {
        _intData.Clear();
        _intData.AddRange(data);
    }

    public void ClearData()
    {
        _floatData.Clear();
        _intData.Clear();
    }

    public void PushBack(IEnumerable<float> data) => _floatData.AddRange(data);
    public void PushBack(IEnumerable<uint> data) => _intData.AddRange(data);

    public unsafe void Upload()
    {
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(_target, _bufferId);

        if (_type == VertexAttribPointerType.Float)
        {
            var arr = _floatData.ToArray();
            fixed (float* p = arr)
            {
                _gl.BufferData(_target, (nuint)(arr.Length * sizeof(float)), p, Usage);
            }
            _uploadedCount = arr.Length;
        }
        else if (_type == VertexAttribPointerType.UnsignedInt)
        {
            var arr = _intData.ToArray();
            fixed (uint* p = arr)
            {
                _gl.BufferData(_target, (nuint)(arr.Length * sizeof(uint)), p, Usage);
            }
            _uploadedCount = arr.Length;
        }
        else
        {
            throw new NotSupportedException($"Buffer type {_type} not yet supported.");
        }

        if (_isArrayBuffer && !_attrsConfigured)
        {
            foreach (var attr in _attrs)
            {
                _gl.EnableVertexAttribArray(attr.Location);
                _gl.VertexAttribPointer(
                    attr.Location,
                    attr.Size,
                    _type,
                    false,
                    (uint)_stride,
                    (void*)(attr.Offset * _typeSize));
            }
            _attrsConfigured = true;
        }

        _gl.BindVertexArray(0);
    }

    public unsafe void Draw()
    {
        _gl.BindVertexArray(_vao);
        if (_isArrayBuffer)
        {
            _gl.DrawArrays(_mode, 0, (uint)(_uploadedCount / Math.Max(1, _elementSize)));
            GLStats.IncDrawCalls(_uploadedCount / Math.Max(1, _elementSize));
        }
        else
        {
            _gl.DrawElements(_mode, (uint)_uploadedCount,
                _type == VertexAttribPointerType.UnsignedInt
                    ? DrawElementsType.UnsignedInt
                    : DrawElementsType.UnsignedShort,
                null);
            GLStats.IncDrawCalls(_uploadedCount);
        }
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_bufferId);
        _gl.DeleteVertexArray(_vao);
    }
}
