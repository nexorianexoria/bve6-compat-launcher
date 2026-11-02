#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <utility>
#include <chrono>
#include <thread>
#include <cerrno>
#include <climits>
#include "../../src/bridge/encoder.h"
#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <streambuf>
#include <string_view>

static bool verify_fast_array_codec() {
    int original[256]{};
    for (size_t i = 0; i < 256; ++i)
        original[i] = static_cast<int>((i * 1234567u) ^ (i << 17));
    original[0] = (std::numeric_limits<int>::min)();
    original[1] = (std::numeric_limits<int>::max)();
    original[2] = -1;
    original[3] = 0;

    static constexpr char digits[] = "0123456789abcdef";
    char expected[2048]{};
    for (size_t i = 0; i < 256; ++i) {
        unsigned int value = static_cast<unsigned int>(original[i]);
        for (int nibble = 7; nibble >= 0; --nibble) {
            expected[i * 8 + static_cast<size_t>(nibble)] = digits[value & 15u];
            value >>= 4;
        }
    }

    std::array<char, 4096> output_bytes{};
    fixed_output_streambuf output_buffer(output_bytes.data(), output_bytes.size());
    std::ostream output(&output_buffer);
    output << fast_array(original);
    if (!output || output_buffer.view() != std::string_view(expected, sizeof(expected)))
        return false;

    std::array<char, 2055> input_bytes{};
    input_bytes[0] = ' ';
    input_bytes[1] = '\t';
    std::memcpy(input_bytes.data() + 2, expected, sizeof(expected));
    input_bytes[2050] = '\n';
    std::memcpy(input_bytes.data() + 2051, "NEXT", 4);
    fixed_input_streambuf input_buffer(input_bytes.data(), input_bytes.size());
    std::istream input(&input_buffer);
    int decoded[256]{};
    if (!read_fast_array(input, decoded) ||
        std::memcmp(original, decoded, sizeof(original)) != 0)
        return false;
    char delimiter = 0;
    if (!input.get(delimiter) || delimiter != '\n') return false;
    char next[4]{};
    if (!input.read(next, sizeof(next)) || std::memcmp(next, "NEXT", 4) != 0)
        return false;

    std::array<char, 2049> invalid{};
    std::memcpy(invalid.data(), expected, sizeof(expected));
    invalid[100] = 'g';
    invalid[2048] = '\n';
    fixed_input_streambuf invalid_buffer(invalid.data(), invalid.size());
    std::istream invalid_input(&invalid_buffer);
    if (read_fast_array(invalid_input, decoded)) return false;

    fixed_input_streambuf short_buffer(expected, sizeof(expected) - 1);
    std::istream short_input(&short_buffer);
    if (read_fast_array(short_input, decoded)) return false;

    std::array<char, 2049> trailing{};
    std::memcpy(trailing.data(), expected, sizeof(expected));
    trailing[2048] = 'X';
    fixed_input_streambuf trailing_buffer(trailing.data(), trailing.size());
    std::istream trailing_input(&trailing_buffer);
    if (read_fast_array(trailing_input, decoded)) return false;

    std::array<char, 8> tiny_output{};
    fixed_output_streambuf tiny_buffer(tiny_output.data(), tiny_output.size());
    std::ostream tiny_stream(&tiny_buffer);
    tiny_stream.write("123456789", 9);
    if (tiny_stream.good() || tiny_buffer.view() != "12345678")
        return false;

    fixed_input_streambuf scalar_buffer("123 456", 7);
    std::istream scalar_input(&scalar_buffer);
    int first = 0, second = 0;
    if (!(scalar_input >> first >> second) || first != 123 || second != 456)
        return false;

    std::printf("FAST_ARRAY_CODEC_OK bytes=2048 arrays=256 invalid_hex=checked short_input=checked delimiter=checked fixed_streams=checked\n");
    return true;
}


static bool same_vehicle_state(const ATS_VEHICLESTATE& left,
                               const ATS_VEHICLESTATE& right) {
    return left.Location == right.Location && left.Speed == right.Speed &&
        left.Time == right.Time && left.BcPressure == right.BcPressure &&
        left.MrPressure == right.MrPressure && left.ErPressure == right.ErPressure &&
        left.BpPressure == right.BpPressure && left.SapPressure == right.SapPressure &&
        left.Current == right.Current;
}

