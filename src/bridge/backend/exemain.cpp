// exemain.cpp : executable entry point

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>
#include <objbase.h>
#pragma comment(lib, "ole32.lib")
#define ATS_IMPORTS

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <set>
#include <string>
#include <string_view>
#include <utility>
#include "../encoder.h"

namespace
{
    HANDLE protocol_output = INVALID_HANDLE_VALUE;
    void protocol_send(std::string_view value) {
        size_t offset = 0;
        while (offset < value.size()) {
            const size_t remaining = value.size() - offset;
            const DWORD requested = static_cast<DWORD>(
                remaining > MAXDWORD ? MAXDWORD : remaining);
            DWORD written = 0;
            if (!WriteFile(protocol_output, value.data() + offset,
                    requested, &written, nullptr) || written == 0)
                ExitProcess(74);
            offset += written;
        }
    }
    std::ofstream event_log;
    HANDLE crash_log_handle = INVALID_HANDLE_VALUE;
    const char* active_ats_call = "startup";
    unsigned long long elapse_frames = 0;
    unsigned long long elapse_total_us = 0;
    unsigned long long elapse_max_us = 0;
    // Session-level opt-in timing: collect in memory, write once on Dispose.
    bool backend_profile_enabled = false;
    struct StageMetric {
        unsigned long long count = 0;
        unsigned long long total_ns = 0;
        unsigned long long max_ns = 0;
        unsigned long long over_5ms = 0;
        void add(std::chrono::steady_clock::time_point start,
                 std::chrono::steady_clock::time_point end) {
            const auto delta = std::chrono::duration_cast<std::chrono::nanoseconds>(end - start).count();
            const unsigned long long ns = delta < 0 ? 0ULL : static_cast<unsigned long long>(delta);
            ++count;
            total_ns += ns;
            if (ns > max_ns) max_ns = ns;
            if (ns > 5000000ULL) ++over_5ms;
        }
    };
    enum HostStage { ParseStage, AtsStage, DiagnosticStage, EncodeStage, SendStage, HostStageCount };
    StageMetric host_metrics[HostStageCount]{};
    const char* host_stage_names[HostStageCount] = { "parse", "ats", "diagnostics", "encode", "send" };
    constexpr unsigned long long host_slow_threshold_ns = 5000000ULL;
    constexpr size_t host_slow_limit = 512;
    struct HostSlowEvent {
        unsigned long long frame;
        int stage;
        unsigned long long duration_ns;
        long long qpc_start;
        long long qpc_end;
    };
    std::array<HostSlowEvent, host_slow_limit> host_slow_events{};
    size_t host_slow_count = 0;
    unsigned long long host_slow_dropped = 0;
    LARGE_INTEGER host_qpc_frequency{};
    // A blocking stdin read includes ordinary inter-frame idle time.
    // It is NOT a measurement of pure pipe transport latency.
    constexpr long long input_wait_threshold_ms = 50;
    constexpr size_t input_wait_capacity = 512;
    struct InputWaitEvent {
        unsigned long long frame;
        unsigned long long duration_ns;
        long long qpc_start;
        long long qpc_end;
        unsigned long long bytes;
    };
    std::array<InputWaitEvent, input_wait_capacity> input_wait_events{};
    size_t input_wait_count = 0;
    unsigned long long input_wait_dropped = 0;
    unsigned long long input_wait_frames = 0;
    unsigned long long input_wait_total_qpc = 0;
    unsigned long long input_wait_max_qpc = 0;
    void observe_input_wait(unsigned long long frame, LARGE_INTEGER begin,
                            LARGE_INTEGER end, size_t bytes) {
        if (!backend_profile_enabled || host_qpc_frequency.QuadPart <= 0 ||
            end.QuadPart < begin.QuadPart) return;
        const unsigned long long ticks =
            static_cast<unsigned long long>(end.QuadPart - begin.QuadPart);
        ++input_wait_frames;
        input_wait_total_qpc += ticks;
        if (ticks > input_wait_max_qpc) input_wait_max_qpc = ticks;
        if (ticks < static_cast<unsigned long long>(
                input_wait_threshold_ms * host_qpc_frequency.QuadPart / 1000)) return;
        if (input_wait_count >= input_wait_capacity) {
            ++input_wait_dropped;
            return;
        }
        const auto duration_ns = static_cast<unsigned long long>(
            static_cast<long double>(ticks) * 1000000000.0L /
            static_cast<long double>(host_qpc_frequency.QuadPart));
        input_wait_events[input_wait_count++] = {
            frame, duration_ns, begin.QuadPart, end.QuadPart,
            static_cast<unsigned long long>(bytes)
        };
    }
    void record_host_slow(unsigned long long frame, int stage,
                          std::chrono::steady_clock::time_point began,
                          std::chrono::steady_clock::time_point ended) {
        const auto diff = std::chrono::duration_cast<std::chrono::nanoseconds>(ended - began).count();
        if (diff < static_cast<long long>(host_slow_threshold_ns)) return;
        if (host_slow_count >= host_slow_limit) { ++host_slow_dropped; return; }
        LARGE_INTEGER qpc{};
        QueryPerformanceCounter(&qpc);
        const auto now = std::chrono::steady_clock::now();
        const auto elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(now - ended).count();
        const long double ticks_per_ns =
            static_cast<long double>(host_qpc_frequency.QuadPart) / 1000000000.0L;
        const long long end_qpc = qpc.QuadPart -
            static_cast<long long>(static_cast<long double>(elapsed_ns) * ticks_per_ns);
        const long long begin_qpc = end_qpc -
            static_cast<long long>(static_cast<long double>(diff) * ticks_per_ns);
        host_slow_events[host_slow_count++] = { frame, stage,
            static_cast<unsigned long long>(diff), begin_qpc, end_qpc };
    }
    std::array<unsigned long long, 18> brake_sound_plays{};
    std::array<unsigned long long, 18> brake_sound_loops{};
    std::array<unsigned long long, 18> brake_sound_stops{};
    std::array<int, 18> previous_brake_sound{};
    bool have_previous_brake_sound = false;
    // Keep BC_SOUND observations out of the ATS callback's file I/O path.
    // Store snapshots as values, not formatted strings, with a hard bound.
    constexpr size_t brake_sound_event_capacity = 4096;
    struct BrakeSoundEvent {
        unsigned long long frame;
        unsigned int index;
        int sound_state;
        int time;
        float bc;
        float mr;
        float er;
        float bp;
        float sap;
        float speed;
    };
    std::array<BrakeSoundEvent, brake_sound_event_capacity> brake_sound_events{};
    size_t brake_sound_event_count = 0;
    unsigned long long brake_sound_events_dropped = 0;
    void capture_brake_sound(unsigned long long frame, unsigned int index,
                             int state, const ATS_VEHICLESTATE& vehicle_state) {
        if (!event_log) return;
        if (brake_sound_event_count >= brake_sound_event_capacity) {
            ++brake_sound_events_dropped;
            return;
        }
        brake_sound_events[brake_sound_event_count++] = {
            frame, index, state, vehicle_state.Time,
            vehicle_state.BcPressure, vehicle_state.MrPressure,
            vehicle_state.ErPressure, vehicle_state.BpPressure,
            vehicle_state.SapPressure, vehicle_state.Speed
        };
    }
    void flush_brake_sound_diagnostics() {
        if (!event_log) return;
        for (size_t i = 0; i < brake_sound_event_count; ++i) {
            const auto& sample = brake_sound_events[i];
            event_log << "BC_SOUND frame=" << sample.frame
                << " index=" << sample.index
                << " state=" << sample.sound_state
                << " time=" << sample.time
                << " bc=" << sample.bc << " mr=" << sample.mr
                << " er=" << sample.er << " bp=" << sample.bp
                << " sap=" << sample.sap << " speed=" << sample.speed << '\n';
        }
        event_log << "BC_SOUND_BUFFER_SUMMARY stored=" << brake_sound_event_count
            << " dropped=" << brake_sound_events_dropped
            << " capacity=" << brake_sound_event_capacity << std::endl;
    }
    float min_bc_pressure = std::numeric_limits<float>::max();
    float max_bc_pressure = std::numeric_limits<float>::lowest();
    float min_bp_pressure = std::numeric_limits<float>::max();
    float max_bp_pressure = std::numeric_limits<float>::lowest();
    float min_mr_pressure = std::numeric_limits<float>::max();
    float max_mr_pressure = std::numeric_limits<float>::lowest();
    unsigned long long set_brake_calls = 0;
    int last_brake_notch = std::numeric_limits<int>::min();
    // Capture ATS sound slots used by the vehicle's HornMaster, but do not
    // mutate sound values. Buffer transitions to avoid per-frame log I/O.
    constexpr size_t horn_sound_capacity = 512;
    struct HornSoundEvent {
        unsigned long long frame;
        unsigned int index;
        int sound_state;
        int time;
        float speed;
        float bc;
    };
    std::array<HornSoundEvent, horn_sound_capacity> horn_sound_events{};
    std::array<int,4> previous_horn_sounds{};
    bool have_previous_horn_sound = false;
    size_t horn_sound_count = 0;
    unsigned long long horn_sound_dropped = 0;
    void capture_horn_sound_changes(const int* sounds, const ATS_VEHICLESTATE& vehicle) {
        if (!event_log) return;
        for (size_t i = 0; i < 4; ++i) {
            const int current = sounds[31 + i];
            if (!have_previous_horn_sound || previous_horn_sounds[i] != current) {
                if (horn_sound_count < horn_sound_capacity)
                    horn_sound_events[horn_sound_count++] = {
                        elapse_frames, static_cast<unsigned int>(31 + i),
                        current, vehicle.Time, vehicle.Speed, vehicle.BcPressure };
                else ++horn_sound_dropped;
            }
            previous_horn_sounds[i] = current;
        }
        have_previous_horn_sound = true;
    }
    void flush_horn_sound_diagnostics() {
        if (!event_log) return;
        for (size_t i = 0; i < horn_sound_count; ++i) {
            const auto& sample = horn_sound_events[i];
            event_log << "HORN_SOUND frame=" << sample.frame
                << " index=" << sample.index << " state=" << sample.sound_state
                << " time=" << sample.time << " speed=" << sample.speed
                << " bc=" << sample.bc << '\n';
        }
        event_log << "HORN_SOUND_BUFFER_SUMMARY stored=" << horn_sound_count
            << " dropped=" << horn_sound_dropped << std::endl;
    }

