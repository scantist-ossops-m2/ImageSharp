// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Memory;

/// <summary>
/// Memory managers are used to allocate memory for image processing operations.
/// </summary>
public abstract class MemoryAllocator
{
    /// <summary>
    /// Gets the default max allocatable size of a 1D buffer in bytes.
    /// </summary>
    public static readonly int DefaultMaxAllocatableSize1DInBytes = GetDefaultMaxAllocatableSize1DInBytes();

    /// <summary>
    /// Gets the default max allocatable size of a 2D buffer in bytes.
    /// </summary>
    public static readonly ulong DefaultMaxAllocatableSize2DInBytes = GetDefaultMaxAllocatableSize2DInBytes();

    /// <summary>
    /// Gets the default platform-specific global <see cref="MemoryAllocator"/> instance that
    /// serves as the default value for <see cref="Configuration.MemoryAllocator"/>.
    /// <para />
    /// This is a get-only property,
    /// you should set <see cref="Configuration.Default"/>'s <see cref="Configuration.MemoryAllocator"/>
    /// to change the default allocator used by <see cref="Image"/> and it's operations.
    /// </summary>
    public static MemoryAllocator Default { get; } = Create();

    /// <summary>
    /// Gets or sets the maximum allowable allocatable size of a 2 dimensional buffer.
    /// Defaults to <value><see cref="DefaultMaxAllocatableSize2DInBytes"/>.</value>
    /// </summary>
    public ulong MaxAllocatableSize2DInBytes { get; set; } = DefaultMaxAllocatableSize2DInBytes;

    /// <summary>
    /// Gets or sets the maximum allowable allocatable size of a 1 dimensional buffer.
    /// </summary>
    /// Defaults to <value><see cref="GetDefaultMaxAllocatableSize1DInBytes"/>.</value>
    public int MaxAllocatableSize1DInBytes { get; set; } = DefaultMaxAllocatableSize1DInBytes;

    /// <summary>
    /// Gets the length of the largest contiguous buffer that can be handled by this allocator instance in bytes.
    /// </summary>
    /// <returns>The length of the largest contiguous buffer that can be handled by this allocator instance.</returns>
    protected internal abstract int GetBufferCapacityInBytes();

    /// <summary>
    /// Creates a default instance of a <see cref="MemoryAllocator"/> optimized for the executing platform.
    /// </summary>
    /// <returns>The <see cref="MemoryAllocator"/>.</returns>
    public static MemoryAllocator Create() =>
        new UniformUnmanagedMemoryPoolMemoryAllocator(null);

    /// <summary>
    /// Creates the default <see cref="MemoryAllocator"/> using the provided options.
    /// </summary>
    /// <param name="options">The <see cref="MemoryAllocatorOptions"/>.</param>
    /// <returns>The <see cref="MemoryAllocator"/>.</returns>
    public static MemoryAllocator Create(MemoryAllocatorOptions options) =>
        new UniformUnmanagedMemoryPoolMemoryAllocator(options.MaximumPoolSizeMegabytes);

    /// <summary>
    /// Allocates an <see cref="IMemoryOwner{T}"/>, holding a <see cref="Memory{T}"/> of length <paramref name="length"/>.
    /// </summary>
    /// <typeparam name="T">Type of the data stored in the buffer.</typeparam>
    /// <param name="length">Size of the buffer to allocate.</param>
    /// <param name="options">The allocation options.</param>
    /// <returns>A buffer of values of type <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When length is zero or negative.</exception>
    /// <exception cref="InvalidMemoryOperationException">When length is over the capacity of the allocator.</exception>
    public abstract IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        where T : struct;

    /// <summary>
    /// Releases all retained resources not being in use.
    /// Eg: by resetting array pools and letting GC to free the arrays.
    /// </summary>
    public virtual void ReleaseRetainedResources()
    {
    }

    /// <summary>
    /// Allocates a <see cref="MemoryGroup{T}"/>.
    /// </summary>
    /// <typeparam name="T">Type of the data stored in the buffer.</typeparam>
    /// <param name="totalLength">The total length of the buffer.</param>
    /// <param name="bufferAlignment">The expected alignment (eg. to make sure image rows fit into single buffers).</param>
    /// <param name="options">The <see cref="AllocationOptions"/>.</param>
    /// <returns>A new <see cref="MemoryGroup{T}"/>.</returns>
    /// <exception cref="InvalidMemoryOperationException">Thrown when 'blockAlignment' converted to bytes is greater than the buffer capacity of the allocator.</exception>
    internal virtual MemoryGroup<T> AllocateGroup<T>(
        long totalLength,
        int bufferAlignment,
        AllocationOptions options = AllocationOptions.None)
        where T : struct
        => MemoryGroup<T>.Allocate(this, totalLength, bufferAlignment, options);

    internal void MemoryGuardAllocation2D<T>(Size value, string paramName)
        where T : struct
    {
        if (value.Width < 0 || value.Height < 0)
        {
            throw new InvalidMemoryOperationException($"An allocation was attempted that exceeded allowable limits; \"{paramName}\" at {value.Width}x{value.Height}");
        }

        ulong typeSizeInBytes = (ulong)Unsafe.SizeOf<T>();
        ulong valueInBytes = (ulong)value.Width * typeSizeInBytes * (ulong)value.Height;

        if (valueInBytes <= this.MaxAllocatableSize2DInBytes)
        {
            return;
        }

        throw new InvalidMemoryOperationException(
            $"An allocation was attempted that exceeded allowable limits; \"{paramName}\" at {value.Width}x{value.Height} must be less than or equal to {this.MaxAllocatableSize2DInBytes}, was {valueInBytes}");
    }

    internal void MemoryGuardAllocation1D<T>(int value, string paramName)
        where T : struct
    {
        if (value < 0)
        {
            throw new InvalidMemoryOperationException($"An allocation was attempted that exceeded allowable limits; {paramName} must be greater than or equal to zero, was {value}");
        }

        ulong typeSizeInBytes = (ulong)Unsafe.SizeOf<T>();
        ulong valueInBytes = (ulong)value * typeSizeInBytes;

        if (valueInBytes <= (ulong)this.MaxAllocatableSize1DInBytes)
        {
            return;
        }

        throw new InvalidMemoryOperationException(
            $"An allocation was attempted that exceeded allowable limits; \"{paramName}\" must be less than or equal {this.MaxAllocatableSize1DInBytes}, was {valueInBytes}");
    }

    private static ulong GetDefaultMaxAllocatableSize2DInBytes()
    {
        // Limit dimensions to 32767x32767 and 16383x16383 @ 4 bytes per pixel for 64 and 32 bit processes respectively.
        ulong maxLength = Environment.Is64BitProcess ? ushort.MaxValue / 2 : (ulong)short.MaxValue / 4;
        return maxLength * (ulong)Unsafe.SizeOf<Rgba32>() * maxLength;
    }

    private static int GetDefaultMaxAllocatableSize1DInBytes()
    {
        // It's possible to require buffers that are not related to image dimensions.
        // For example, when we need to allocate buffers for IDAT chunks in PNG files or when allocating
        // cache buffers for image quantization.
        // Limit the maximum buffer size to 64MB for 64-bit processes and 32MB for 32-bit processes.
        int limitInMB = Environment.Is64BitProcess ? 64 : 32;
        return limitInMB * 1024 * 1024;
    }
}
