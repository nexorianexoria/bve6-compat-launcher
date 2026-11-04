// Standalone protocol microbenchmark. No BVE installation, game, ATS plugin
// or live vehicle files are accessed. Both codecs receive identical bytes.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "../../src/bridge/encoder.h"
#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <istream>
#include <numeric>
#include <string_view>

// Original b11 implementation, retained only for independent A/B comparison.
static bool b11_decode(std::istream& in, int* values) {
    std::istream::sentry sentry(in);
    if (!sentry) return false;
    std::streambuf* source = in.rdbuf();
    for (size_t i = 0; i < 256; ++i) {
        unsigned int value = 0;
        for (size_t nibble = 0; nibble < 8; ++nibble) {
            const auto next = source->sbumpc();
            if (std::char_traits<char>::eq_int_type(
                    next, std::char_traits<char>::eof())) {
                in.setstate(std::ios::failbit);
                return false;
            }
            const char digit = std::char_traits<char>::to_char_type(next);
            unsigned int decoded;
            if (digit >= '0' && digit <= '9') decoded = digit - '0';
            else if (digit >= 'a' && digit <= 'f') decoded = digit - 'a' + 10u;
            else {
                in.setstate(std::ios::failbit);
                return false;
            }
            value = (value << 4) | decoded;
        }
        std::memcpy(values + i, &value, sizeof(value));
    }
    const auto delimiter = source->sgetc();
    if (!std::char_traits<char>::eq_int_type(delimiter, std::char_traits<char>::eof()) &&
        !fast_array_space(std::char_traits<char>::to_char_type(delimiter))) {
        in.setstate(std::ios::failbit);
        return false;
    }
    return true;
}

template<typename Decode>
static uint64_t bench(std::string_view wire, const int* original,
                      int loops, Decode decode, bool& correct) {
    int result[256]{};
    uint32_t checksum = 0;
    const auto began = std::chrono::steady_clock::now();
    for (int i = 0; i < loops; ++i) {
        fixed_input_streambuf input_buffer(wire.data(), wire.size());
        std::istream input(&input_buffer);
        if (!decode(input, result) || std::memcmp(result, original, 1024) != 0) {
            correct = false;
            return 0;
        }
        checksum += static_cast<uint32_t>(result[i & 255]);
    }
    const auto ended = std::chrono::steady_clock::now();
    // Volatile sink prevents the compiler from removing the useful work.
    static volatile uint32_t sink;
    sink = checksum;
    correct = true;
    return static_cast<uint64_t>(std::chrono::duration_cast<
        std::chrono::nanoseconds>(ended - began).count()) /
        static_cast<uint64_t>(loops);
}

int main() {
    int values[256]{};
    uint32_t rng = 0x12345678u;
    for (int& value : values) {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        std::memcpy(&value, &rng, sizeof(value));
    }
    char wire[2100]{};
    fixed_output_streambuf buf(wire, sizeof(wire));
    std::ostream output(&buf);
    output << fast_array(values) << " NEXT";
    if (!output || buf.view().size() != 2053) return 2;

    // Verify a second token remains untouched and invalid data is rejected.
    int decoded[256]{};
    auto parse_test = [&](std::string_view source,
                          bool (*fn)(std::istream&, int*)) {
        fixed_input_streambuf streambuf(source.data(), source.size());
        std::istream stream(&streambuf);
        return fn(stream, decoded);
    };
    const std::string_view valid(buf.view().data(), 2048);
    if (!parse_test(valid, b11_decode) || std::memcmp(decoded, values, 1024) != 0 ||
        !parse_test(valid, read_fast_array) || std::memcmp(decoded, values, 1024) != 0)
        return 3;
    char invalid[2049]{};
    std::memcpy(invalid, valid.data(), valid.size());
    for (const auto parser : {b11_decode, read_fast_array}) {
        invalid[99] = 'g';
        if (parse_test(std::string_view(invalid, 2048), parser)) return 4;
        if (parse_test(std::string_view(wire, 2047), parser)) return 5;
        invalid[99] = wire[99];
        invalid[2048] = 'X';
        if (parse_test(std::string_view(invalid, 2049), parser)) return 6;
        invalid[2048] = '\0';
    }

    const auto new_codec = [](std::istream& in, int* data) {
        return read_fast_array(in, data);
    };
    constexpr int repeats = 7;
    constexpr int loops = 16000;
    std::array<uint64_t, repeats> previous{}, current{};
    bool good = false;
    for (int round = 0; round < repeats; ++round) {
        if ((round & 1) == 0) {
            previous[round] = bench(valid, values, loops, b11_decode, good);
            if (!good) return 7;
            current[round] = bench(valid, values, loops, new_codec, good);
            if (!good) return 8;
        } else {
            current[round] = bench(valid, values, loops, new_codec, good);
            if (!good) return 8;
            previous[round] = bench(valid, values, loops, b11_decode, good);
            if (!good) return 7;
        }
    }
    std::sort(previous.begin(), previous.end());
    std::sort(current.begin(), current.end());
    const uint64_t old_ns = previous[repeats / 2], new_ns = current[repeats / 2];
    std::printf("FAST_ARRAY_DECODE_BENCH loops=%d rounds=%d baseline_b11_ns=%llu candidate_ns=%llu candidate_over_baseline=%.4f\n",
        loops, repeats, static_cast<unsigned long long>(old_ns),
        static_cast<unsigned long long>(new_ns),
        old_ns ? static_cast<double>(new_ns) / old_ns : 0.0);
    return 0;
}
