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
#include <sstream>
#include <string>
#include <utility>
#include "../encoder.h"

namespace
{
    HANDLE protocol_output = INVALID_HANDLE_VALUE;
    void protocol_send(const std::string& value) {
        size_t offset=0;
        while(offset<value.size()) { DWORD written=0; if(!WriteFile(protocol_output,value.data()+offset,static_cast<DWORD>(value.size()-offset),&written,nullptr)||written==0) ExitProcess(74); offset+=written; }
    }
    std::ofstream event_log;
    HANDLE crash_log_handle = INVALID_HANDLE_VALUE;
    const char* active_ats_call = "startup";
    unsigned long long elapse_frames = 0;
    unsigned long long elapse_total_us = 0;
    unsigned long long elapse_max_us = 0;
    std::array<unsigned long long, 18> brake_sound_plays{};
    std::array<unsigned long long, 18> brake_sound_loops{};
    std::array<unsigned long long, 18> brake_sound_stops{};
    std::array<int, 18> previous_brake_sound{};
    bool have_previous_brake_sound = false;
    float min_bc_pressure = std::numeric_limits<float>::max();
    float max_bc_pressure = std::numeric_limits<float>::lowest();
    float min_bp_pressure = std::numeric_limits<float>::max();
    float max_bp_pressure = std::numeric_limits<float>::lowest();
    float min_mr_pressure = std::numeric_limits<float>::max();
    float max_mr_pressure = std::numeric_limits<float>::lowest();
    unsigned long long set_brake_calls = 0;
    int last_brake_notch = std::numeric_limits<int>::min();

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
        for (std::string buffer; std::getline(std::cin, buffer); ) {
            std::istringstream line(std::move(buffer));
            std::string command;
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
                ATS_VEHICLESTATE state;
                // Some legacy modules write sentinel indexes such as 1000.
                // BVE exposes only indexes 0..255, so keep padding inside the
                // isolated host and forward just the supported range.
                int panels[2048]{}, sounds[2048]{};
                if ((line >> state) && (command == "ElapseFast"sv ? (read_fast_array(line,panels) && read_fast_array(line,sounds)) : static_cast<bool>(line >> arrayref<int,256>(panels) >> arrayref<int,256>(sounds)))) {
                    active_ats_call = "Elapse";
                    auto started = std::chrono::steady_clock::now();
                    ATS_HANDLES handles = elapse(state, panels, sounds);
                    auto duration = std::chrono::duration_cast<std::chrono::microseconds>(
                        std::chrono::steady_clock::now() - started).count();
                    ++elapse_frames;
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
                        if (event_log && (!have_previous_brake_sound || previous_brake_sound[i] != state_value))
                            event_log << "BC_SOUND frame=" << elapse_frames << " index=" << (150 + i)
                                << " state=" << state_value << " time=" << state.Time
                                << " bc=" << state.BcPressure << " mr=" << state.MrPressure
                                << " er=" << state.ErPressure << " bp=" << state.BpPressure
                                << " sap=" << state.SapPressure << " speed=" << state.Speed << std::endl;
                        previous_brake_sound[i] = state_value;
                    }
                    have_previous_brake_sound = true;
                    std::ostringstream response; response << handles; if(command == "ElapseFast"sv)response << " " << fast_array(panels) << " " << fast_array(sounds);else response << arrayref<int,256>(panels) << arrayref<int,256>(sounds);response << "\n"; protocol_send(response.str());
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
                    keyDown(key);
                }
            }
            else if (command == "KeyUp"sv) {
                int key;
                if (line >> key) {
                    keyUp(key);
                }
            }
            else if (command == "HornBlow"sv) {
                int type;
                if (line >> type) {
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