static unsigned long long benchmark_legacy_protocol(
    const ATS_VEHICLESTATE& state, const int* panel, const int* sound,
    int iterations, bool& valid) {
    unsigned long long checksum = 0;
    const auto began = std::chrono::steady_clock::now();
    for (int i = 0; i < iterations; ++i) {
        std::ostringstream output;
        output << "ElapseFast " << state
            << " " << fast_array(panel) << " " << fast_array(sound) << '\n';
        std::string wire = output.str();
        std::istringstream input(std::move(wire));
        std::string command;
        ATS_VEHICLESTATE decoded{};
        int decoded_panel[256]{}, decoded_sound[256]{};
        if (!(input >> command >> decoded) || command != "ElapseFast" ||
            !read_fast_array(input, decoded_panel) ||
            !read_fast_array(input, decoded_sound) ||
            !same_vehicle_state(state, decoded) ||
            std::memcmp(panel, decoded_panel, sizeof(decoded_panel)) != 0 ||
            std::memcmp(sound, decoded_sound, sizeof(decoded_sound)) != 0) {
            valid = false;
            return 0;
        }
        checksum += static_cast<unsigned long long>(decoded.Time) +
            static_cast<unsigned int>(decoded_panel[i & 255]) +
            static_cast<unsigned int>(decoded_sound[(i + 31) & 255]);
    }
    const auto ended = std::chrono::steady_clock::now();
    valid = true;
    return static_cast<unsigned long long>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(ended - began).count()) /
        static_cast<unsigned long long>(iterations) + (checksum == 0 ? 1ULL : 0ULL);
}

static unsigned long long benchmark_fixed_protocol(
    const ATS_VEHICLESTATE& state, const int* panel, const int* sound,
    int iterations, bool& valid) {
    unsigned long long checksum = 0;
    const auto began = std::chrono::steady_clock::now();
    for (int i = 0; i < iterations; ++i) {
        std::array<char, 8192> wire;
        fixed_output_streambuf output_buffer(wire.data(), wire.size());
        std::ostream output(&output_buffer);
        output << "ElapseFast " << state
            << " " << fast_array(panel) << " " << fast_array(sound) << '\n';
        if (!output) { valid = false; return 0; }

        fixed_input_streambuf input_buffer(
            output_buffer.view().data(), output_buffer.view().size());
        std::istream input(&input_buffer);
        std::string command;
        ATS_VEHICLESTATE decoded{};
        int decoded_panel[256]{}, decoded_sound[256]{};
        if (!(input >> command >> decoded) || command != "ElapseFast" ||
            !read_fast_array(input, decoded_panel) ||
            !read_fast_array(input, decoded_sound) ||
            !same_vehicle_state(state, decoded) ||
            std::memcmp(panel, decoded_panel, sizeof(decoded_panel)) != 0 ||
            std::memcmp(sound, decoded_sound, sizeof(decoded_sound)) != 0) {
            valid = false;
            return 0;
        }
        checksum += static_cast<unsigned long long>(decoded.Time) +
            static_cast<unsigned int>(decoded_panel[i & 255]) +
            static_cast<unsigned int>(decoded_sound[(i + 31) & 255]);
    }
    const auto ended = std::chrono::steady_clock::now();
    valid = true;
    return static_cast<unsigned long long>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(ended - began).count()) /
        static_cast<unsigned long long>(iterations) + (checksum == 0 ? 1ULL : 0ULL);
}

