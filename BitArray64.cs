using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// A high-performance 64-bit bit array implementation.
/// Uses a single ulong for arrays of size <= 64, and ulong[] for larger arrays.
/// </summary>
public sealed class BitArray64
{
    // Single ulong for small arrays
    private ulong _bits;
    
    // Array of ulongs for larger arrays
    private ulong[] _array;
    
    // Length of the bit array
    private readonly int _length;
    
    // Constant to determine how many bits are in a ulong
    private const int BitsPerULong = 64;
    
    // Mask for the bit index within a ulong (6 bits, as 2^6 = 64)
    private const int BitIndexMask = 0x3F;
    
    /// <summary>
    /// Gets the length of the bit array.
    /// </summary>
    public int Length => _length;
    
    /// <summary>
    /// Initializes a new instance of the BitArray64 class with the specified length, defaulting all bits to 0.
    /// </summary>
    /// <param name="length">The number of bits in the array.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitArray64(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than 0.");
        
        _length = length;
        
        if (length <= BitsPerULong)
        {
            _bits = 0UL;
            _array = null;
        }
        else
        {
            int arrayLength = (length + BitsPerULong - 1) / BitsPerULong;
            _array = new ulong[arrayLength];
            // No need to initialize as new arrays are zero-initialized by default
        }
    }
    
    /// <summary>
    /// Initializes a new instance of the BitArray64 class with the specified length, setting all bits to the specified value.
    /// </summary>
    /// <param name="length">The number of bits in the array.</param>
    /// <param name="defaultValue">The default value for all bits.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitArray64(int length, bool defaultValue)
        : this(length)
    {
        if (defaultValue)
        {
            SetAll();
        }
    }
    
    /// <summary>
    /// Gets or sets the bit at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to get or set.</param>
    /// <returns>The value of the bit at the specified index.</returns>
    public bool this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Get(index);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Set(index, value);
    }
    
    /// <summary>
    /// Gets the value of the bit at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to get.</param>
    /// <returns>The value of the bit at the specified index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Get(int index)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            return unchecked((_bits & (1UL << index)) != 0);
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask; // Faster than index % 64
            
            return unchecked((_array[arrayIndex] & (1UL << bitIndex)) != 0);
        }
    }
    
    /// <summary>
    /// Sets the bit at the specified index to the specified value.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to set.</param>
    /// <param name="value">The value to set (true for 1, false for 0).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index, bool value)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            if (value)
            {
                _bits |= 1UL << index;
            }
            else
            {
                _bits &= ~(1UL << index);
            }
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask; // Faster than index % 64
            
            if (value)
            {
                _array[arrayIndex] |= 1UL << bitIndex;
            }
            else
            {
                _array[arrayIndex] &= ~(1UL << bitIndex);
            }
        }
    }
    
    /// <summary>
    /// Sets the bit at the specified index to 1.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBit(int index)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            _bits |= 1UL << index;
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask;
            
            _array[arrayIndex] |= 1UL << bitIndex;
        }
    }
    
    /// <summary>
    /// Clears the bit at the specified index (sets it to 0).
    /// </summary>
    /// <param name="index">The zero-based index of the bit to clear.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearBit(int index)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            _bits &= ~(1UL << index);
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask;
            
            _array[arrayIndex] &= ~(1UL << bitIndex);
        }
    }
    
    /// <summary>
    /// Toggles the bit at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to toggle.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Toggle(int index)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            _bits ^= 1UL << index;
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask;
            
            _array[arrayIndex] ^= 1UL << bitIndex;
        }
    }
    
    /// <summary>
    /// Sets all bits in the array to 1.
    /// </summary>
    public void SetAll()
    {
        if (_length <= BitsPerULong)
        {
            // Create a mask with all 1s for the valid bits
            ulong mask = _length == 64 ? ulong.MaxValue : (1UL << _length) - 1;
            _bits = mask;
        }
        else
        {
            int fullULongCount = _length / BitsPerULong;
            int remainingBits = _length % BitsPerULong;
            
            // Set all complete ulongs to all 1s
            for (int i = 0; i < fullULongCount; i++)
            {
                _array[i] = ulong.MaxValue;
            }
            
            // Set the remaining bits in the last ulong if needed
            if (remainingBits > 0)
            {
                _array[fullULongCount] = (1UL << remainingBits) - 1;
            }
        }
    }
    
    /// <summary>
    /// Clears all bits in the array (sets them to 0).
    /// </summary>
    public void ClearAll()
    {
        if (_length <= BitsPerULong)
        {
            _bits = 0UL;
        }
        else
        {
            Array.Clear(_array, 0, _array.Length);
        }
    }
    
    /// <summary>
    /// Returns the number of bits set to 1 in the array.
    /// </summary>
    /// <returns>The count of bits set to 1.</returns>
    public int CountSetBits()
    {
        if (_length <= BitsPerULong)
        {
            return CountBits(_bits);
        }
        else
        {
            int count = 0;
            int len = (_length + BitsPerULong - 1) / BitsPerULong;
            
            for (int i = 0; i < len; i++)
            {
                count += CountBits(_array[i]);
            }
            
            return count;
        }
    }
    
    /// <summary>
    /// Counts the number of set bits in a ulong using population count algorithm.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountBits(ulong value)
    {
        // Uses the Brian Kernighan's algorithm: 
        // Repeatedly clear the least significant set bit until 0
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }
        return count;
    }
    
    /// <summary>
    /// Performs a bitwise AND operation with another BitArray64.
    /// </summary>
    /// <param name="other">The other BitArray64 to AND with.</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths.</exception>
    public void And(BitArray64 other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        if (_length != other._length)
            throw new ArgumentException("Array lengths must be equal for bitwise operations.", nameof(other));
        
        if (_length <= BitsPerULong)
        {
            _bits &= other._bits;
        }
        else
        {
            int len = (_length + BitsPerULong - 1) / BitsPerULong;
            
            for (int i = 0; i < len; i++)
            {
                _array[i] &= other._array[i];
            }
        }
    }
    
    /// <summary>
    /// Performs a bitwise OR operation with another BitArray64.
    /// </summary>
    /// <param name="other">The other BitArray64 to OR with.</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths.</exception>
    public void Or(BitArray64 other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        if (_length != other._length)
            throw new ArgumentException("Array lengths must be equal for bitwise operations.", nameof(other));
        
        if (_length <= BitsPerULong)
        {
            _bits |= other._bits;
        }
        else
        {
            int len = (_length + BitsPerULong - 1) / BitsPerULong;
            
            for (int i = 0; i < len; i++)
            {
                _array[i] |= other._array[i];
            }
        }
    }
    
    /// <summary>
    /// Performs a bitwise XOR operation with another BitArray64.
    /// </summary>
    /// <param name="other">The other BitArray64 to XOR with.</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths.</exception>
    public void Xor(BitArray64 other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        if (_length != other._length)
            throw new ArgumentException("Array lengths must be equal for bitwise operations.", nameof(other));
        
        if (_length <= BitsPerULong)
        {
            _bits ^= other._bits;
        }
        else
        {
            int len = (_length + BitsPerULong - 1) / BitsPerULong;
            
            for (int i = 0; i < len; i++)
            {
                _array[i] ^= other._array[i];
            }
        }
    }
    
    /// <summary>
    /// Performs a bitwise NOT operation (inverts all bits).
    /// </summary>
    public void Not()
    {
        if (_length <= BitsPerULong)
        {
            // Invert all bits, then clear the high bits that are beyond the length
            _bits = ~_bits;
            
            if (_length < BitsPerULong)
            {
                _bits &= (1UL << _length) - 1;
            }
        }
        else
        {
            int fullULongCount = _length / BitsPerULong;
            int remainingBits = _length % BitsPerULong;
            
            // Invert all complete ulongs
            for (int i = 0; i < fullULongCount; i++)
            {
                _array[i] = ~_array[i];
            }
            
            // Invert the remaining bits in the last ulong if needed
            if (remainingBits > 0)
            {
                int lastIndex = fullULongCount;
                ulong mask = (1UL << remainingBits) - 1;
                _array[lastIndex] = (~_array[lastIndex]) & mask;
            }
        }
    }
    
    /// <summary>
    /// Returns the index of the first bit that is set to true that occurs on or after the specified position.
    /// </summary>
    /// <param name="startIndex">The search starting position.</param>
    /// <returns>The index of the next set bit, or -1 if no set bits are found.</returns>
    public int NextSetBit(int startIndex)
    {
        if ((uint)startIndex >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        
        if (_length <= BitsPerULong)
        {
            // Check if there are any bits set from startIndex onward
            ulong mask = ~((1UL << startIndex) - 1);
            ulong result = _bits & mask;
            
            if (result == 0)
                return -1;
            
            // Find the position of the lowest set bit
            return FindLowestSetBit(result);
        }
        else
        {
            int arrayIndex = startIndex / BitsPerULong;
            int bitIndex = startIndex & BitIndexMask;
            
            // Create a mask for the first ulong to check
            ulong mask = ~((1UL << bitIndex) - 1);
            ulong current = _array[arrayIndex] & mask;
            
            // If we found a set bit in the current ulong, return its position
            if (current != 0)
            {
                return (arrayIndex * BitsPerULong) + FindLowestSetBit(current);
            }
            
            // Check remaining ulongs
            for (int i = arrayIndex + 1; i < _array.Length; i++)
            {
                if (_array[i] != 0)
                {
                    return (i * BitsPerULong) + FindLowestSetBit(_array[i]);
                }
            }
            
            return -1;
        }
    }
    
    /// <summary>
    /// Returns the index of the first bit that is set to false that occurs on or after the specified position.
    /// </summary>
    /// <param name="startIndex">The search starting position.</param>
    /// <returns>The index of the next clear bit, or -1 if no clear bits are found.</returns>
    public int NextClearBit(int startIndex)
    {
        if ((uint)startIndex >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        
        if (_length <= BitsPerULong)
        {
            // Check if there are any bits clear from startIndex onward
            ulong mask = ~((1UL << startIndex) - 1);
            ulong result = (~_bits) & mask;
            
            if (result == 0)
                return -1;
            
            // Find the position of the lowest clear bit
            return FindLowestSetBit(result);
        }
        else
        {
            int arrayIndex = startIndex / BitsPerULong;
            int bitIndex = startIndex & BitIndexMask;
            
            // Create a mask for the first ulong to check
            ulong mask = ~((1UL << bitIndex) - 1);
            ulong current = (~_array[arrayIndex]) & mask;
            
            // If we found a clear bit in the current ulong, return its position
            if (current != 0)
            {
                int position = (arrayIndex * BitsPerULong) + FindLowestSetBit(current);
                return position < _length ? position : -1;
            }
            
            // Check remaining ulongs
            for (int i = arrayIndex + 1; i < _array.Length; i++)
            {
                ulong inverted = ~_array[i];
                if (inverted != 0)
                {
                    int position = (i * BitsPerULong) + FindLowestSetBit(inverted);
                    return position < _length ? position : -1;
                }
            }
            
            return -1;
        }
    }
    
    /// <summary>
    /// Finds the position of the lowest set bit using De Bruijn sequence.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLowestSetBit(ulong value)
    {
        // De Bruijn sequence-based algorithm for finding the index of the least significant bit
        if (value == 0) return -1;
        
        const ulong debruijn64 = 0x03f79d71b4cb0a89UL;
        
        // Isolate the lowest bit
        ulong isolatedBit = value & ((~value) + 1);
        
        // Multiply by the de Bruijn constant and shift right
        int index = BitPositionTable[(int)((isolatedBit * debruijn64) >> 58)];
        
        return index;
    }
    
    // Lookup table for the position of the lowest bit using de Bruijn sequence
    private static readonly int[] BitPositionTable = 
    {
        0, 1, 2, 7, 3, 13, 8, 19, 4, 25, 14, 28, 9, 34, 20, 40,
        5, 17, 26, 38, 15, 46, 29, 48, 10, 31, 35, 54, 21, 50, 41, 57,
        63, 6, 12, 18, 24, 27, 33, 39, 16, 37, 45, 47, 30, 53, 49, 56,
        62, 11, 23, 32, 36, 44, 52, 55, 61, 22, 43, 51, 60, 42, 59, 58
    };
    
    #region Thread-Safe Operations
    
    /// <summary>
    /// Thread-safe method to get the value of the bit at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to get.</param>
    /// <returns>The value of the bit at the specified index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetAtomic(int index)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            // Read the value atomically
            ulong bits = Interlocked.Read(ref _bits);
            return unchecked((bits & (1UL << index)) != 0);
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask;
            
            // Read the value atomically
            ulong value = Interlocked.Read(ref _array[arrayIndex]);
            return unchecked((value & (1UL << bitIndex)) != 0);
        }
    }
    
    /// <summary>
    /// Thread-safe method to set the bit at the specified index to the specified value.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to set.</param>
    /// <param name="value">The value to set (true for 1, false for 0).</param>
    public void SetAtomic(int index, bool value)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            ulong mask = 1UL << index;
            
            if (value)
            {
                // Set the bit atomically using OR
                Interlocked.Or(ref _bits, mask);
            }
            else
            {
                // Clear the bit atomically using AND with complement
                Interlocked.And(ref _bits, ~mask);
            }
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask;
            ulong mask = 1UL << bitIndex;
            
            if (value)
            {
                // Set the bit atomically using OR
                Interlocked.Or(ref _array[arrayIndex], mask);
            }
            else
            {
                // Clear the bit atomically using AND with complement
                Interlocked.And(ref _array[arrayIndex], ~mask);
            }
        }
    }
    
    /// <summary>
    /// Thread-safe method to toggle the bit at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the bit to toggle.</param>
    /// <returns>The new value of the bit after toggling.</returns>
    public bool ToggleAtomic(int index)
    {
        if ((uint)index >= (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        if (_length <= BitsPerULong)
        {
            ulong mask = 1UL << index;
            ulong oldValue, newValue;
            
            do
            {
                oldValue = _bits;
                newValue = oldValue ^ mask;
            }
            while (Interlocked.CompareExchange(ref _bits, newValue, oldValue) != oldValue);
            
            return (newValue & mask) != 0;
        }
        else
        {
            int arrayIndex = index / BitsPerULong;
            int bitIndex = index & BitIndexMask;
            ulong mask = 1UL << bitIndex;
            ulong oldValue, newValue;
            
            do
            {
                oldValue = _array[arrayIndex];
                newValue = oldValue ^ mask;
            }
            while (Interlocked.CompareExchange(ref _array[arrayIndex], newValue, oldValue) != oldValue);
            
            return (newValue & mask) != 0;
        }
    }
    
    #endregion
    
    #region Unsafe Operations
    
    /// <summary>
    /// Unsafe operations for high-performance scenarios where bounds checking is not needed.
    /// Use with caution - these methods bypass safety checks for maximum performance.
    /// </summary>
    public static unsafe class UnsafeOperations
    {
        /// <summary>
        /// Gets the bit at the specified index without bounds checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool GetUnchecked(BitArray64 bitArray, int index)
        {
            if (bitArray._length <= BitsPerULong)
            {
                return (bitArray._bits & (1UL << index)) != 0;
            }
            else
            {
                int arrayIndex = index / BitsPerULong;
                int bitIndex = index & BitIndexMask;
                
                fixed (ulong* pArray = bitArray._array)
                {
                    return (pArray[arrayIndex] & (1UL << bitIndex)) != 0;
                }
            }
        }
        
        /// <summary>
        /// Sets the bit at the specified index without bounds checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetUnchecked(BitArray64 bitArray, int index, bool value)
        {
            if (bitArray._length <= BitsPerULong)
            {
                if (value)
                {
                    bitArray._bits |= 1UL << index;
                }
                else
                {
                    bitArray._bits &= ~(1UL << index);
                }
            }
            else
            {
                int arrayIndex = index / BitsPerULong;
                int bitIndex = index & BitIndexMask;
                
                fixed (ulong* pArray = bitArray._array)
                {
                    if (value)
                    {
                        pArray[arrayIndex] |= 1UL << bitIndex;
                    }
                    else
                    {
                        pArray[arrayIndex] &= ~(1UL << bitIndex);
                    }
                }
            }
        }
    }
    
    #endregion
}