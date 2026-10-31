#pragma once

#include <array>
#include <cstring>
#include <string>
#include <iomanip>
#include <ios>
#include <istream>
#include <limits>
#include <ostream>
#include "atsplugin.h"

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

// Fixed hexadecimal representation avoids per-integer locale parsing/formatting.
inline std::string fast_array(const int* values) {
 static const char digits[]="0123456789abcdef"; std::string out(2048,'0');
 for(size_t i=0;i<256;++i){unsigned int v=static_cast<unsigned int>(values[i]);for(int n=7;n>=0;--n){out[i*8+n]=digits[v&15];v>>=4;}}return out;
}
inline bool read_fast_array(std::istream& in,int* values) {
 std::string text;if(!(in>>text)||text.size()!=2048)return false;
 for(size_t i=0;i<256;++i){unsigned int v=0;for(size_t n=0;n<8;++n){char c=text[i*8+n];unsigned int x;if(c>='0'&&c<='9')x=c-'0';else if(c>='a'&&c<='f')x=c-'a'+10;else return false;v=(v<<4)|x;}std::memcpy(values+i,&v,sizeof(v));}return true;
}