    LONG WINAPI log_unhandled_exception(EXCEPTION_POINTERS* info)
    {
        if (crash_log_handle != INVALID_HANDLE_VALUE && info != nullptr && info->ExceptionRecord != nullptr) {
            char line[256]{};
            const int length = sprintf_s(line, "UNHANDLED_EXCEPTION call=%s code=0x%08lX address=0x%p\r\n",
                active_ats_call, info->ExceptionRecord->ExceptionCode, info->ExceptionRecord->ExceptionAddress);
            if (length > 0) {
                DWORD written = 0;
                WriteFile(crash_log_handle, line, static_cast<DWORD>(length), &written, nullptr);
                FlushFileBuffers(crash_log_handle);
            }
        }
        return EXCEPTION_EXECUTE_HANDLER;
    }

    std::wstring environment_value(const wchar_t* name)
    {
        DWORD length = GetEnvironmentVariableW(name, nullptr, 0);
        if (length == 0) return {};
        std::wstring value(length, L'\0');
        DWORD copied = GetEnvironmentVariableW(name, value.data(), length);
        if (copied == 0 || copied >= length) return {};
        value.resize(copied);
        return value;
    }

    std::filesystem::path get_module_file_name(HMODULE hModule)
    {
        std::wstring name;
        name.resize(1u << 8);
        while (name.size() < 1u << 16) {
            DWORD s = GetModuleFileNameW(
                hModule,
                name.data(),
                static_cast<DWORD>(name.size()));
            if (0 < s && s < name.size()) {
                name.resize(s);
                name.shrink_to_fit();
                return std::move(name);
            }
            name.resize(name.size() * 2);
        }
        name.clear();
        name.shrink_to_fit();
        return std::move(name);
    }

