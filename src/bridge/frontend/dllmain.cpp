// dllmain.cpp : DLL entry point

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#define ATS_EXPORTS

#include <array>
#include <chrono>
#include <cstddef>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <string_view>
#include <utility>
#include "../encoder.h"

namespace {
    HMODULE dll_module_handle;
    HANDLE pipe_to_backend, pipe_from_backend;
    HANDLE pipe_read_event = NULL;
    HANDLE backend_process = NULL;
    bool backend_failed = false;
    unsigned long long roundtrip_frames = 0;
    unsigned long long roundtrip_total_us = 0;
    unsigned long long roundtrip_max_us = 0;
    // Opt-in timing profile. No per-frame file I/O.
    struct PhaseMetric {
        unsigned long long count = 0;
        unsigned long long total_ns = 0;
        unsigned long long max_ns = 0;
        unsigned long long over_1ms = 0;
        unsigned long long over_5ms = 0;
        unsigned long long over_16ms = 0;
        unsigned long long over_33ms = 0;
        void observe(std::chrono::steady_clock::time_point begin,
                     std::chrono::steady_clock::time_point end) {
            const auto elapsed = std::chrono::duration_cast<std::chrono::nanoseconds>(end - begin).count();
            const auto ns = elapsed > 0 ? static_cast<unsigned long long>(elapsed) : 0ULL;
            ++count;
            total_ns += ns;
            if (ns > max_ns) max_ns = ns;
            if (ns > 1000000ULL) ++over_1ms;
            if (ns > 5000000ULL) ++over_5ms;
            if (ns > 16000000ULL) ++over_16ms;
            if (ns > 33000000ULL) ++over_33ms;
        }
    };
    // Core stops after decoding the ATS response. The remaining work used to
    // be invisible to phase profiling, even though it affects Elapse latency.
    enum PhaseIndex {
        EncodePhase = 0, SendPhase, ReceivePhase, DecodePhase, CorePhase,
        FrameLogPhase, SoundLogPhase, PostPhase, FullPhase, PhaseCount
    };
    PhaseMetric phase_metrics[PhaseCount]{};
    const char* phase_names[PhaseCount] = {
        "encode", "send", "receive", "decode", "core",
        "frame_log", "sound_log", "post", "full"
    };
    bool profile_enabled = false;
    // Bounded, in-memory slow event list. Written once on Dispose, never on Elapse.
    constexpr unsigned long long slow_threshold_ns = 5000000ULL;
    constexpr size_t slow_capacity = 512;
    struct SlowPhaseEvent {
        unsigned long long frame;
        int stage;
        unsigned long long duration_ns;
        long long qpc_start;
        long long qpc_end;
        unsigned long long utc_sample_filetime100ns;
    };
    std::array<SlowPhaseEvent, slow_capacity> slow_events{};
    size_t slow_event_count = 0;
    unsigned long long slow_events_dropped = 0;
    LARGE_INTEGER perf_frequency{};

