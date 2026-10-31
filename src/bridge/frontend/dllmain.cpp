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
#include "../encoder.h"

namespace {
    HMODULE dll_module_handle;
    HANDLE pipe_to_backend, pipe_from_backend;
    HANDLE backend_process = NULL;
    bool backend_failed = false;
    unsigned long long roundtrip_frames = 0;
    unsigned long long roundtrip_total_us = 0;
    unsigned long long roundtrip_max_us = 0;
    int last_diag_sound_168 = -2147483647;
    int last_diag_sound_169 = -2147483647;
    unsigned long long frontend_elapse_frames = 0;
    ATS_HANDLES last_returned_handles{};
    std::array<int, 256> last_returned_panel;
    bool have_returned_panel = false;
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

    bool send_to_backend(const std::string_view &s) {
        if (backend_failed || pipe_to_backend == NULL) return false;
        DWORD written_count;
        return WriteFile(
            pipe_to_backend,
            s.data(),
            static_cast<DWORD>(s.size()),
            &written_count, NULL);
    }

    std::string receive_from_backend() {
        if (backend_failed || pipe_from_backend == NULL) return {};
        std::string line;
        std::array<char, 8192> buffer{};
        ULONGLONG deadline = GetTickCount64() + 10000;
        HANDLE event = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (event == NULL) { fail_backend("bridge read event unavailable"); return {}; }
        for (;;) {
            OVERLAPPED operation{};
            operation.hEvent = event;
            ResetEvent(event);
            DWORD read_count = 0;
            BOOL completed = ReadFile(pipe_from_backend, buffer.data(),
                static_cast<DWORD>(buffer.size()), &read_count, &operation);
            if (!completed && GetLastError() == ERROR_IO_PENDING) {
                ULONGLONG now = GetTickCount64();
                DWORD remaining = now < deadline ? static_cast<DWORD>(deadline - now) : 0;
                DWORD wait = WaitForSingleObject(event, remaining);
                if (wait != WAIT_OBJECT_0) {
                    CancelIoEx(pipe_from_backend, &operation);
                    GetOverlappedResult(pipe_from_backend, &operation, &read_count, TRUE);
                    CloseHandle(event);
                    fail_backend("bridge backend response timeout");
                    return {};
                }
                completed = GetOverlappedResult(pipe_from_backend, &operation, &read_count, FALSE);
            }
            if (!completed || read_count == 0) {
                CloseHandle(event);
                fail_backend("bridge pipe disconnected");
                return {};
            }

            std::string_view fragment(
                buffer.data(), static_cast<std::size_t>(read_count));
            line.append(fragment);
            if (line.size() > 1024 * 1024) {
                CloseHandle(event);
                fail_backend("bridge response exceeded limit");
                return {};
            }
            if (fragment.find('\n') != std::string_view::npos) {
                break;
            }
        }
        CloseHandle(event);
        return line;
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

    // Call the Load function on the backend
    send_to_backend("Load\n");
    if (receive_from_backend().find("READY") == std::string::npos) return;
}

ATS_API void WINAPI Dispose() {
    // Call the Dispose function on the backend
    send_to_backend("Dispose\n");

    // Close pipes
    CloseHandle(pipe_to_backend);
    pipe_to_backend = NULL;
    CloseHandle(pipe_from_backend);
    pipe_from_backend = NULL;
    if (backend_process != NULL) {
        if (WaitForSingleObject(backend_process, 1000) == WAIT_TIMEOUT)
            TerminateProcess(backend_process, 1);
        CloseHandle(backend_process);
        backend_process = NULL;
    }
    const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
    if (!log_path.empty() && roundtrip_frames > 0) {
        std::ofstream log(std::filesystem::path(log_path), std::ios::app);
        if (log) log << "METRICS bridge_elapse_frames=" << roundtrip_frames
            << " avg_us=" << (roundtrip_total_us / roundtrip_frames)
            << " max_us=" << roundtrip_max_us << std::endl;
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

    std::ostringstream s;
    s << "ElapseFast "
        << state
        << " " << fast_array(panelValues)
        << " " << fast_array(soundStates)
        << std::endl;

    if (s) {
        send_to_backend(std::move(s).str());

        std::string buffer = receive_from_backend();
        std::istringstream line(std::move(buffer));
        line >> handles;
        if(!read_fast_array(line,panelValues)||!read_fast_array(line,soundStates))line.setstate(std::ios::failbit);
        ++frontend_elapse_frames;
        bool handles_changed = frontend_elapse_frames == 1 ||
            handles.Brake != last_returned_handles.Brake ||
            handles.Power != last_returned_handles.Power ||
            handles.Reverser != last_returned_handles.Reverser ||
            handles.ConstantSpeed != last_returned_handles.ConstantSpeed;
        bool panel_changed = false;
        if (panelValues != nullptr) {
            for (size_t i = 0; i < 256; ++i) {
                if (!have_returned_panel || panelValues[i] != last_returned_panel[i]) {
                    panel_changed = true;
                    last_returned_panel[i] = panelValues[i];
                }
            }
            have_returned_panel = true;
        }
        if (!backend_failed && (frontend_elapse_frames == 1 || frontend_elapse_frames % 300 == 0)) {
            const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
            if (!log_path.empty()) {
                std::ofstream log(std::filesystem::path(log_path), std::ios::app);
                if (log) {
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
                    log << std::endl;
                }
            }
        }
        last_returned_handles = handles;
        if (!backend_failed && soundStates != nullptr &&
            (soundStates[168] != last_diag_sound_168 || soundStates[169] != last_diag_sound_169)) {
            const std::wstring log_path = environment_value(L"BVE5COMPAT_LOG");
            if (!log_path.empty()) {
                std::ofstream log(std::filesystem::path(log_path), std::ios::app);
                if (log) log << "FRONTEND_SOUND_RETURN index168=" << soundStates[168]
                    << " index169=" << soundStates[169] << std::endl;
            }
            last_diag_sound_168 = soundStates[168];
            last_diag_sound_169 = soundStates[169];
        }
        if (!backend_failed) {
            auto duration = std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now() - started).count();
            ++roundtrip_frames;
            roundtrip_total_us += static_cast<unsigned long long>(duration);
            if (static_cast<unsigned long long>(duration) > roundtrip_max_us)
                roundtrip_max_us = static_cast<unsigned long long>(duration);
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
