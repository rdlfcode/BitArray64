using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BitManipulation
{
    /// <summary>
    /// A highly optimized 64-bit bit array implementation that uses a single
    // ulong for arrays of length 64 or less, and an array of ulongs for larger sizes.
    /// </summary>
    [Serializable]
    public sealed class BitArray64 : IEnumerable<bool>, IEquatable<BitArray64>
    {
        #region Data Members
        // Constants for optimization
        private const int BitsPerUlong = 64;
        private const int BitsPerUlongShift = 6; // 2^6 = 64, used for division/multiplication by 64
        private const ulong AllBitsSet = ~0UL;

        // De Bruijn sequence and position table for finding the position of the least significant bit
        private static readonly ulong DeBruijnSequence = 0x03f79d71b4cb0a89UL;
        private static readonly int[] DeBruijnPositionTable = {
            0, 1, 48, 2, 57, 49, 28, 3, 61, 58, 50, 42, 38, 29, 17, 4,
            62, 55, 59, 36, 53, 51, 43, 22, 45, 39, 33, 30, 24, 18, 12, 5,
            63, 47, 56, 27, 60, 41, 37, 16, 54, 35, 52, 21, 44, 32, 23, 11,
            46, 26, 40, 15, 34, 20, 31, 10, 25, 14, 19, 9, 13, 8, 7, 6
        };

        // Storage fields
        private readonly int _length;
        private readonly ulong _singleValue; // Used when length <= 64
        private readonly ulong[] _values;    // Used when length > 64
        private readonly int _spinLockFlag;  // For lightweight synchronization

        /// <summary>
        /// Gets the length of the bit array.
        /// </summary>
        public int Length => _length;

        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the BitArray64 class with the specified length.
        /// All bits are initially set to false.
        /// </summary>
        /// <param name="length">The number of bits in the array.</param>
        public BitArray64(int length)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than 0.");

            _length = length;
            _spinLockFlag = 0;

            if (length <= BitsPerUlong)
            {
                _singleValue = 0UL;
                _values = null;
            }
            else
            {
                _values = new ulong[GetArrayLength(length)];
                _singleValue = 0UL; // Required for proper struct layout
            }
        }

        /// <summary>
        /// Creates a BitArray64 from an existing array of boolean values.
        /// </summary>
        /// <param name="values">Array of boolean values to initialize from.</param>
        public BitArray64(bool[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            
            _length = values.Length;
            _spinLockFlag = 0;

            if (_length <= BitsPerUlong)
            {
                ulong result = 0UL;
                for (int i = 0; i < _length; i++)
                {
                    if (values[i])
                        result |= 1UL << i;
                }
                _singleValue = result;
                _values = null;
            }
            else
            {
                _values = new ulong[GetArrayLength(_length)];
                
                for (int i = 0; i < _length; i++)
                {
                    if (values[i])
                        Set(i, true);
                }
                _singleValue = 0UL; // Required for proper struct layout
            }
        }

        /// <summary>
        /// Creates a BitArray64 from an array of bytes.
        /// </summary>
        /// <param name="bytes">Array of bytes to initialize from.</param>
        public BitArray64(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            _length = bytes.Length * 8;
            _spinLockFlag = 0;

            if (_length <= BitsPerUlong)
            {
                ulong result = 0UL;
                for (int i = 0; i < bytes.Length; i++)
                {
                    result |= (ulong)bytes[i] << (i * 8);
                }
                _singleValue = result;
                _values = null;
            }
            else
            {
                _values = new ulong[GetArrayLength(_length)];
                
                for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
                {
                    byte currentByte = bytes[byteIndex];
                    int bitArrayIndex = byteIndex * 8;
                    
                    for (int bitIndex = 0; bitIndex < 8; bitIndex++)
                    {
                        if ((currentByte & (1 << bitIndex)) != 0)
                            Set(bitArrayIndex + bitIndex, true);
                    }
                }
                _singleValue = 0UL; // Required for proper struct layout
            }
        }

        /// <summary>
        /// Copy constructor for creating a deep copy of another BitArray64.
        /// </summary>
        /// <param name="other">The BitArray64 to copy.</param>
        public BitArray64(BitArray64 other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            _length = other._length;
            _spinLockFlag = 0;

            if (_length <= BitsPerUlong)
            {
                _singleValue = other._singleValue;
                _values = null;
            }
            else
            {
                _values = new ulong[other._values.Length];
                Array.Copy(other._values, _values, other._values.Length);
                _singleValue = 0UL; // Required for proper struct layout
            }
        }

        #endregion
        
        #region Public Methods

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
            ValidateIndex(index);
            
            if (_length <= BitsPerUlong)
            {
                return (_singleValue & (1UL << index)) != 0;
            }
            else
            {
                int arrayIndex = index >> BitsPerUlongShift;        // Divide by 64
                int bitIndex = index & (BitsPerUlong - 1);          // Modulo 64
                return (_values[arrayIndex] & (1UL << bitIndex)) != 0;
            }
        }

        /// <summary>
        /// Sets the bit at the specified index to the specified value.
        /// </summary>
        /// <param name="index">The zero-based index of the bit to set.</param>
        /// <param name="value">The value to set the bit to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index, bool value)
        {
            ValidateIndex(index);

            if (_length <= BitsPerUlong)
            {
                // Use a local variable to avoid multiple memory accesses
                ulong localVal = _singleValue;
                
                if (value)
                    localVal |= 1UL << index;
                else
                    localVal &= ~(1UL << index);

                // Ensure memory ordering - store with release semantics
                Thread.MemoryBarrier();
                Interlocked.Exchange(ref _singleValue, localVal);
            }
            else
            {
                int arrayIndex = index >> BitsPerUlongShift;        // Divide by 64
                int bitIndex = index & (BitsPerUlong - 1);          // Modulo 64
                
                EnterSpinLock();
                try
                {
                    if (value)
                        _values[arrayIndex] |= 1UL << bitIndex;
                    else
                        _values[arrayIndex] &= ~(1UL << bitIndex);
                    
                    // Ensure memory ordering
                    Thread.MemoryBarrier();
                }
                finally
                {
                    ExitSpinLock();
                }
            }
        }

        /// <summary>
        /// Resizes the BitArray64 to the specified new length, preserving as much data as possible.
        /// </summary>
        /// <param name="newLength">The new length of the bit array.</param>
        public void Resize(int newLength)
        {
            if (newLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(newLength), "Length must be greater than 0.");
                
            if (newLength == _length)
                return;
                
            BitArray64 newArray = new BitArray64(newLength);
            
            // Copy as many bits as possible to the new array
            int copyLength = Math.Min(_length, newLength);
            for (int i = 0; i < copyLength; i++)
            {
                if (Get(i))
                    newArray.Set(i, true);
            }
            
            // If we're going from single ulong to array storage
            if (_length <= BitsPerUlong && newLength > BitsPerUlong)
            {
                // Create new array storage and transfer bits
                _values = new ulong[GetArrayLength(newLength)];
            }
            // If we're going from array storage to single ulong
            else if (_length > BitsPerUlong && newLength <= BitsPerUlong)
            {
                // Clear array storage, bits will be in _singleValue
                _values = null;
            }
            // If we're staying in array storage but changing size
            else if (_length > BitsPerUlong && newLength > BitsPerUlong)
            {
                // Resize the array if needed
                if (GetArrayLength(_length) != GetArrayLength(newLength))
                {
                    ulong[] newValues = new ulong[GetArrayLength(newLength)];
                    Array.Copy(_values, newValues, Math.Min(_values.Length, newValues.Length));
                    _values = newValues;
                }
            }
            
            // Always update _length and copy all bits from temporary array
            _length = newLength;
            for (int i = 0; i < copyLength; i++)
            {
                Set(i, newArray.Get(i));
            }
        }
        
        /// <summary>
        /// Safely toggles the bit at the specified index and returns the new value.
        /// </summary>
        /// <param name="index">The zero-based index of the bit to toggle.</param>
        /// <returns>The new value of the bit at the specified index after toggling.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Toggle(int index)
        {
            ValidateIndex(index);
            
            if (_length <= BitsPerUlong)
            {
                // Use a local variable to avoid multiple memory accesses
                ulong localVal = _singleValue;
                localVal ^= 1UL << index;
                
                // Ensure memory ordering - store with release semantics
                Thread.MemoryBarrier();
                Interlocked.Exchange(ref _singleValue, localVal);
                
                return (localVal & (1UL << index)) != 0;
            }
            else
            {
                int arrayIndex = index >> BitsPerUlongShift;        // Divide by 64
                int bitIndex = index & (BitsPerUlong - 1);          // Modulo 64
                bool newValue;
                
                EnterSpinLock();
                try
                {
                    _values[arrayIndex] ^= 1UL << bitIndex;
                    newValue = (_values[arrayIndex] & (1UL << bitIndex)) != 0;
                    
                    // Ensure memory ordering
                    Thread.MemoryBarrier();
                }
                finally
                {
                    ExitSpinLock();
                }
                
                return newValue;
            }
        }

        /// <summary>
        /// Sets all bits in the array to the specified value.
        /// </summary>
        /// <param name="value">The value to set all bits to.</param>
        public void SetAll(bool value)
        {
            ulong setValue = value ? AllBitsSet : 0UL;
            
            if (_length <= BitsPerUlong)
            {
                ulong mask = AllBitsSet >> (BitsPerUlong - _length);
                
                // Ensure memory ordering - store with release semantics
                Thread.MemoryBarrier();
                Interlocked.Exchange(ref _singleValue, setValue & mask);
            }
            else
            {
                EnterSpinLock();
                try
                {
                    // Set all full ulongs
                    int fullUlongs = _length >> BitsPerUlongShift; // Divide by 64
                    for (int i = 0; i < fullUlongs; i++)
                    {
                        _values[i] = setValue;
                    }
                    
                    // Handle the remaining bits if length is not a multiple of 64
                    int remainingBits = _length & (BitsPerUlong - 1); // Modulo 64
                    if (remainingBits > 0)
                    {
                        ulong mask = AllBitsSet >> (BitsPerUlong - remainingBits);
                        _values[fullUlongs] = setValue & mask;
                    }
                    
                    // Ensure memory ordering
                    Thread.MemoryBarrier();
                }
                finally
                {
                    ExitSpinLock();
                }
            }
        }

        /// <summary>
        /// Sets all bits in the array to 0 (false).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            SetAll(false);
        }

        /// <summary>
        /// Sets all bits in the array to 1 (true).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAllTrue()
        {
            SetAll(true);
        }

        /// <summary>
        /// Returns the number of bits set to true in the array.
        /// Uses the efficient Hamming Weight algorithm.
        /// </summary>
        /// <returns>The number of bits set to true.</returns>
        public int CountSetBits()
        {
            if (_length <= BitsPerUlong)
            {
                return HammingWeight(_singleValue);
            }
            else
            {
                int count = 0;
                int fullUlongs = _length >> BitsPerUlongShift; // Divide by 64
                
                EnterSpinLock();
                try
                {
                    // Count bits in all full ulongs
                    for (int i = 0; i < fullUlongs; i++)
                    {
                        count += HammingWeight(_values[i]);
                    }
                    
                    // Handle the remaining bits if length is not a multiple of 64
                    int remainingBits = _length & (BitsPerUlong - 1); // Modulo 64
                    if (remainingBits > 0)
                    {
                        ulong mask = AllBitsSet >> (BitsPerUlong - remainingBits);
                        count += HammingWeight(_values[fullUlongs] & mask);
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
                
                return count;
            }
        }

        /// <summary>
        /// Gets an enumerable collection of the indices of all set bits.
        /// </summary>
        /// <returns>An enumerable collection of indices.</returns>
        public IEnumerable<int> GetSetBitIndices()
        {
            if (_length <= BitsPerUlong)
            {
                ulong value = _singleValue;
                int index = 0;
                
                while (value != 0)
                {
                    if ((value & 1) != 0)
                        yield return index;
                        
                    value >>= 1;
                    index++;
                }
            }
            else
            {
                for (int i = 0; i < _length; i++)
                {
                    if (Get(i))
                        yield return i;
                }
            }
        }

        /// <summary>
        /// Finds the index of the first bit set to true.
        /// </summary>
        /// <returns>The index of the first bit set to true, or -1 if no bits are set.</returns>
        public int FindFirstSetBit()
        {
            if (_length <= BitsPerUlong)
            {
                if (_singleValue == 0) return -1;
                return FindLowestSetBit(_singleValue);
            }
            else
            {
                int result = -1;
                
                EnterSpinLock();
                try
                {
                    int arrayLength = GetArrayLength(_length);
                    
                    for (int i = 0; i < arrayLength; i++)
                    {
                        if (_values[i] != 0)
                        {
                            int bitPos = FindLowestSetBit(_values[i]);
                            result = (i << BitsPerUlongShift) + bitPos;
                            
                            // Ensure the result is within bounds
                            if (result >= _length)
                                return -1;
                                
                            break;
                        }
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
                
                return result;
            }
        }

        /// <summary>
        /// Performs a bitwise NOT operation on this bit array.
        /// </summary>
        /// <returns>A new BitArray64 with all bits flipped.</returns>
        public BitArray64 Not()
        {
            BitArray64 result = new BitArray64(_length);
            
            if (_length <= BitsPerUlong)
            {
                // Ensure we only flip valid bits based on the length
                ulong mask = AllBitsSet >> (BitsPerUlong - _length);
                result._singleValue = (~_singleValue) & mask;
            }
            else
            {
                EnterSpinLock();
                try
                {
                    int fullUlongs = _length >> BitsPerUlongShift; // Divide by 64
                    
                    // NOT all full ulongs
                    for (int i = 0; i < fullUlongs; i++)
                    {
                        result._values[i] = ~_values[i];
                    }
                    
                    // Handle the remaining bits if length is not a multiple of 64
                    int remainingBits = _length & (BitsPerUlong - 1); // Modulo 64
                    if (remainingBits > 0)
                    {
                        ulong mask = AllBitsSet >> (BitsPerUlong - remainingBits);
                        result._values[fullUlongs] = (~_values[fullUlongs]) & mask;
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Performs a bitwise AND operation with another BitArray64.
        /// </summary>
        /// <param name="other">The BitArray64 to perform the AND operation with.</param>
        /// <returns>A new BitArray64 with the result of the AND operation.</returns>
        public BitArray64 And(BitArray64 other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
                
            if (_length != other._length)
                throw new ArgumentException("BitArray64 lengths must be equal for bitwise operations.", nameof(other));
                
            BitArray64 result = new BitArray64(_length);
            
            if (_length <= BitsPerUlong)
            {
                result._singleValue = _singleValue & other._singleValue;
            }
            else
            {
                int arrayLength = GetArrayLength(_length);
                
                EnterSpinLock();
                try
                {
                    other.EnterSpinLock();
                    try
                    {
                        for (int i = 0; i < arrayLength; i++)
                        {
                            result._values[i] = _values[i] & other._values[i];
                        }
                    }
                    finally
                    {
                        other.ExitSpinLock();
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Performs a bitwise OR operation with another BitArray64.
        /// </summary>
        /// <param name="other">The BitArray64 to perform the OR operation with.</param>
        /// <returns>A new BitArray64 with the result of the OR operation.</returns>
        public BitArray64 Or(BitArray64 other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
                
            if (_length != other._length)
                throw new ArgumentException("BitArray64 lengths must be equal for bitwise operations.", nameof(other));
                
            BitArray64 result = new BitArray64(_length);
            
            if (_length <= BitsPerUlong)
            {
                result._singleValue = _singleValue | other._singleValue;
            }
            else
            {
                int arrayLength = GetArrayLength(_length);
                
                EnterSpinLock();
                try
                {
                    other.EnterSpinLock();
                    try
                    {
                        for (int i = 0; i < arrayLength; i++)
                        {
                            result._values[i] = _values[i] | other._values[i];
                        }
                    }
                    finally
                    {
                        other.ExitSpinLock();
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Performs a bitwise XOR operation with another BitArray64.
        /// </summary>
        /// <param name="other">The BitArray64 to perform the XOR operation with.</param>
        /// <returns>A new BitArray64 with the result of the XOR operation.</returns>
        public BitArray64 Xor(BitArray64 other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
                
            if (_length != other._length)
                throw new ArgumentException("BitArray64 lengths must be equal for bitwise operations.", nameof(other));
                
            BitArray64 result = new BitArray64(_length);
            
            if (_length <= BitsPerUlong)
            {
                result._singleValue = _singleValue ^ other._singleValue;
            }
            else
            {
                int arrayLength = GetArrayLength(_length);
                
                EnterSpinLock();
                try
                {
                    other.EnterSpinLock();
                    try
                    {
                        for (int i = 0; i < arrayLength; i++)
                        {
                            result._values[i] = _values[i] ^ other._values[i];
                        }
                    }
                    finally
                    {
                        other.ExitSpinLock();
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Determines whether this instance and another specified BitArray64 object have the same value.
        /// </summary>
        /// <param name="other">The BitArray64 to compare to this instance.</param>
        /// <returns>true if the value of the other parameter is the same as this instance; otherwise, false.</returns>
        public bool Equals(BitArray64 other)
        {
            if (other == null)
                return false;
                
            if (ReferenceEquals(this, other))
                return true;
                
            if (_length != other._length)
                return false;
                
            if (_length <= BitsPerUlong)
            {
                return _singleValue == other._singleValue;
            }
            else
            {
                bool result = true;
                int arrayLength = GetArrayLength(_length);
                
                EnterSpinLock();
                try
                {
                    other.EnterSpinLock();
                    try
                    {
                        for (int i = 0; i < arrayLength; i++)
                        {
                            if (_values[i] != other._values[i])
                            {
                                result = false;
                                break;
                            }
                        }
                    }
                    finally
                    {
                        other.ExitSpinLock();
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
                
                return result;
            }
        }

        /// <summary>
        /// Performs a left shift operation on the bit array.
        /// </summary>
        /// <param name="count">The number of positions to shift.</param>
        /// <returns>A new BitArray64 with the bits shifted to the left.</returns>
        public BitArray64 LeftShift(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative.");
                
            if (count == 0)
                return new BitArray64(this);
                
            if (count >= _length)
                return new BitArray64(_length); // All zeros
                
            BitArray64 result = new BitArray64(_length);
            
            for (int i = count; i < _length; i++)
            {
                if (Get(i - count))
                    result.Set(i, true);
            }
            
            return result;
        }

        /// <summary>
        /// Performs a right shift operation on the bit array.
        /// </summary>
        /// <param name="count">The number of positions to shift.</param>
        /// <returns>A new BitArray64 with the bits shifted to the right.</returns>
        public BitArray64 RightShift(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative.");
                
            if (count == 0)
                return new BitArray64(this);
                
            if (count >= _length)
                return new BitArray64(_length); // All zeros
                
            BitArray64 result = new BitArray64(_length);
            
            for (int i = 0; i < _length - count; i++)
            {
                if (Get(i + count))
                    result.Set(i, true);
            }
            
            return result;
        }

        /// <summary>
        /// Sets a range of bits to the specified value.
        /// </summary>
        /// <param name="startIndex">The zero-based starting position of the range.</param>
        /// <param name="length">The number of bits in the range.</param>
        /// <param name="value">The value to set the bits to.</param>
        public void SetRange(int startIndex, int length, bool value)
        {
            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be non-negative.");
                
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
                
            if (startIndex + length > _length)
                throw new ArgumentOutOfRangeException(nameof(length), "Range exceeds the bounds of the bit array.");
            
            // Optimize for small arrays
            if (_length <= BitsPerUlong)
            {
                ulong localVal = _singleValue;
                ulong mask;
                
                if (length == BitsPerUlong)
                    mask = AllBitsSet;
                else
                    mask = ((1UL << length) - 1) << startIndex;
                    
                if (value)
                    localVal |= mask;
                else
                    localVal &= ~mask;
                    
                Thread.MemoryBarrier();
                Interlocked.Exchange(ref _singleValue, localVal);
                return;
            }
            else
            {
                EnterSpinLock();
                try
                {
                    // For large arrays, handle each ulong individually
                    int startArrayIndex = startIndex >> BitsPerUlongShift;    // Divide by 64
                    int endArrayIndex = (startIndex + length - 1) >> BitsPerUlongShift;
                    int startBitIndex = startIndex & (BitsPerUlong - 1);      // Modulo 64
                    
                    if (startArrayIndex == endArrayIndex)
                    {
                        // Range fits in a single ulong
                        ulong mask;
                        if (length == BitsPerUlong)
                            mask = AllBitsSet;
                        else
                            mask = ((1UL << length) - 1) << startBitIndex;
                            
                        if (value)
                            _values[startArrayIndex] |= mask;
                        else
                            _values[startArrayIndex] &= ~mask;
                    }
                    else
                    {
                        // Handle first ulong partially
                        ulong firstMask = AllBitsSet << startBitIndex;
                        if (value)
                            _values[startArrayIndex] |= firstMask;
                        else
                            _values[startArrayIndex] &= ~firstMask;
                            
                        // Handle middle ulongs completely
                        for (int i = startArrayIndex + 1; i < endArrayIndex; i++)
                        {
                            _values[i] = value ? AllBitsSet : 0UL;
                        }
                        
                        // Handle last ulong partially
                        int endBitIndex = (startIndex + length - 1) & (BitsPerUlong - 1);
                        ulong lastMask = AllBitsSet >> (BitsPerUlong - endBitIndex - 1);
                        if (value)
                            _values[endArrayIndex] |= lastMask;
                        else
                            _values[endArrayIndex] &= ~lastMask;
                    }
                    
                    // Ensure memory ordering
                    Thread.MemoryBarrier();
                }
                finally
                {
                    ExitSpinLock();
                }
            }
        }

        /// <summary>
        /// Creates a new BitArray64 containing a subrange of bits from this instance.
        /// </summary>
        /// <param name="startIndex">The zero-based starting position of the range.</param>
        /// <param name="length">The number of bits in the range.</param>
        /// <returns>A new BitArray64 containing the specified range of bits.</returns>
        public BitArray64 GetSubArray(int startIndex, int length)
        {
            // Validate parameters
            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be non-negative.");
                
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
                
            if (startIndex + length > _length)
                throw new ArgumentOutOfRangeException(nameof(length), "Range exceeds the bounds of the bit array.");
            
            BitArray64 result = new BitArray64(length);
            
            for (int i = 0; i < length; i++)
            {
                if (Get(startIndex + i))
                    result.Set(i, true);
            }
            
            return result;
        }

        /// <summary>
        /// Converts this BitArray64 to an array of boolean values.
        /// </summary>
        /// <returns>An array of boolean values representing this BitArray64.</returns>
        public bool[] ToBoolArray()
        {
            bool[] result = new bool[_length];
            
            if (_length <= BitsPerUlong)
            {
                for (int i = 0; i < _length; i++)
                {
                    result[i] = (_singleValue & (1UL << i)) != 0;
                }
            }
            else
            {
                EnterSpinLock();
                try
                {
                    for (int i = 0; i < _length; i++)
                    {
                        int arrayIndex = i >> BitsPerUlongShift;        // Divide by 64
                        int bitIndex = i & (BitsPerUlong - 1);          // Modulo 64
                        result[i] = (_values[arrayIndex] & (1UL << bitIndex)) != 0;
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Converts this BitArray64 to an array of bytes.
        /// </summary>
        /// <returns>An array of bytes representing this BitArray64.</returns>
        public byte[] ToByteArray()
        {
            // Calculate the number of bytes needed (length / 8, rounded up)
            int byteLength = (_length + 7) >> 3;
            byte[] result = new byte[byteLength];
            
            if (_length <= BitsPerUlong)
            {
                for (int i = 0; i < byteLength; i++)
                {
                    result[i] = (byte)((_singleValue >> (i * 8)) & 0xFF);
                }
            }
            else
            {
                EnterSpinLock();
                try
                {
                    for (int i = 0; i < _length; i++)
                    {
                        int arrayIndex = i >> BitsPerUlongShift;        // Divide by 64
                        int bitIndex = i & (BitsPerUlong - 1);          // Modulo 64
                        
                        if ((_values[arrayIndex] & (1UL << bitIndex)) != 0)
                        {
                            int byteIndex = i >> 3;                      // Divide by 8
                            int bitInByte = i & 0x7;                     // Modulo 8
                            result[byteIndex] |= (byte)(1 << bitInByte);
                        }
                    }
                }
                finally
                {
                    ExitSpinLock();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Gets an enumerator that iterates through the bits in this BitArray64.
        /// </summary>
        /// <returns>An enumerator that iterates through the bits in this BitArray64.</returns>
        public IEnumerator<bool> GetEnumerator()
        {
            for (int i = 0; i < _length; i++)
            {
                yield return Get(i);
            }
        }

        /// <summary>
        /// Gets a non-generic enumerator that iterates through the bits in this BitArray64.
        /// </summary>
        /// <returns>A non-generic enumerator that iterates through the bits in this BitArray64.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
        
        #region Object Overrides

        /// <summary>
        /// Returns a string representation of this BitArray64.
        /// </summary>
        /// <returns>A string representation of this BitArray64.</returns>
        public override string ToString()
        {
            char[] result = new char[_length];
            
            for (int i = 0; i < _length; i++)
            {
                result[i] = Get(i) ? '1' : '0';
            }
            
            return new string(result);
        }

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be a BitArray64 object, have the same value.
        /// </summary>
        /// <param name="obj">The object to compare to this instance.</param>
        /// <returns>true if obj is a BitArray64 and its value is the same as this instance; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as BitArray64);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _length.GetHashCode();
                
                if (_length <= BitsPerUlong)
                {
                    hash = hash * 31 + _singleValue.GetHashCode();
                }
                else
                {
                    int arrayLength = GetArrayLength(_length);
                    
                    EnterSpinLock();
                    try
                    {
                        for (int i = 0; i < arrayLength; i++)
                        {
                            hash = hash * 31 + _values[i].GetHashCode();
                        }
                    }
                    finally
                    {
                        ExitSpinLock();
                    }
                }
                
                return hash;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Validates that the specified index is within the bounds of the array.
        /// </summary>
        /// <param name="index">The index to validate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= _length)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index must be non-negative and less than the size of the collection. Index: {index}, Size: {_length}");
        }
        
        /// <summary>
        /// Calculates the length of the ulong array needed to store the specified number of bits.
        /// </summary>
        /// <param name="bitLength">The number of bits to store.</param>
        /// <returns>The length of the ulong array needed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetArrayLength(int bitLength)
        {
            return (bitLength + BitsPerUlong - 1) >> BitsPerUlongShift; // Ceiling division by 64
        }
        
        /// <summary>
        /// Calculates the Hamming weight (population count) of a 64-bit value.
        /// </summary>
        /// <param name="value">The 64-bit value.</param>
        /// <returns>The number of bits set to 1 in the value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HammingWeight(ulong value)
        {
            // https://en.wikipedia.org/wiki/Hamming_weight
            value = value - ((value >> 1) & 0x5555555555555555UL);
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            value = value + (value >> 8);
            value = value + (value >> 16);
            value = value + (value >> 32);
            return (int)(value & 0x7F);
        }
        
        /// <summary>
        /// Finds the position of the lowest set bit using De Bruijn sequences.
        /// </summary>
        /// <param name="value">The 64-bit value to search.</param>
        /// <returns>The position of the lowest set bit.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindLowestSetBit(ulong value)
        {
            return DeBruijnPositionTable[((value & -value) * DeBruijnSequence) >> 58];
        }

        /// <summary>
        /// Enters the spin lock, waiting until it becomes available.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnterSpinLock()
        {
            if (_length <= BitsPerUlong)
                return; // No lock needed for single-value storage
                
            // Attempt to acquire the lock using a spin lock approach
            while (Interlocked.CompareExchange(ref _spinLockFlag, 1, 0) != 0)
            {
                // Spin wait a bit to reduce contention
                Thread.SpinWait(10);
            }
            
            // Memory barrier to ensure proper ordering of operations
            Thread.MemoryBarrier();
        }

        /// <summary>
        /// Exits the spin lock, allowing other threads to enter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExitSpinLock()
        {
            if (_length <= BitsPerUlong)
                return; // No lock needed for single-value storage
                
            // Ensure memory ordering before releasing the lock
            Thread.MemoryBarrier();
            
            // Release the lock
            Interlocked.Exchange(ref _spinLockFlag, 0);
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for BitArray64 to provide additional functionality.
    /// </summary>
    public static class BitArray64Extensions
    {
        #region Operators

        /// <summary>
        /// Compares two BitArray64 instances and returns the result.
        /// </summary>
        /// <param name="left">The first BitArray64 to compare.</param>
        /// <param name="right">The second BitArray64 to compare.</param>
        /// <returns>true if the BitArray64 instances are equal; otherwise, false.</returns>
        public static bool operator ==(BitArray64 left, BitArray64 right)
        {
            if (ReferenceEquals(left, null))
                return ReferenceEquals(right, null);
                
            return left.Equals(right);
        }
        
        /// <summary>
        /// Compares two BitArray64 instances and returns the result.
        /// </summary>
        /// <param name="left">The first BitArray64 to compare.</param>
        /// <param name="right">The second BitArray64 to compare.</param>
        /// <returns>true if the BitArray64 instances are not equal; otherwise, false.</returns>
        public static bool operator !=(BitArray64 left, BitArray64 right)
        {
            return !(left == right);
        }
        
        /// <summary>
        /// Performs a bitwise AND operation on two BitArray64 instances.
        /// </summary>
        /// <param name="left">The first BitArray64.</param>
        /// <param name="right">The second BitArray64.</param>
        /// <returns>A new BitArray64 containing the result of the bitwise AND operation.</returns>
        public static BitArray64 operator &(BitArray64 left, BitArray64 right)
        {
            if (left == null)
                throw new ArgumentNullException(nameof(left));
                
            return left.And(right);
        }
        
        /// <summary>
        /// Performs a bitwise OR operation on two BitArray64 instances.
        /// </summary>
        /// <param name="left">The first BitArray64.</param>
        /// <param name="right">The second BitArray64.</param>
        /// <returns>A new BitArray64 containing the result of the bitwise OR operation.</returns>
        public static BitArray64 operator |(BitArray64 left, BitArray64 right)
        {
            if (left == null)
                throw new ArgumentNullException(nameof(left));
                
            return left.Or(right);
        }
        
        /// <summary>
        /// Performs a bitwise XOR operation on two BitArray64 instances.
        /// </summary>
        /// <param name="left">The first BitArray64.</param>
        /// <param name="right">The second BitArray64.</param>
        /// <returns>A new BitArray64 containing the result of the bitwise XOR operation.</returns>
        public static BitArray64 operator ^(BitArray64 left, BitArray64 right)
        {
            if (left == null)
                throw new ArgumentNullException(nameof(left));
                
            return left.Xor(right);
        }
        
        /// <summary>
        /// Performs a bitwise NOT operation on a BitArray64 instance.
        /// </summary>
        /// <param name="array">The BitArray64 to negate.</param>
        /// <returns>A new BitArray64 containing the result of the bitwise NOT operation.</returns>
        public static BitArray64 operator ~(BitArray64 array)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
                
            return array.Not();
        }

                /// <summary>
        /// Performs a left shift operation on a BitArray64.
        /// </summary>
        /// <param name="bitArray">The BitArray64 to shift.</param>
        /// <param name="count">The number of positions to shift.</param>
        /// <returns>A new BitArray64 with the bits shifted to the left.</returns>
        public static BitArray64 operator <<(BitArray64 bitArray, int count)
        {
            if (bitArray == null)
                throw new ArgumentNullException(nameof(bitArray));
                
            return bitArray.LeftShift(count);
        }
        
        /// <summary>
        /// Performs a right shift operation on a BitArray64.
        /// </summary>
        /// <param name="bitArray">The BitArray64 to shift.</param>
        /// <param name="count">The number of positions to shift.</param>
        /// <returns>A new BitArray64 with the bits shifted to the right.</returns>
        public static BitArray64 operator >>(BitArray64 bitArray, int count)
        {
            if (bitArray == null)
                throw new ArgumentNullException(nameof(bitArray));
                
            return bitArray.RightShift(count);
        }

        #endregion
        
        #region Public Methods
        /// <summary>
        /// Checks if any bit in the specified range is set to true.
        /// </summary>
        /// <param name="bitArray">The BitArray64 to check.</param>
        /// <param name="startIndex">The zero-based index of the first bit in the range.</param>
        /// <param name="length">The number of bits in the range.</param>
        /// <returns>true if any bit in the range is set to true; otherwise, false.</returns>
        public static bool AnySet(this BitArray64 bitArray, int startIndex, int length)
        {
            if (bitArray == null)
                throw new ArgumentNullException(nameof(bitArray));
                
            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be non-negative.");
                
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
                
            if (startIndex + length > bitArray.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "Range exceeds the bounds of the bit array.");
                
            for (int i = 0; i < length; i++)
            {
                if (bitArray[startIndex + i])
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if all bits in the specified range are set to true.
        /// </summary>
        /// <param name="bitArray">The BitArray64 to check.</param>
        /// <param name="startIndex">The zero-based index of the first bit in the range.</param>
        /// <param name="length">The number of bits in the range.</param>
        /// <returns>true if all bits in the range are set to true; otherwise, false.</returns>
        public static bool AllSet(this BitArray64 bitArray, int startIndex, int length)
        {
            if (bitArray == null)
                throw new ArgumentNullException(nameof(bitArray));
                
            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be non-negative.");
                
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
                
            if (startIndex + length > bitArray.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "Range exceeds the bounds of the bit array.");
                
            for (int i = 0; i < length; i++)
            {
                if (!bitArray[startIndex + i])
                    return false;
            }
            
            return true;
        }

        #endregion
    }
}
