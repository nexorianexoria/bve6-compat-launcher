#pragma once

#include <array>
#include <cstring>
#include <string>
#include <iomanip>
#include <ios>
#include <istream>
#include <limits>
#include <ostream>
#include <streambuf>
#include <string_view>
#include "atsplugin.h"

// Non-owning stream buffers for one protocol line. They let the per-frame
// request/response use caller-owned stack storage instead of allocating
// an ostringstream/istringstream backing string.
class fixed_output_streambuf final : public std::streambuf {
public:
    fixed_output_streambuf(char* data, size_t capacity) noexcept {
        setp(data, data + capacity);
    }

    std::string_view view() const noexcept {
        return { pbase(), static_cast<size_t>(pptr() - pbase()) };
    }

protected:
    int_type overflow(int_type value) override {
        if (traits_type::eq_int_type(value, traits_type::eof()))
            return traits_type::not_eof(value);
        if (pptr() == epptr()) return traits_type::eof();
        *pptr() = traits_type::to_char_type(value);
        pbump(1);
        return value;
    }

    std::streamsize xsputn(const char* source, std::streamsize count) override {
        if (count <= 0) return 0;
        const auto available = static_cast<std::streamsize>(epptr() - pptr());
        const auto copied = count < available ? count : available;
        if (copied > 0) {
            std::memcpy(pptr(), source, static_cast<size_t>(copied));
            pbump(static_cast<int>(copied));
        }
        return copied;
    }
};

class fixed_input_streambuf final : public std::streambuf {
public:
    fixed_input_streambuf(const char* data, size_t size) noexcept {
        char* begin = const_cast<char*>(data);
        setg(begin, begin, begin + size);
    }
};

template<typename CharT, typename Traits>
std::basic_ostream<CharT, Traits> &operator<<(
    std::basic_ostream<CharT, Traits> &s, const struct ATS_VEHICLESPEC &v)
{
    return s
        << ' ' << v.BrakeNotches
        << ' ' << v.PowerNotches
        << ' ' << v.AtsNotch
        << ' ' << v.B67Notch
        << ' ' << v.Cars;
}

template<typename CharT, typename Traits>
std::basic_istream<CharT, Traits> &operator>>(
    std::basic_istream<CharT, Traits> &s, struct ATS_VEHICLESPEC &v)
{
    return s >> std::skipws
        >> v.BrakeNotches
        >> v.PowerNotches
        >> v.AtsNotch
        >> v.B67Notch
        >> v.Cars;
}

template<typename CharT, typename Traits>
std::basic_ostream<CharT, Traits> &operator<<(
    std::basic_ostream<CharT, Traits> &s, const struct ATS_VEHICLESTATE &v)
{
    return s
        << std::setprecision(std::numeric_limits<double>::max_digits10)
        << ' ' << v.Location
        << std::setprecision(std::numeric_limits<float>::max_digits10)
        << ' ' << v.Speed
        << ' ' << v.Time
        << ' ' << v.BcPressure
        << ' ' << v.MrPressure
        << ' ' << v.ErPressure
        << ' ' << v.BpPressure
        << ' ' << v.SapPressure
        << ' ' << v.Current;
}

template<typename CharT, typename Traits>
std::basic_istream<CharT, Traits> &operator>>(
    std::basic_istream<CharT, Traits> &s, struct ATS_VEHICLESTATE &v)
{
    return s >> std::skipws
        >> v.Location
        >> v.Speed
        >> v.Time
        >> v.BcPressure
        >> v.MrPressure
        >> v.ErPressure
        >> v.BpPressure
        >> v.SapPressure
        >> v.Current;
}

template<typename CharT, typename Traits>
std::basic_ostream<CharT, Traits> &operator<<(
    std::basic_ostream<CharT, Traits> &s, const struct ATS_BEACONDATA &v)
{
    return s << std::setprecision(std::numeric_limits<float>::max_digits10)
        << ' ' << v.Type
        << ' ' << v.Signal
        << ' ' << v.Distance
        << ' ' << v.Optional;
}

template<typename CharT, typename Traits>
std::basic_istream<CharT, Traits> &operator>>(
    std::basic_istream<CharT, Traits> &s, struct ATS_BEACONDATA &v)
{
    return s >> std::skipws
        >> v.Type
        >> v.Signal
        >> v.Distance
        >> v.Optional;
}

template<typename CharT, typename Traits>
std::basic_ostream<CharT, Traits> &operator<<(
    std::basic_ostream<CharT, Traits> &s, const struct ATS_HANDLES &v)
{
    return s
        << ' ' << v.Brake
        << ' ' << v.Power
        << ' ' << v.Reverser
        << ' ' << v.ConstantSpeed;
}

