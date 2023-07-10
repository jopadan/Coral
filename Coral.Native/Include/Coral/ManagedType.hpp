﻿#pragma once

namespace Coral {

	enum class ManagedType
	{
		Unknown = -1,

		SByte, Byte,
		Short, UShort,
		Int, UInt,
		Long, ULong,

		Float, Double,

		Bool,

		Pointer
	};

	template<typename TArg>
	constexpr ManagedType GetManagedType()
	{
		if constexpr (std::is_pointer_v<std::remove_reference_t<TArg>>)
			return ManagedType::Pointer;
		else if constexpr (std::same_as<TArg, uint8_t>)
			return ManagedType::Byte;
		else if constexpr (std::same_as<TArg, uint16_t>)
			return ManagedType::UShort;
		else if constexpr (std::same_as<TArg, uint32_t> || (std::same_as<TArg, unsigned long> && sizeof(TArg) == 4))
			return ManagedType::UInt;
		else if constexpr (std::same_as<TArg, uint64_t> || (std::same_as<TArg, unsigned long> && sizeof(TArg) == 8))
			return ManagedType::ULong;
		else if constexpr (std::same_as<TArg, char8_t>)
			return ManagedType::SByte;
		else if constexpr (std::same_as<TArg, int16_t>)
			return ManagedType::Short;
		else if constexpr (std::same_as<TArg, int32_t> || (std::same_as<TArg, long> && sizeof(TArg) == 4))
			return ManagedType::Int;
		else if constexpr (std::same_as<TArg, int64_t> || (std::same_as<TArg, long> && sizeof(TArg) == 8))
			return ManagedType::Long;
		else if constexpr (std::same_as<TArg, float>)
			return ManagedType::Float;
		else if constexpr (std::same_as<TArg, double>)
			return ManagedType::Double;
		else if constexpr (std::same_as<TArg, bool>)
			return ManagedType::Bool;
		else
			return ManagedType::Unknown;
	}
	
}