static bool benchmark_protocol_stream_buffers() {
    ATS_VEHICLESTATE state{};
    state.Location = 12345.678901234;
    state.Speed = 45.625f;
    state.Time = 1234567;
    state.BcPressure = 490.125f;
    state.MrPressure = 780.25f;
    state.ErPressure = 490.5f;
    state.BpPressure = 490.75f;
    state.SapPressure = 490.875f;
    state.Current = 123.125f;
    int panel[256]{}, sound[256]{};
    for (size_t i = 0; i < 256; ++i) {
        panel[i] = static_cast<int>((i * 7919u) ^ (i << 9));
        sound[i] = static_cast<int>((i * 104729u) ^ (i << 13));
    }

    // The fixed-buffer path must emit exactly the same wire bytes as the
    // previous ostringstream protocol before it is timed.
    std::ostringstream legacy_wire_stream;
    legacy_wire_stream << "ElapseFast " << state
        << " " << fast_array(panel) << " " << fast_array(sound) << '\n';
    const std::string legacy_wire = legacy_wire_stream.str();
    std::array<char, 8192> fixed_wire;
    fixed_output_streambuf fixed_wire_buffer(fixed_wire.data(), fixed_wire.size());
    std::ostream fixed_wire_stream(&fixed_wire_buffer);
    fixed_wire_stream << "ElapseFast " << state
        << " " << fast_array(panel) << " " << fast_array(sound) << '\n';
    if (!fixed_wire_stream ||
        std::string_view(legacy_wire) != fixed_wire_buffer.view())
        return false;

    constexpr int iterations = 10000;
    constexpr int repeats = 5;
    bool valid = false;
    (void)benchmark_legacy_protocol(state, panel, sound, 100, valid);
    if (!valid) return false;
    (void)benchmark_fixed_protocol(state, panel, sound, 100, valid);
    if (!valid) return false;

    std::array<unsigned long long, repeats> legacy_samples{};
    std::array<unsigned long long, repeats> fixed_samples{};
    for (int repeat = 0; repeat < repeats; ++repeat) {
        // Alternate order to reduce systematic warm-cache / scheduling bias.
        if ((repeat & 1) == 0) {
            fixed_samples[repeat] = benchmark_fixed_protocol(
                state, panel, sound, iterations, valid);
            if (!valid) return false;
            legacy_samples[repeat] = benchmark_legacy_protocol(
                state, panel, sound, iterations, valid);
        }
        else {
            legacy_samples[repeat] = benchmark_legacy_protocol(
                state, panel, sound, iterations, valid);
            if (!valid) return false;
            fixed_samples[repeat] = benchmark_fixed_protocol(
                state, panel, sound, iterations, valid);
        }
        if (!valid) return false;
    }
    std::sort(legacy_samples.begin(), legacy_samples.end());
    std::sort(fixed_samples.begin(), fixed_samples.end());
    const auto legacy_median = legacy_samples[repeats / 2];
    const auto fixed_median = fixed_samples[repeats / 2];
    const double ratio = legacy_median
        ? static_cast<double>(fixed_median) / static_cast<double>(legacy_median) : 0.0;
    std::printf("PROTOCOL_STREAM_MICROBENCH iterations=%d repeats=%d statistic=median legacy_ns=%llu fixed_ns=%llu fixed_over_legacy=%.3f\n",
        iterations, repeats, legacy_median, fixed_median, ratio);
    return true;
}