template<typename CharT, typename Traits>
std::basic_istream<CharT, Traits> &operator>>(
    std::basic_istream<CharT, Traits> &s, struct ATS_HANDLES &v)
{
    return s >> std::skipws
        >> v.Brake
        >> v.Power
        >> v.Reverser
        >> v.ConstantSpeed;
}

template<typename T, std::size_t N>
struct arrayref {
    T *head;

    constexpr explicit arrayref(T *head) noexcept : head{head} {}
    constexpr explicit arrayref(T(*head)[N]) noexcept : head{*head} {}
};

template<typename CharT, typename Traits, typename T, std::size_t N>
std::basic_ostream<CharT, Traits> &operator<<(
    std::basic_ostream<CharT, Traits> &s, arrayref<T, N> v)
{
    for (size_t i = 0; i < N; i++) {
        if (!(s << ' ' << v.head[i])) {
            break;
        }
    }
    return s;
}

template<typename CharT, typename Traits, typename T, std::size_t N>
std::basic_istream<CharT, Traits> &operator>>(
    std::basic_istream<CharT, Traits> &s, arrayref<T, N> v)
{
    for (size_t i = 0; i < N; i++) {
        if (!(s >> v.head[i])) {
            break;
        }
    }
    return s;
}

// Fixed hexadecimal representation avoids locale-sensitive integer formatting.
// The view writes directly to the caller's stream buffer: no 2 KiB temporary
// std::string is allocated for each panel/sound array on every ATS frame.
struct fast_array_view {
    const int* values;
};

inline fast_array_view fast_array(const int* values) noexcept {
    return { values };
}

template<typename CharT, typename Traits>
std::basic_ostream<CharT, Traits>& operator<<(
    std::basic_ostream<CharT, Traits>& out, fast_array_view view)
{
    static constexpr char digits[] = "0123456789abcdef";
    char encoded[2048];
    for (size_t i = 0; i < 256; ++i) {
        unsigned int value = static_cast<unsigned int>(view.values[i]);
        for (int nibble = 7; nibble >= 0; --nibble) {
            encoded[i * 8 + static_cast<size_t>(nibble)] = digits[value & 15u];
            value >>= 4;
        }
    }
    return out.write(encoded, static_cast<std::streamsize>(sizeof(encoded)));
}

inline bool fast_array_space(int value) noexcept {
    return value == ' ' || value == '\t' || value == '\n' ||
        value == '\r' || value == '\f' || value == '\v';
}

// Read 2 KiB in one stream-buffer transfer. Decode two validated nibbles per
// byte using an immutable 256-entry lookup table instead of two comparisons
// for each of the 2048 digits. The wire format, signed integer bit pattern,
// stream error behavior, and following delimiter remain unchanged.
inline bool read_fast_array(std::istream& in, int* values) {
    std::istream::sentry sentry(in);
    if (!sentry) return false;

    std::streambuf* source = in.rdbuf();
    std::array<char, 2048> encoded;
    if (source->sgetn(encoded.data(),
            static_cast<std::streamsize>(encoded.size())) !=
            static_cast<std::streamsize>(encoded.size())) {
        in.setstate(std::ios::failbit);
        return false;
    }
    static constexpr auto hex = [] {
        std::array<unsigned char, 256> result{};
        for (auto& element : result) element = 0xff;
        for (unsigned int digit = 0; digit < 10; ++digit)
            result['0' + digit] = static_cast<unsigned char>(digit);
        for (unsigned int digit = 0; digit < 6; ++digit)
            result['a' + digit] = static_cast<unsigned char>(10 + digit);
        return result;
    }();
    for (size_t i = 0; i < 256; ++i) {
        unsigned int value = 0;
        for (size_t byte = 0; byte < 4; ++byte) {
            const unsigned char high = hex[static_cast<unsigned char>(encoded[i * 8 + byte * 2])];
            const unsigned char low = hex[static_cast<unsigned char>(encoded[i * 8 + byte * 2 + 1])];
            if ((high | low) > 15) {
                in.setstate(std::ios::failbit);
                return false;
            }
            value = (value << 8) | (static_cast<unsigned int>(high) << 4) | low;
        }
        std::memcpy(values + i, &value, sizeof(value));
    }

    const auto delimiter = source->sgetc();
    if (!std::char_traits<char>::eq_int_type(
            delimiter, std::char_traits<char>::eof()) &&
        !fast_array_space(std::char_traits<char>::to_char_type(delimiter))) {
        in.setstate(std::ios::failbit);
        return false;
    }
    return true;
}