    // PresentMon's --qpc_time CPUStartQPC and these samples share one Windows QPC clock.
    // UTC is supplemental, not the frame alignment source.
    void record_slow_phase(unsigned long long frame, int stage,
        std::chrono::steady_clock::time_point began,
        std::chrono::steady_clock::time_point ended) {
        const auto duration = std::chrono::duration_cast<std::chrono::nanoseconds>(ended - began).count();
        if (duration < static_cast<long long>(slow_threshold_ns)) return;
        if (slow_event_count >= slow_capacity) { ++slow_events_dropped; return; }
        LARGE_INTEGER now_qpc{};
        QueryPerformanceCounter(&now_qpc);
        const auto now_steady = std::chrono::steady_clock::now();
        const auto after_end_ns =
            std::chrono::duration_cast<std::chrono::nanoseconds>(now_steady - ended).count();
        const long double ticks_per_ns = static_cast<long double>(perf_frequency.QuadPart) / 1000000000.0L;
        const auto end_qpc = now_qpc.QuadPart -
            static_cast<long long>(static_cast<long double>(after_end_ns) * ticks_per_ns);
        const auto start_qpc = end_qpc -
            static_cast<long long>(static_cast<long double>(duration) * ticks_per_ns);
        FILETIME filetime{};
        GetSystemTimePreciseAsFileTime(&filetime);
        const unsigned long long utc_filetime100ns =
            (static_cast<unsigned long long>(filetime.dwHighDateTime) << 32) | filetime.dwLowDateTime;
        slow_events[slow_event_count++] = {
            frame, stage, static_cast<unsigned long long>(duration),
            start_qpc, end_qpc, utc_filetime100ns
        };
    }
    int last_diag_sound_168 = -2147483647;
    int last_diag_sound_169 = -2147483647;
    unsigned long long frontend_elapse_frames = 0;
    // Do not open or write a file from the driving thread. The diagnostic
    // lines are sampled every 300 Elapse calls or on a sound-state change.
    // A fixed capacity bounds memory use even for very long simulator runs.
    constexpr size_t diagnostic_capacity = 4096;
    std::array<std::string, diagnostic_capacity> diagnostic_lines{};
    size_t diagnostic_line_count = 0;
    unsigned long long diagnostic_lines_dropped = 0;
    void buffer_diagnostic(std::string value) {
        if (diagnostic_line_count >= diagnostic_capacity) {
            ++diagnostic_lines_dropped;
            return;
        }
        diagnostic_lines[diagnostic_line_count++] = std::move(value);
    }
    std::wstring environment_value(const wchar_t* name);

