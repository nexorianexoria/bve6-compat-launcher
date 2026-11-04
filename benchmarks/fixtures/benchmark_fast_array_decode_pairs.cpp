// Compare the prior b13 per-nibble decoder against the new lookup decoder.
// No live BVE, plugin or route files are read.
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
#include <string_view>

// Retain the prior production b13 implementation as an independent baseline.
static bool decode_b13_prior(std::istream& in, int* values) {
    std::istream::sentry sentry(in);
    if (!sentry) return false;
    auto* source = in.rdbuf();
    std::array<char, 2048> encoded{};
    if (source->sgetn(encoded.data(), encoded.size()) != encoded.size()) {
        in.setstate(std::ios::failbit);
        return false;
    }
    for (size_t i = 0; i < 256; ++i) {
        unsigned int value = 0;
        for (size_t nibble = 0; nibble < 8; ++nibble) {
            const char digit = encoded[i * 8 + nibble];
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
    if (!std::char_traits<char>::eq_int_type(delimiter,
            std::char_traits<char>::eof()) &&
        !fast_array_space(std::char_traits<char>::to_char_type(delimiter))) {
        in.setstate(std::ios::failbit);
        return false;
    }
    return true;
}

static bool decode_lookup(std::istream& input, int* destination) {
    std::istream::sentry sentry(input);
    if (!sentry) return false;
    auto* source = input.rdbuf();
    std::array<char, 2048> encoded;
    if (source->sgetn(encoded.data(), encoded.size()) != encoded.size()) {
        input.setstate(std::ios::failbit);
        return false;
    }
    static constexpr auto hex = [] {
        std::array<unsigned char, 256> values{};
        for (auto& v : values) v = 0xff;
        for (unsigned int i = 0; i < 10; ++i)
            values['0' + i] = static_cast<unsigned char>(i);
        for (unsigned int i = 0; i < 6; ++i)
            values['a' + i] = static_cast<unsigned char>(10 + i);
        return values;
    }();
    for (size_t i = 0; i < 256; ++i) {
        uint32_t value = 0;
        for (size_t byte = 0; byte < 4; ++byte) {
            const unsigned char a = hex[static_cast<unsigned char>(encoded[i * 8 + byte * 2])];
            const unsigned char b = hex[static_cast<unsigned char>(encoded[i * 8 + byte * 2 + 1])];
            if ((a | b) > 15) {
                input.setstate(std::ios::failbit);
                return false;
            }
            value = (value << 8) | (static_cast<uint32_t>(a) << 4) | b;
        }
        std::memcpy(destination + i, &value, sizeof(value));
    }
    const auto delimiter = source->sgetc();
    if (!std::char_traits<char>::eq_int_type(delimiter,
            std::char_traits<char>::eof()) &&
        !fast_array_space(std::char_traits<char>::to_char_type(delimiter))) {
        input.setstate(std::ios::failbit);
        return false;
    }
    return true;
}

using Decoder = bool (*)(std::istream&, int*);
static bool check(std::string_view wire, Decoder decoder, bool should_succeed,
                  const int* expected = nullptr) {
    int actual[256]{};
    fixed_input_streambuf b(wire.data(), wire.size());
    std::istream s(&b);
    const bool ok = decoder(s, actual);
    return ok == should_succeed &&
        (!ok || expected == nullptr || std::memcmp(actual, expected, 1024) == 0);
}

static uint64_t bench(std::string_view wire, int loops, Decoder decoder,
                      const int* expected, bool& ok) {
    unsigned int checksum = 0;
    const auto begin = std::chrono::steady_clock::now();
    for (int i = 0; i < loops; ++i) {
        int values[256]{};
        fixed_input_streambuf b(wire.data(), wire.size());
        std::istream s(&b);
        if (!decoder(s, values) || std::memcmp(values, expected, 1024) != 0) {
            ok = false;
            return 0;
        }
        checksum += static_cast<uint32_t>(values[i & 255]);
    }
    static volatile unsigned int sink;
    sink = checksum;
    ok = true;
    return static_cast<uint64_t>(std::chrono::duration_cast<
        std::chrono::nanoseconds>(std::chrono::steady_clock::now() - begin).count()) / loops;
}

int main() {
    int values[256]{};
    uint32_t seed = 0xabcde123u;
    for (int& x : values) {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        std::memcpy(&x, &seed, sizeof(x));
    }
    values[0] = 0; values[1] = -1;
    values[2] = INT32_MIN; values[3] = INT32_MAX;
    char bytes[2100]{};
    fixed_output_streambuf b(bytes, sizeof(bytes));
    std::ostream out(&b);
    out << fast_array(values) << " NEXT";
    if (!out || b.view().size() != 2053) return 2;
    for (const Decoder decode : {decode_b13_prior, read_fast_array, decode_lookup}) {
        if (!check(std::string_view(bytes, 2048), decode, true, values) ||
            !check(b.view(), decode, true, values) ||
            !check(std::string_view(bytes, 2047), decode, false)) return 3;
        char invalid[2100];
        std::memcpy(invalid, bytes, sizeof(bytes));
        invalid[2048] = 'X';
        if (!check(std::string_view(invalid, 2049), decode, false)) return 4;
        invalid[2048] = ' ';
        for (size_t offset = 0; offset < 2048; offset += 17) {
            const char old = invalid[offset];
            for (unsigned int bad = 0; bad < 256; bad += 7) {
                const char x = static_cast<char>(bad);
                if ((x >= '0' && x <= '9') || (x >= 'a' && x <= 'f')) continue;
                invalid[offset] = x;
                if (!check(std::string_view(invalid, 2049), decode, false)) return 5;
            }
            invalid[offset] = old;
        }
    }
    constexpr int loops = 16000, rounds = 7;
    const std::string_view wire(bytes, 2048);
    std::array<uint64_t, rounds> current{}, candidate{};
    bool ok = false;
    for (int i = 0; i < rounds; ++i) {
        if (i % 2 == 0) {
            current[i] = bench(wire, loops, decode_b13_prior, values, ok);
            if (!ok) return 6;
            candidate[i] = bench(wire, loops, read_fast_array, values, ok);
            if (!ok) return 7;
        } else {
            candidate[i] = bench(wire, loops, read_fast_array, values, ok);
            if (!ok) return 7;
            current[i] = bench(wire, loops, decode_b13_prior, values, ok);
            if (!ok) return 6;
        }
    }
    std::sort(current.begin(), current.end());
    std::sort(candidate.begin(), candidate.end());
    const auto original = current[rounds/2], faster = candidate[rounds/2];
    std::printf("FAST_ARRAY_DECODE_PAIR_BENCH loops=%d rounds=%d baseline_b13_ns=%llu candidate_ns=%llu ratio=%.4f\n",
        loops, rounds, static_cast<unsigned long long>(original),
        static_cast<unsigned long long>(faster),
        original ? static_cast<double>(faster)/original : 0.0);
    return 0;
}