    void log_loaded_modules(const char* stage)
    {
        if (!event_log) return;
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
        if (snapshot == INVALID_HANDLE_VALUE) {
            event_log << "MODULE_SNAPSHOT_FAILED stage=" << stage << " error=" << GetLastError() << std::endl;
            return;
        }
        MODULEENTRY32W module{};
        module.dwSize = sizeof(module);
        if (Module32FirstW(snapshot, &module)) {
            do {
                event_log << "MODULE stage=" << stage << " file="
                    << std::filesystem::path(module.szExePath).filename().string() << std::endl;
                module.dwSize = sizeof(module);
            } while (Module32NextW(snapshot, &module));
        }
        CloseHandle(snapshot);
    }

    bool load_functions_and_run(HMODULE library_handle)
    {
        FARPROC f;

        f = GetProcAddress(library_handle, "Load");
        if (f == NULL) {
            std::cerr << "Failed to load the Load function" << std::endl;
        }
        decltype(Load) *load = reinterpret_cast<decltype(Load) *>(f);

        f = GetProcAddress(library_handle, "Dispose");
        if (f == NULL) {
            std::cerr << "Failed to load the Dispose function" << std::endl;
        }
        decltype(Dispose) *dispose = reinterpret_cast<decltype(Dispose) *>(f);

        f = GetProcAddress(library_handle, "SetVehicleSpec");
        if (f == NULL) {
            std::cerr << "Failed to load the SetVehicleSpec function" << std::endl;
            return false;
        }
        decltype(SetVehicleSpec) *setVehicleSpec = reinterpret_cast<decltype(SetVehicleSpec) *>(f);

        f = GetProcAddress(library_handle, "Initialize");
        if (f == NULL) {
            std::cerr << "Failed to load the Initialize function" << std::endl;
            return false;
        }
        decltype(Initialize) *initialize = reinterpret_cast<decltype(Initialize) *>(f);

        f = GetProcAddress(library_handle, "Elapse");
        if (f == NULL) {
            std::cerr << "Failed to load the Elapse function" << std::endl;
            return false;
        }
        decltype(Elapse) *elapse = reinterpret_cast<decltype(Elapse) *>(f);

        f = GetProcAddress(library_handle, "SetPower");
        if (f == NULL) {
            std::cerr << "Failed to load the SetPower function" << std::endl;
            return false;
        }
        decltype(SetPower) *setPower = reinterpret_cast<decltype(SetPower) *>(f);

        f = GetProcAddress(library_handle, "SetBrake");
        if (f == NULL) {
            std::cerr << "Failed to load the SetBrake function" << std::endl;
            return false;
        }
        decltype(SetBrake) *setBrake = reinterpret_cast<decltype(SetBrake) *>(f);

        f = GetProcAddress(library_handle, "SetReverser");
        if (f == NULL) {
            std::cerr << "Failed to load the SetReverser function" << std::endl;
            return false;
        }
        decltype(SetReverser) *setReverser = reinterpret_cast<decltype(SetReverser) *>(f);

        f = GetProcAddress(library_handle, "KeyDown");
        if (f == NULL) {
            std::cerr << "Failed to load the KeyDown function" << std::endl;
            return false;
        }
        decltype(KeyDown) *keyDown = reinterpret_cast<decltype(KeyDown) *>(f);

        f = GetProcAddress(library_handle, "KeyUp");
        if (f == NULL) {
            std::cerr << "Failed to load the KeyUp function" << std::endl;
            return false;
        }
        decltype(KeyUp) *keyUp = reinterpret_cast<decltype(KeyUp) *>(f);

        f = GetProcAddress(library_handle, "HornBlow");
        if (f == NULL) {
            std::cerr << "Failed to load the HornBlow function" << std::endl;
            return false;
        }
        decltype(HornBlow) *hornBlow = reinterpret_cast<decltype(HornBlow) *>(f);

        f = GetProcAddress(library_handle, "DoorOpen");
        if (f == NULL) {
            std::cerr << "Failed to load the DoorOpen function" << std::endl;
            return false;
        }
        decltype(DoorOpen) *doorOpen = reinterpret_cast<decltype(DoorOpen) *>(f);

        f = GetProcAddress(library_handle, "DoorClose");
        if (f == NULL) {
            std::cerr << "Failed to load the DoorClose function" << std::endl;
            return false;
        }
        decltype(DoorClose) *doorClose = reinterpret_cast<decltype(DoorClose) *>(f);

        f = GetProcAddress(library_handle, "SetSignal");
        if (f == NULL) {
            std::cerr << "Failed to load the SetSignal function" << std::endl;
            return false;
        }
        decltype(SetSignal) *setSignal = reinterpret_cast<decltype(SetSignal) *>(f);

        f = GetProcAddress(library_handle, "SetBeaconData");
        if (f == NULL) {
            std::cerr << "Failed to load the SetBeaconData function" << std::endl;
            return false;
        }
        decltype(SetBeaconData) *setBeaconData = reinterpret_cast<decltype(SetBeaconData) *>(f);

        std::set<std::string> seen_commands;
        std::string buffer;
        buffer.reserve(8192);
        std::string command;
        for (;;) {
            LARGE_INTEGER input_began{}, input_ended{};
            buffer.clear();
            if (backend_profile_enabled) QueryPerformanceCounter(&input_began);
            if (!std::getline(std::cin, buffer)) break;
            if (backend_profile_enabled) QueryPerformanceCounter(&input_ended);
            const size_t input_size = buffer.size();
            fixed_input_streambuf line_buffer(buffer.data(), buffer.size());
            std::istream line(&line_buffer);
            command.clear();
            if (!(line >> command)) continue;
            if (seen_commands.insert(command).second && event_log) {
                event_log << "EVENT " << command << std::endl;
            }

            using namespace std::literals;
            if (command == "Load"sv) {
                if (load) {
                    active_ats_call = "Load";
                    if (event_log) { event_log << "ATS_CALL_BEGIN Load" << std::endl; event_log.flush(); }
                    load();
                    if (event_log) event_log << "ATS_CALL_END Load" << std::endl;
                }
                log_loaded_modules("Load");
                protocol_send("READY\n");
            }
            else if (command == "Dispose"sv) {
                flush_brake_sound_diagnostics();
                flush_horn_sound_diagnostics();
                if (event_log && elapse_frames > 0) {
                    event_log << "METRICS host_elapse_frames=" << elapse_frames
                        << " avg_us=" << (elapse_total_us / elapse_frames)
                        << " max_us=" << elapse_max_us << " set_brake_calls=" << set_brake_calls
                        << " last_brake_notch=" << (last_brake_notch == std::numeric_limits<int>::min() ? 0 : last_brake_notch)
                        << " bc_pressure_min=" << min_bc_pressure << " bc_pressure_max=" << max_bc_pressure
                        << " bp_pressure_min=" << min_bp_pressure << " bp_pressure_max=" << max_bp_pressure
                        << " mr_pressure_min=" << min_mr_pressure << " mr_pressure_max=" << max_mr_pressure
                        << " brake_sound_states=";
                    for (size_t i = 0; i < brake_sound_plays.size(); ++i) {
                        if (brake_sound_plays[i] || brake_sound_loops[i] || brake_sound_stops[i])
                            event_log << (150 + i) << ":once" << brake_sound_plays[i]
                                << "/loop" << brake_sound_loops[i] << "/stop" << brake_sound_stops[i] << ',';
                    }
                    event_log << std::endl;
                    if (backend_profile_enabled) {
                        for (int i = 0; i < HostStageCount; ++i) {
                            const auto& metric = host_metrics[i];
                            if (metric.count == 0) continue;
                            event_log << "METRICS host_phase stage=" << host_stage_names[i]
                                << " count=" << metric.count
                                << " avg_ns=" << (metric.total_ns / metric.count)
                                << " max_ns=" << metric.max_ns
                                << " over_5ms=" << metric.over_5ms << std::endl;
                        }
                        event_log << "SLOW_HOST_PHASE_SUMMARY threshold_ns=" << host_slow_threshold_ns
                            << " stored=" << host_slow_count << " dropped=" << host_slow_dropped
                            << " qpc_freq=" << host_qpc_frequency.QuadPart << std::endl;
                        for (size_t i = 0; i < host_slow_count; ++i) {
                            const HostSlowEvent& event = host_slow_events[i];
                            event_log << "SLOW_HOST_PHASE frame=" << event.frame
                                << " stage=" << host_stage_names[event.stage]
                                << " duration_ns=" << event.duration_ns
                                << " qpc_start=" << event.qpc_start
                                << " qpc_end=" << event.qpc_end
                                << " qpc_freq=" << host_qpc_frequency.QuadPart
                                << std::endl;
                        }
                        event_log << "HOST_INPUT_READ_SUMMARY threshold_ms="
                            << input_wait_threshold_ms
                            << " frames=" << input_wait_frames
                            << " avg_ns=" << (input_wait_frames
                                ? static_cast<unsigned long long>(
                                    static_cast<long double>(input_wait_total_qpc) *
                                    1000000000.0L /
                                    static_cast<long double>(host_qpc_frequency.QuadPart) /
                                    static_cast<long double>(input_wait_frames)) : 0)
                            << " max_ns=" << static_cast<unsigned long long>(
                                static_cast<long double>(input_wait_max_qpc) * 1000000000.0L /
                                static_cast<long double>(host_qpc_frequency.QuadPart))
                            << " stored=" << input_wait_count
                            << " dropped=" << input_wait_dropped
                            << " qpc_freq=" << host_qpc_frequency.QuadPart << std::endl;
                        for (size_t i = 0; i < input_wait_count; ++i) {
                            const auto& wait = input_wait_events[i];
                            event_log << "SLOW_HOST_INPUT frame=" << wait.frame
                                << " duration_ns=" << wait.duration_ns
                                << " qpc_start=" << wait.qpc_start
                                << " qpc_end=" << wait.qpc_end
                                << " qpc_freq=" << host_qpc_frequency.QuadPart
                                << " bytes=" << wait.bytes << std::endl;
                        }
                    }
                }
                if (dispose) {
                    dispose();
                }
            }
            else if (command == "SetVehicleSpec"sv) {
                ATS_VEHICLESPEC spec;
                if (line >> spec) {
                    active_ats_call = "SetVehicleSpec";
                    if (event_log) event_log << "VEHICLE_SPEC brake=" << spec.BrakeNotches
                        << " power=" << spec.PowerNotches << " ats=" << spec.AtsNotch
                        << " b67=" << spec.B67Notch << " cars=" << spec.Cars << std::endl;
                    setVehicleSpec(spec);
                }
            }
            else if (command == "Initialize"sv) {
                int brake;
                if (line >> brake) {
                    active_ats_call = "Initialize";
                    initialize(brake);
                    log_loaded_modules("Initialize");
                }
            }
            else if ((command == "Elapse"sv || command == "ElapseFast"sv)) {
                if (backend_profile_enabled)
                    observe_input_wait(elapse_frames + 1, input_began, input_ended, input_size);
                const auto parse_started = std::chrono::steady_clock::now();
                ATS_VEHICLESTATE state;
                // Some legacy modules write sentinel indexes such as 1000.
                // BVE exposes only indexes 0..255, so keep padding inside the
                // isolated host and forward just the supported range.
                int panels[2048]{}, sounds[2048]{};
                if ((line >> state) && (command == "ElapseFast"sv ? (read_fast_array(line,panels) && read_fast_array(line,sounds)) : static_cast<bool>(line >> arrayref<int,256>(panels) >> arrayref<int,256>(sounds)))) {
                    active_ats_call = "Elapse";
                    auto started = std::chrono::steady_clock::now();
                    ATS_HANDLES handles = elapse(state, panels, sounds);
                    auto finished_ats = std::chrono::steady_clock::now();
                    auto duration = std::chrono::duration_cast<std::chrono::microseconds>(
                        finished_ats - started).count();
                    ++elapse_frames;
                    // Emit once, after the first successfully completed ATS Elapse.
                    // The one-click recorder waits for this marker to avoid
                    // consuming its FPS capture window while BVE is still loading.
                    if (elapse_frames == 1 && backend_profile_enabled && event_log) {
                        LARGE_INTEGER first_elapse_qpc{};
                        QueryPerformanceCounter(&first_elapse_qpc);
                        event_log << "HOST_FIRST_ELAPSE frame=1 qpc="
                            << first_elapse_qpc.QuadPart
                            << " qpc_freq=" << host_qpc_frequency.QuadPart << std::endl;
                        event_log.flush();
                    }
                    if (state.BcPressure < min_bc_pressure) min_bc_pressure = state.BcPressure;
                    if (state.BcPressure > max_bc_pressure) max_bc_pressure = state.BcPressure;
                    if (state.BpPressure < min_bp_pressure) min_bp_pressure = state.BpPressure;
                    if (state.BpPressure > max_bp_pressure) max_bp_pressure = state.BpPressure;
                    if (state.MrPressure < min_mr_pressure) min_mr_pressure = state.MrPressure;
                    if (state.MrPressure > max_mr_pressure) max_mr_pressure = state.MrPressure;
                    elapse_total_us += static_cast<unsigned long long>(duration);
                    if (static_cast<unsigned long long>(duration) > elapse_max_us)
                        elapse_max_us = static_cast<unsigned long long>(duration);
                    for (size_t i = 0; i < brake_sound_plays.size(); ++i) {
                        const int state_value = sounds[150 + i];
                        if (state_value == ATS_SOUND_PLAY) ++brake_sound_plays[i];
                        else if (state_value == ATS_SOUND_PLAYLOOPING) ++brake_sound_loops[i];
                        else if (state_value == ATS_SOUND_STOP) ++brake_sound_stops[i];
                        if (!have_previous_brake_sound || previous_brake_sound[i] != state_value)
                            capture_brake_sound(elapse_frames, static_cast<unsigned int>(150 + i),
                                state_value, state);
                        previous_brake_sound[i] = state_value;
                    }
                    have_previous_brake_sound = true;
                    capture_horn_sound_changes(sounds, state);
                    const auto diagnostics_done = std::chrono::steady_clock::now();
                    std::array<char, 8192> response_bytes;
                    fixed_output_streambuf response_buffer(
                        response_bytes.data(), response_bytes.size());
                    std::ostream response(&response_buffer);
                    response << handles;
                    if (command == "ElapseFast"sv)
                        response << " " << fast_array(panels) << " " << fast_array(sounds);
                    else
                        response << arrayref<int,256>(panels) << arrayref<int,256>(sounds);
                    response << '\n';
                    if (!response) ExitProcess(75);
                    const auto encoded = std::chrono::steady_clock::now();
                    protocol_send(response_buffer.view());
                    const auto sent = std::chrono::steady_clock::now();
                    if (backend_profile_enabled) {
                        host_metrics[ParseStage].add(parse_started, started);
                        host_metrics[AtsStage].add(started, finished_ats);
                        host_metrics[DiagnosticStage].add(finished_ats, diagnostics_done);
                        host_metrics[EncodeStage].add(diagnostics_done, encoded);
                        host_metrics[SendStage].add(encoded, sent);
                        record_host_slow(elapse_frames, ParseStage, parse_started, started);
                        record_host_slow(elapse_frames, AtsStage, started, finished_ats);
                        record_host_slow(elapse_frames, DiagnosticStage, finished_ats, diagnostics_done);
                        record_host_slow(elapse_frames, EncodeStage, diagnostics_done, encoded);
                        record_host_slow(elapse_frames, SendStage, encoded, sent);
                    }
                }
            }
            else if (command == "SetPower"sv) {
                int notch;
                if (line >> notch) {
                    active_ats_call = "SetPower";
                    setPower(notch);
                    if (event_log) event_log << "INPUT_SET_POWER notch=" << notch << std::endl;
                }
            }
            else if (command == "SetBrake"sv) {
                int notch;
                if (line >> notch) {
                    active_ats_call = "SetBrake";
                    setBrake(notch);
                    ++set_brake_calls;
                    last_brake_notch = notch;
                    if (event_log) event_log << "INPUT_SET_BRAKE notch=" << notch << std::endl;
                }
            }
            else if (command == "SetReverser"sv) {
                int notch;
                if (line >> notch) {
                    active_ats_call = "SetReverser";
                    setReverser(notch);
                }
            }
            else if (command == "KeyDown"sv) {
                int key;
                if (line >> key) {
                    active_ats_call = "KeyDown";
                    if (event_log) event_log << "INPUT_KEY_DOWN code=" << key
                        << " tick=" << GetTickCount64() << std::endl;
                    keyDown(key);
                }
            }
            else if (command == "KeyUp"sv) {
                int key;
                if (line >> key) {
                    active_ats_call = "KeyUp";
                    if (event_log) event_log << "INPUT_KEY_UP code=" << key
                        << " tick=" << GetTickCount64() << std::endl;
                    keyUp(key);
                }
            }
            else if (command == "HornBlow"sv) {
                int type;
                if (line >> type) {
                    active_ats_call = "HornBlow";
                    if (event_log) event_log << "INPUT_HORN type=" << type
                        << " tick=" << GetTickCount64() << std::endl;
                    hornBlow(type);
                }
            }
            else if (command == "DoorOpen"sv) {
                doorOpen();
            }
            else if (command == "DoorClose"sv) {
                doorClose();
            }
            else if (command == "SetSignal"sv) {
                int aspect;
                if (line >> aspect) {
                    setSignal(aspect);
                }
            }
            else if (command == "SetBeaconData"sv) {
                ATS_BEACONDATA beacon;
                if (line >> beacon) {
                    setBeaconData(beacon);
                }
            }
            else {
                std::cerr << "Unrecognized command: " << command << std::endl;
            }
        }

        return true;
    }
}