int main(int argc, char** argv) {
    if (!verify_fast_array_codec()) {
        std::fprintf(stderr, "fast array codec regression failed\n");
        return 12;
    }
    if (!benchmark_protocol_stream_buffers()) {
        std::fprintf(stderr, "protocol stream buffer benchmark failed\n");
        return 13;
    }
    if (argc != 3) { std::fprintf(stderr,"usage: bridge_harness <frontend DLL> <frame count>\n"); return 2; }
    const int frames = std::atoi(argv[2]);
    if (frames <= 0 || frames > 100000) return 3;
    HMODULE module = LoadLibraryA(argv[1]);
    if (!module) { std::fprintf(stderr,"bridge load failed %lu\n",GetLastError()); return 4; }
    using Simple = void (WINAPI*)(void);
    using Spec = void (WINAPI*)(ATS_VEHICLESPEC);
    using Init = void (WINAPI*)(int);
    using ElapseFn = ATS_HANDLES (WINAPI*)(ATS_VEHICLESTATE,int*,int*);
    Simple load = reinterpret_cast<Simple>(GetProcAddress(module,"Load"));
    Simple dispose = reinterpret_cast<Simple>(GetProcAddress(module,"Dispose"));
    Spec spec = reinterpret_cast<Spec>(GetProcAddress(module,"SetVehicleSpec"));
    Init init = reinterpret_cast<Init>(GetProcAddress(module,"Initialize"));
    ElapseFn elapse = reinterpret_cast<ElapseFn>(GetProcAddress(module,"Elapse"));
    if (!load || !dispose || !spec || !init || !elapse) return 5;
    using InputFn = void (WINAPI*)(int);
    InputFn keyDown = reinterpret_cast<InputFn>(GetProcAddress(module,"KeyDown"));
    InputFn keyUp = reinterpret_cast<InputFn>(GetProcAddress(module,"KeyUp"));
    InputFn horn = reinterpret_cast<InputFn>(GetProcAddress(module,"HornBlow"));
    const bool check_inputs =
        GetEnvironmentVariableA("BVE5COMPAT_TEST_INPUT_DIAGNOSTICS",nullptr,0) > 0;
    const bool parallel_inputs =
        GetEnvironmentVariableA("BVE5COMPAT_TEST_PARALLEL_INPUT",nullptr,0) > 0;
    if ((check_inputs || parallel_inputs) && (!keyDown || !keyUp || !horn)) return 14;
    load();
    ATS_VEHICLESPEC vehicle{};
    vehicle.BrakeNotches = 8;
    vehicle.PowerNotches = 5;
    vehicle.Cars = 4;
    spec(vehicle);
    init(0);
    int panel[256]{};
    int sound[256]{};
    // Test-only optional inter-frame idle interval; never included in the launcher.
    char pause_input[24]{};
    DWORD pause_count=GetEnvironmentVariableA("BVE5COMPAT_TEST_HARNESS_PAUSE_MS",
        pause_input,static_cast<DWORD>(sizeof(pause_input)));
    const int pause_ms=pause_count>0 && pause_count<sizeof(pause_input)?
        std::atoi(pause_input):0;
    if(pause_ms<0 || pause_ms>500) return 8;

    // Test-only cadence and planned interruption. No production ATS logic
    // and no actual BVE graphics are involved.
    auto test_int = [](const char* key, int fallback, int lower, int upper) -> int {
        char value[32]{};
        DWORD n = GetEnvironmentVariableA(key, value, sizeof(value));
        if (!n) return fallback;
        if (n >= sizeof(value)) return -1;
        char* end = nullptr;
        errno = 0;
        long parsed = std::strtol(value, &end, 10);
        if (errno != 0 || end == value || *end != '\0' ||
            parsed < lower || parsed > upper) return -1;
        return static_cast<int>(parsed);
    };
    const int sound_cycle = test_int("BVE5COMPAT_TEST_MOCK_SOUND_CYCLE", 0, 0, 500);
    if (sound_cycle < 0) return 8;
    const int cadence_us = test_int("BVE5COMPAT_TEST_HARNESS_CADENCE_US", 0, 0, 100000);
    const int pause_frame = test_int("BVE5COMPAT_TEST_HARNESS_PAUSE_FRAME", 124, 0, frames-1);
    if (cadence_us < 0 || pause_frame < 0) return 8;
    const auto cadence_start = std::chrono::steady_clock::now();
    unsigned long long max_elapse_us = 0;
    std::thread parallel_keys, parallel_horn;
    if (parallel_inputs) {
        parallel_keys = std::thread([&] {
            for (int i = 0; i < 80; ++i) { keyDown(7); keyUp(7); }
        });
        parallel_horn = std::thread([&] {
            for (int i = 0; i < 80; ++i) horn(0);
        });
    }
    for(int i=0;i<frames;i++) {
        if (check_inputs && i == 5) { keyDown(7); horn(0); }
        if (check_inputs && i == 6) keyUp(7);
        if (cadence_us) {
            const auto target = cadence_start + std::chrono::microseconds(
                static_cast<long long>(i) * cadence_us);
            std::this_thread::sleep_until(target);
        }
        if(i==pause_frame && pause_ms>0) Sleep(static_cast<DWORD>(pause_ms));
        ATS_VEHICLESTATE state{};
        state.Time = i * 16;
        const auto frame_began = std::chrono::steady_clock::now();
        ATS_HANDLES result = elapse(state,panel,sound);
        const auto call_us = std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - frame_began).count();
        if (call_us > 0 && static_cast<unsigned long long>(call_us) > max_elapse_us)
            max_elapse_us = static_cast<unsigned long long>(call_us);
        const int expected_brake_sound = sound_cycle > 0 ?
            (i / sound_cycle) % 2 : 0;
        if(result.Brake!=1 || result.Power!=2 || panel[0]!=state.Time || panel[1]!=7 ||
           panel[255]!=0x12345678 || sound[0]!=0 || sound[255]!=-20000 ||
           sound[150]!=expected_brake_sound) {
            std::fprintf(stderr,"roundtrip mismatch at frame %d: brake=%d power=%d panel=%d\n",
                i,result.Brake,result.Power,panel[0]);
            dispose();
            return 7;
        }
        if (check_inputs && i>=5 &&
            (sound[31]!=ATS_SOUND_PLAY || sound[33]!=ATS_SOUND_PLAY)) {
            std::fprintf(stderr,"key/horn input was lost at frame %d: horn=%d key=%d\n",
                i,sound[31],sound[33]);
            dispose();
            return 15;
        }
        // b5 regression: frontend's periodic diagnostics must not be
        // written to bridge.log from the running driving thread.
        // Short paced trials also verify no disk diagnostic lines mid-run.
        if (i == (frames > 601 ? 600 : frames / 2)) {
            const char* inspect = std::getenv("BVE5COMPAT_TEST_ASSERT_BUFFERED");
            const char* inspect_host = std::getenv("BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED");
            const char* log_path = std::getenv("BVE5COMPAT_LOG");
            if ((inspect != nullptr && inspect[0] == '1') ||
                (inspect_host != nullptr && inspect_host[0] == '1')) {
                if (log_path == nullptr || *log_path == '\0') return 9;
                std::ifstream log(log_path);
                if (!log) return 10;
                for (std::string entry; std::getline(log, entry); ) {
                    const bool frontend_line =
                        entry.rfind("FRONTEND_FRAME ", 0) == 0 ||
                        entry.rfind("FRONTEND_SOUND_RETURN ", 0) == 0 ||
                        entry.rfind("FRONTEND_DIAGNOSTIC_BUFFER_SUMMARY ", 0) == 0;
                    const bool host_line =
                        entry.rfind("BC_SOUND ", 0) == 0 ||
                        entry.rfind("BC_SOUND_BUFFER_SUMMARY ", 0) == 0;
                    if ((inspect != nullptr && inspect[0] == '1' && frontend_line) ||
                        (inspect_host != nullptr && inspect_host[0] == '1' && host_line)) {
                        std::fprintf(stderr, "Diagnostics were written before Dispose.\n");
                        return 11;
                    }
                }
            }
        }
    }
    if (parallel_keys.joinable()) parallel_keys.join();
    if (parallel_horn.joinable()) parallel_horn.join();
    if (parallel_inputs) {
        ATS_VEHICLESTATE endstate{};
        endstate.Time=frames*16;
        elapse(endstate,panel,sound);
        if (sound[40] != 80 || sound[41] != 80 || sound[42] != 80) {
            std::fprintf(stderr,"parallel inputs lost: down=%d up=%d horn=%d\n",
                sound[40],sound[41],sound[42]);
            dispose();
            return 16;
        }
    }
    dispose();
    FreeLibrary(module);
    const auto elapsed_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - cadence_start).count();
    std::printf("HARNESS_TIMING frames=%d cadence_us=%d elapsed_ms=%lld max_elapse_us=%llu pause_frame=%d pause_ms=%d\n",
        frames, cadence_us, static_cast<long long>(elapsed_ms),
        max_elapse_us, pause_frame, pause_ms);
    std::printf("MOCK_ATS_BRIDGE_OK frames=%d\n",frames);
    return 0;
}