    // A named response pipe supports event-based reads with a timeout.
    // Polling an anonymous pipe with Sleep adds a delay to every Elapse.
    bool create_response_pipe(HANDLE* reader, HANDLE* writer, SECURITY_ATTRIBUTES* attrs) {
        const std::wstring name = L"\\\\.\\pipe\\Bve5VehicleCompatibility-" +
            std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64());
        *reader = CreateNamedPipeW(name.c_str(),
            PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, 8192, 8192, 0, NULL);
        if (*reader == INVALID_HANDLE_VALUE) { *reader = NULL; return false; }
        *writer = CreateFileW(name.c_str(), GENERIC_WRITE, 0, attrs, OPEN_EXISTING, 0, NULL);
        if (*writer == INVALID_HANDLE_VALUE) {
            CloseHandle(*reader); *reader = NULL; *writer = NULL; return false;
        }
        OVERLAPPED connection{};
        connection.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
        bool connected = false;
        if (connection.hEvent != NULL) {
            connected = ConnectNamedPipe(*reader, &connection) != FALSE;
            if (!connected) {
                DWORD error = GetLastError();
                connected = error == ERROR_PIPE_CONNECTED;
                if (error == ERROR_IO_PENDING) {
                    DWORD transferred = 0;
                    if (WaitForSingleObject(connection.hEvent, 10000) == WAIT_OBJECT_0)
                        connected = GetOverlappedResult(*reader, &connection, &transferred, FALSE) != FALSE;
                    else {
                        CancelIoEx(*reader, &connection);
                        GetOverlappedResult(*reader, &connection, &transferred, TRUE);
                    }
                }
            }
            CloseHandle(connection.hEvent);
        }
        if (!connected) {
            CloseHandle(*reader); CloseHandle(*writer); *reader = NULL; *writer = NULL;
        }
        return connected;
    }

    void fail_backend(const char* reason) {
        if (backend_failed) return;
        backend_failed = true;
        DWORD exit_code = STILL_ACTIVE;
        if (backend_process != NULL) GetExitCodeProcess(backend_process, &exit_code);
        const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
        if (!log_path.empty()) {
            std::ofstream log(std::filesystem::path(log_path), std::ios::app);
            if (log) log << "ERROR " << reason << " host_exit=0x" << std::hex << exit_code << std::endl;
        }
        if (backend_process != NULL) TerminateProcess(backend_process, 1);
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

    std::filesystem::path get_module_file_name()
    {
        std::wstring name;
        name.resize(1u << 8);
        while (name.size() < 1u << 16) {
            DWORD s = GetModuleFileNameW(
                dll_module_handle,
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

    std::filesystem::path backend_path()
    {
        std::filesystem::path path = get_module_file_name();
        path.replace_extension();
        std::wstring name = path.filename().native();
        if (!name.empty() && name.back() == L'_') {
            name.pop_back();
            name += L".exe";
            path.replace_filename(name);
        }
        else {
            // Unexpected name; abort
            path.clear();
        }
        return path;
    }

    bool send_to_backend(std::string_view data) {
        if (backend_failed || pipe_to_backend == NULL) return false;
        size_t offset = 0;
        while (offset < data.size()) {
            const size_t remaining = data.size() - offset;
            const DWORD requested = static_cast<DWORD>(
                remaining > MAXDWORD ? MAXDWORD : remaining);
            DWORD written = 0;
            if (!WriteFile(pipe_to_backend, data.data() + offset,
                    requested, &written, NULL) || written == 0)
                return false;
            offset += written;
        }
        return true;
    }

    bool receive_from_backend(char* destination, size_t capacity, size_t& received) {
        received = 0;
        if (backend_failed || pipe_from_backend == NULL ||
            pipe_read_event == NULL || destination == nullptr || capacity == 0)
            return false;

        const ULONGLONG deadline = GetTickCount64() + 10000;
        for (;;) {
            OVERLAPPED operation{};
            operation.hEvent = pipe_read_event;
            ResetEvent(pipe_read_event);

            const size_t available = capacity - received;
            const DWORD requested = static_cast<DWORD>(
                available > MAXDWORD ? MAXDWORD : available);
            DWORD read_count = 0;
            BOOL completed = ReadFile(pipe_from_backend,
                destination + received, requested, &read_count, &operation);
            if (!completed && GetLastError() == ERROR_IO_PENDING) {
                const ULONGLONG now = GetTickCount64();
                const DWORD remaining = now < deadline
                    ? static_cast<DWORD>(deadline - now) : 0;
                const DWORD wait = WaitForSingleObject(pipe_read_event, remaining);
                if (wait != WAIT_OBJECT_0) {
                    CancelIoEx(pipe_from_backend, &operation);
                    GetOverlappedResult(pipe_from_backend, &operation, &read_count, TRUE);
                    fail_backend("bridge backend response timeout");
                    return false;
                }
                completed = GetOverlappedResult(
                    pipe_from_backend, &operation, &read_count, FALSE);
            }
            if (!completed || read_count == 0) {
                fail_backend("bridge pipe disconnected");
                return false;
            }

            const size_t previous = received;
            received += read_count;
            if (std::memchr(destination + previous, '\n', read_count) != nullptr)
                return true;
            if (received == capacity) {
                fail_backend("bridge response exceeded fixed buffer");
                return false;
            }
        }
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID)
{
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        dll_module_handle = hModule;
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

ATS_API int WINAPI GetPluginVersion() {
    return ATS_VERSION;
}

ATS_API void WINAPI Load() {
    backend_failed = false;
    roundtrip_frames = roundtrip_total_us = roundtrip_max_us = 0;
    for (auto& metric : phase_metrics) metric = PhaseMetric{};
    slow_event_count = 0;
    slow_events_dropped = 0;
    profile_enabled = environment_value(L"BVE5COMPAT_PROFILE") == L"1";
    if (profile_enabled && (!QueryPerformanceFrequency(&perf_frequency) || perf_frequency.QuadPart <= 0))
        profile_enabled = false;
    // Open pipes to communicate with the backend
    SECURITY_ATTRIBUTES attrs{};
    attrs.nLength = sizeof(attrs);
    attrs.lpSecurityDescriptor = NULL;
    attrs.bInheritHandle = true;
    HANDLE reader_to_backend, writer_to_backend;
    if (!CreatePipe(&reader_to_backend, &writer_to_backend, &attrs, 0)) {
        return;
    }
    if (!SetHandleInformation(writer_to_backend, HANDLE_FLAG_INHERIT, 0)) {
        CloseHandle(reader_to_backend);
        CloseHandle(writer_to_backend);
        return;
    }
    HANDLE reader_from_backend, writer_from_backend;
    if (!create_response_pipe(&reader_from_backend, &writer_from_backend, &attrs)) {
        CloseHandle(reader_to_backend);
        CloseHandle(writer_to_backend);
        return;
    }
    if (!SetHandleInformation(reader_from_backend, HANDLE_FLAG_INHERIT, 0)) {
        CloseHandle(reader_to_backend);
        CloseHandle(writer_to_backend);
        CloseHandle(reader_from_backend);
        CloseHandle(writer_from_backend);
        return;
    }

    // Start the backend
    auto path = backend_path();
    STARTUPINFOW startup_info{};
    startup_info.cb = sizeof(startup_info);
    startup_info.dwFlags = STARTF_USESTDHANDLES;
    startup_info.hStdInput = reader_to_backend;
    startup_info.hStdOutput = writer_from_backend;
    startup_info.hStdError = GetStdHandle(STD_ERROR_HANDLE);
    PROCESS_INFORMATION process_info{};
    const std::wstring ats_path = environment_value(L"BVE5COMPAT_ATS");
    const std::wstring working_directory = environment_value(L"BVE5COMPAT_WORKDIR");
    if (ats_path.empty() || working_directory.empty() || !CreateProcessW(
        path.c_str(),
        NULL,
        NULL,
        NULL,
        TRUE,
        CREATE_NO_WINDOW,
        NULL,
        working_directory.c_str(),
        &startup_info,
        &process_info)) {
        const std::string reason = "bridge host start failed win32=" + std::to_string(GetLastError());
        fail_backend(reason.c_str());
        CloseHandle(reader_to_backend);
        CloseHandle(writer_to_backend);
        CloseHandle(reader_from_backend);
        CloseHandle(writer_from_backend);
        return;
    }
    backend_process = process_info.hProcess;
    CloseHandle(process_info.hThread);

    // Close pipes that are unnecessary on the frontend
    CloseHandle(reader_to_backend);
    CloseHandle(writer_from_backend);

    pipe_to_backend = writer_to_backend;
    pipe_from_backend = reader_from_backend;
    pipe_read_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (pipe_read_event == NULL) {
        fail_backend("bridge read event unavailable");
        return;
    }

    // Reuse one overlapped-read event for the entire ATS session rather than
    // creating and closing a Windows event for every Elapse response.
    send_to_backend("Load\n");
    std::array<char, 8192> ready_buffer{};
    size_t ready_size = 0;
    if (!receive_from_backend(ready_buffer.data(), ready_buffer.size(), ready_size))
        return;
    const std::string_view ready(ready_buffer.data(), ready_size);
    if (ready.find("READY") == std::string_view::npos) return;
}

ATS_API void WINAPI Dispose() {
    // Call the Dispose function on the backend
    send_to_backend("Dispose\n");

    // Close pipes
    CloseHandle(pipe_to_backend);
    pipe_to_backend = NULL;
    CloseHandle(pipe_from_backend);
    pipe_from_backend = NULL;
    if (pipe_read_event != NULL) {
        CloseHandle(pipe_read_event);
        pipe_read_event = NULL;
    }
    if (backend_process != NULL) {
        if (WaitForSingleObject(backend_process, 1000) == WAIT_TIMEOUT)
            TerminateProcess(backend_process, 1);
        CloseHandle(backend_process);
        backend_process = NULL;
    }
    const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
    if (!log_path.empty()) {
        // Preserve original FRONTEND_FRAME and FRONTEND_SOUND_RETURN line
        // formats, but write them only after BVE has finished driving.
        std::ofstream log(std::filesystem::path(log_path), std::ios::app);
        if (log) {
            for (size_t i = 0; i < diagnostic_line_count; ++i)
                log << diagnostic_lines[i] << '\n';
            log << "FRONTEND_DIAGNOSTIC_BUFFER_SUMMARY stored=" << diagnostic_line_count
                << " dropped=" << diagnostic_lines_dropped
                << " capacity=" << diagnostic_capacity << std::endl;
        }
    }
    if (!log_path.empty() && roundtrip_frames > 0) {
        std::ofstream log(std::filesystem::path(log_path), std::ios::app);
        if (log) {
            log << "METRICS bridge_elapse_frames=" << roundtrip_frames
                << " avg_us=" << (roundtrip_total_us / roundtrip_frames)
                << " max_us=" << roundtrip_max_us << std::endl;
            if (profile_enabled) {
                for (int phase = 0; phase < PhaseCount; ++phase) {
                    const PhaseMetric& metric = phase_metrics[phase];
                    if (!metric.count) continue;
                    log << "METRICS bridge_phase stage=" << phase_names[phase]
                        << " count=" << metric.count
                        << " avg_ns=" << (metric.total_ns / metric.count)
                        << " max_ns=" << metric.max_ns
                        << " over_1ms=" << metric.over_1ms
                        << " over_5ms=" << metric.over_5ms
                        << " over_16ms=" << metric.over_16ms
                        << " over_33ms=" << metric.over_33ms << std::endl;
                }
                log << "SLOW_PHASE_SUMMARY threshold_ns=" << slow_threshold_ns
                    << " stored=" << slow_event_count
                    << " dropped=" << slow_events_dropped
                    << " qpc_freq=" << perf_frequency.QuadPart << std::endl;
                for (size_t i = 0; i < slow_event_count; ++i) {
                    const SlowPhaseEvent& event = slow_events[i];
                    log << "SLOW_PHASE frame=" << event.frame
                        << " stage=" << phase_names[event.stage]
                        << " duration_ns=" << event.duration_ns
                        << " qpc_start=" << event.qpc_start
                        << " qpc_end=" << event.qpc_end
                        << " qpc_freq=" << perf_frequency.QuadPart
                        << " utc_sample_filetime100ns=" << event.utc_sample_filetime100ns
                        << std::endl;
                }
            }
        }
    }
}

ATS_API void WINAPI SetVehicleSpec(ATS_VEHICLESPEC spec) {
    std::ostringstream s;
    s << "SetVehicleSpec " << spec << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI Initialize(int brake) {
    std::ostringstream s;
    s << "Initialize " << brake << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API ATS_HANDLES WINAPI Elapse(
    ATS_VEHICLESTATE state, int *panelValues, int *soundStates)
{
    auto started = std::chrono::steady_clock::now();
    ATS_HANDLES handles{};

    std::array<char, 8192> request_bytes;
    fixed_output_streambuf request_buffer(request_bytes.data(), request_bytes.size());
    std::ostream request(&request_buffer);
    request << "ElapseFast "
        << state
        << " " << fast_array(panelValues)
        << " " << fast_array(soundStates)
        << '\n';

    if (request) {
        const auto encoded = std::chrono::steady_clock::now();
        const bool sent = send_to_backend(request_buffer.view());
        const auto transmitted = std::chrono::steady_clock::now();

        std::array<char, 8192> response_bytes;
        size_t response_size = 0;
        const bool received_ok = receive_from_backend(
            response_bytes.data(), response_bytes.size(), response_size);
        const auto received = std::chrono::steady_clock::now();
        fixed_input_streambuf response_buffer(response_bytes.data(), response_size);
        std::istream line(&response_buffer);
        if (received_ok) {
            line >> handles;
            if (!read_fast_array(line, panelValues) ||
                !read_fast_array(line, soundStates))
                line.setstate(std::ios::failbit);
        }
        else {
            line.setstate(std::ios::failbit);
        }
        const auto decoded = std::chrono::steady_clock::now();
        if (profile_enabled && sent && !backend_failed && !line.fail()) {
            phase_metrics[EncodePhase].observe(started, encoded);
            phase_metrics[SendPhase].observe(encoded, transmitted);
            phase_metrics[ReceivePhase].observe(transmitted, received);
            phase_metrics[DecodePhase].observe(received, decoded);
            phase_metrics[CorePhase].observe(started, decoded);
            const auto frame_number = frontend_elapse_frames + 1;
            record_slow_phase(frame_number, EncodePhase, started, encoded);
            record_slow_phase(frame_number, SendPhase, encoded, transmitted);
            record_slow_phase(frame_number, ReceivePhase, transmitted, received);
            record_slow_phase(frame_number, DecodePhase, received, decoded);
        }
        ++frontend_elapse_frames;
        if (!backend_failed && (frontend_elapse_frames == 1 || frontend_elapse_frames % 300 == 0)) {
            const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
            if (!log_path.empty()) {
                std::ostringstream log;
                log << "FRONTEND_FRAME frame=" << frontend_elapse_frames
                    << " time=" << state.Time << " speed=" << state.Speed
                    << " bc=" << state.BcPressure << " mr=" << state.MrPressure
                    << " er=" << state.ErPressure << " bp=" << state.BpPressure
                    << " sap=" << state.SapPressure << " current=" << state.Current
                    << " handles=" << handles.Brake
                    << ',' << handles.Power << ',' << handles.Reverser << ',' << handles.ConstantSpeed;
                if (panelValues != nullptr) {
                    log << " panel_changes=";
                    size_t emitted = 0;
                    size_t changed = 0;
                    for (size_t i = 0; i < 256; ++i) {
                        if (panelValues[i] != 0) {
                            ++changed;
                            if (emitted < 32) {
                                log << i << ':' << panelValues[i] << ',';
                                ++emitted;
                            }
                        }
                    }
                    log << "nonzero_count:" << changed;
                }
                buffer_diagnostic(log.str());
            }
        }
        const auto after_frame_log = std::chrono::steady_clock::now();
        if (!backend_failed && soundStates != nullptr &&
            (soundStates[168] != last_diag_sound_168 || soundStates[169] != last_diag_sound_169)) {
            const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
            if (!log_path.empty()) {
                std::ostringstream log;
                log << "FRONTEND_SOUND_RETURN index168=" << soundStates[168]
                    << " index169=" << soundStates[169];
                buffer_diagnostic(log.str());
            }
            last_diag_sound_168 = soundStates[168];
            last_diag_sound_169 = soundStates[169];
        }
        const auto after_sound_log = std::chrono::steady_clock::now();
        if (!backend_failed) {
            auto duration = std::chrono::duration_cast<std::chrono::microseconds>(
                after_sound_log - started).count();
            ++roundtrip_frames;
            roundtrip_total_us += static_cast<unsigned long long>(duration);
            if (static_cast<unsigned long long>(duration) > roundtrip_max_us)
                roundtrip_max_us = static_cast<unsigned long long>(duration);
        }
        const auto post_finished = std::chrono::steady_clock::now();
        if (profile_enabled && sent && !backend_failed && !line.fail()) {
            phase_metrics[FrameLogPhase].observe(decoded, after_frame_log);
            phase_metrics[SoundLogPhase].observe(after_frame_log, after_sound_log);
            phase_metrics[PostPhase].observe(decoded, post_finished);
            phase_metrics[FullPhase].observe(started, post_finished);
            record_slow_phase(frontend_elapse_frames, FrameLogPhase, decoded, after_frame_log);
            record_slow_phase(frontend_elapse_frames, SoundLogPhase, after_frame_log, after_sound_log);
            record_slow_phase(frontend_elapse_frames, PostPhase, decoded, post_finished);
            record_slow_phase(frontend_elapse_frames, FullPhase, started, post_finished);
        }
    }
    return handles;
}

ATS_API void WINAPI SetPower(int notch) {
    std::ostringstream s;
    s << "SetPower " << notch << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI SetBrake(int notch) {
    std::ostringstream s;
    s << "SetBrake " << notch << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI SetReverser(int notch) {
    std::ostringstream s;
    s << "SetReverser " << notch << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI KeyDown(int key) {
    std::ostringstream s;
    s << "KeyDown " << key << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI KeyUp(int key) {
    std::ostringstream s;
    s << "KeyUp " << key << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI HornBlow(int type) {
    std::ostringstream s;
    s << "HornBlow " << type << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI DoorOpen() {
    send_to_backend("DoorOpen\n");
}

ATS_API void WINAPI DoorClose() {
    send_to_backend("DoorClose\n");
}

ATS_API void WINAPI SetSignal(int aspect) {
    std::ostringstream s;
    s << "SetSignal " << aspect << std::endl;
    send_to_backend(std::move(s).str());
}

ATS_API void WINAPI SetBeaconData(ATS_BEACONDATA data) {
    std::ostringstream s;
    s << "SetBeaconData " << data << std::endl;
    send_to_backend(std::move(s).str());
}