int main(void)
{
    if(!DuplicateHandle(GetCurrentProcess(),GetStdHandle(STD_OUTPUT_HANDLE),GetCurrentProcess(),&protocol_output,0,FALSE,DUPLICATE_SAME_ACCESS))return 74;
    FILE* discard=nullptr;
    if(freopen_s(&discard,"NUL","w",stdout)!=0)return 74;
    SetStdHandle(STD_OUTPUT_HANDLE,CreateFileW(L"NUL",GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,0,nullptr));

    // Audio-oriented ATS modules may use COM while initializing XAudio2.
    const HRESULT com_result = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    // The BveEX extension sets these values after it selects a BVE5 vehicle.
    // The original 32-bit ATS DLL stays in the user's vehicle directory.
    const std::wstring target = environment_value(L"BVE5COMPAT_ATS");
    backend_profile_enabled = environment_value(L"BVE5COMPAT_PROFILE") == L"1";
    if (backend_profile_enabled &&
        (!QueryPerformanceFrequency(&host_qpc_frequency) || host_qpc_frequency.QuadPart <= 0))
        backend_profile_enabled = false;
    const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
    if (!log_path.empty()) {
        event_log.open(std::filesystem::path(log_path), std::ios::app);
        crash_log_handle = CreateFileW(log_path.c_str(), FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    }
    SetUnhandledExceptionFilter(log_unhandled_exception);
    if (event_log) event_log << "HOST_STARTED pid=" << GetCurrentProcessId() << std::endl;
    if (event_log) event_log << "COM_INIT result=0x" << std::hex << com_result << std::dec << std::endl;
    if (target.empty()) {
        if (event_log) event_log << "ERROR missing ATS path" << std::endl;
        return 1;
    }
    std::filesystem::path path(target);
    if (!path.is_absolute() || !std::filesystem::is_regular_file(path)) {
        if (event_log) event_log << "ERROR ATS path must be an existing absolute file" << std::endl;
        return 1;
    }
    HMODULE library_handle = LoadLibraryW(path.c_str());
    if (library_handle == NULL) {
        if (event_log) event_log << "ERROR LoadLibrary code=" << GetLastError() << std::endl;
        std::cerr << "Failed to load library" << std::endl;
        return 1;
    }
    if (event_log) event_log << "ATS_DLL_LOADED" << std::endl;

    const bool ran = load_functions_and_run(library_handle);
    if (event_log) event_log << (ran ? "HOST_FINISHED" : "ERROR missing ATS export") << std::endl;

    // The host is exiting; Windows releases the ATS DLL with the process.
    // Some ATS implementations unload themselves during Dispose.
    return 0;
}
