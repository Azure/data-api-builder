// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;

/// <summary>
/// A helper to write to pooled arrays.
/// </summary>
internal sealed class ArrayPoolWriter : IBufferWriter<byte>, IDisposable
{
    private const int _initialBufferSize = 512;
    private byte[] _buffer;
    private int _capacity;
    private int _start;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayPoolWriter"/> class.
    /// </summary>
    public ArrayPoolWriter()
    {
        _buffer = ArrayPool<byte>.Shared.Rent(_initialBufferSize);
        _capacity = _buffer.Length;
        _start = 0;
    }
    
    /// <summary>
    /// Gets the part of the buffer that has been written to.
    /// </summary>
    /// <returns>
    /// A <see cref="ReadOnlyMemory{T}"/> of the written portion of the buffer.
    /// </returns>
    public ReadOnlyMemory<byte> GetWrittenMemory() 
        => _buffer.AsMemory()[.._start];

    /// <summary>
    /// Gets the part of the buffer that has been written to.
    /// </summary>
    /// <returns>
    /// A <see cref="ReadOnlySpan{T}"/> of the written portion of the buffer.
    /// </returns>
    public ReadOnlySpan<byte> GetWrittenSpan() 
        => _buffer.AsSpan()[.._start];

    /// <summary>
    /// Advances the writer by the specified number of bytes.
    /// </summary>
    /// <param name="count">
    /// The number of bytes to advance the writer by.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="count"/> is negative or
    /// if <paramref name="count"/> is greater than the
    /// available capacity on the internal buffer.
    /// </exception>
    public void Advance(int count)
    {
        if(_disposed)
        {
            throw new ObjectDisposedException(nameof(ArrayPoolWriter));
        }
        
        if(count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        
        if(count > _capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot advance past the end of the buffer.");
        }
        
        _start += count;
        _capacity -= count;
    }

    /// <summary>
    /// Gets a <see cref="Memory{T}"/> to write to.
    /// </summary>
    /// <param name="sizeHint">
    /// The minimum size of the returned <see cref="Memory{T}"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Memory{T}"/> to write to.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="sizeHint"/> is negative.
    /// </exception>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if(_disposed)
        {
            throw new ObjectDisposedException(nameof(ArrayPoolWriter));
        }
        
        if(sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }
        
        int size = sizeHint < 1 ? _initialBufferSize : sizeHint;
        EnsureBufferCapacity(size);
        return _buffer.AsMemory().Slice(_start, size);
    }

    /// <summary>
    /// Gets a <see cref="Span{T}"/> to write to.
    /// </summary>
    /// <param name="sizeHint">
    /// The minimum size of the returned <see cref="Span{T}"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Span{T}"/> to write to.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="sizeHint"/> is negative.
    /// </exception>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if(_disposed)
        {
            throw new ObjectDisposedException(nameof(ArrayPoolWriter));
        }
        
        if(sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }
        
        int size = sizeHint < 1 ? _initialBufferSize : sizeHint;
        EnsureBufferCapacity(size);
        return _buffer.AsSpan().Slice(_start, size);
    }

    private void EnsureBufferCapacity(int neededCapacity)
    {
        if (_capacity < neededCapacity)
        {
            byte[] buffer = _buffer;

            int newSize = buffer.Length * 2;
            if (neededCapacity > buffer.Length)
            {
                newSize += neededCapacity;
            }

            _buffer = ArrayPool<byte>.Shared.Rent(newSize);
            _capacity += _buffer.Length - buffer.Length;

            buffer.AsSpan().CopyTo(_buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = Array.Empty<byte>();
            _capacity = 0;
            _start = 0;
            _disposed = true;
        }
    }
}