﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Reinterpret.Net
{
	/// <summary>
	/// Extension methods that cast from <see cref="byte"/>s to
	/// the specified target Type.
	/// </summary>
	public static class ReInterpretFromBytesExtensions
	{
		/// <summary>
		/// Reinterprets the provided <see cref="bytes"/> in a similar fashion to C++
		/// reinterpret_cast by casting the byte chunk into the specified generic type
		/// <typeparamref name="TConvetType"/>.
		/// </summary>
		/// <typeparam name="TConvertType">The type to reinterpret to.</typeparam>
		/// <param name="bytes">The bytes chunk.</param>
		/// <param name="allowDestroyByteArray ">Indicates if the provided <see cref="bytes"/> array can be modified or 
		/// changed/destroyed in the process of casting. Indicating true  can yield higher performance results but the
		/// byte array must never be touched or used again. This will only work for certain types of reinterpret casting.</param>
		/// <returns>The resultant of the cast operation.</returns>
#if NETSTANDARD1_1
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
		public static unsafe TConvertType Reinterpret<TConvertType>(this byte[] bytes, bool allowDestroyByteArray = false)
			where TConvertType : struct
		{
			//Originally we null and length checked the bytes. This caused performance issues on .NET Core for some reason
			//Removing them increased the speed by almost an order of magnitude.
			//We shouldn't really handhold the user trying to reinterpet things into other things
			//If they're using this library then they should KNOW they shouldn't mess around and anything could happen
			//We already sacrfice safety for performance. An order of magnitude performance increase is a no brainer here.

			if(TypeIntrospector<TConvertType>.IsPrimitive)
				return ReinterpretPrimitive<TConvertType>(bytes);

			//We know it's not a primitive so it's a struct, either custom or made by MS/.NET.
			return ReinterpretCustomStruct<TConvertType>(bytes, 0);
		}

		//This feature is unavailable on Netstandard1.0 because Runtime Interop Services are NOT available
		//Even as a supplemental nuget package it required netstandard1.1
#if NETSTANDARD1_1
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
		private unsafe static TConvertType ReinterpretCustomStruct<TConvertType>(byte[] bytes, int offset) 
			where TConvertType : struct
		{
			fixed(byte* p = &bytes[offset])
			{
#if NET20 || NET30 || NET35 || NET40 || NETSTANDARD1_1
				return (TConvertType)Marshal.PtrToStructure((IntPtr)p, typeof(TConvertType));
#else
				return Marshal.PtrToStructure<TConvertType>((IntPtr)p);
#endif
			}
		}

		/// <summary>
		/// Reinterprets the provided <see cref="bytes"/> in a similar fashion to C++
		/// reinterpret_cast by casting the byte chunk into the specified generic type
		/// <typeparamref name="TConvetType"/>.
		/// </summary>
		/// <typeparam name="TConvertType">The type to reinterpret to.</typeparam>
		/// <param name="bytes">The bytes chunk.</param>
		/// <param name="allowDestroyByteArray ">Indicates if the provided <see cref="bytes"/> array can be modified or 
		/// changed/destroyed in the process of casting. Indicating true  can yield higher performance results but the
		/// byte array must never be touched or used again. This will only work for certain types of reinterpret casting.</param>
		/// <returns>The resultant of the cast operation.</returns>
		public static unsafe TConvertType[] ReinterpretToArray<TConvertType>(this byte[] bytes, bool allowDestroyByteArray = false)
			where TConvertType : struct
		{
			if(bytes == null) throw new ArgumentNullException(nameof(bytes));
			if(bytes.Length == 0) return new TConvertType[0];

#if NETSTANDARD1_0 || NETSTANDARD1_1
			TypeInfo convertTypeInfo = typeof(TConvertType).GetTypeInfo();
#else
			Type convertTypeInfo = typeof(TConvertType);
#endif
			//We can only handle primitive arrays
			if(convertTypeInfo.IsPrimitive)
				return ReinterpretPrimitiveArray<TConvertType>(bytes);

			//We must validate that the byte array is the proper size
			if(bytes.Length % MarshalSizeOf<TConvertType>.SizeOf != 0)
				throw new InvalidOperationException($"Provided bytes must be a multiple of  {MarshalSizeOf<TConvertType>.SizeOf} to reinterpret to {typeof(TConvertType).Name}.");

			return ReinterpretCustomStructArray<TConvertType>(bytes);
		}
		private static unsafe TConvertType[] ReinterpretCustomStructArray<TConvertType>(byte[] bytes) 
			where TConvertType : struct
		{
			TConvertType[] result = new TConvertType[bytes.Length / MarshalSizeOf<TConvertType>.SizeOf];

			for(int i = 0; i < bytes.Length / MarshalSizeOf<TConvertType>.SizeOf; i++)
				result[i] = ReinterpretCustomStruct<TConvertType>(bytes, i * MarshalSizeOf<TConvertType>.SizeOf);

			return result;
		}

		private static TConvertType[] ReinterpretPrimitiveArray<TConvertType>(byte[] bytes, bool allowDestroyByteArray = false)
			where TConvertType : struct
		{
			//If someone happens to ask for the byte representation of bytes
			if(typeof(TConvertType) == typeof(byte))
				return bytes as TConvertType[];

			return allowDestroyByteArray ? bytes.ToConvertedArrayPerm<TConvertType>() : bytes.ToArray().ToConvertedArrayPerm<TConvertType>();
		}

		/// <summary>
		/// High performance reinterpret cast for the <see cref="bytes"/> converting
		/// the byte chunk to a <see cref="string"/> using Unicode encoding (2byte char).
		/// </summary>
		/// <param name="bytes">The bytes chunk.</param>
		/// <param name="allowDestroyByteArray ">Indicates if the provided <see cref="bytes"/> array can be modified or 
		/// changed/destroyed in the process of casting. Indicates that it can will yield higher performance results but the
		/// byte array must never be touched or used again. </param>
		/// <returns>The resultant of the cast operation.</returns>
		public static unsafe string ReinterpretToString(this byte[] bytes, bool allowDestroyByteArray = false)
		{
			if(bytes == null) throw new ArgumentNullException(nameof(bytes));
			if(bytes.Length == 0) return "";

			//The caller may want to reuse the byte array so we check if they will allow us to destroy it
			char[] chars = allowDestroyByteArray ? bytes.ToConvertedArrayPerm<char>() : bytes.ToArray().ToConvertedArrayPerm<char>();
			return new string(chars);
		}

#if NETSTANDARD1_1
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
		private static unsafe TConvertType ReinterpretPrimitive<TConvertType>(byte[] bytes)
			where TConvertType : struct
		{
			return Unsafe.ReadUnaligned<TConvertType>(ref bytes[0]);
		}
	}
}
